using InkCanvas.Core.Ink;
using InkCanvas.Platform.Interop;
using Microsoft.Graphics.Canvas;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;

namespace InkCanvas.Features.Tools;

/// <summary>Countdown with pause/resume. Ticks arrive on a thread-pool thread; callers marshal.</summary>
public sealed class CountdownTimer : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private DateTime _endTime;
    private TimeSpan _pausedRemaining;
    private bool _running;

    public CountdownTimer()
    {
        _timer = new System.Threading.Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TimeSpan Total { get; private set; }

    public TimeSpan Remaining => _running
        ? Max(TimeSpan.Zero, _endTime - DateTime.UtcNow)
        : _pausedRemaining;

    public bool IsRunning => _running;

    public event Action<TimeSpan> Tick;
    public event Action Finished;

    public void Start(TimeSpan duration)
    {
        Total = duration;
        _pausedRemaining = duration;
        Resume();
    }

    public void Resume()
    {
        if (_running || _pausedRemaining <= TimeSpan.Zero) return;
        _endTime = DateTime.UtcNow + _pausedRemaining;
        _running = true;
        _timer.Change(0, 100);
    }

    public void Pause()
    {
        if (!_running) return;
        _pausedRemaining = Remaining;
        _running = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Stop()
    {
        _running = false;
        _pausedRemaining = TimeSpan.Zero;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnTick()
    {
        if (!_running) return;
        var remaining = Remaining;
        Tick?.Invoke(remaining);
        if (remaining <= TimeSpan.Zero)
        {
            Stop();
            Finished?.Invoke();
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public void Dispose() => _timer.Dispose();
}

/// <summary>Random name/number picker for classroom use.</summary>
public sealed class LotteryService
{
    private readonly HashSet<int> _used = new();
    private Random _random = new();

    public List<string> Names { get; } = new();

    public int PeopleCount { get; set; } = 60;

    public bool NoRepeat { get; set; }

    public IReadOnlyCollection<int> Used => _used;

    public void Reseed(int seed) => _random = new Random(seed);

    public void Reset() => _used.Clear();

    /// <summary>Returns the picked display text, or null when the pool is exhausted.</summary>
    public string Pick()
    {
        int poolSize = Names.Count > 0 ? Names.Count : Math.Max(1, PeopleCount);
        if (NoRepeat && _used.Count >= poolSize) return null;

        int index;
        int guard = 0;
        do
        {
            index = _random.Next(poolSize);
            guard++;
        } while (NoRepeat && _used.Contains(index) && guard < poolSize * 8);

        if (NoRepeat) _used.Add(index);
        return Names.Count > 0 ? Names[index] : (index + 1).ToString();
    }

    public void LoadNames(IEnumerable<string> names)
    {
        Names.Clear();
        foreach (var n in names)
        {
            var trimmed = n?.Trim();
            if (!string.IsNullOrEmpty(trimmed)) Names.Add(trimmed);
        }
        _used.Clear();
    }
}

/// <summary>Screen capture to PNG, used by the screenshot tool and PPT auto-save.</summary>
public static class ScreenshotService
{
    public static (byte[] bgra, int width, int height) Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return (Array.Empty<byte>(), 0, 0);

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr bmp = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        IntPtr old = NativeMethods.SelectObject(memDc, bmp);
        NativeMethods.BitBlt(memDc, 0, 0, width, height, screenDc, x, y, 0x00CC0020 /* SRCCOPY */);

        var bi = new NativeMethods.BITMAPINFO
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFO>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        var pixels = new byte[width * height * 4];
        NativeMethods.GetDIBits(memDc, bmp, 0, (uint)height, pixels, ref bi, 0);

        NativeMethods.SelectObject(memDc, old);
        NativeMethods.DeleteObject(bmp);
        NativeMethods.DeleteDC(memDc);
        NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        return (pixels, width, height);
    }

    public static async Task SavePngAsync(string path, byte[] bgra, int width, int height)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var device = CanvasDevice.GetSharedDevice();
        using var bitmap = CanvasBitmap.CreateFromBytes(device, bgra, width, height,
            DirectXPixelFormat.B8G8R8A8UIntNormalized);
        await bitmap.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }

    public static async Task<string> CaptureToFileAsync(string directory, int x, int y, int width, int height,
        bool dateFolders = false)
    {
        var (pixels, w, h) = Capture(x, y, width, height);
        if (w == 0) return null;
        string dir = dateFolders ? Path.Combine(directory, DateTime.Now.ToString("yyyy-MM-dd")) : directory;
        string path = Path.Combine(dir, $"Screenshot-{DateTime.Now:HHmmss}.png");
        await SavePngAsync(path, pixels, w, h);
        return path;
    }
}

/// <summary>
/// Ink replay ("time machine"): re-reveals committed strokes in drawing order.
/// The UI advances a clock and asks for the visible prefix; nothing here mutates the document.
/// </summary>
public sealed class InkReplaySession
{
    private readonly List<InkStroke> _source;
    private readonly List<float> _cumulative;
    private readonly float _totalMs;

    public InkReplaySession(IEnumerable<InkStroke> strokes, float msPerPoint = 12f)
    {
        _source = strokes.ToList();
        _cumulative = new List<float>();
        float acc = 0;
        foreach (var s in _source)
        {
            acc += Math.Max(1, s.Count) * msPerPoint;
            _cumulative.Add(acc);
        }
        _totalMs = acc;
    }

    public TimeSpan Duration => TimeSpan.FromMilliseconds(_totalMs);

    public bool IsComplete(float elapsedMs) => elapsedMs >= _totalMs;

    /// <summary>Strokes visible at <paramref name="elapsedMs"/>, the last one possibly partial.</summary>
    public List<InkStroke> VisibleAt(float elapsedMs)
    {
        var result = new List<InkStroke>();
        for (int i = 0; i < _source.Count; i++)
        {
            float start = i == 0 ? 0 : _cumulative[i - 1];
            if (elapsedMs < start) break;
            float end = _cumulative[i];
            var src = _source[i];
            if (elapsedMs >= end)
            {
                result.Add(src);
                continue;
            }

            float fraction = (elapsedMs - start) / Math.Max(1f, end - start);
            int take = Math.Max(1, (int)(src.Count * fraction));
            var partial = new InkStroke(src.Attributes.Clone());
            // Preserve stored widths so replay looks exactly like the original drawing.
            partial.RestorePoints(src.Points.Take(take));
            result.Add(partial);
        }
        return result;
    }
}