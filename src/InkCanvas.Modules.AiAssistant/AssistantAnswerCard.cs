using InkCanvas.Controls.Controls;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace InkCanvas.Modules.AiAssistant;

/// <summary>Each response keeps stable rendered blocks and updates only its changed suffix.</summary>
internal sealed class AssistantAnswerCard : UserControl
{
    private readonly Border _card = new() { Padding = new Thickness(12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
    private readonly FeatureSettings _features;
    private readonly StackPanel _content = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _verification = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressRing _progress = new() { Width = 14, Height = 14, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly Button _stop, _paste, _copy, _favorite, _retry, _next;
    private readonly TextBlock _nextLabel = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly List<string> _blockKeys = new();
    private readonly SolidColorBrush _foreground = new(), _muted = new(), _accent = new(), _surface = new(), _stroke = new();
    private bool _isCurrent = true;
    public AssistantTurn Turn { get; }
    public bool IsCurrent { get => _isCurrent; set { _isCurrent = value; RefreshActions(); } }
    public event Action StopRequested;
    public event Action RetryRequested;
    public event Action<AssistantTurn> PasteRequested;
    public event Action<AssistantTurn> CopyRequested;
    public event Action<AssistantTurn> FavoriteRequested;
    public event Action<AssistantAnswerCard> RevealRequested;

    public AssistantAnswerCard(AssistantTurn turn, FeatureSettings features)
    {
        Turn = turn; _features = features;
        AutomationProperties.SetName(this, "AI 回答");
        var body = new StackPanel { Spacing = 12 };
        var header = new Grid { ColumnSpacing = 6 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _dot, _progress, _status } };
        status.Children.Add(new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2), Background = _surface, Child = _verification });
        header.Children.Add(status);
        _stop = Button("停止", Symbol.Stop); _stop.FontSize = 12; _stop.Padding = new Thickness(6, 3, 6, 3);
        _stop.Click += (_, _) => StopRequested?.Invoke(); Grid.SetColumn(_stop, 1); header.Children.Add(_stop);
        body.Children.Add(header); body.Children.Add(_content);
        _next = Button("", Symbol.Download); _next.Content = _nextLabel; _next.HorizontalAlignment = HorizontalAlignment.Stretch;
        _next.Padding = new Thickness(10, 10, 10, 10); _next.HorizontalContentAlignment = HorizontalAlignment.Left;
        _next.Click += (_, _) => RevealRequested?.Invoke(this); body.Children.Add(_next);
        var actions = new Grid { ColumnSpacing = 4 };
        actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _paste = Button("贴回白板", Symbol.Paste); _paste.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _paste.HorizontalAlignment = HorizontalAlignment.Left; _paste.CornerRadius = new CornerRadius(18); _paste.FontSize = 12;
        _paste.Click += (_, _) => PasteRequested?.Invoke(Turn); actions.Children.Add(_paste);
        var secondary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        _copy = IconButton("复制回答", Symbol.Copy); _copy.Click += (_, _) => CopyRequested?.Invoke(Turn);
        _favorite = IconButton("收藏例题", Symbol.Favorite); _favorite.Click += (_, _) => FavoriteRequested?.Invoke(Turn);
        _retry = IconButton("重新获取并重试", Symbol.Refresh); _retry.Click += (_, _) => RetryRequested?.Invoke();
        secondary.Children.Add(_copy); secondary.Children.Add(_favorite); secondary.Children.Add(_retry);
        Grid.SetColumn(secondary, 1); actions.Children.Add(secondary); body.Children.Add(actions);
        _card.Child = body; Content = _card;
        Loaded += (_, _) => { ApplyTheme(); RefreshActions(); }; ActualThemeChanged += (_, _) => ApplyTheme();
        Unloaded += (_, _) => _progress.IsActive = false;
        ApplyTheme();
    }

