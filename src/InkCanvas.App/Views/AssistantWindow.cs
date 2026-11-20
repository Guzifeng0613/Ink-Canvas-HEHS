using InkCanvas.App.Services;
using InkCanvas.Core.AiAssistant;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.UI.Text;

namespace InkCanvas.App.Views;

/// <summary>
/// AI 助教浮窗：纯 code-behind，无 XAML。正文按 <see cref="AssistantText"/> 的解析结果
/// 渲染 Markdown/公式，底部提供停止、重试、复制、贴回与追问。窗口不持有流，只把点击
/// 转成事件，由 ShellController 统一管理会话与取消。
/// </summary>
public sealed class AssistantWindow : Window
{
    public const double CardWidth = 380;
    public const double MaxHeightRatio = 0.62;

    private const double MaxCardHeight = 460;
    private const double TitleHeight = 40;
    private const double MonoFontSize = 12;
    private const double BodyFontSize = 13.5;
    private const double BodyLineHeight = 21;
    private const double MaxThumbnailHeight = 96;
    private const double MinRegionSize = 6;
    private const double DragThreshold = 4;
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(1.6);

    private readonly Grid _titleBar;
    private readonly Ellipse _statusDot;
    private readonly TextBlock _statusText;
    private readonly TextBlock _titleText;
    private readonly Border _modeCapsule;
    private readonly TextBlock _modeText;
    private readonly ProgressRing _progressRing;
    private readonly Border _thumbnailHost;
    private readonly Image _thumbnail;
    private readonly Canvas _selectionOverlay;
    private readonly Rectangle _selectionRect;
    private readonly Border _hintRow;
    private readonly TextBlock _hintText;
    private readonly Border _statusBadge;
    private readonly TextBlock _statusBadgeText;
    private readonly Border _formulaBadge;
    private readonly TextBlock _formulaBadgeText;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body;
    private readonly StackPanel _content;
    private readonly Border _formulaCard;
    private readonly TextBlock _formulaText;
    private readonly Grid _actions;
    private readonly Button _stop;
    private readonly Button _retry;
    private readonly Button _copy;
    private readonly Button _paste;
    private readonly Button _close;
    private readonly TextBlock _cursor;
    private readonly DispatcherTimer _cursorTimer;
    private readonly TextBox _followUp;
    private readonly Button _send;
    private readonly Border _root;

    private readonly Brush _rootBgBrush;
    private readonly Brush _highlightBrush;
    private readonly Brush _dividerBrush;
    private readonly Brush _textPrimary;
    private readonly Brush _textSecondary;
    private readonly Brush _accentBrush;
    private readonly Brush _accentSoftBrush;
    private readonly Brush _accentSolidBrush;
    private readonly Brush _pillBrush;
    private readonly Brush _codeBgBrush;
    private readonly Brush _controlStrokeBrush;
    private readonly Brush _inputBgBrush;

    private static readonly Brush OkBrush = Solid(255, 16, 124, 16);
    private static readonly Brush ErrorBrush = Solid(255, 232, 17, 35);
    private static readonly Brush WhiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
    private static readonly Brush ThumbnailBgBrush = Solid(0x33, 0, 0, 0);

    private InMemoryRandomAccessStream _thumbStream;
    private string _thumbBase64 = "";
    private AssistantSession _lastSession;
    private AssistantRect _anchor;
    private Windows.Foundation.Point _thumbStart;
    private bool _thumbTracking;
    private bool _thumbDragging;
    private readonly DispatcherTimer _highlightTimer;
    private readonly DispatcherTimer _noticeTimer;
    private bool _busy;
    private bool _pasted;
    private bool _hasCopyable;

    public new event Action Closed;

    public event Action StopRequested;

    public event Action RetryRequested;

    public event Action PasteRequested;

    public event Action CopyRequested;

    public event Action<string> FollowUpRequested;

    /// <summary>缩略图上框选完成；矩形为原选区锚点的屏幕 DIP 坐标。</summary>
    public event Action<AssistantRect> RegionSelected;

    /// <summary>关闭“局部纠错”开关时禁用缩略图圈选交互。</summary>
    public bool RegionSelectionEnabled { get; set; } = true;

