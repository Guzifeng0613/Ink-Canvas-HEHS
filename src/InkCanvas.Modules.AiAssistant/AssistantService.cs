using InkCanvas.Controls.Controls;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Ink;
using InkCanvas.Features.AiAssistant;
using InkCanvas.Features.Board;
using InkCanvas.Features.Classroom;
using InkCanvas.Host.Contracts;
using InkCanvas.Platform.Ai;
using InkCanvas.Platform.System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Numerics;
using System.Text;
using Windows.ApplicationModel.DataTransfer;

namespace InkCanvas.Modules.AiAssistant;

internal sealed class AssistantService(IAiAssistantHost _host) : IAiAssistantService, IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _disposed;
    private int _captureGeneration;
    private int _regionGeneration;
    private DispatcherQueueTimer _renderTimer;
    private (int Id, int Index, int Step) _lastSlide;
    private AssistantWindow _aiAssistant;
    private AiStreamHandle _aiStream;
    private AiStreamHandle _aiRegionStream;
    private AssistantSession _aiSession;
    private AssistantRect _aiAnchor;
    private AiClient _aiClient;
    private string _aiThumb = "";
    private ProblemContext _aiProblem;
    private List<AssistantStroke> _aiStrokes = new();
    private double _aiScale = 1.0;
    private int _aiSlideId;
    private int _aiSlideIndex;
    private bool _aiTwoStage;
    private string _aiStageFirstToken;
    private bool _aiStageReset;
    private string _aiPreStageText = "";
    private List<AssistantStep> _aiPreStageSteps = new();
    private string _aiPreStageFormula = "";
    private bool _aiSecondStageStarted;
    private readonly AiResponseGate _aiGate = new();
    private ExerciseDifficulty _variantDifficulty;
    private bool _variantPending;
    private string _reRecognizeRegionId;

    // ---- AI 助教 ---------------------------------------------------------

    public void Open()
    {
        if (_disposed) return;
        if (_aiAssistant is not null) { _aiAssistant.Activate(); return; }
        CaptureCanvas();
    }

    private void CaptureCanvas()
    {
        if (_disposed) return;
        var controller = _host.ActiveController;
        IReadOnlyList<InkStroke> sourceStrokes = controller.Selection;
        var bounds = controller.SelectionBounds;
        string source = "selection";

        if (sourceStrokes.Count == 0 || bounds.Width < 16 || bounds.Height < 16)
        {
            source = "canvas";
            // Friendly fallback: the selection tool is tap-only today. When nothing is
            // selected we recognize every stroke on the canvas; when the canvas is empty
            // we recognize the whole overlay viewport (screenshot-only chat).
            sourceStrokes = controller.Document.Strokes;
            if (sourceStrokes.Count == 0)
            {
                sourceStrokes = Array.Empty<InkStroke>();
                bounds = new Rect(
                    0, 0,
                    (float)(_host.OverlayBounds.Width / _host.DpiScale),
                    (float)(_host.OverlayBounds.Height / _host.DpiScale));
            }
            else
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                foreach (InkStroke stroke in sourceStrokes)
                {
                    foreach (var point in stroke.Points)
                    {
                        minX = MathF.Min(minX, point.X);
                        minY = MathF.Min(minY, point.Y);
                        maxX = MathF.Max(maxX, point.X);
                        maxY = MathF.Max(maxY, point.Y);
                    }
                }
                bounds = new Rect(minX - 8, minY - 8, maxX - minX + 16, maxY - minY + 16);
                if (bounds.Width < 16 || bounds.Height < 16)
                {
                    ShowAssistantMessage("画布上还没有可识别的内容，请先书写或用「选择」工具点选。");
                    return;
                }
            }
        }

        Matrix3x2 matrix = controller.ViewTransform;
        Vector2 topLeft = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), matrix);
        Vector2 bottomRight = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), matrix);

        var viewRect = new AssistantRect(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Max(topLeft.X, bottomRight.X),
            Math.Max(topLeft.Y, bottomRight.Y));
        _aiAnchor = viewRect;

        List<AssistantStroke> strokes = BuildAssistantStrokes(sourceStrokes, matrix);
        double scale = Math.Sqrt(Math.Abs(matrix.GetDeterminant()));
        double dpi = _host.DpiScale;

        int px = (int)Math.Round(viewRect.Left * dpi);
        int py = (int)Math.Round(viewRect.Top * dpi);
        int pw = (int)Math.Round(viewRect.Width * dpi);
        int ph = (int)Math.Round(viewRect.Height * dpi);

        _aiSlideId = _host.Source.SlideId;
        _aiSlideIndex = _host.Source.SlideIndex;

        _ = StartAssistantAsync(strokes, scale, px, py, pw, ph, source);
    }

    private void ShowAssistantMessage(string message)
    {
        _aiThumb = "";
        _aiAnchor = CenterAnchor();
        _aiSession = AssistantMachine.CreateSession(AssistantMode.Auto, "");
        AssistantMachine.Fail(_aiSession, message);
        PrepareAssistantWindow().Apply(_aiSession, _aiThumb);
    }

    private async Task StartAssistantAsync(
        List<AssistantStroke> strokes, double scale, int px, int py, int pw, int ph, string source)
    {
        int capture = ++_captureGeneration;
        string thumb;
        try
        {
            thumb = await AssistantCapture.CapturePngBase64Async(px, py, pw, ph);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 截图失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            thumb = "";
        }

        if (_disposed || capture != _captureGeneration) return;
        _aiThumb = thumb;

        var problem = new ProblemContext
        {
            SlideId = _aiSlideId,
            SlideIndex = _aiSlideIndex,
            AnimationStep = _host.Source.AnimationStep,
        };
        ProblemContextPolicy.Add(problem, new ProblemRegion
        {
            ImageBase64 = _aiThumb,
            Anchor = _aiAnchor,
            Source = source ?? "",
        });

        _aiProblem = problem;
        _aiStrokes = strokes;
        _aiScale = scale;

        try
        {
            PrepareAssistantWindow();
            StartAssistantStream(BuildProblemRequest(), followUp: false, question: "");
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 浮窗启动失败：{ex}", Core.Diagnostics.LogType.Error);
        }
    }

    /// <summary>
    /// 用当前拼题上下文构造请求。多选区时不发墨迹（先截图为准），并让外接矩形决定浮窗位置。
    /// </summary>
    private AssistantRequest BuildProblemRequest(AssistantTeachingMode? teachingOverride = null)
    {
        IReadOnlyList<ProblemRegion> ordered = ProblemContextPolicy.Ordered(_aiProblem);
        AssistantRect combined = ProblemContextPolicy.CombinedAnchor(_aiProblem);
        if (!combined.IsEmpty) _aiAnchor = combined;

        _aiThumb = ProblemContextPolicy.PrimaryImage(_aiProblem);
        bool multi = ordered.Count > 1;

        AssistantTeachingMode teaching = teachingOverride ?? AssistantTeachingPolicy.Parse(
            _host.Settings.Ai.TeachingMode);
        // 课堂/投屏默认不直接公布完整答案；教师仍可在浮窗内手动切到完整解答。
        if (teachingOverride is null && teaching == AssistantTeachingMode.Full && IsClassroomPresentation())
            teaching = AssistantTeachingMode.StepByStep;

        return new AssistantRequest
        {
            ImageBase64 = _aiThumb,
            Images = ordered,
            InkStrokes = multi ? Array.Empty<AssistantStroke>() : _aiStrokes,
            Viewport = new AssistantViewport { Scale = _aiScale, Rect = _aiAnchor },
            Mode = AssistantMode.Auto,
            Teaching = teaching,
            SessionId = _aiProblem?.SessionId ?? "",
            SlideContext = BuildSlideContext(),
        };
    }

    private bool IsClassroomPresentation() => _host.IsClassroomPresentation;

    private SlideContext BuildSlideContext() => _host.GetSlideContext(_aiSlideId, _aiSlideIndex);

    private AssistantWindow PrepareAssistantWindow()
    {
        if (_aiAssistant is not null)
        {
            UpdateSource(_aiAssistant);
            return _aiAssistant;
        }

        var window = new AssistantWindow(_host.Settings.Appearance.Theme, _host.Settings.Features,
            demoMode: string.IsNullOrEmpty(AiConfig.ReadFromEnvironment().Endpoint));
        _aiAssistant = window;
        window.StopRequested += OnAiStopRequested;
        window.RetryRequested += OnAiRetryRequested;
        window.CopyRequested += OnAiCopyRequested;
        window.PasteRequested += OnAiPasteRequested;
        window.FollowUpRequested += OnAiFollowUpRequested;
        window.TeachingModeRequested += OnAiTeachingModeChanged;
        window.RevealNextRequested += OnAiRevealNext;
        window.VariantRequested += OnAiVariantRequested;
        window.AddRegionRequested += () => _ = RunWindowOperationAsync(AddAiRegionAsync);
        window.RemoveRegionRequested += OnAiRemoveRegion;
        window.ReRecognizeRegionRequested += OnAiReRecognizeRegion;
        window.MoveRegionRequested += OnAiMoveRegion;
        window.RegionSelected += OnAiRegionSelected;
        window.Closed += () => OnAiWindowClosed(window);
        UpdateSource(window);
        _host.ConfigureWindow(window);
        window.UseNativeFrame();
        window.Activate();
        ShowLassoAiHintOnce(window);
        return window;
    }

    private void UpdateSource(AssistantWindow window)
    {
        window.SetAnchor(_aiAnchor);
        window.RegionSelectionEnabled =
            _host.Settings.Ai.EnableLocalCorrection && !(_aiProblem is { Count: > 1 });
        window.ApplyProblem(_aiProblem);
        window.SetFavoriteSource(
            _host.Source.Presentation,
            _aiSlideId > 0 ? _aiSlideId : _aiProblem?.SlideId ?? 0,
            _host.BoardOpen ? _host.Board.CurrentIndex : -1);

    }

    /// <summary>
    /// 首次使用套索 / AI 识别时的一次性轻提示：提示内容走浮窗现有 notice 行，
    /// 约 2.6 秒自动消失；写入设置后不再出现，避免反复打扰。
    /// </summary>
    private void ShowLassoAiHintOnce(AssistantWindow window)
    {
        if (window is null || _host.Settings.Ai.HasSeenLassoHint) return;

        try
        {
            _host.Settings.Ai.HasSeenLassoHint = true;
            _host.SaveSettings();
        }
        catch
        {
            // 设置写入失败不应阻断 AI 功能；下次启动会再次提示一次。
        }

        window.ShowNotice("框选内容后，可使用 AI 识别公式和图形。");
    }

    private void StartAssistantStream(AssistantRequest request, bool followUp, string question)
    {
        if (_disposed || _aiAssistant is null) return;
        _renderTimer?.Stop();
        _aiClient ??= new AiClient(AiConfig.ReadFromEnvironment());
        _aiStream?.Abort();
        _aiStream = null;
        _regionGeneration++;
        _aiRegionStream?.Abort();
        _aiRegionStream = null;

        long now = Environment.TickCount64;
        if (followUp)
        {
            _aiSession ??= AssistantMachine.CreateSession(AssistantMode.Chat, request.SessionId);
            AssistantMachine.BeginFollowUp(_aiSession, question, now);
            _aiSession.Mode = AssistantMode.Chat;
        }
        else
        {
            _aiSession = AssistantMachine.CreateSession(request.Mode, request.SessionId);
            AssistantMachine.BeginCapture(_aiSession, now);
            AssistantMachine.BeginRecognize(_aiSession);
            _aiSession.TeachingMode = request.Teaching;
            _aiSession.RevealedSteps = 0;
        }

        _aiAssistant?.Apply(_aiSession, _aiThumb);
        _aiAssistant?.SetBusy(true);

        // 翻页后复用同一浮窗时刷新收藏来源，避免把新页讲评记到旧 slideId。
        _aiAssistant?.SetFavoriteSource(
            _host.Source.Presentation,
            _aiSlideId > 0 ? _aiSlideId : _aiProblem?.SlideId ?? 0,
            _host.BoardOpen ? _host.Board.CurrentIndex : -1);

        request.Question = followUp ? question ?? "" : "";
        _aiTwoStage = !followUp && _host.Settings.Ai.EnableTwoStage
            && (_aiProblem is null || _aiProblem.Count <= 1);
        _aiStageFirstToken = null;
        _aiStageReset = false;
        ResetAiPreStage();

        if (_aiTwoStage)
        {
            try
            {
                _aiSession.LocalHint = LocalFormulaTranscriber.Hint(request.InkStrokes, _aiSession);
                _aiAssistant?.Apply(_aiSession, _aiThumb);
            }
            catch (Exception ex)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    $"AI 本地提示失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            }
        }

        int generation = _aiGate.Begin(request.SessionId);

        try
        {
            if (_aiTwoStage)
            {
                AiStreamHandle handle = _aiClient.StartTwoStage(
                    request,
                    ev => _dispatcher.TryEnqueue(() =>
                    {
                        if (_aiGate.IsCurrent(generation)) OnAiEvent(ev);
                    }),
                    err => _dispatcher.TryEnqueue(() =>
                    {
                        if (_aiGate.IsCurrent(generation)) OnAiError(err);
                    }),
                    () => _dispatcher.TryEnqueue(() =>
                    {
                        if (_aiGate.IsCurrent(generation)) OnAiDone();
                    }));
                _aiStream = handle;
                handle.SecondStageStarted += () => _dispatcher.TryEnqueue(() =>
                {
                    if (ReferenceEquals(_aiStream, handle) && _aiGate.IsCurrent(generation))
                        OnAiSecondStageStarted();
                });
            }
            else
            {
                _aiStream = _aiClient.Start(
                    request,
                    ev => _dispatcher.TryEnqueue(() =>
                    {
                        if (_aiGate.IsCurrent(generation)) OnAiEvent(ev);
                    }),
                    err => _dispatcher.TryEnqueue(() =>
                    {
                        if (_aiGate.IsCurrent(generation)) OnAiError(err);
                    }),
                    () => _dispatcher.TryEnqueue(() =>
                    {
                        if (_aiGate.IsCurrent(generation)) OnAiDone();
                    }));
            }
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 请求启动失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            OnAiError("AI 请求启动失败");
        }
    }

    private void OnAiEvent(AssistantEvent ev)
    {
        if (_aiSession is null) return;
        try
        {
            if (_aiTwoStage) ApplyTwoStageEvent(ev);
            else AssistantMachine.ApplyEvent(_aiSession, ev, Environment.TickCount64);

            if (_host.Settings.Ai.EnableVerification && ev.Kind == AssistantEventKind.Formula)
                VerifySessionFormula(_aiSession);
            QueueRender();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 事件处理失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    /// <summary>
    /// 两阶段去重：InkOnly 只作首屏预览，WithImage 通常从头重放最终结果。
    /// 检测到首 token 重放时清空草稿，避免两份内容叠加。
    /// </summary>
    private void ApplyTwoStageEvent(AssistantEvent ev)
    {
        if (ev is null || _aiSession is null) return;

        if (ev.Kind == AssistantEventKind.Token)
        {
            bool replay = !_aiStageReset
                && _aiStageFirstToken is not null
                && _aiSession.Text.Length > 0
                && string.Equals(ev.Text, _aiStageFirstToken, StringComparison.Ordinal);

            if (replay)
            {
                _aiSession.Text = "";
                _aiSession.Formula = "";
                _aiSession.Steps.Clear();
                _aiSession.Annotations.Clear();
                _aiSession.TokenCount = 0;
                _aiSession.FirstTokenAtMs = 0;
                _aiSession.LocalHint = "";
                _aiStageReset = true;
            }

            _aiStageFirstToken ??= ev.Text ?? "";
        }

        AssistantMachine.ApplyEvent(_aiSession, ev, Environment.TickCount64);
    }

    /// <summary>
    /// WithImage 阶段启动前，把 InkOnly 阶段产生的草稿暂存并清空会话；
    /// 两段是同一份正文的重放/续写，完成后按语义合并去重。
    /// </summary>
    private void OnAiSecondStageStarted()
    {
        if (_aiSession is null || !_aiTwoStage) return;

        _aiPreStageText = _aiSession.Text;
        _aiPreStageSteps = _aiSession.Steps
            .Select(step => CloneStep(step))
            .ToList();
        _aiPreStageFormula = _aiSession.Formula;
        _aiSecondStageStarted = true;

        _aiSession.Text = "";
        _aiSession.Steps.Clear();
        _aiSession.Formula = "";
        _aiSession.TokenCount = 0;
        _aiSession.State = AssistantState.Streaming;
        _aiAssistant?.Apply(_aiSession, _aiThumb, preserveScroll: true);
    }

    /// <summary>合并两阶段结果：新文本已包含旧文本时取较长的，否则拼接；步骤按内容去重。</summary>
    private void MergeAiPreStage()
    {
        if (!_aiSecondStageStarted || _aiSession is null) return;

        string newText = _aiSession.Text ?? "";
        string preText = _aiPreStageText ?? "";
        if (string.IsNullOrEmpty(newText))
        {
            _aiSession.Text = preText;
        }
        else if (!string.IsNullOrEmpty(preText))
        {
            if (newText.Contains(preText, StringComparison.Ordinal)
                || preText.Contains(newText, StringComparison.Ordinal))
            {
                _aiSession.Text = newText.Length >= preText.Length ? newText : preText;
            }
            else
            {
                _aiSession.Text = preText + newText;
            }
        }

        if (_aiPreStageSteps.Count > 0)
        {
            List<AssistantStep> current = _aiSession.Steps;
            if (current.Count == 0)
            {
                ReplaceSteps(current, _aiPreStageSteps);
            }
            else if (current.Count != _aiPreStageSteps.Count || !StepsEquivalent(current, _aiPreStageSteps))
            {
                var merged = new List<AssistantStep>(current.Count + _aiPreStageSteps.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (AssistantStep step in _aiPreStageSteps)
                {
                    if (seen.Add(StepKey(step))) merged.Add(CloneStep(step));
                }

                foreach (AssistantStep step in current)
                {
                    if (seen.Add(StepKey(step))) merged.Add(CloneStep(step));
                }

                ReplaceSteps(current, merged);
            }
        }

        if (string.IsNullOrEmpty(_aiSession.Formula))
            _aiSession.Formula = _aiPreStageFormula;

        ResetAiPreStage();
    }

    private void ResetAiPreStage()
    {
        _aiPreStageText = "";
        _aiPreStageSteps.Clear();
        _aiPreStageFormula = "";
        _aiSecondStageStarted = false;
    }

    private static AssistantStep CloneStep(AssistantStep step) => new()
    {
        Index = step.Index,
        Text = step.Text ?? "",
        Latex = step.Latex ?? "",
    };

    private static string StepKey(AssistantStep step) =>
        step.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "\u0001" + (step.Text ?? "")
        + "\u0001" + (step.Latex ?? "");

    private static bool StepsEquivalent(List<AssistantStep> left, List<AssistantStep> right)
    {
        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Text ?? "", right[i].Text ?? "", StringComparison.Ordinal)
                || !string.Equals(left[i].Latex ?? "", right[i].Latex ?? "", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ReplaceSteps(List<AssistantStep> target, IReadOnlyList<AssistantStep> source)
    {
        target.Clear();
        foreach (AssistantStep step in source) target.Add(CloneStep(step));
    }

    private static void VerifySessionFormula(AssistantSession session)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.Formula)) return;

        try
        {
            VerificationResult result = EquationVerifier.Verify(session.Formula, session.Confidence);
            session.Verified = result.Status == VerificationStatus.Verified;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 自检失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    private void OnAiError(string message)
    {
        _renderTimer?.Stop();
        if (_aiSession is null) { _aiAssistant?.SetBusy(false); return; }
        ResetAiPreStage();
        AssistantMachine.Fail(_aiSession, message);
        _aiAssistant?.Apply(_aiSession, _aiThumb);
        _aiAssistant?.SetBusy(false);
        _aiStream = null;
    }

    private void OnAiDone()
    {
        _renderTimer?.Stop();
        if (_aiSession is null) { _aiAssistant?.SetBusy(false); return; }
        try
        {
            MergeAiPreStage();
            AssistantMachine.Finish(_aiSession);
            if (_host.Settings.Ai.EnableVerification) VerifySessionFormula(_aiSession);
            _aiSession.LocalHint = "";
            _aiAssistant?.Apply(_aiSession, _aiThumb);
            _aiAssistant?.SetPasted(false);
            _aiAssistant?.SetBusy(false);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 完成处理失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            _aiAssistant?.SetBusy(false);
        }

        _aiStream = null;
        RecordAiExplanation();

        if (!string.IsNullOrEmpty(_reRecognizeRegionId))
        {
            string id = _reRecognizeRegionId;
            _reRecognizeRegionId = null;
            if (_aiProblem is not null)
            {
                ProblemRegion region = _aiProblem.Regions.Find(item => item.Id == id);
                if (region is not null && !string.IsNullOrWhiteSpace(_aiSession?.Text))
                    region.Text = _aiSession.Text.Trim();
                _aiAssistant?.ApplyProblem(_aiProblem);
                if (!_aiProblem.IsEmpty)
                    StartAssistantStream(BuildProblemRequest(), followUp: false, question: "");
            }
        }

        if (_variantPending)
        {
            _variantPending = false;
            var session = _aiSession;
            _ = RunWindowOperationAsync(() => PreviewVariantAsync(session));
        }
    }

    /// <summary>把 AI 讲解按 slideId 归档到课件页索引，翻回该页可回查并导出讲义。</summary>
    private void RecordAiExplanation() => _host.RecordExplanation(_aiSession, _aiSlideId, _aiSlideIndex);

    /// <summary>统一课堂会话落盘：课件、画板、AI、练习、工具结果与导出都关联到同一 ID。</summary>

    private void OnAiStopRequested()
    {
        _aiGate.Invalidate();
        _renderTimer?.Stop();
        _aiStream?.Abort();
        _aiStream = null;
        ResetAiPreStage();
        if (_aiSession is null) return;
        AssistantMachine.Finish(_aiSession);
        _aiAssistant?.Apply(_aiSession, _aiThumb);
        _aiAssistant?.SetBusy(false);
    }

    private void OnAiRetryRequested() => CaptureCanvas();

    /// <summary>
    /// 切换讲题模式只调整展示裁剪，不重新识别或重发请求；已生成内容按新模式重排。
    /// </summary>
    private void OnAiTeachingModeChanged(AssistantTeachingMode mode)
    {
        if (_aiSession is null) return;

        AssistantMachine.SetTeachingMode(_aiSession, mode);
        _host.Settings.Ai.TeachingMode = AssistantTeachingPolicy.Wire(mode);
        _host.SaveSettings();
        _aiAssistant?.Apply(_aiSession, _aiThumb, preserveScroll: true);

        // 若当前会话由「只给提示」裁剪而来（没有步骤/公式），切到需要更多内容的模式时，
        // 复用同一识别结果重新生成，而不是重新截图或重新识别。
        bool lacksDetail = _aiSession.Steps.Count == 0
            && string.IsNullOrEmpty(_aiSession.Formula);
        if (mode != AssistantTeachingMode.Hint && lacksDetail && !string.IsNullOrEmpty(_aiThumb))
        {
            StartAssistantStream(BuildProblemRequest(mode), followUp: false, question: "");
        }
    }

    private void OnAiRevealNext()
    {
        if (_aiSession is null) return;
        if (!AssistantMachine.RevealNextStep(_aiSession)) return;
        _aiAssistant?.Apply(_aiSession, _aiThumb, preserveScroll: true);
    }

    /// <summary>基于当前题生成同知识点变式题；结果先预览编辑，不直接贴入。</summary>
    private void OnAiVariantRequested(bool advanced)
    {
        if (_aiProblem is null || _aiProblem.IsEmpty)
        {
            _aiAssistant?.ShowNotice("先框选一道题，再生成变式题");
            return;
        }

        _variantDifficulty = advanced ? ExerciseDifficulty.Advanced : ExerciseDifficulty.Basic;
        _variantPending = true;

        AssistantRequest request = ExerciseFlowPolicy.BuildRequest(
            _aiProblem,
            _variantDifficulty,
            _aiSession?.SessionId ?? _aiProblem.SessionId,
            AssistantTeachingMode.Full);
        request.SlideContext = BuildSlideContext();

        StartAssistantStream(request, followUp: false, question: "");
    }

    private async Task PreviewVariantAsync(AssistantSession session)
    {
        if (session is null || session.State != AssistantState.Done) return;

        VariantQuestionDraft draft = ExerciseFlowPolicy.Parse(
            session.Text, _variantDifficulty,
            sourceSessionId: session.SessionId,
            slideId: _aiSlideId,
            slideIndex: _aiSlideIndex,
            nowMs: Environment.TickCount64);

        if (!draft.IsValid)
        {
            await _host.NotifyAsync("变式题", "AI 没有返回可用题目，请重试或补充框选。");
            return;
        }

        var textBox = new TextBox
        {
            Text = draft.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            FontSize = 13,
        };
        var answerBox = new TextBox
        {
            Text = draft.Answer,
            Height = 34,
            FontSize = 13,
            PlaceholderText = "参考答案（可留空）",
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "预览并编辑后再贴入新画板页；原题不会被覆盖。",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock { Text = "题目", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(textBox);
        content.Children.Add(new TextBlock { Text = "参考答案", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(answerBox);

        var dialog = new ContentDialog
        {
            Title = (_variantDifficulty == ExerciseDifficulty.Advanced ? "提高变式" : "基础变式") + " · 随堂练习",
            Content = content,
            PrimaryButtonText = "开始练习",
            SecondaryButtonText = "仅贴到新页",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var window = _aiAssistant;
        if (window?.Content is not FrameworkElement root || root.XamlRoot is null) return;
        dialog.XamlRoot = root.XamlRoot;
        ContentDialogResult result = await dialog.ShowAsync();
        if (_disposed || !ReferenceEquals(window, _aiAssistant)) return;
        if (result == ContentDialogResult.None) return;

        draft.Text = textBox.Text?.Trim() ?? "";
        draft.Answer = answerBox.Text?.Trim() ?? "";
        if (!ExerciseFlowPolicy.CanStart(draft))
        {
            await _host.NotifyAsync("随堂练习", "题目内容不能为空。");
            return;
        }

        await _host.StartPracticeAsync(draft, startTimer: result == ContentDialogResult.Primary);
    }

    /// <summary>把画布当前选区截图追加为拼题的又一个区域。</summary>
    private async Task AddAiRegionAsync()
    {
        if (_aiProblem is null || _aiAssistant is null) return;

        var controller = _host.ActiveController;
        IReadOnlyList<InkStroke> selection = controller.Selection.ToArray();
        Rect bounds = controller.SelectionBounds;
        if (selection.Count == 0 || bounds.Width < 16 || bounds.Height < 16)
        {
            _aiAssistant.ShowNotice("先在画布上用选择工具框选题干/图形/条件，再点「追加选区」");
            return;
        }

        var window = _aiAssistant;
        var problem = _aiProblem;
        int capture = _captureGeneration;
        Matrix3x2 matrix = controller.ViewTransform;
        var strokes = BuildAssistantStrokes(selection, matrix);
        Vector2 topLeft = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), matrix);
        Vector2 bottomRight = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), matrix);
        var viewRect = new AssistantRect(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Max(topLeft.X, bottomRight.X),
            Math.Max(topLeft.Y, bottomRight.Y));

        double dpi = _host.DpiScale;
        string thumb = await AssistantCapture.CapturePngBase64Async(
            (int)Math.Round(viewRect.Left * dpi),
            (int)Math.Round(viewRect.Top * dpi),
            (int)Math.Round(viewRect.Width * dpi),
            (int)Math.Round(viewRect.Height * dpi));

        if (_disposed || capture != _captureGeneration || !ReferenceEquals(_aiAssistant, window)
            || !ReferenceEquals(_aiProblem, problem) || !ReferenceEquals(_host.ActiveController, controller)) return;

        ProblemRegion added = ProblemContextPolicy.Add(_aiProblem, new ProblemRegion
        {
            ImageBase64 = thumb,
            Anchor = viewRect,
            Source = "selection",
        });
        if (added is null)
        {
            window.ShowNotice("该选区没有可识别内容");
            return;
        }

        _aiStrokes.AddRange(strokes);
        AssistantRect combined = ProblemContextPolicy.CombinedAnchor(_aiProblem);
        if (!combined.IsEmpty)
        {
            _aiAnchor = combined;
            window.SetAnchor(_aiAnchor);
        }

        window.ApplyProblem(_aiProblem);
        window.ShowNotice("已追加第 " + added.Order + " 个选区，共 " + _aiProblem.Count + " 块");
        StartAssistantStream(BuildProblemRequest(), followUp: false, question: "");
    }

    private void OnAiRemoveRegion(string id)
    {
        if (_aiProblem is null || _aiAssistant is null) return;
        if (!ProblemContextPolicy.Remove(_aiProblem, id)) return;

        _aiAssistant.ApplyProblem(_aiProblem);
        if (_aiProblem.IsEmpty)
        {
            _aiAssistant.ShowNotice("已移除全部选区，可回画布重新框选");
            return;
        }

        StartAssistantStream(BuildProblemRequest(), followUp: false, question: "");
    }

    /// <summary>只重新识别一个选区：识别完成后写回该区文字，再携带完整拼题上下文重新求解。</summary>
    private void OnAiReRecognizeRegion(string id)
    {
        if (_aiProblem is null || _aiAssistant is null) return;

        ProblemRegion region = _aiProblem.Regions.Find(item => item.Id == id);
        if (region is null || string.IsNullOrEmpty(region.ImageBase64))
        {
            _aiAssistant.ShowNotice("该选区没有可重新识别的图片");
            return;
        }

        _reRecognizeRegionId = id;
        var request = new AssistantRequest
        {
            ImageBase64 = region.ImageBase64,
            Images = new[] { region },
            InkStrokes = Array.Empty<AssistantStroke>(),
            Viewport = new AssistantViewport { Scale = 1.0, Rect = region.Anchor },
            Mode = AssistantMode.Auto,
            SessionId = _aiProblem.SessionId,
            Teaching = AssistantTeachingMode.Full,
            Purpose = "region",
            SlideContext = BuildSlideContext(),
        };

        _aiAssistant.ShowNotice("正在重新识别第 " + region.Order + " 个选区…");
        StartAssistantStream(request, followUp: false, question: "");
    }

    private void OnAiMoveRegion(string id, int delta)
    {
        if (_aiProblem is null || _aiAssistant is null) return;
        if (!ProblemContextPolicy.Move(_aiProblem, id, delta)) return;

        _aiAssistant.ApplyProblem(_aiProblem);
        StartAssistantStream(BuildProblemRequest(), followUp: false, question: "");
    }

    private void OnAiCopyRequested()
    {
        if (_aiSession is null) return;
        string text = string.IsNullOrEmpty(_aiSession.Formula) ? _aiSession.Text : _aiSession.Formula;
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"AI 复制失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    private void OnAiPasteRequested() => _ = RunWindowOperationAsync(PasteAssistantAsync);

    private async Task PasteAssistantAsync()
    {
        if (_aiSession is null || _aiAssistant is null) return;
        if (_aiSession.State != AssistantState.Done) return;

        var window = _aiAssistant;
        AssistantSession session = _aiSession;
        List<string> lines = BuildPasteLines(session);
        var (width, height) = PasteCardLayout.Estimate(lines);

        window.SetBusy(true);
        byte[] png = await PasteCardRenderer.RenderPngAsync("AI 助教", lines, width, height);
        if (_disposed || !ReferenceEquals(_aiAssistant, window) || !ReferenceEquals(_aiSession, session)) return;

        if (png is null || png.Length == 0)
        {
            window.SetBusy(false);
            AssistantMachine.Fail(session, "贴回失败");
            window.Apply(session, _aiThumb);
            return;
        }

        var work = ScreenService.WorkArea;
        double dpi = _host.DpiScale;
        var (left, top, w, h) = PasteCardLayout.Place(
            _aiAnchor, width, height, work.Width / dpi, work.Height / dpi);

        string metadata = "";
        if (_host.Settings.Ai.EnableEditablePaste)
        {
            try
            {
                metadata = BuildPasteMetadata(session).Serialize();
            }
            catch (Exception ex)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    $"AI 贴回元数据生成失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            }
        }

        _host.Board.AddImage(new BoardImageAsset
        {
            Name = "AI 助教",
            Data = png,
            ContentType = "image/png",
            Left = (float)left,
            Top = (float)top,
            Width = (float)w,
            Height = (float)h,
            Metadata = metadata,
        });

        if (!_host.BoardOpen) _host.ShowBoard();

        window.SetBusy(false);
        window.SetPasted(true);
    }

    private void OnAiFollowUpRequested(string question)
    {
        if (_aiSession is null) return;

        var request = new AssistantRequest
        {
            ImageBase64 = _aiThumb,
            InkStrokes = Array.Empty<AssistantStroke>(),
            Viewport = new AssistantViewport { Scale = 1.0, Rect = _aiAnchor },
            Mode = AssistantMode.Chat,
            SessionId = _aiSession.SessionId,
        };
        StartAssistantStream(request, followUp: true, question: question);
    }

    private void OnAiWindowClosed(AssistantWindow window)
    {
        if (!ReferenceEquals(_aiAssistant, window)) return;
        _renderTimer?.Stop();
        _captureGeneration++;
        _aiGate.Invalidate();
        _variantPending = false;
        _aiStream?.Abort();
        _aiStream = null;
        _aiRegionStream?.Abort();
        _aiRegionStream = null;
        _regionGeneration++;
        ResetAiPreStage();
        _aiAssistant = null;
        _aiClient?.Dispose();
        _aiClient = null;
        _aiSession = null;
        _aiProblem = null;
        _aiStrokes.Clear();
        _aiThumb = "";
        _reRecognizeRegionId = null;
    }

    private void OnAiRegionSelected(AssistantRect rect)
    {
        AssistantSession receivedSession = _aiSession;
        AssistantWindow receivedWindow = _aiAssistant;
        Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 局部纠错入口：recv rect=({rect.Left:0.0},{rect.Top:0.0})-({rect.Right:0.0},{rect.Bottom:0.0})，"
            + $"EnableLocalCorrection={_host.Settings.Ai.EnableLocalCorrection}，"
            + $"会话={(receivedSession is null ? "null" : receivedSession.State.ToString())}，"
            + $"窗口={(receivedWindow is null ? "null" : "ok")}",
            Core.Diagnostics.LogType.Trace);

        if (!_host.Settings.Ai.EnableLocalCorrection) return;
        if (rect.IsEmpty) return;

        AssistantSession session = _aiSession;
        AssistantWindow window = _aiAssistant;
        if (session is null || window is null) return;
        if (session.State != AssistantState.Done)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 局部纠错入口：会话状态 {session.State} 非 Done，提示后返回",
                Core.Diagnostics.LogType.Trace);
            window.ShowNotice("生成完成后才能局部纠错");
            return;
        }

        bool formulaTarget = !string.IsNullOrWhiteSpace(session.Formula);
        string target = formulaTarget ? session.Formula ?? "" : session.Text ?? "";
        AssistantRect anchor = _aiAnchor;
        var (start, length) = RegionTextMapper.Map(anchor, rect, target);
        AssistantRegion region = new()
        {
            Rect = rect,
            TextStart = start,
            TextLength = length,
            Source = "region",
        };

        _aiRegionStream?.Abort();
        _aiRegionStream = null;
        int generation = ++_regionGeneration;

        try
        {
            _aiClient ??= new AiClient(AiConfig.ReadFromEnvironment());

            var request = new AssistantRequest
            {
                ImageBase64 = _aiThumb,
                InkStrokes = Array.Empty<AssistantStroke>(),
                Viewport = new AssistantViewport { Scale = 1.0, Rect = _aiAnchor },
                Mode = session.Mode,
                SessionId = session.SessionId ?? "",
                Stage = AssistantStage.WithImage,
                Region = region,
            };

            var buffer = new StringBuilder();
            string formula = null;
            bool failure = false;

            window.ShowNotice("局部纠错中…");

            _aiRegionStream = _aiClient.Start(
                request,
                ev => _dispatcher.TryEnqueue(() =>
                {
                    if (generation != _regionGeneration || !ReferenceEquals(_aiAssistant, window)) return;
                    if (ev.Kind == AssistantEventKind.Token)
                    {
                        buffer.Append(ev.Text ?? "");
                    }
                    else if (ev.Kind == AssistantEventKind.Formula && !string.IsNullOrEmpty(ev.Latex))
                    {
                        formula = ev.Latex;
                    }
                }),
                err => _dispatcher.TryEnqueue(() =>
                {
                    failure = true;
                    Core.Diagnostics.FileLog.WriteLogToFile(
                        $"AI 局部纠错失败：{err}", Core.Diagnostics.LogType.Error);
                    if (generation != _regionGeneration || !ReferenceEquals(_aiAssistant, window)) return;
                    window.ShowNotice("局部纠错失败，已保留原结果");
                }),
                () => _dispatcher.TryEnqueue(() =>
                {
                    if (generation != _regionGeneration) return;
                    _aiRegionStream = null;
                    if (failure || !ReferenceEquals(_aiAssistant, window)) return;
                    if (!ReferenceEquals(_aiSession, session)) return;

                    string replacement = string.IsNullOrWhiteSpace(formula) ? buffer.ToString() : formula;
                    if (string.IsNullOrWhiteSpace(replacement))
                    {
                        Core.Diagnostics.FileLog.WriteLogToFile(
                            "AI 局部纠错结果：返回内容为空，保留原结果",
                            Core.Diagnostics.LogType.Trace);
                        window.ShowNotice("局部纠错未返回内容");
                        return;
                    }

                    try
                    {
                        string trimmed = replacement.Trim();
                        LocalCorrectionResult result = LocalCorrectionFlow.Apply(
                            session, anchor, rect, trimmed);
                        AssistantSession updated = result.Session;

                        if (updated is null)
                        {
                            Core.Diagnostics.FileLog.WriteLogToFile(
                                $"AI 局部纠错结果应用失败：流程返回 null（Field={result.Field}，替换长度 {trimmed.Length}）",
                                Core.Diagnostics.LogType.Error);
                            window.ShowNotice("局部纠错结果应用失败");
                            return;
                        }

                        int previousLength = result.Field == nameof(AssistantSession.Formula)
                            ? (session.Formula ?? "").Length
                            : (session.Text ?? "").Length;
                        int updatedLength = result.Field == nameof(AssistantSession.Formula)
                            ? (updated.Formula ?? "").Length
                            : (updated.Text ?? "").Length;

                        updated.Regions.Add(region);
                        _aiSession = updated;
                        window.Apply(updated, _aiThumb, preserveScroll: true);

                        bool appended = result.Field == nameof(AssistantSession.Text)
                            && string.IsNullOrEmpty(session.Text)
                            && !string.IsNullOrEmpty(updated.Text);
                        Core.Diagnostics.FileLog.WriteLogToFile(
                            $"AI 局部纠错结果应用成功：Field={result.Field}，"
                            + $"start={result.Start}，length={result.Length}，长度 {previousLength}->{updatedLength}，"
                            + $"替换长度 {trimmed.Length}，追加={appended}，"
                            + $"region=({region.Rect.Left:0.0},{region.Rect.Top:0.0})-({region.Rect.Right:0.0},{region.Rect.Bottom:0.0})",
                            Core.Diagnostics.LogType.Event);
                        window.ShowNotice(appended ? "已将局部结果追加到正文" : "已应用局部纠错");
                    }
                    catch (Exception ex)
                    {
                        Core.Diagnostics.FileLog.WriteLogToFile(
                            $"AI 局部纠错结果应用失败：{ex.Message}", Core.Diagnostics.LogType.Error);
                        window.ShowNotice("局部纠错结果应用失败");
                    }
                }));
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 局部纠错启动失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            window.ShowNotice("局部纠错失败，已保留原结果");
        }
    }

    private AssistantRect CenterAnchor() => new(8, 8, 24, 24);

    private static List<AssistantStroke> BuildAssistantStrokes(IReadOnlyList<InkStroke> selection, Matrix3x2 matrix)
    {
        var strokes = new List<AssistantStroke>(selection.Count);
        foreach (InkStroke stroke in selection)
        {
            var points = new List<AssistantPoint>(stroke.Points.Count);
            foreach (InkPoint point in stroke.Points)
            {
                Vector2 view = Vector2.Transform(new Vector2(point.X, point.Y), matrix);
                points.Add(new AssistantPoint
                {
                    X = view.X,
                    Y = view.Y,
                    Pressure = point.Pressure,
                    TimeMs = point.TimeMs,
                });
            }

            strokes.Add(new AssistantStroke
            {
                BaseWidth = stroke.Attributes.BaseWidth,
                ColorArgb = stroke.Attributes.EffectiveColor.PackedArgb,
                Points = points,
            });
        }

        return strokes;
    }

    private static List<string> BuildPasteLines(AssistantSession session)
    {
        var lines = new List<string>();
        string text = (session.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0) lines.Add(line);
        }

        int number = 1;
        foreach (AssistantStep step in session.Steps)
        {
            string detail = step.Text ?? "";
            string latex = AssistantText.LatexToPlain(step.Latex ?? "");
            if (!string.IsNullOrEmpty(latex))
                detail = string.IsNullOrEmpty(detail) ? latex : detail + " / " + latex;
            if (string.IsNullOrEmpty(detail)) continue;

            int index = step.Index > 0 ? step.Index : number;
            lines.Add(index + ". " + detail);
            number = index + 1;
        }

        if (!string.IsNullOrEmpty(session.Formula))
            lines.Add(AssistantText.LatexToPlain(session.Formula));

        if (lines.Count == 0) lines.Add("AI 助教暂无返回内容");
        return lines;
    }

    /// <summary>贴回卡片的可编辑源数据：公式优先，其次步骤，再看图形批注。</summary>
    private static AiPasteMetadata BuildPasteMetadata(AssistantSession session)
    {
        var metadata = new AiPasteMetadata
        {
            Kind = DerivePasteKind(session),
            Latex = session.Formula ?? "",
            Text = session.Text ?? "",
            Lines = BuildPasteLines(session),
        };

        foreach (var annotation in session.Annotations ?? new List<IReadOnlyList<IReadOnlyList<double>>>())
        {
            var points = new List<double>();
            foreach (var element in annotation ?? Array.Empty<IReadOnlyList<double>>())
            {
                if (element is null) continue;
                foreach (double value in element) points.Add(value);
            }

            if (points.Count > 0) metadata.Annotations.Add(points);
        }

        return metadata;
    }

    private static AiPasteKind DerivePasteKind(AssistantSession session)
    {
        if (!string.IsNullOrEmpty(session.Formula)) return AiPasteKind.Formula;
        if (session.Steps is { Count: > 0 }) return AiPasteKind.Steps;
        if (session.Annotations is { Count: > 0 }) return AiPasteKind.Shape;
        return AiPasteKind.Steps;
    }

    public async Task OpenRegionAsync(ScreenRect rect)
    {
        if (_disposed) return;
        int capture = ++_captureGeneration;
        string thumb = await AssistantCapture.CapturePngBase64Async(
            rect.X, rect.Y, rect.Width, rect.Height);
        if (_disposed || capture != _captureGeneration) return;
        double dpi = _host.DpiScale > 0 ? _host.DpiScale : 1.0;

        _aiAnchor = new AssistantRect(
            rect.X / dpi, rect.Y / dpi,
            (rect.X + rect.Width) / dpi, (rect.Y + rect.Height) / dpi);
        _aiThumb = thumb;
        _aiStrokes = new List<AssistantStroke>();
        _aiScale = 1.0;
        _aiSlideId = _host.Source.SlideId;
        _aiSlideIndex = _host.Source.SlideIndex;

        var problem = new ProblemContext
        {
            SlideId = _aiSlideId,
            SlideIndex = _aiSlideIndex,
            AnimationStep = _host.Source.AnimationStep,
        };
        ProblemContextPolicy.Add(problem, new ProblemRegion
        {
            ImageBase64 = thumb,
            Anchor = _aiAnchor,
            Source = "focus",
        });
        _aiProblem = problem;

        PrepareAssistantWindow();
        StartAssistantStream(BuildProblemRequest(), followUp: false, question: "");
    }

    public void Review(int slideId, int slideIndex, int animationStep, string text)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return;
        _aiStream?.Abort();
        _aiStream = null;
        _aiRegionStream?.Abort();
        _aiRegionStream = null;
        _regionGeneration++;
        _renderTimer?.Stop();
        _captureGeneration++;
        _variantPending = false;
        _aiGate.Invalidate();
        _aiProblem = null;
        _aiSlideId = slideId;
        _aiSlideIndex = slideIndex;
        _aiThumb = "";

        PrepareAssistantWindow().Activate();

        AssistantSession review = AssistantMachine.CreateSession(
            AssistantMode.Chat, "review-" + slideId + "-" + animationStep);
        review.State = AssistantState.Done;
        review.TeachingMode = AssistantTeachingMode.Full;
        review.RevealedSteps = 0;
        review.Text = text;
        _aiSession = review;

        _aiAssistant?.Apply(review, "");
        _aiAssistant?.SetBusy(false);
        _aiAssistant?.ShowNotice("已调出第 " + slideIndex + " 页保存的 AI 讲解");
    }

    public bool IsOpen => _aiAssistant is not null;
    public void NotifySlideChanged(int slideId, int slideIndex, int animationStep)
    {
        var current = (slideId, slideIndex, animationStep);
        if (current == _lastSlide) return;
        _lastSlide = current;
        string notice = ProblemContextPolicy.DescribeSlideChange(_aiProblem, slideId, slideIndex, animationStep);
        if (notice is not null) _aiAssistant?.ShowNotice(notice);
    }
    public void NotifyBoardChanged()
    {
        if (_aiProblem is { Count: > 0 }) _aiAssistant?.ShowNotice("画板已换页，可重新框选当前页面。");
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _captureGeneration++;
        _aiGate.Invalidate();
        _regionGeneration++;
        _renderTimer?.Stop();
        _aiStream?.Abort();
        _aiRegionStream?.Abort();
        _aiClient?.Dispose();
        _aiAssistant?.Close();
        _aiAssistant = null;
        _aiProblem = null;
        _aiStrokes.Clear();
        _aiThumb = "";
    }

    private void QueueRender()
    {
        if (_renderTimer is null)
        {
            _renderTimer = _dispatcher.CreateTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(33);
            _renderTimer.IsRepeating = false;
            _renderTimer.Tick += (_, _) =>
            {
                if (!_disposed) _aiAssistant?.Apply(_aiSession, _aiThumb, preserveScroll: true);
            };
        }
        if (!_renderTimer.IsRunning) _renderTimer.Start();
    }

    private async Task RunWindowOperationAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile("AI 助教操作失败 " + ex, Core.Diagnostics.LogType.Error);
            if (_disposed) return;
            _aiAssistant?.SetBusy(false);
            _aiAssistant?.ShowNotice("操作未完成，请重试。");
        }
    }
}