    public void Refresh()
    {
        var session = Turn.Session;
        _status.Text = session.State switch { AssistantState.Capturing => "获取中", AssistantState.Recognizing => "识别中",
            AssistantState.Streaming => "生成中", AssistantState.Done => "已完成", AssistantState.Error => "生成失败", _ => "就绪" };
        bool active = session.State is AssistantState.Capturing or AssistantState.Recognizing or AssistantState.Streaming;
        _progress.IsActive = active && IsLoaded; _progress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _dot.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        _verification.Text = session.Confidence is >= 0 and < .5 ? "低置信" : session.Verified == true ? "已验证" : "未验证";
        _dot.Fill = session.State == AssistantState.Error ? ThemeBrushes.Resolve(this, "SystemFillColorCriticalBrush") : _accent;
        var blocks = new List<(string Key, Func<UIElement> Render)>();
        if (!string.IsNullOrWhiteSpace(session.LocalHint))
            blocks.Add(("hint:" + session.LocalHint, () => Text(session.LocalHint, muted: true)));
        foreach (var block in AssistantText.ParseMarkdown(session.Text ?? ""))
        {
            var current = block;
            string key = block.Kind + "|" + block.Level + "|" + block.Text;
            if (block.Kind == MarkdownBlockKind.Table) key += string.Join("\n", block.Rows.Select(row => string.Join("|", row)));
            blocks.Add((key, () => RenderBlock(current)));
        }
        int visible = AssistantTeachingPolicy.VisibleSteps(session.TeachingMode, session.RevealedSteps, session.Steps.Count);
        for (int index = 0; index < visible; index++)
        {
            var step = session.Steps[index];
            string caption = $"{(step.Index > 0 ? step.Index : index + 1)}. {step.Text}";
            blocks.Add(("step:" + caption + "|" + step.Latex, () =>
            {
                var panel = new StackPanel { Spacing = 6 }; panel.Children.Add(RenderProse(caption));
                if (!string.IsNullOrWhiteSpace(step.Latex)) panel.Children.Add(Formula(AssistantText.LatexToPlain(step.Latex)));
                return panel;
            }));
        }
        if (AssistantTeachingPolicy.ShowsFinalAnswer(session.TeachingMode, session.RevealedSteps, session.Steps.Count)
            && !string.IsNullOrWhiteSpace(session.Formula))
            blocks.Add(("formula:" + session.Formula, () => Formula(AssistantText.LatexToPlain(session.Formula))));
        if (session.State == AssistantState.Error && !string.IsNullOrWhiteSpace(session.Error))
            blocks.Add(("error:" + session.Error, () => Text(session.Error)));
        int stable = 0;
        while (stable < blocks.Count && stable < _blockKeys.Count && blocks[stable].Key == _blockKeys[stable]) stable++;
        while (_content.Children.Count > stable) _content.Children.RemoveAt(_content.Children.Count - 1);
        if (_blockKeys.Count > stable) _blockKeys.RemoveRange(stable, _blockKeys.Count - stable);
        for (int index = stable; index < blocks.Count; index++)
        { _content.Children.Add(blocks[index].Render()); _blockKeys.Add(blocks[index].Key); }
        bool more = AssistantTeachingPolicy.HasMoreSteps(session.TeachingMode, session.RevealedSteps, session.Steps.Count);
        _next.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
        _nextLabel.Text = $"展开下一步  ·  剩余 {session.Steps.Count - visible} 步  ﹀";
        AutomationProperties.SetName(_next, _nextLabel.Text);
        RefreshActions();
    }

