using InkCanvas.Core.Classroom;
using InkCanvas.Core.Ink;
using InkCanvas.Features.Board;
using InkCanvas.Ink;
using System.Diagnostics;

namespace InkCanvas.Features.Classroom;

/// <summary>
/// 笔画级录制器：订阅 <see cref="BoardService"/> 当前页控制器的笔画事件与切页 / 背景事件，
/// 生成可回放的 <see cref="ClassroomSession"/>。
/// <para>
/// 笔画添加 / 擦除 / 清空来自 <see cref="InkController"/> 的最小新增事件；
/// 撤销 / 重做没有独立事件，通过比较 <see cref="InkController.Invalidated"/> 前后的
/// 文档笔画 id 集合差异推导（<c>History</c> 的 Undo/Redo 计数用于区分撤销与重做）。
/// </para>
/// </summary>
public sealed class InkRecorder : IDisposable
{
    private const long SnapshotIntervalMs = 2_000;

    private readonly BoardService _board;
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();
    private InkController _controller;
    private HashSet<string> _knownIds = new();
    private int _lastUndoCount;
    private int _lastRedoCount;
    private InkTimeline _timeline;
    private SessionJournal _journal;
    private long _lastSnapshotTimestamp;

    public InkRecorder(BoardService board)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
    }

    public ClassroomSession Session { get; private set; }

    public bool IsRecording { get; private set; }

    public InkTimeline Timeline => _timeline;

    /// <summary>录制中用于异常恢复的增量日志；未启用时为 null。</summary>
    public SessionJournal Journal => _journal;

    /// <summary>笔画提交时的抽稀容差（DIP）。默认 0.8，设为 0 表示不抽稀。</summary>
    public double SimplificationTolerance { get; set; } = StrokeSimplifier.DefaultTolerance;

    /// <summary>会话 / 日志 / 快照目录；默认 <see cref="ClassroomSessionStore.DefaultDirectory"/>。</summary>
    public string SessionDirectory { get; set; }

    /// <summary>当前录制时钟（毫秒，相对录制开始）。供外部事件源打时间戳。</summary>
    public long ElapsedMs => _clock.ElapsedMilliseconds;

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
        OpenJournal();

        _clock.Restart();
        IsRecording = true;

        Bind(_board.Current);

        AddEvent(TimelineEventKind.PageChanged, _board.CurrentIndex);
        AddEvent(TimelineEventKind.BackgroundChanged, _board.CurrentIndex, payload: _board.Background.ToString());
        SeedExistingStrokes();
        SaveSnapshot();

        _board.PageChanged += OnBoardPageChanged;
        _board.BackgroundChanged += OnBoardBackgroundChanged;
    }

    /// <summary>
    /// 停止录制：刷盘增量日志，合并为正式会话 JSON，并删除日志与快照。
    /// 合并失败时保留日志，下次启动可由 <see cref="ClassroomSessionStore.LoadWithJournal"/> 恢复。
    /// </summary>
    public void Stop()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _clock.Stop();

        _board.PageChanged -= OnBoardPageChanged;
        _board.BackgroundChanged -= OnBoardBackgroundChanged;
        UnbindController();
        FinishJournal();
    }

    /// <summary>保存当前会话到会话目录，返回文件路径。</summary>
    public string Save(string directory = null)
        => Session is null ? null : ClassroomSessionStore.Save(Session, directory);

    private void OpenJournal()
    {
        if (Session is null) return;
        try
        {
            _journal = SessionJournal.Open(Session.Id, SessionDirectory, Session.Name, Session.CreatedAt);
        }
        catch
        {
            // 日志不可用时录制照常进行，只是失去异常恢复能力。
            _journal = null;
        }
    }

    private void FinishJournal()
    {
        var journal = _journal;
        _journal = null;
        if (journal is null) return;

        try { journal.Close(); }
        catch { /* 已尽量刷盘 */ }

        bool saved = false;
        try
        {
            ClassroomSessionStore.Save(Session, SessionDirectory);
            saved = true;
        }
        catch
        {
            // 保存失败时保留日志供恢复。
        }

        if (saved) ClassroomSessionStore.DeleteRecoveryArtifacts(Session.Id, SessionDirectory);
    }

    private void SaveSnapshot()
    {
        if (Session is null) return;
        try
        {
            ClassroomSessionStore.SaveSnapshot(Session, SessionDirectory);
            _lastSnapshotTimestamp = Stopwatch.GetTimestamp();
        }
        catch
        {
            // 快照失败不影响录制。
        }
    }

    private void MaybeSaveSnapshot()
    {
        if (_journal is null || _lastSnapshotTimestamp == 0) return;
        long elapsed = (Stopwatch.GetTimestamp() - _lastSnapshotTimestamp) * 1000 / Stopwatch.Frequency;
        if (elapsed >= SnapshotIntervalMs) SaveSnapshot();
    }

    private string SerializeStroke(InkStroke stroke)
    {
        if (stroke is null) return null;
        if (!(SimplificationTolerance > 0)) return StrokeJsonCodec.Serialize(stroke);
        return StrokeJsonCodec.Serialize(StrokeSimplifier.Simplify(stroke, SimplificationTolerance));
    }

    private void SeedExistingStrokes()
    {
        for (int page = 0; page < _board.Pages.Count; page++)
        {
            foreach (var stroke in _board.Pages[page].Document.Strokes)
                AddEvent(TimelineEventKind.StrokeAdded, page, stroke.Id.ToString(), SerializeStroke(stroke));
        }
    }

    private void Bind(InkController controller)
    {
        UnbindController();
        _controller = controller;
        if (_controller is not null)
        {
            _controller.StrokeCommitted += OnStrokeCommitted;
            _controller.StrokesErased += OnStrokesErased;
            _controller.Cleared += OnCleared;
            _controller.Invalidated += OnControllerInvalidated;
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
    }

    private void OnBoardPageChanged()
    {
        if (!IsRecording) return;
        Bind(_board.Current);
        AddEvent(TimelineEventKind.PageChanged, _board.CurrentIndex);
    }

    private void OnBoardBackgroundChanged()
    {
        if (!IsRecording) return;
        AddEvent(TimelineEventKind.BackgroundChanged, _board.CurrentIndex, payload: _board.Background.ToString());
    }

    private void OnStrokeCommitted(InkStroke stroke)
    {
        if (!IsRecording || stroke is null) return;
        string id = stroke.Id.ToString();
        _knownIds.Add(id);
        AddEvent(TimelineEventKind.StrokeAdded, _board.CurrentIndex, id, SerializeStroke(stroke));
        SyncHistoryCounts();
    }

    private void OnStrokesErased(IReadOnlyList<InkStroke> strokes)
    {
        if (!IsRecording || strokes is null || strokes.Count == 0) return;
        var ids = strokes.Select(s => s.Id.ToString()).ToList();
        foreach (var id in ids) _knownIds.Remove(id);
        AddEvent(TimelineEventKind.StrokesRemoved, _board.CurrentIndex, ids: ids);
        SyncHistoryCounts();
    }

    private void OnCleared()
    {
        if (!IsRecording) return;
        _knownIds.Clear();
        AddEvent(TimelineEventKind.Cleared, _board.CurrentIndex);
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

        AddEvent(kind, _board.CurrentIndex, ids: removed,
            payload: replayStrokes.Count > 0 ? InkTimeline.EncodeAddedStrokes(replayStrokes) : null);

        _knownIds = current;
        SyncHistoryCounts();
    }

    private void SyncHistoryCounts()
    {
        if (_controller is null) return;
        _lastUndoCount = _controller.History.UndoCount;
        _lastRedoCount = _controller.History.RedoCount;
    }

    private void AddEvent(TimelineEventKind kind, int page,
        string strokeId = null, string strokeJson = null,
        IReadOnlyList<string> ids = null, string payload = null)
    {
        if (!IsRecording || Session is null) return;

        Append(new TimelineEvent
        {
            TimeMs = _clock.ElapsedMilliseconds,
            PageIndex = page,
            Kind = kind,
            StrokeId = strokeId,
            StrokeJson = strokeJson,
            StrokeIds = ids,
            Payload = payload,
        });
    }

    /// <summary>
    /// 线程安全地追加一个外部事件（PPT 翻页 / 点名 / 倒计时等）。
    /// 事件时间早于最后一个事件时按时间插入，并重建检查点（增量游标只支持尾部追加）。
    /// 标书签候选事件会自动补一条章节书签。
    /// </summary>
    public void Append(TimelineEvent ev)
    {
        if (ev is null || Session is null) return;

        lock (_gate)
        {
            if (!IsRecording) return;
            var events = Session.Events ??= new List<TimelineEvent>();

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

            // 增量日志在每次追加后立即进入缓冲，2 秒 / 50 个事件自动刷盘。
            _journal?.Append(ev);
            MaybeSaveSnapshot();
        }
    }

    private static bool IsBookmarkSource(TimelineEventKind kind)
        => kind is TimelineEventKind.PageChanged
            or TimelineEventKind.Cleared
            or TimelineEventKind.PptSlideChanged
            or TimelineEventKind.RosterDrawn
            or TimelineEventKind.TimerEnded;

    public void Dispose()
    {
        Stop();
        _controller = null;
    }
}