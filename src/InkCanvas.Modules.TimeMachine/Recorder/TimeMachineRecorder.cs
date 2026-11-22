using InkCanvas.Core.Ink;
using InkCanvas.Features.Board;
using InkCanvas.Features.Ppt;
using InkCanvas.Host.Contracts;
using InkCanvas.Ink;
using System.Diagnostics;
using System.Numerics;

namespace InkCanvas.Modules.TimeMachine;

/// <summary>
/// 笔画级录制器：订阅宿主暴露的活动控制器、画板、PPT 与会话信号，
/// 生成可回放的 <see cref="ClassroomSession"/>。
/// <para>
/// 笔画添加 / 擦除 / 清空来自 <see cref="InkController"/> 的最小新增事件；
/// 撤销 / 重做没有独立事件，通过比较 <see cref="InkController.Invalidated"/> 前后的
/// 文档笔画 id 集合差异推导（<c>History</c> 的 Undo/Redo 计数用于区分撤销与重做）。
/// </para>
/// <para>本实现不包含暂停 / 继续、异常恢复日志、会话留存清理与音频录制。</para>
/// </summary>
public sealed class TimeMachineRecorder : IDisposable
{
    private readonly ITimeMachineHost _host;
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();
    private InkController _controller;
    private HashSet<string> _knownIds = new();
    private int _lastUndoCount;
    private int _lastRedoCount;
    private InkTimeline _timeline;
    private int _nextSeq = 1;
    private bool _disposed;
    private readonly List<Task> _pendingCaptures = new();

    public TimeMachineRecorder(ITimeMachineHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public ClassroomSession Session { get; private set; }

    public bool IsRecording { get; private set; }

    public InkTimeline Timeline => _timeline;

    /// <summary>笔画提交时的抽稀容差（DIP）。默认 0.8，设为 0 表示不抽稀。</summary>
    public double SimplificationTolerance { get; set; } = StrokeSimplifier.DefaultTolerance;

    /// <summary>画板关闭时触发（由服务侧决定停止并保存）。</summary>
    public event Action BoardClosed;

    /// <summary>当前录制时钟（毫秒，相对录制开始）。</summary>
    public long ElapsedMs => Math.Max(0, _clock.ElapsedMilliseconds);

    /// <summary>
    /// 尚未完成的截图采集任务；测试可在触发事件后 <c>await</c> 本属性，
    /// 等待截图路径回填到对应事件。
    /// </summary>
    public Task PendingCaptures => Task.WhenAll(_pendingCaptures);

    /// <summary>开始录制。会在 t=0 记录初始页码、背景，并把已有笔画作为起点快照。</summary>
    public void Start(string name = null)
    {
        if (IsRecording) return;

        Session = new ClassroomSession
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? $"板书 {DateTime.Now:yyyy-MM-dd HH:mm}" : name,
            CreatedAt = DateTime.Now,
            Events = new List<TimelineEvent>(),
            Checkpoints = new List<SessionCheckpoint>(),
        };
        _timeline = new InkTimeline(Session);

        _clock.Restart();
        IsRecording = true;
        _nextSeq = 1;
        _pendingCaptures.Clear();

        BindController(_host.ActiveController);

        AddEvent(TimelineEventKind.PageChanged, CurrentPageIndex());
        AddEvent(TimelineEventKind.BackgroundChanged, CurrentPageIndex(), payload: CurrentBackgroundName());
        SeedExistingStrokes();

        var board = _host.Board;
        if (board is not null)
        {
            board.PageChanged += OnBoardPageChanged;
            board.BackgroundChanged += OnBoardBackgroundChanged;
        }

        var ppt = _host.Ppt;
        if (ppt is not null)
        {
            ppt.InkTransition += OnPptTransition;
            ppt.ScreenshotRequested += OnPptScreenshotRequested;
        }

        _host.ClassroomSignal += OnClassroomSignal;
        _host.ActiveControllerChanged += OnActiveControllerChanged;
        _host.BoardOpenChanged += OnBoardOpenChanged;
    }

    /// <summary>停止录制并解绑全部事件，返回已填充的会话。</summary>
    public ClassroomSession Stop()
    {
        if (!IsRecording) return Session;
        IsRecording = false;
        _clock.Stop();

        var board = _host.Board;
        if (board is not null)
        {
            board.PageChanged -= OnBoardPageChanged;
            board.BackgroundChanged -= OnBoardBackgroundChanged;
        }

        var ppt = _host.Ppt;
        if (ppt is not null)
        {
            ppt.InkTransition -= OnPptTransition;
            ppt.ScreenshotRequested -= OnPptScreenshotRequested;
        }

        _host.ClassroomSignal -= OnClassroomSignal;
        _host.ActiveControllerChanged -= OnActiveControllerChanged;
        _host.BoardOpenChanged -= OnBoardOpenChanged;

        UnbindController();
        return Session;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _controller = null;
    }