    public AssistantWindow(int theme)
    {
        Title = "AI 助教";

        bool light = theme == 1 || (theme != 2 && Application.Current.RequestedTheme == ApplicationTheme.Light);
        if (light)
        {
            _rootBgBrush = Solid(0xF2, 0xFF, 0xFF, 0xFF);
            _highlightBrush = Solid(0xCC, 0xFF, 0xFF, 0xFF);
            _dividerBrush = Solid(0x14, 0x00, 0x00, 0x00);
            _textPrimary = Solid(0xE4, 0x00, 0x00, 0x00);
            _textSecondary = Solid(0x9E, 0x00, 0x00, 0x00);
            _accentBrush = Solid(0xFF, 0x00, 0x67, 0xC0);
            _accentSoftBrush = Solid(0x1A, 0x00, 0x67, 0xC0);
            _codeBgBrush = Solid(0x0F, 0x00, 0x00, 0x00);
            _controlStrokeBrush = Solid(0x22, 0x00, 0x00, 0x00);
            _inputBgBrush = Solid(0x0F, 0x00, 0x00, 0x00);
        }
        else
        {
            _rootBgBrush = Solid(0xE6, 0x20, 0x21, 0x24);
            _highlightBrush = Solid(0x22, 0xFF, 0xFF, 0xFF);
            _dividerBrush = Solid(0x1A, 0xFF, 0xFF, 0xFF);
            _textPrimary = Solid(0xF2, 0xFF, 0xFF, 0xFF);
            _textSecondary = Solid(0xA8, 0xFF, 0xFF, 0xFF);
            _accentBrush = Solid(0xFF, 0x4C, 0xC2, 0xFF);
            _accentSoftBrush = Solid(0x26, 0x4C, 0xC2, 0xFF);
            _codeBgBrush = Solid(0x22, 0xFF, 0xFF, 0xFF);
            _controlStrokeBrush = Solid(0x33, 0xFF, 0xFF, 0xFF);
            _inputBgBrush = Solid(0x14, 0xFF, 0xFF, 0xFF);
        }

        _pillBrush = _accentSoftBrush;
        _accentSolidBrush = GetSystemAccent() ?? _accentBrush;

        // ---- header ------------------------------------------------------
        _statusDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = _textSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _titleText = new TextBlock
        {
            Text = "AI 助教",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _modeText = new TextBlock
        {
            Text = "自动",
            FontSize = 11,
            Foreground = _accentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _modeCapsule = new Border
        {
            Background = Solid(0x0F, 0x00, 0x67, 0xC0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _modeText,
        };

        var headerLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        headerLeft.Children.Add(_statusDot);
        headerLeft.Children.Add(_titleText);
        headerLeft.Children.Add(_modeCapsule);

        var closeButton = new Button
        {
            Content = new TextBlock { Text = "✕", FontSize = 12, Foreground = _textPrimary },
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        closeButton.Click += (_, _) => Close();

        _titleBar = new Grid { Height = TitleHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerLeft, 0);
        Grid.SetColumn(closeButton, 2);
        _titleBar.Children.Add(headerLeft);
        _titleBar.Children.Add(closeButton);

        var divider = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = _dividerBrush,
        };
        Grid.SetColumnSpan(divider, 3);
        _titleBar.Children.Add(divider);

        // ---- status row --------------------------------------------------
        _progressRing = new ProgressRing
        {
            Width = 16,
            Height = 16,
            IsActive = false,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = _textSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "就绪",
        };
        _statusBadge = CreateBadge(out _statusBadgeText);
        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(12, 8, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusRow.Children.Add(_progressRing);
        statusRow.Children.Add(_statusText);
        statusRow.Children.Add(_statusBadge);

        // ---- thumbnail ---------------------------------------------------
        _thumbnail = new Image
        {
            Stretch = Stretch.Fill,
            MaxHeight = MaxThumbnailHeight,
        };
        _selectionRect = new Rectangle
        {
            Stroke = _accentBrush,
            StrokeThickness = 1.5,
            Fill = Solid(0x22, 0x00, 0x67, 0xC0),
            RadiusX = 3,
            RadiusY = 3,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _selectionOverlay = new Canvas
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _selectionOverlay.Children.Add(_selectionRect);
        _selectionOverlay.PointerPressed += OnThumbnailPointerPressed;
        _selectionOverlay.PointerMoved += OnThumbnailPointerMoved;
        _selectionOverlay.PointerReleased += OnThumbnailPointerReleased;
        _selectionOverlay.PointerCanceled += OnThumbnailPointerCanceled;
        _selectionOverlay.PointerCaptureLost += OnThumbnailPointerCanceled;

        var thumbGrid = new Grid();
        thumbGrid.Children.Add(_thumbnail);
        thumbGrid.Children.Add(_selectionOverlay);
        _thumbnailHost = new Border
        {
            Background = ThumbnailBgBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = thumbGrid,
        };

        _highlightTimer = new DispatcherTimer { Interval = HighlightDuration };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            _selectionRect.Visibility = Visibility.Collapsed;
        };
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            if (_lastSession is not null) UpdateStatus(_lastSession);
        };

        // ---- local hint ---------------------------------------------------
        _hintText = new TextBlock
        {
            Text = "",
            FontSize = 11.5,
            Foreground = _textSecondary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hintRow = new Border
        {
            Background = _inputBgBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = Visibility.Collapsed,
            Child = _hintText,
        };

        // ---- body --------------------------------------------------------
        _content = new StackPanel { Spacing = 6 };
        _formulaText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = _textPrimary,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var formulaLabel = new TextBlock
        {
            Text = "LaTeX",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = _accentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _formulaBadge = CreateBadge(out _formulaBadgeText);
        _formulaBadge.HorizontalAlignment = HorizontalAlignment.Right;
        var formulaHeader = new Grid();
        formulaHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        formulaHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(formulaLabel, 0);
        Grid.SetColumn(_formulaBadge, 1);
        formulaHeader.Children.Add(formulaLabel);
        formulaHeader.Children.Add(_formulaBadge);

        var formulaStack = new StackPanel { Spacing = 4 };
        formulaStack.Children.Add(formulaHeader);
        formulaStack.Children.Add(_formulaText);
        _formulaCard = new Border
        {
            Background = _accentSoftBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 0),
            Child = formulaStack,
            Visibility = Visibility.Collapsed,
        };

        _cursor = new TextBlock
        {
            Text = "▍",
            FontSize = 14,
            Foreground = _accentBrush,
            Margin = new Thickness(2, -2, 0, 0),
        };
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _cursorTimer.Tick += (_, _) => _cursor.Opacity = _cursor.Opacity < 0.5 ? 1 : 0.15;

        _body = new StackPanel { Spacing = 10 };
        _body.Children.Add(_thumbnailHost);
        _body.Children.Add(_hintRow);
        _body.Children.Add(_content);
        _body.Children.Add(_formulaCard);

        _scroll = new ScrollViewer
        {
            Content = _body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 8, 12, 8),
        };

        // ---- footer actions ---------------------------------------------
        _paste = CreatePrimaryButton("贴回白板");
        _stop = CreateSecondaryButton("停止生成");
        _copy = CreateSecondaryButton("复制");
        _retry = CreateSecondaryButton("重试");
        _close = new Button
        {
            Content = new TextBlock { Text = "✕", FontSize = 12, Foreground = _textPrimary },
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        _stop.Visibility = Visibility.Collapsed;
        _retry.Visibility = Visibility.Collapsed;
        _paste.Visibility = Visibility.Collapsed;
        _copy.IsEnabled = false;

        _stop.Click += (_, _) => StopRequested?.Invoke();
        _retry.Click += (_, _) => RetryRequested?.Invoke();
        _copy.Click += (_, _) => CopyRequested?.Invoke();
        _paste.Click += (_, _) => PasteRequested?.Invoke();
        _close.Click += (_, _) => Close();

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actionRow.Children.Add(_paste);
        actionRow.Children.Add(_stop);
        actionRow.Children.Add(_copy);
        actionRow.Children.Add(_retry);

        _actions = new Grid { VerticalAlignment = VerticalAlignment.Center };
        _actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(actionRow, 0);
        Grid.SetColumn(_close, 1);
        _actions.Children.Add(actionRow);
        _actions.Children.Add(_close);

        _followUp = new TextBox
        {
            PlaceholderText = "继续追问…",
            Height = 36,
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Background = _inputBgBrush,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _send = CreatePrimaryButton("发送");
        _send.Height = 36;
        _send.Click += (_, _) => SendFollowUp();
        _followUp.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) SendFollowUp();
        };

        var followRow = new Grid { ColumnSpacing = 8 };
        followRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        followRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_followUp, 0);
        Grid.SetColumn(_send, 1);
        followRow.Children.Add(_followUp);
        followRow.Children.Add(_send);

        var footer = new StackPanel { Spacing = 8, Padding = new Thickness(12, 8, 12, 12) };
        footer.Children.Add(_actions);
        footer.Children.Add(followRow);

        // ---- root --------------------------------------------------------
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleHeight) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_titleBar, 0);
        Grid.SetRow(statusRow, 1);
        Grid.SetRow(_scroll, 2);
        Grid.SetRow(footer, 3);
        rootGrid.Children.Add(_titleBar);
        rootGrid.Children.Add(statusRow);
        rootGrid.Children.Add(_scroll);
        rootGrid.Children.Add(footer);

        _root = new Border
        {
            Background = _rootBgBrush,
            BorderBrush = _highlightBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = rootGrid,
        };
        Content = _root;

        SystemBackdrop = new DesktopAcrylicBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_titleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        WindowChrome.ApplyTheme(this, theme);
        ApplyRoundedCorners();

        base.Closed += (_, _) =>
        {
            _cursorTimer.Stop();
            _highlightTimer.Stop();
            _noticeTimer.Stop();
            _thumbStream?.Dispose();
            _thumbStream = null;
            Closed?.Invoke();
        };
    }

