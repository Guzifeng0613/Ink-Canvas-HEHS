using System.Collections.Concurrent;

namespace InkCanvas.Features.Ppt;

/// <summary>Serializes preview COM work on its own STA and retains a bounded set of small original slide images.</summary>
public sealed class PptSlidePreviewSource : IDisposable
{
    private sealed record ImageEntry(byte[] Data, long Used);
    private readonly BlockingCollection<Action<PptInteropService>> _jobs = new();
    private readonly Dictionary<(string Presentation, int Slide), ImageEntry> _images = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("inkcanvas-ppt-preview-").FullName;
    private readonly Thread _thread;
    private volatile bool _disposed;
    private long _clock, _bytes;
    public int ExportCount { get; private set; }

    public PptSlidePreviewSource()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "PPT slide previews" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<IReadOnlyList<PptSlideInfo>> GetSlidesAsync(PptPresentationInfo presentation, CancellationToken token)
        => Queue<IReadOnlyList<PptSlideInfo>>(interop =>
        {
            Attach(interop, presentation);
            return interop.GetPreviewSlides(presentation, token);
        }, token);

    public Task<byte[]> GetImageAsync(PptPresentationInfo presentation, int slideId, CancellationToken token)
        => Queue(interop =>
        {
            var key = (presentation.Identity, slideId);
            if (_images.TryGetValue(key, out var cached))
            {
                _images[key] = cached with { Used = ++_clock };
                return cached.Data;
            }
            Attach(interop, presentation);
            string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                if (!interop.ExportSlidePreview(presentation, slideId, path))
                    throw new InvalidOperationException("此页暂时无法生成预览。");
                token.ThrowIfCancellationRequested();
                byte[] data = File.ReadAllBytes(path);
                ExportCount++;
                _images[key] = new(data, ++_clock);
                _bytes += data.Length;
                while (_images.Count > 32 || _bytes > 16 * 1024 * 1024)
                {
                    var oldest = _images.MinBy(pair => pair.Value.Used);
                    _images.Remove(oldest.Key);
                    _bytes -= oldest.Value.Data.Length;
                }
                return data;
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }, token);

    private static void Attach(PptInteropService interop, PptPresentationInfo presentation)
    {
        if (interop.AttachedProgId != presentation.ApplicationProgId) interop.Detach();
        interop.ProgIds = new[] { presentation.ApplicationProgId };
        if (!interop.TryAttach()) throw new InvalidOperationException("尚未连接到当前课件。");
    }

    private Task<T> Queue<T>(Func<PptInteropService, T> operation, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = token.Register(() => completion.TrySetCanceled(token));
        _jobs.Add(interop =>
        {
            try
            {
                if (completion.Task.IsCompleted) return;
                ObjectDisposedException.ThrowIf(_disposed, this);
                T result = operation(interop);
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally { cancellation.Dispose(); }
        });
        return completion.Task;
    }

    private void Run()
    {
        try
        {
            using var interop = new PptInteropService();
            foreach (var job in _jobs.GetConsumingEnumerable()) job(interop);
        }
        finally
        {
            _images.Clear();
            _jobs.Dispose();
            try { Directory.Delete(_directory, recursive: false); } catch (IOException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _jobs.CompleteAdding();
    }
}
