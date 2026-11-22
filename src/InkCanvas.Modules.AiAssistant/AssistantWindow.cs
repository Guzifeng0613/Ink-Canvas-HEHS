using InkCanvas.Controls.Controls;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Classroom;
using InkCanvas.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.Modules.AiAssistant;

/// <summary>Native narrow assistant with independent response cards and deferred context previews.</summary>
public sealed partial class AssistantWindow : Window
{
    private readonly FeatureSettings _features;
    private readonly Grid _root = new() { RowSpacing = 12, Padding = new Thickness(12, 4, 12, 12) };
    private readonly StackPanel _conversation = new() { Spacing = 12, Padding = new Thickness(0, 0, 0, 8) };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled };
    private readonly TextBlock _contextTitle = new() { Text = "当前画布", FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _modeText = new() { Text = "分步引导", FontSize = 13 };
    private readonly TextBlock _notice = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly TextBox _followUp = new() { PlaceholderText = "继续追问…", FontSize = 14, MinHeight = 44,
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 104, BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly List<AssistantAnswerCard> _answers = new();
    private readonly Brush _textPrimary = new SolidColorBrush(), _textSecondary = new SolidColorBrush(),
        _accentBrush = new SolidColorBrush(), _accentSoftBrush = new SolidColorBrush(),
        _inputBgBrush = new SolidColorBrush(), _controlStrokeBrush = new SolidColorBrush();
    private readonly Button _variantBasic, _variantAdvanced, _summary, _send;
    private AssistantAnswerCard _currentCard;
    private AssistantSession _lastSession;
    private bool _closed, _busy, _annotationsVisible = true, _aiTeachingEnabled = true, _scrollQueued, _composing;
    private bool _following = true;
    private string _sourcePresentation = "";
    private string _pendingThumbnail = "";
    private int _sourceSlideId, _sourceBoardPage = -1;
    private Button _annotationButton, _agentButton;

    public new event Action Closed;
    public event Action StopRequested;
    public event Action RetryRequested;
    public event Action<AssistantTurn> AnswerPasteRequested;
    public event Action<AssistantTurn> AnswerCopyRequested;
    public event Action<string> FollowUpRequested;
    public event Action<AssistantTeachingMode> TeachingModeRequested;
    public event Action RevealNextRequested;
    public event Action<bool> VariantRequested;
    public event Action AddRegionRequested;
    public event Action<string> RemoveRegionRequested;
    public event Action<string> ReRecognizeRegionRequested;
    public event Action<string, int> MoveRegionRequested;
    public event Action<AssistantRect> RegionSelected;
    public bool RegionSelectionEnabled { get; set; } = true;

    public AssistantWindow(int theme, FeatureSettings features = null, bool demoMode = false)
    {
        Title = "AI 助教";
        _features = features;
        _root.RequestedTheme = theme switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        _variantBasic = CreateSecondaryButton("基础变式");
        _variantAdvanced = CreateSecondaryButton("提高变式");
        _summary = CreateSecondaryButton("易错点总结");
        _send = CreateIconButton("发送 (Enter)", Symbol.Send);
        BuildPreview(); BuildLayout(demoMode);
        Content = _root;
        _root.Loaded += (_, _) => RefreshTheme();
        _root.ActualThemeChanged += (_, _) => RefreshTheme();
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); _notice.Visibility = Visibility.Collapsed; };
        _scroll.ViewChanged += (_, args) =>
        {
            // Layout changes during streaming also raise ViewChanged. Only an actual viewport move changes the reading intent.
            if (_scrolling || Math.Abs(_scroll.VerticalOffset - _lastScrollOffset) < .5) return;
            _lastScrollOffset = _scroll.VerticalOffset;
            _following = _scroll.ScrollableHeight - _scroll.VerticalOffset <= 32;
        };
        base.Closed += (_, _) =>
        {
            _closed = true; _thumbnailGeneration++; _problemGeneration++;
            _highlightTimer.Stop(); _noticeTimer.Stop();
            _preview.Hide(); _agentFlyout.Hide();
            _thumbStream?.Dispose();
            foreach (var stream in _problemStreams) stream.Dispose();
            _problemStreams.Clear(); _problemImages.Clear();
            CloseEdgeHandle();
            Closed?.Invoke();
        };
        RefreshTheme(); UpdateActionState();
    }

    public void BeginTurn(string question = "")
    {
        if (_currentCard is not null) _currentCard.IsCurrent = false;
        var turn = new AssistantTurn(question, _anchor, _sourcePresentation, _sourceSlideId, _sourceBoardPage);
        _currentCard = new AssistantAnswerCard(turn, _features);
        _answers.Add(_currentCard);
        _currentCard.StopRequested += () => StopRequested?.Invoke();
        _currentCard.RetryRequested += () => RetryRequested?.Invoke();
        _currentCard.PasteRequested += selected => AnswerPasteRequested?.Invoke(selected);
        _currentCard.CopyRequested += selected => AnswerCopyRequested?.Invoke(selected);
        _currentCard.FavoriteRequested += FavoriteExample;
        _currentCard.RevealRequested += card =>
        {
            if (ReferenceEquals(card, _currentCard)) RevealNextRequested?.Invoke();
            else if (AssistantMachine.RevealNextStep(card.Turn.Session))
            { card.Turn.Pasted = false; card.Refresh(); }
        };
        if (!string.IsNullOrWhiteSpace(question))
        {
            _conversation.Children.Add(new Border { Background = _accentSoftBrush, CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(28, 0, 0, 0),
                Child = new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = _textPrimary } });
        }
        _conversation.Children.Add(_currentCard);
        ScrollToEnd(force: true);
    }

    public void Apply(AssistantSession session, string thumbnailBase64, bool preserveScroll = false)
    {
        if (_closed || session is null) return;
        _lastSession = session;
        _pendingThumbnail = thumbnailBase64 ?? "";
        if (_currentCard is null) BeginTurn();
        _currentCard.Turn.Update(session);
        if (_collapsed) return;
        _currentCard.Refresh();
        _modeText.Text = AssistantTeachingPolicy.Label(session.TeachingMode);
        if (!string.IsNullOrEmpty(thumbnailBase64) && thumbnailBase64 != _thumbBase64) _ = LoadThumbnailAsync(thumbnailBase64);
        else if (string.IsNullOrEmpty(thumbnailBase64) && _thumbBase64.Length > 0)
        {
            _thumbnailGeneration++; _thumbBase64 = "";
            _thumbnail.Source = _contextThumbnail.Source = null;
            _thumbnailHost.Visibility = Visibility.Collapsed;
            _thumbStream?.Dispose(); _thumbStream = null;
        }
        if (_previewOpen) ApplyAnnotations(session);
        UpdateActionState();
        ScrollToEnd();
    }

    public IReadOnlyList<AssistantMessage> ConversationMessages()
    {
        var messages = new List<AssistantMessage>();
        foreach (var card in _answers)
        {
            if (!string.IsNullOrWhiteSpace(card.Turn.Question)) messages.Add(new("user", card.Turn.Question));
            string text = AssistantTurn.VisibleText(card.Turn.Session);
            if (!string.IsNullOrWhiteSpace(text)) messages.Add(new("assistant", text));
        }
        return messages.TakeLast(24).ToArray();
    }

    public void SetBusy(bool busy) { _busy = busy; UpdateActionState(); }
    public void SetPasted(bool pasted)
    {
        if (_currentCard is null) return;
        _currentCard.Turn.Pasted = pasted; _currentCard.RefreshActions();
    }
    public void RefreshAnswer(AssistantTurn turn) => _answers.FirstOrDefault(card => card.Turn.Id == turn.Id)?.RefreshActions();
    public void SetFavoriteSource(string presentation, int slideId, int boardPage)
    { _sourcePresentation = presentation ?? ""; _sourceSlideId = slideId; _sourceBoardPage = boardPage; }
    public void ShowNotice(string message)
    {
        if (_closed || string.IsNullOrWhiteSpace(message)) return;
        _notice.Text = message; _notice.Visibility = Visibility.Visible;
        _noticeTimer.Stop(); _noticeTimer.Start();
    }

    private void FavoriteExample(AssistantTurn turn)
    {
        string answer = AssistantTurn.VisibleText(turn.Session);
        if (string.IsNullOrWhiteSpace(answer)) return;
        string title = answer.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "AI 讲题收藏";
        try
        {
            bool added = ExampleLibraryStore.Add(new ExampleEntry { Title = title[..Math.Min(60, title.Length)],
                Answer = answer, SourcePresentation = turn.Presentation, SourceSlideId = turn.SlideId,
                BoardPage = turn.BoardPage, CreatedAt = DateTime.Now }, settings: _features);
            ShowNotice(added ? "已收藏为例题" : "收藏失败");
        }
        catch (Exception ex)
        { Core.Diagnostics.FileLog.WriteLogToFile("收藏为例题失败 " + ex.Message, Core.Diagnostics.LogType.Error); ShowNotice("收藏失败"); }
    }

    private void SendFollowUp()
    {
        if (_busy || !_aiTeachingEnabled) return;
        string text = _followUp.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        _followUp.Text = "";
        FollowUpRequested?.Invoke(text);
    }

    private void UpdateActionState()
    {
        bool aiTeachingEnabled = _features?.EnableAiTeaching ?? true;
        bool exerciseFlowEnabled = _features?.EnableExerciseFlow ?? true;
        _aiTeachingEnabled = aiTeachingEnabled;
        _send.IsEnabled = !_busy && aiTeachingEnabled;
        _followUp.IsEnabled = !_busy && aiTeachingEnabled;
        _variantBasic.IsEnabled = exerciseFlowEnabled;
        _variantAdvanced.IsEnabled = exerciseFlowEnabled;
        _variantBasic.IsEnabled &= !_busy && aiTeachingEnabled;
        _variantAdvanced.IsEnabled &= !_busy && aiTeachingEnabled;
        _summary.IsEnabled = !_busy && aiTeachingEnabled;
        ToolTipService.SetToolTip(_variantBasic, exerciseFlowEnabled ? "生成基础难度的同知识点题目" : "随堂练习已关闭");
        ToolTipService.SetToolTip(_variantAdvanced, exerciseFlowEnabled ? "生成提高难度的同知识点题目" : "随堂练习已关闭");
        ToolTipService.SetToolTip(_send, aiTeachingEnabled ? "发送 (Enter)，Shift+Enter 换行" : "AI 讲题已关闭");
        _annotationButton.IsEnabled = _lastSession?.Annotations is { Count: > 0 };
        _currentCard?.RefreshActions();
    }

    private static bool IsActive(AssistantState state) => state is AssistantState.Capturing or AssistantState.Recognizing or AssistantState.Streaming;

    private bool _scrolling;
    private double _lastScrollOffset;
    private void ScrollToEnd(bool force = false)
    {
        if (force) _following = true;
        if (!_following || _scrollQueued || _collapsed) return;
        _scrollQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _scrollQueued = false;
            if (_closed || !_following) return;
            _scrolling = true;
            _scroll.UpdateLayout();
            _scroll.ChangeView(null, _scroll.ScrollableHeight, null, true);
            _lastScrollOffset = _scroll.VerticalOffset;
            _scrolling = false;
        });
    }

    private void RefreshTheme()
    {
        void Update(Brush target, string token)
        {
            if (target is SolidColorBrush color && ThemeBrushes.Resolve(_root, token) is SolidColorBrush value) color.Color = value.Color;
        }
        Update(_textPrimary, "IcTextPrimaryBrush"); Update(_textSecondary, "IcTextSecondaryBrush");
        Update(_accentBrush, "IcAccentBrush"); Update(_accentSoftBrush, "IcAccentSoftBrush");
        Update(_inputBgBrush, "IcSurfaceAltBrush"); Update(_controlStrokeBrush, "IcStrokeBrush");
        _contextTitle.Foreground = _textPrimary; _modeText.Foreground = _accentBrush; _notice.Foreground = _textSecondary;
        _root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private static Brush Solid(byte a, byte r, byte g, byte b) => new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
}