    /// <summary>按 DIP 定位并显示；高度取工作区高的 <see cref="MaxHeightRatio"/>（上限 460）。</summary>
    public void ShowAt(AssistantPlacement placement, double dpiScale)
    {
        double dpi = dpiScale > 0 ? dpiScale : 1.0;
        var work = InkCanvas.Platform.System.ScreenService.WorkArea;
        double workHeightDip = work.Height / dpi;
        double cardHeight = Math.Min(MaxCardHeight, workHeightDip * MaxHeightRatio);
        cardHeight = Math.Max(200, cardHeight);

        _scroll.MaxHeight = Math.Max(120, cardHeight - TitleHeight - 104);

        AppWindow.MoveAndResize(new RectInt32(
            (int)Math.Round(placement.X * dpi),
            (int)Math.Round(placement.Y * dpi),
            (int)Math.Round(CardWidth * dpi),
            (int)Math.Round(cardHeight * dpi)));
        Activate();
        ApplyRoundedCorners();
    }

    public void Apply(AssistantSession session, string thumbnailBase64, bool preserveScroll = false)
    {
        if (session is null) return;
        _lastSession = session;

        double offset = preserveScroll ? _scroll.VerticalOffset : 0;

        UpdateStatus(session);
        UpdateVerification(session);
        UpdateHint(session);
        _modeText.Text = ModeLabel(session.Mode);

        if (!string.IsNullOrEmpty(thumbnailBase64) && thumbnailBase64 != _thumbBase64)
            _ = LoadThumbnailAsync(thumbnailBase64);

        UpdateThumbnailSize();

        _content.Children.Clear();
        _hasCopyable = !string.IsNullOrEmpty(session.Text) || !string.IsNullOrEmpty(session.Formula);

        foreach (MarkdownBlock block in AssistantText.ParseMarkdown(session.Text ?? ""))
            AddBlock(block);

        AddSteps(session);

        bool active = IsActive(session.State);
        if (active)
        {
            _cursor.Opacity = 1;
            _content.Children.Add(_cursor);
            if (!_cursorTimer.IsEnabled) _cursorTimer.Start();
        }
        else
        {
            _cursorTimer.Stop();
        }

        if (session.State == AssistantState.Error)
            AddError(session);

        if (!string.IsNullOrEmpty(session.Formula))
        {
            _formulaText.Text = AssistantText.LatexToPlain(session.Formula);
            _formulaCard.Visibility = Visibility.Visible;
        }
        else
        {
            _formulaText.Text = "";
            _formulaCard.Visibility = Visibility.Collapsed;
        }

        UpdateActionState();

        if (preserveScroll && offset > 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _scroll.ChangeView(null, offset, null, true);
                }
                catch (Exception)
                {
                    // 滚动位置恢复失败不影响内容更新。
                }
            });
        }
    }

    /// <summary>设置缩略图对应的原选区锚点（屏幕 DIP），用于把框选换算回选区坐标。</summary>
    public void SetAnchor(AssistantRect anchor)
    {
        _anchor = anchor;
        UpdateThumbnailSize();
    }

    /// <summary>状态行临时提示（例如局部纠错失败），不改变会话状态。</summary>
    public void ShowNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        _noticeTimer.Stop();
        _statusText.Text = message;
        _statusText.Foreground = _accentBrush;
        _noticeTimer.Start();
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateActionState();
    }

    public void SetPasted(bool pasted)
    {
        _pasted = pasted;
        UpdateActionState();
    }

    private static string ModeLabel(AssistantMode mode) => mode switch
    {
        AssistantMode.Formula => "公式",
        AssistantMode.Solve => "求解",
        AssistantMode.Shape => "图形",
        AssistantMode.Chat => "问答",
        _ => "自动",
    };

    private Border CreateBadge(out TextBlock text)
    {
        text = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 1, 6, 1),
            Background = _inputBgBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = text,
        };
    }

    private void UpdateVerification(AssistantSession session)
    {
        string label;
        Brush background;
        Brush foreground;

        if (session.Confidence >= 0 && session.Confidence < 0.5)
        {
            label = "低置信";
            background = Solid(0x24, 0xB8, 0x86, 0x0B);
            foreground = Solid(0xFF, 0xB8, 0x86, 0x0B);
        }
        else if (session.Verified == true)
        {
            label = "已验证";
            background = Solid(0x22, 0x10, 0x7C, 0x10);
            foreground = OkBrush;
        }
        else if (session.Verified == false)
        {
            label = "未验证";
            background = _inputBgBrush;
            foreground = _textSecondary;
        }
        else
        {
            SetBadge(_statusBadge, _statusBadgeText, null, null, null);
            SetBadge(_formulaBadge, _formulaBadgeText, null, null, null);
            return;
        }

        SetBadge(_statusBadge, _statusBadgeText, label, background, foreground);
        SetBadge(_formulaBadge, _formulaBadgeText, label, background, foreground);
    }

    private static void SetBadge(Border badge, TextBlock text, string label, Brush background, Brush foreground)
    {
        if (label is null)
        {
            badge.Visibility = Visibility.Collapsed;
            return;
        }

        text.Text = label;
        text.Foreground = foreground;
        badge.Background = background;
        badge.Visibility = Visibility.Visible;
    }

    private void UpdateHint(AssistantSession session)
    {
        string hint = session.LocalHint ?? "";
        bool show = hint.Length > 0 && IsActive(session.State);
        _hintText.Text = show ? "本地提示：" + hint : "";
        _hintRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateThumbnailSize()
    {
        if (_anchor.IsEmpty)
        {
            _thumbnailHost.Height = MaxThumbnailHeight;
            return;
        }

        double available = _scroll.ActualWidth > 0
            ? Math.Max(80, _scroll.ActualWidth - 24)
            : CardWidth - 48;
        double ratio = _anchor.Height / Math.Max(1.0, _anchor.Width);
        _thumbnailHost.Height = Math.Clamp(available * ratio, 24, MaxThumbnailHeight);
    }

    private Button CreatePrimaryButton(string text) => new()
    {
        Content = text,
        Height = 32,
        MinHeight = 32,
        Padding = new Thickness(14, 0, 14, 0),
        FontSize = 13,
        CornerRadius = new CornerRadius(6),
        Background = _accentSolidBrush,
        Foreground = WhiteBrush,
        BorderThickness = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private Button CreateSecondaryButton(string text) => new()
    {
        Content = text,
        Height = 32,
        MinHeight = 32,
        Padding = new Thickness(12, 0, 12, 0),
        FontSize = 13,
        CornerRadius = new CornerRadius(6),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        Foreground = _textPrimary,
        BorderBrush = _controlStrokeBrush,
        BorderThickness = new Thickness(1),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private void SendFollowUp()
    {
        if (_busy) return;
        string text = _followUp.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _followUp.Text = "";
        FollowUpRequested?.Invoke(text);
    }

    private void UpdateStatus(AssistantSession session)
    {
        string text;
        Brush dot;
        Brush textForeground = _textSecondary;
        bool ring = false;
        switch (session.State)
        {
            case AssistantState.Capturing:
            case AssistantState.Recognizing:
                text = "识别中";
                dot = _accentBrush;
                ring = true;
                break;
            case AssistantState.Streaming:
                text = "生成中";
                dot = _accentBrush;
                ring = true;
                break;
            case AssistantState.Done:
                text = "已完成";
                dot = OkBrush;
                break;
            case AssistantState.Error:
                text = "出错";
                dot = ErrorBrush;
                textForeground = ErrorBrush;
                break;
            default:
                text = "就绪";
                dot = _textSecondary;
                break;
        }

        _statusText.Text = text;
        _statusText.Foreground = textForeground;
        _statusDot.Fill = dot;
        _progressRing.IsActive = ring;
        _progressRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActionState()
    {
        bool active = _lastSession is not null && IsActive(_lastSession.State);
        bool error = _lastSession?.State == AssistantState.Error;
        bool done = _lastSession?.State == AssistantState.Done;

        _stop.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        _paste.Visibility = done ? Visibility.Visible : Visibility.Collapsed;

        _stop.IsEnabled = active;
        _retry.IsEnabled = !_busy && error;
        _copy.IsEnabled = !_busy && _hasCopyable;
        _paste.IsEnabled = !_busy && done && !_pasted;
        _paste.Content = _pasted ? "已贴回" : "贴回白板";
        _send.IsEnabled = !_busy;
        _followUp.IsEnabled = !_busy;
        _close.IsEnabled = true;
    }

    private static bool IsActive(AssistantState state)
        => state is AssistantState.Capturing or AssistantState.Recognizing or AssistantState.Streaming;

    private void AddBlock(MarkdownBlock block)
    {
        switch (block.Kind)
        {
            case MarkdownBlockKind.Heading:
            {
                var text = CreateRichText(block.Text, 15, FontWeights.SemiBold, _textPrimary);
                text.LineHeight = BodyLineHeight;
                text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                text.Margin = new Thickness(0, block.Level == 1 ? 8 : 5, 0, 0);
                _content.Children.Add(text);
                break;
            }

            case MarkdownBlockKind.ListItem:
            {
                var text = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = BodyFontSize,
                    LineHeight = BodyLineHeight,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    Foreground = _textPrimary,
                    Margin = new Thickness(6, 0, 0, 0),
                };
                text.Inlines.Add(new Run { Text = "• ", Foreground = _accentBrush, FontWeight = FontWeights.SemiBold });
                AppendSegments(text, block.Text);
                _content.Children.Add(text);
                break;
            }

            case MarkdownBlockKind.Code:
            {
                _content.Children.Add(new Border
                {
                    Background = _codeBgBrush,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8),
                    Child = new TextBlock
                    {
                        Text = block.Text ?? "",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = MonoFontSize,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = _textPrimary,
                        IsTextSelectionEnabled = true,
                    },
                });
                break;
            }

            case MarkdownBlockKind.Table:
                _content.Children.Add(CreateTable(block));
                break;

            default:
                _content.Children.Add(CreateRichText(block.Text, BodyFontSize, FontWeights.Normal, _textPrimary));
                break;
        }
    }

    private TextBlock CreateRichText(string text, double fontSize, FontWeight weight, Brush foreground)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            LineHeight = BodyLineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            FontWeight = weight,
            Foreground = foreground,
            IsTextSelectionEnabled = true,
        };
        AppendSegments(block, text);
        return block;
    }

    private void AppendSegments(TextBlock target, string text)
    {
        foreach (FormulaSegment segment in AssistantText.SplitFormulas(text ?? ""))
        {
            if (segment.IsFormula)
            {
                target.Inlines.Add(new InlineUIContainer { Child = CreateFormulaPill(segment) });
            }
            else
            {
                target.Inlines.Add(new Run { Text = segment.Text });
            }
        }
    }

    private FrameworkElement CreateFormulaPill(FormulaSegment segment)
    {
        var label = new TextBlock
        {
            Text = segment.Text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = MonoFontSize + 1,
            Foreground = _accentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var pill = new Border
        {
            Background = _pillBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2, 1, 2, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = label,
        };

        if (segment.Complete) return pill;

        var dashed = new Rectangle
        {
            Stroke = _accentBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            RadiusX = 4,
            RadiusY = 4,
        };
        var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(dashed);
        host.Children.Add(pill);
        return host;
    }

    private FrameworkElement CreateTable(MarkdownBlock block)
    {
        var rows = block.Rows ?? Array.Empty<IReadOnlyList<string>>();
        int columns = 1;
        foreach (var row in rows) columns = Math.Max(columns, row?.Count ?? 0);

        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int c = 0; c < columns; c++)
            {
                string value = c < (row?.Count ?? 0) ? row[c] : "";
                var cell = new Border
                {
                    BorderBrush = _controlStrokeBrush,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 3, 6, 3),
                    Child = new TextBlock
                    {
                        Text = value ?? "",
                        FontSize = 12,
                        FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = _textPrimary,
                        TextWrapping = TextWrapping.Wrap,
                    },
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private void AddSteps(AssistantSession session)
    {
        if (session.Steps is null || session.Steps.Count == 0) return;

        for (int i = 0; i < session.Steps.Count; i++)
        {
            AssistantStep step = session.Steps[i];
            string latex = AssistantText.LatexToPlain(step.Latex ?? "");
            string line = step.Text ?? "";
            if (string.IsNullOrEmpty(line) && string.IsNullOrEmpty(latex)) continue;

            int number = step.Index > 0 ? step.Index : i + 1;

            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = _accentSoftBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0),
                Child = new TextBlock
                {
                    Text = number.ToString(),
                    FontSize = 10,
                    Foreground = _accentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var right = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = BodyFontSize,
                LineHeight = BodyLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = _textSecondary,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AppendSegments(right, line);
            if (!string.IsNullOrEmpty(latex))
            {
                if (!string.IsNullOrEmpty(line)) right.Inlines.Add(new Run { Text = " " });
                right.Inlines.Add(new InlineUIContainer
                {
                    Child = CreateFormulaPill(new FormulaSegment
                    {
                        IsFormula = true,
                        Complete = true,
                        Text = latex,
                        Raw = step.Latex ?? "",
                    }),
                });
            }

            var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(right, 1);
            row.Children.Add(badge);
            row.Children.Add(right);
            _content.Children.Add(row);
        }
    }

    private void AddError(AssistantSession session)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = session.Error ?? "",
            TextWrapping = TextWrapping.Wrap,
            FontSize = BodyFontSize,
            Foreground = ErrorBrush,
            IsTextSelectionEnabled = true,
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var retry = CreateSecondaryButton("重试");
        retry.Click += (_, _) => RetryRequested?.Invoke();
        var rephrase = CreateSecondaryButton("换个问法");
        rephrase.Click += (_, _) =>
        {
            _followUp.PlaceholderText = "换个问法：例如再讲详细一点";
            _followUp.Focus(FocusState.Programmatic);
        };
        row.Children.Add(retry);
        row.Children.Add(rephrase);
        panel.Children.Add(row);
        _content.Children.Add(panel);
    }

    private void OnThumbnailPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 圈选按下：enabled={RegionSelectionEnabled} anchorEmpty={_anchor.IsEmpty}",
            InkCanvas.Core.Diagnostics.LogType.Trace);
        if (!RegionSelectionEnabled || _anchor.IsEmpty) return;

        _thumbStart = e.GetCurrentPoint(_selectionOverlay).Position;
        _thumbTracking = true;
        _thumbDragging = false;
        _selectionOverlay.CapturePointer(e.Pointer);
        _highlightTimer.Stop();
        _selectionRect.Visibility = Visibility.Collapsed;
    }

    private void OnThumbnailPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_thumbTracking) return;

        Windows.Foundation.Point current = e.GetCurrentPoint(_selectionOverlay).Position;
        double dx = current.X - _thumbStart.X;
        double dy = current.Y - _thumbStart.Y;
        if (!_thumbDragging && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;

        if (!_thumbDragging)
        {
            _thumbDragging = true;
            _selectionOverlay.CapturePointer(e.Pointer);
        }

        // 只有真正开始画框才吞掉指针事件，避免轻触时阻塞 ScrollViewer 滚动。
        e.Handled = true;
        UpdateSelectionVisual(_thumbStart, current);
    }

    private void OnThumbnailPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_thumbTracking) return;

        _thumbTracking = false;
        if (!_thumbDragging)
        {
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        _thumbDragging = false;
        Windows.Foundation.Point current = e.GetCurrentPoint(_selectionOverlay).Position;
        try
        {
            _selectionOverlay.ReleasePointerCapture(e.Pointer);
        }
        catch (Exception)
        {
            // 指针可能已被系统释放。
        }

        (double left, double top, double right, double bottom) = Normalize(_thumbStart, current);
        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 缩略图圈选松手：rect=({left:0.0},{top:0.0})-({right:0.0},{bottom:0.0})，"
            + $"anchor=({_anchor.Left:0.0},{_anchor.Top:0.0})-({_anchor.Right:0.0},{_anchor.Bottom:0.0})，"
            + $"RegionSelectionEnabled={RegionSelectionEnabled}",
            InkCanvas.Core.Diagnostics.LogType.Trace);
        if (right - left < MinRegionSize || bottom - top < MinRegionSize)
        {
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateSelectionVisual(_thumbStart, current);
        AssistantRect region = MapToAnchor(left, top, right, bottom);
        if (region.IsEmpty)
        {
            InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 缩略图圈选：映射后区域为空（缩略图 {_selectionOverlay.ActualWidth:0.0}x{_selectionOverlay.ActualHeight:0.0}），忽略",
                InkCanvas.Core.Diagnostics.LogType.Trace);
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 缩略图圈选：发送 RegionSelected，region=({region.Left:0.0},{region.Top:0.0})-({region.Right:0.0},{region.Bottom:0.0})",
            InkCanvas.Core.Diagnostics.LogType.Event);
        _highlightTimer.Start();
        RegionSelected?.Invoke(region);
    }

    private void OnThumbnailPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 缩略图圈选取消：tracking={_thumbTracking} dragging={_thumbDragging}",
            InkCanvas.Core.Diagnostics.LogType.Trace);
        _thumbTracking = false;
        _thumbDragging = false;
        _selectionRect.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelectionVisual(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        (double left, double top, double right, double bottom) = Normalize(start, end);
        Canvas.SetLeft(_selectionRect, left);
        Canvas.SetTop(_selectionRect, top);
        _selectionRect.Width = Math.Max(0, right - left);
        _selectionRect.Height = Math.Max(0, bottom - top);
        _selectionRect.Visibility = Visibility.Visible;
    }

    private static (double Left, double Top, double Right, double Bottom) Normalize(
        Windows.Foundation.Point start,
        Windows.Foundation.Point end)
    {
        return (
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y));
    }

    private AssistantRect MapToAnchor(double left, double top, double right, double bottom)
    {
        double width = _selectionOverlay.ActualWidth;
        double height = _selectionOverlay.ActualHeight;
        if (width <= 0 || height <= 0 || _anchor.IsEmpty) return default;

        double scaleX = _anchor.Width / width;
        double scaleY = _anchor.Height / height;

        return new AssistantRect(
            _anchor.Left + left * scaleX,
            _anchor.Top + top * scaleY,
            _anchor.Left + right * scaleX,
            _anchor.Top + bottom * scaleY);
    }

    private async Task LoadThumbnailAsync(string base64)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            _thumbStream?.Dispose();
            _thumbStream = stream;
            _thumbnail.Source = bitmap;
            _thumbnailHost.Visibility = Visibility.Visible;
            _thumbBase64 = base64;
            UpdateThumbnailSize();
        }
        catch (Exception)
        {
            // 截图可能是空或损坏的 base64；正文仍应正常显示。
        }
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            IntPtr hwnd = WindowChrome.GetHwnd(this);
            if (hwnd == IntPtr.Zero) return;
            int preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // 旧版 Windows 或 DWM 不可用：忽略，保留方形窗口。
        }
    }

    private static Brush GetSystemAccent()
    {
        try
        {
            var color = new Windows.UI.ViewManagement.UISettings()
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return new SolidColorBrush(color);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Brush Solid(byte a, byte r, byte g, byte b)
        => new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}