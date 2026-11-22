using System.Numerics;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Ink;
using InkCanvas.Features.AiAssistant;
using InkCanvas.Host.Contracts;
using InkCanvas.Ink;
using Microsoft.UI.Dispatching;

namespace InkCanvas.Modules.AiAssistant;

internal sealed partial class AssistantService
{
    private IAssistantAgentProvider _agentProvider;
    private CancellationTokenSource _agentCancellation;

    public IDisposable Register(IAssistantAgentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Exchange(ref _agentProvider, provider);
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || !ReferenceEquals(_agentProvider, provider)) return;
            _agentCancellation?.Cancel();
            _aiAssistant?.SetAgentAvailability(provider.Name);
        });
        return new AgentRegistration(() =>
        {
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _agentProvider, null, provider), provider)) return;
            _dispatcher.TryEnqueue(() =>
            {
                _agentCancellation?.Cancel();
                _aiAssistant?.SetAgentAvailability(null);
            });
        });
    }

    private async Task RunAgentAsync(string question)
    {
        var provider = _agentProvider;
        var window = _aiAssistant;
        if (_disposed || provider is null || window is null) return;
        _aiGate.Invalidate(); _renderTimer?.Stop(); _aiStream?.Abort();
        _aiRegionStream?.Abort(); _regionGeneration++; _variantPending = false;
        _agentCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _agentCancellation = cancellation;
        var request = new AssistantAgentRequest(question, window.ConversationMessages());
        var session = AssistantMachine.CreateSession(AssistantMode.Chat, _aiSession?.SessionId ?? "");
        session.State = AssistantState.Streaming; session.TeachingMode = AssistantTeachingMode.Full;
        _aiSession = session; window.BeginTurn(question); window.Apply(session, _aiThumb); window.SetBusy(true);
        var target = _canvasHost.CaptureTarget();
        var canvas = new AgentCanvas(_host, _canvasHost, target, _dispatcher, cancellation.Token);
        var progress = new Progress<AssistantAgentProgress>(item => _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed && !cancellation.IsCancellationRequested && ReferenceEquals(_aiSession, session)) window.SetAgentProgress(item);
        }));
        try
        {
            string result = await provider.RunAsync(request, canvas, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            session.Text = result ?? ""; AssistantMachine.Finish(session);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { AssistantMachine.Finish(session); }
        catch (Exception exception)
        { AssistantMachine.Fail(session, exception.Message); }
        finally
        {
            cancellation.Cancel();
            if (ReferenceEquals(_agentCancellation, cancellation)) _agentCancellation = null;
            if (!_disposed && ReferenceEquals(_aiSession, session) && ReferenceEquals(_aiAssistant, window))
            { window.Apply(session, _aiThumb); window.SetBusy(false); }
        }
    }

    private sealed class AgentRegistration(Action release) : IDisposable
    {
        private Action _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class AgentCanvas(IAiAssistantHost host, ICanvasToolsHost canvasHost,
        CanvasToolTarget target, DispatcherQueue dispatcher, CancellationToken lifetime) : IAssistantAgentCanvas
    {
        public Task<AssistantCanvasSnapshot> ReadAsync(CancellationToken cancellationToken) => OnCanvas(() =>
        {
            var controller = target.Controller;
            var selected = controller.Selection.Select(stroke => stroke.Id).Concat(controller.SelectedElements.Select(element => element.Id)).ToArray();
            return new AssistantCanvasSnapshot(target.Kind, controller.Document.Strokes.Select(stroke => stroke.Clone()).ToArray(),
                selected, controller.ViewTransform)
            {
                Objects = (controller.GetSelectableElements?.Invoke() ?? Array.Empty<ISelectionElement>())
                    .Where(element => element.IsPresent).Select(element => new AssistantCanvasObject(element.Id,
                        element is IImageSelectionElement ? "image" : "media", element.Bounds, selected.Contains(element.Id))).ToArray(),
            };
        }, cancellationToken);

        public Task<bool> CreateShapesAsync(IReadOnlyList<InkStroke> strokes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(strokes);
            var copies = strokes.Select(stroke => stroke.Clone(preserveId: false)).ToArray();
            if (copies.Any(stroke => stroke.Count == 0 || !float.IsFinite(stroke.Attributes.BaseWidth)
                || stroke.Points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y))))
                throw new ArgumentException("图形包含无效坐标。", nameof(strokes));
            return OnCanvas(() =>
            {
                bool committed = canvasHost.CommitShape(target, copies);
                if (committed) canvasHost.EnsureVisible(target);
                return committed;
            }, cancellationToken);
        }

        public Task<bool> TransformSelectionAsync(Matrix3x2 transform, CancellationToken cancellationToken)
        {
            if (!float.IsFinite(transform.M11) || !float.IsFinite(transform.M12) || !float.IsFinite(transform.M21)
                || !float.IsFinite(transform.M22) || !float.IsFinite(transform.M31) || !float.IsFinite(transform.M32)
                || Math.Abs(transform.GetDeterminant()) < .000001f)
                throw new ArgumentException("变换矩阵无效。", nameof(transform));
            return OnCanvas(() =>
            {
                var controller = target.Controller;
                if (!target.CanEdit || !controller.HasSelection || controller.HasActiveInteraction || controller.Playback is not null) return false;
                controller.TransformSelectionUndoable(transform, "Agent 调整对象"); return true;
            }, cancellationToken);
        }

        public async Task<byte[]> CaptureAsync(CancellationToken cancellationToken)
        {
            var bounds = await OnCanvas(() => host.OverlayBounds, cancellationToken);
            string png = await AssistantCapture.CapturePngBase64Async(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            await OnCanvas(() => true, cancellationToken);
            return string.IsNullOrEmpty(png) ? Array.Empty<byte>() : Convert.FromBase64String(png);
        }

        private Task<T> OnCanvas<T>(Func<T> action, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lifetime.ThrowIfCancellationRequested();
                    if (!canvasHost.IsCurrent(target)) throw new InvalidOperationException("画布已切换，请重新发起操作。");
                    completion.TrySetResult(action());
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
                catch (Exception exception) { completion.TrySetException(exception); }
            })) completion.TrySetException(new ObjectDisposedException(nameof(AgentCanvas)));
            return completion.Task;
        }
    }
}