    public void RefreshActions()
    {
        var session = Turn.Session;
        bool active = session.State is AssistantState.Capturing or AssistantState.Recognizing or AssistantState.Streaming;
        bool done = session.State == AssistantState.Done;
        bool content = !string.IsNullOrWhiteSpace(session.Text)
            || AssistantTeachingPolicy.VisibleSteps(session.TeachingMode, session.RevealedSteps, session.Steps.Count) > 0
            || AssistantTeachingPolicy.ShowsFinalAnswer(session.TeachingMode, session.RevealedSteps, session.Steps.Count)
                && !string.IsNullOrWhiteSpace(session.Formula);
        _stop.Visibility = IsCurrent && active ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = IsCurrent && session.State == AssistantState.Error ? Visibility.Visible : Visibility.Collapsed;
        _paste.Visibility = done && content ? Visibility.Visible : Visibility.Collapsed;
        _paste.IsEnabled = !Turn.Pasting && !Turn.Pasted;
        ((StackPanel)_paste.Content).Children.OfType<TextBlock>().Single().Text = Turn.Pasting ? "正在贴回" : Turn.Pasted ? "已贴回" : "贴回白板";
        _copy.IsEnabled = content;
        _favorite.Visibility = _features is null || _features.EnableExampleLibrary ? Visibility.Visible : Visibility.Collapsed;
        _favorite.IsEnabled = done && content;
    }

    private UIElement RenderBlock(MarkdownBlock block) => block.Kind switch
    {
        MarkdownBlockKind.Code => Horizontal(new TextBlock { Text = block.Text, FontFamily = new FontFamily("Consolas"), FontSize = 13,
            Foreground = _foreground, IsTextSelectionEnabled = true }),
        MarkdownBlockKind.Table => Table(block),
        MarkdownBlockKind.Heading => RenderProse("### " + block.Text),
        MarkdownBlockKind.ListItem => RenderProse("• " + block.Text),
        _ => RenderProse(block.Text),
    };

    private UIElement RenderProse(string text)
    {
        var segments = AssistantText.SplitFormulas(text);
        if (!segments.Any(segment => segment.IsFormula)) return Markdown(text);
        var panel = new StackPanel { Spacing = 6 };
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Text)) continue;
            panel.Children.Add(segment.IsFormula ? Formula(segment.Text) : Markdown(segment.Text));
        }
        return panel;
    }

    private static MarkdownView Markdown(string text)
    {
        var view = new MarkdownView(0) { Spacing = 6 }; view.SetMarkdown(text); return view;
    }

    private UIElement Formula(string text) => Horizontal(new TextBlock { Text = text, FontFamily = new FontFamily("Cambria Math"),
        FontSize = 16, IsTextSelectionEnabled = true, Foreground = _foreground });

    private Border Horizontal(UIElement content) => new() { Background = _surface, CornerRadius = new CornerRadius(6), Padding = new Thickness(8),
        Child = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled } };

    private UIElement Table(MarkdownBlock block)
    {
        var lines = block.Rows.Select(row => "| " + string.Join(" | ", row) + " |").ToList();
        if (lines.Count > 0) lines.Insert(1, "| " + string.Join(" | ", block.Rows[0].Select(_ => "---")) + " |");
        return Markdown(string.Join("\n", lines));
    }

    private TextBlock Text(string text, bool muted = false) => new() { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
        Foreground = muted ? _muted : _foreground, IsTextSelectionEnabled = true };

    private static Button Button(string label, Symbol symbol)
    {
        var button = new Button { MinHeight = 40, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = {
                new SymbolIcon(symbol) { Width = 16, Height = 16 }, new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } } } };
        AutomationProperties.SetName(button, label); return button;
    }

    private static TouchIconButton IconButton(string name, Symbol symbol)
    {
        var button = new TouchIconButton { Width = 40, Height = 40, MinWidth = 40, MinHeight = 40, Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), Content = new SymbolIcon(symbol) };
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name); return button;
    }

    private void ApplyTheme()
    {
        void Update(SolidColorBrush brush, string resource)
        { if (ThemeBrushes.Resolve(this, resource) is SolidColorBrush color) brush.Color = color.Color; }
        Update(_foreground, "IcTextPrimaryBrush"); Update(_muted, "IcTextSecondaryBrush"); Update(_accent, "IcAccentBrush");
        Update(_surface, "IcSurfaceAltBrush"); Update(_stroke, "IcStrokeBrush");
        _card.Background = _surface; _card.BorderBrush = _stroke;
        _status.Foreground = _verification.Foreground = _muted; _nextLabel.Foreground = _accent;
    }
}
