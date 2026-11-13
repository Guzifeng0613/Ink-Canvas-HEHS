using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Replay;
using InkCanvas.Ink;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>
/// Ink replay ("time machine"). Replays a snapshot of a document on its own surface, so the
/// live document is never mutated and closing the window leaves the ink exactly as it was.
/// </summary>
public sealed class ReplayWindow : Window
{
    private readonly InkReplaySession _session;
    private readonly ReplayClock _clock;
    private readonly InkController _replayController = new();
    private readonly InkSurface _surface = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly TouchIconButton _playPause;
    private readonly Slider _progress;
    private readonly TextBlock _timeLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 96 };
    private DateTime _lastTick;
    private bool _suppressSeek;

    public ReplayWindow(IEnumerable<InkStroke> source, int theme)
    {
        Title = "墨迹回放";
        var strokes = source.ToList();
        _session = new InkReplaySession(strokes);
        _clock = new ReplayClock(_session.Duration);

        _replayController.RecognizeShapes = false;
        _replayController.MultiTouchEnabled = false;
        _replayController.Tool = InkTool.Select;
        _surface.Controller = _replayController;

        _playPause = new TouchIconButton { Glyph = Glyphs.Play };
        ToolTipService.SetToolTip(_playPause, "播放 / 暂停");
        _playPause.Click += (_, _) => { _clock.Toggle(); SyncPlayGlyph(); };

        var restart = new TouchIconButton { Glyph = Glyphs.Restart };
        ToolTipService.SetToolTip(restart, "从头播放");
        restart.Click += (_, _) => { _clock.Seek(0); _clock.Play(); SyncPlayGlyph(); RenderFrame(); };

        _progress = new Slider { Minimum = 0, Maximum = 1000, Width = 320, VerticalAlignment = VerticalAlignment.Center };
        _progress.ValueChanged += (_, e) =>
        {
            if (_suppressSeek) return;
            _clock.Seek((float)(e.NewValue / 1000.0 * _clock.DurationMs));
            RenderFrame();
        };

        var speed = new ComboBox { Width = 96, SelectedIndex = 1, VerticalAlignment = VerticalAlignment.Center };
        foreach (var s in new[] { "0.5x", "1x", "2x", "4x" }) speed.Items.Add(s);
        speed.SelectionChanged += (_, _) => _clock.Speed = speed.SelectedIndex switch
        {
            0 => 0.5f,
            2 => 2f,
            3 => 4f,
            _ => 1f,
        };

        var close = new TouchIconButton { Glyph = Glyphs.Close };
        ToolTipService.SetToolTip(close, "关闭回放");
        close.Click += (_, _) => Close();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bar.Children.Add(_playPause);
        bar.Children.Add(restart);
        bar.Children.Add(_progress);
        bar.Children.Add(_timeLabel);
        bar.Children.Add(speed);
        bar.Children.Add(close);

        var toolbar = new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = bar,
        };

        var host = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 250, 250)) };
        host.Children.Add(_surface);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(host, 0);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(host);
        root.Children.Add(toolbar);
        Content = root;

        WindowChrome.MakeBorderless(this, topmost: false, resizable: true);
        WindowChrome.CenterOnPrimary(this, 900, 640);
        WindowChrome.ApplyTheme(this, theme);

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => OnTick();
        _lastTick = DateTime.UtcNow;
        _timer.Start();

        Closed += (_, _) => { _timer.Stop(); _replayController.Dispose(); };
        Activated += (_, _) => Motion.PlayEntrance(toolbar, 16f);

        RenderFrame();
        if (strokes.Count > 0) { _clock.Play(); SyncPlayGlyph(); }
    }

    private void SyncPlayGlyph() => _playPause.Glyph = _clock.IsPlaying ? Glyphs.Pause : Glyphs.Play;

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        float delta = (float)(now - _lastTick).TotalMilliseconds;
        _lastTick = now;
        if (!_clock.IsPlaying) return;
        _clock.Advance(delta);
        RenderFrame();
        SyncPlayGlyph();
    }

    private void RenderFrame()
    {
        var visible = _session.VisibleAt(_clock.ElapsedMs);
        // Replace the replay document wholesale; no history is recorded for playback.
        _replayController.Document.Clear();
        _replayController.Document.AddRange(visible);

        _suppressSeek = true;
        _progress.Value = _clock.Progress * 1000;
        _suppressSeek = false;

        _timeLabel.Text = $"{TimeSpan.FromMilliseconds(_clock.ElapsedMs):mm\\:ss} / {_session.Duration:mm\\:ss}";
    }
}