    // ---- 事件绑定 ----------------------------------------------------------

    private void OnActiveControllerChanged()
    {
        if (!IsRecording) return;
        BindController(_host.ActiveController);
    }

    private void OnBoardOpenChanged()
    {
        if (!IsRecording) return;
        if (!_host.BoardOpen) BoardClosed?.Invoke();
    }

    private void BindController(InkController controller)
    {
        UnbindController();
        _controller = controller;
        if (_controller is not null)
        {
            _controller.StrokeCommitted += OnStrokeCommitted;
            _controller.StrokesErased += OnStrokesErased;
            _controller.Cleared += OnCleared;
            _controller.Invalidated += OnControllerInvalidated;
            _controller.ViewChanged += OnViewChanged;
            _knownIds = new HashSet<string>(_controller.Document.Strokes.Select(s => s.Id.ToString()));
        }
        else
        {
            _knownIds = new HashSet<string>();
        }
        SyncHistoryCounts();
    }

    private void UnbindController()
    {
        if (_controller is null) return;
        _controller.StrokeCommitted -= OnStrokeCommitted;
        _controller.StrokesErased -= OnStrokesErased;
        _controller.Cleared -= OnCleared;
        _controller.Invalidated -= OnControllerInvalidated;
        _controller.ViewChanged -= OnViewChanged;
    }

    private void OnBoardPageChanged()
    {
        if (!IsRecording) return;
        BindController(_host.ActiveController);
        AddEvent(TimelineEventKind.PageChanged, CurrentPageIndex());
    }

    private void OnBoardBackgroundChanged()
    {
        if (!IsRecording) return;
        AddEvent(TimelineEventKind.BackgroundChanged, CurrentPageIndex(), payload: CurrentBackgroundName());
    }

    private void OnStrokeCommitted(InkStroke stroke)
    {
        if (!IsRecording || stroke is null) return;
        string id = stroke.Id.ToString();
        _knownIds.Add(id);
        AddEvent(TimelineEventKind.StrokeAdded, CurrentPageIndex(), id, SerializeStroke(stroke));
        SyncHistoryCounts();
    }

    private void OnStrokesErased(IReadOnlyList<InkStroke> strokes)
    {
        if (!IsRecording || strokes is null || strokes.Count == 0) return;
        var ids = strokes.Select(s => s.Id.ToString()).ToList();
        foreach (var id in ids) _knownIds.Remove(id);
        AddEvent(TimelineEventKind.StrokesRemoved, CurrentPageIndex(), ids: ids);
        SyncHistoryCounts();
    }

    private void OnCleared()
    {
        if (!IsRecording) return;
        _knownIds.Clear();
        AddEvent(TimelineEventKind.Cleared, CurrentPageIndex());
        SyncHistoryCounts();
    }

    /// <summary>撤销 / 重做：文档 id 集合的差异即本次操作的效果。</summary>
    private void OnControllerInvalidated()
    {
        if (!IsRecording || _controller is null) return;

        var current = new HashSet<string>(_controller.Document.Strokes.Select(s => s.Id.ToString()));
        if (current.SetEquals(_knownIds))
        {
            SyncHistoryCounts();
            return;
        }

        var removed = _knownIds.Where(id => !current.Contains(id)).ToList();
        var added = current.Where(id => !_knownIds.Contains(id)).ToList();

        bool isUndo = _controller.History.RedoCount > _lastRedoCount;
        bool isRedo = !isUndo && _controller.History.UndoCount > _lastUndoCount;
        var kind = isUndo ? TimelineEventKind.Undo
            : isRedo ? TimelineEventKind.Redo
            : (added.Count > 0 ? TimelineEventKind.Redo : TimelineEventKind.Undo);

        var addedSet = new HashSet<string>(added);
        var addedStrokes = _controller.Document.Strokes
            .Where(s => addedSet.Contains(s.Id.ToString()))
            .ToList();

        // 撤销/重做重新出现的笔画与 StrokeAdded 一样先抽稀，避免回放体积反弹。
        var replayStrokes = addedStrokes
            .Select(s => SimplificationTolerance > 0
                ? StrokeSimplifier.Simplify(s, SimplificationTolerance)
                : s)
            .ToList();

        AddEvent(kind, CurrentPageIndex(), ids: removed,
            payload: replayStrokes.Count > 0 ? InkTimeline.EncodeAddedStrokes(replayStrokes) : null);

        _knownIds = current;
        SyncHistoryCounts();
    }

