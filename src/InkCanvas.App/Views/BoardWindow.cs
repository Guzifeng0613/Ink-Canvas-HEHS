using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using InkCanvas.Features.Board;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace InkCanvas.App.Views;

/// <summary>
/// The blackboard / whiteboard window: an opaque WinUI 3 surface with pages, grid paper and
/// its own per-page undo history.
/// </summary>
public sealed class BoardWindow : Window
{
    private readonly BoardService _board;
    private readonly InkSurface _surface = new();
    private readonly CanvasControl _paper;
    private readonly Border _background;
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center };

    public BoardWindow(AppServices services)
    {
        _board = services.Board;
        Title = "Ink Canvas 画板";

        _background = new Border();
        _paper = new CanvasControl { ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0) };
        _paper.Draw += OnDrawPaper;

        var surfaceHost = new Grid();
        surfaceHost.Children.Add(_background);
        surfaceHost.Children.Add(_paper);
        surfaceHost.Children.Add(_surface);

        var toolbar = BuildToolbar();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(surfaceHost, 0);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(surfaceHost);
        root.Children.Add(toolbar);
        Content = root;

        WindowChrome.MakeBorderless(this, topmost: false, resizable: true);
        var screen = Platform.System.ScreenService.Primary;
        WindowChrome.Place(this, screen.X, screen.Y, screen.Width, screen.Height);

        _board.PageChanged += OnPageChanged;
        _board.BackgroundChanged += RefreshBackground;
        Closed += (_, _) =>
        {
            _board.PageChanged -= OnPageChanged;
            _board.BackgroundChanged -= RefreshBackground;
        };

        BindPage();
        RefreshBackground();
        Activated += (_, _) => Motion.PlayEntrance(toolbar, 20f);
    }

    private Border BuildToolbar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        row.Children.Add(Bar(Glyphs.ChevronLeft, "上一页", () => _board.PreviousPage()));
        row.Children.Add(_pageLabel);
        row.Children.Add(Bar(Glyphs.ChevronRight, "下一页", () => _board.NextPage()));
        row.Children.Add(Bar(Glyphs.Add, "插入页", () => _board.InsertPageAfterCurrent()));
        row.Children.Add(Bar(Glyphs.Delete, "删除本页", () => _board.RemoveCurrentPage()));

        row.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(6),
            Background = (Brush)Application.Current.Resources["IcStrokeBrush"],
        });

        foreach (var (bg, label) in new[]
        {
            (BoardBackground.Black, "黑板"),
            (BoardBackground.White, "白板"),
            (BoardBackground.Grid, "方格"),
            (BoardBackground.Lined, "横线"),
        })
        {
            var captured = bg;
            var button = new Button { Content = label, MinHeight = 40, MinWidth = 56 };
            button.Click += (_, _) => _board.SetBackground(captured);
            row.Children.Add(button);
        }

        row.Children.Add(Bar(Glyphs.Undo, "撤销", () => _board.Current.Undo()));
        row.Children.Add(Bar(Glyphs.Redo, "重做", () => _board.Current.Redo()));
        row.Children.Add(Bar(Glyphs.Close, "关闭画板", Close));

        return new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            Child = row,
        };
    }

    private static TouchIconButton Bar(string glyph, string tooltip, Action onClick)
    {
        var b = new TouchIconButton { Glyph = glyph };
        ToolTipService.SetToolTip(b, tooltip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void OnPageChanged() => BindPage();

    private void BindPage()
    {
        _surface.Controller = _board.Current;
        _pageLabel.Text = $"  {_board.CurrentIndex + 1} / {_board.PageCount}  ";
        _paper.Invalidate();
    }

    private void RefreshBackground()
    {
        bool dark = _board.Background is BoardBackground.Black;
        var color = dark
            ? (Windows.UI.Color)Application.Current.Resources["IcBoardBlackColor"]
            : (Windows.UI.Color)Application.Current.Resources["IcBoardWhiteColor"];
        _background.Background = new SolidColorBrush(color);
        // 1:1 GetMainWindowTheme：非屏幕画笔模式下画板明暗跟随黑板/白板选择。未验证。
        var boardTheme = Core.Theming.ThemeResolver.GetBoardTheme(!dark);
        WindowChrome.ApplyTheme(this, boardTheme == Core.Theming.AppTheme.Light ? 1 : 2);
        // On a dark board the default ink must be light to stay visible.
        _board.Current.CurrentAttributes.Color = dark
            ? Core.Ink.InkColor.Opaque(245, 245, 245)
            : Core.Ink.InkColor.Opaque(20, 20, 20);
        _paper.Invalidate();
    }

    private void OnDrawPaper(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_board.Background is not (BoardBackground.Grid or BoardBackground.Lined)) return;

        float spacing = Math.Max(12f, _board.GridSpacing);
        var line = Windows.UI.Color.FromArgb(40, 128, 128, 128);
        float w = (float)sender.ActualWidth, h = (float)sender.ActualHeight;

        for (float y = spacing; y < h; y += spacing)
            args.DrawingSession.DrawLine(new Vector2(0, y), new Vector2(w, y), line, 1f);

        if (_board.Background == BoardBackground.Grid)
            for (float x = spacing; x < w; x += spacing)
                args.DrawingSession.DrawLine(new Vector2(x, 0), new Vector2(x, h), line, 1f);
    }
}