    /// <summary>视图缩放/平移：记录矩阵推导出的 scale=sqrt(|det|)、tx=M31、ty=M32。</summary>
    private void OnViewChanged(Matrix3x2 transform)
    {
        if (!IsRecording) return;
        double scale = Math.Sqrt(Math.Abs(transform.GetDeterminant()));
        if (!double.IsFinite(scale)) scale = 1;
        AddEvent(TimelineEventKind.ViewChanged, CurrentPageIndex(),
            payload: InkTimeline.EncodeViewChanged(scale, transform.M31, transform.M32));
    }

    private void OnPptTransition(PptInkTransition transition)
    {
        if (!IsRecording || transition is null) return;
        var slide = transition.Current?.Slide;
        if (slide is null) return;

        var ev = new TimelineEvent
        {
            PageIndex = CurrentPageIndex(),
            Kind = TimelineEventKind.PptSlideChanged,
            StrokeId = slide.SlideId.ToString(),
            Payload = InkTimeline.EncodePptSlideChanged(slide.SlideId, slide.Index, ""),
        };
        Append(ev);
        RequestCapture(ev);
    }

    private void OnPptScreenshotRequested(PptScreenshotRequest request)
    {
        if (!IsRecording || request is null) return;
        var slide = request.InkSlide ?? request.Snapshot?.Slide;
        if (slide is null) return;

        var ev = new TimelineEvent
        {
            PageIndex = CurrentPageIndex(),
            Kind = TimelineEventKind.PptSlideChanged,
            StrokeId = slide.SlideId.ToString(),
            Payload = InkTimeline.EncodePptSlideChanged(slide.SlideId, slide.Index, ""),
        };
        Append(ev);
        RequestCapture(ev);
    }

    /// <summary>
    /// 异步采集事件时刻的课堂截图，回填到事件的 <see cref="TimelineEvent.Screenshot"/>；
    /// PPT 事件同时把截图路径写回课件载荷，保持旧回放链路兼容。采集失败或返回空路径时
    /// 事件保留无截图，不阻断录制。
    /// </summary>
    private void RequestCapture(TimelineEvent ev)
    {
        if (ev is null) return;
        _pendingCaptures.Add(CaptureAsync(ev));
    }

    private async Task CaptureAsync(TimelineEvent ev)
    {
        string path;
        try
        {
            path = await _host.CaptureScreenshotAsync();
        }
        catch
        {
            return; // 截图失败：该事件回退无截图，不阻断录制。
        }

        if (!IsRecording || string.IsNullOrWhiteSpace(path)) return;
        lock (_gate)
        {
            ev.Screenshot = path;
            if (ev.Kind == TimelineEventKind.PptSlideChanged)
            {
                var change = InkTimeline.DecodePptSlideChanged(ev.Payload);
                if (change.IsValid)
                    ev.Payload = InkTimeline.EncodePptSlideChanged(change.SlideId, change.Index, path);
            }
        }
    }

    private void OnClassroomSignal(ClassroomSignal signal)
    {
        if (!IsRecording || signal is null) return;
        switch (signal.Kind)
        {
            case ClassroomSignalKind.RosterDrawn:
                Append(new TimelineEvent
                {
                    PageIndex = CurrentPageIndex(),
                    Kind = TimelineEventKind.RosterDrawn,
                    Payload = signal.Label,
                });
                break;
            case ClassroomSignalKind.TimerStarted:
                Append(new TimelineEvent
                {
                    PageIndex = CurrentPageIndex(),
                    Kind = TimelineEventKind.TimerStarted,
                    Payload = signal.Label,
                });
                break;
            case ClassroomSignalKind.TimerStopped:
                Append(new TimelineEvent
                {
                    PageIndex = CurrentPageIndex(),
                    Kind = TimelineEventKind.TimerStopped,
                    Payload = signal.Label,
                });
                break;
            case ClassroomSignalKind.TimerEnded:
            case ClassroomSignalKind.PracticeFinished:
                var ended = new TimelineEvent
                {
                    PageIndex = CurrentPageIndex(),
                    Kind = TimelineEventKind.TimerEnded,
                    Payload = string.IsNullOrWhiteSpace(signal.Label) ? "练习结束" : signal.Label,
                };
                Append(ended);
                // 倒计时结束与点名同为自动书签来源；按规范要求，结束时刻补一张课堂截图。
                RequestCapture(ended);
                break;
        }
    }

    // ---- 事件追加 ----------------------------------------------------------

    private int CurrentPageIndex() => _host.Board?.CurrentIndex ?? 0;

    private string CurrentBackgroundName() => _host.Board?.Background.ToString() ?? "Black";

    private void SyncHistoryCounts()
    {
        if (_controller is null) return;
        _lastUndoCount = _controller.History.UndoCount;
        _lastRedoCount = _controller.History.RedoCount;
    }

    private string SerializeStroke(InkStroke stroke)
    {
        if (stroke is null) return null;
        if (!(SimplificationTolerance > 0)) return StrokeJsonCodec.Serialize(stroke);
        return StrokeJsonCodec.Serialize(StrokeSimplifier.Simplify(stroke, SimplificationTolerance));
    }

    private void SeedExistingStrokes()
    {
        var board = _host.Board;
        if (board is null) return;
        for (int page = 0; page < board.Pages.Count; page++)
        {
            foreach (var stroke in board.Pages[page].Document.Strokes)
                AddEvent(TimelineEventKind.StrokeAdded, page, stroke.Id.ToString(), SerializeStroke(stroke));
        }
    }

    private void AddEvent(TimelineEventKind kind, int page,
        string strokeId = null, string strokeJson = null,
        IReadOnlyList<string> ids = null, string payload = null)
    {
        if (!IsRecording || Session is null) return;

        Append(new TimelineEvent
        {
            TimeMs = ElapsedMs,
            PageIndex = page,
            Kind = kind,
            StrokeId = strokeId,
            StrokeJson = strokeJson,
            StrokeIds = ids,
            Payload = payload,
        });
    }

    /// <summary>
    /// 线程安全地追加一个外部事件（PPT 翻页 / 点名 / 倒计时等）。与尾部事件的归属和完整内容
    /// 相同时忽略；事件时间早于最后一个事件时按时间插入，并重建检查点。
    /// 标书签候选事件会自动补一条章节书签。
    /// </summary>
    public void Append(TimelineEvent ev)
    {
        if (ev is null || Session is null) return;

        lock (_gate)
        {
            if (!IsRecording) return;
            var events = Session.Events ??= new List<TimelineEvent>();

            // 归属与序号由录制器统一分配：外部事件源（PPT / 点名 / 倒计时）只需给出
            // 时间、页与载荷，禁止自行编号，避免同毫秒事件在恢复后失去稳定顺序。
            ev.SessionId = Session.Id;
            if (ev.Seq <= 0) ev.Seq = _nextSeq++;
            else if (ev.Seq >= _nextSeq) _nextSeq = ev.Seq + 1;

            if (ev.TimeMs <= 0) ev.TimeMs = ElapsedMs;

            if (ev.PptSlideId <= 0 && ev.Kind == TimelineEventKind.PptSlideChanged)
            {
                PptSlideChange change = InkTimeline.DecodePptSlideChanged(ev.Payload);
                if (change.IsValid && change.SlideId > 0) ev.PptSlideId = change.SlideId;
            }

            // 相邻完全重复事件（同一次手势/切换被多次上报）不再入时间轴。
            if (events.Count > 0 && SameSignature(events[^1], ev)) return;

            if (events.Count > 0 && ev.TimeMs < events[^1].TimeMs)
            {
                int index = 0;
                while (index < events.Count && events[index].TimeMs <= ev.TimeMs) index++;
                events.Insert(index, ev);
                Session.Checkpoints.Clear();
                _timeline = new InkTimeline(Session);
            }
            else
            {
                events.Add(ev);
            }

            if (IsBookmarkSource(ev.Kind)) _timeline?.ApplyAutoBookmarks();
        }
    }

    private static bool SameSignature(TimelineEvent a, TimelineEvent b)
    {
        if (a.Kind != b.Kind || a.TimeMs != b.TimeMs || a.PageIndex != b.PageIndex || a.PptSlideId != b.PptSlideId) return false;
        if (!string.Equals(a.StrokeId, b.StrokeId, StringComparison.Ordinal)) return false;
        return string.Equals(a.StrokeJson, b.StrokeJson, StringComparison.Ordinal)
            && string.Equals(a.Payload, b.Payload, StringComparison.Ordinal)
            && string.Equals(a.Screenshot, b.Screenshot, StringComparison.Ordinal)
            && (a.StrokeIds ?? Array.Empty<string>()).SequenceEqual(b.StrokeIds ?? Array.Empty<string>());
    }

    private static bool IsBookmarkSource(TimelineEventKind kind)
        => kind is TimelineEventKind.PageChanged
            or TimelineEventKind.Cleared
            or TimelineEventKind.PptSlideChanged
            or TimelineEventKind.RosterDrawn
            or TimelineEventKind.TimerEnded;
}
