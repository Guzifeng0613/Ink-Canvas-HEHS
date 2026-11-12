using InkCanvas.Ink;

namespace InkCanvas.Features.Board;

public enum BoardBackground { Black, White, Grid, Lined }

/// <summary>
/// Multi-page blackboard/whiteboard. Each page owns its own <see cref="InkController"/>, so
/// document and undo history are per-page and flipping pages never mixes them.
/// </summary>
public sealed class BoardService
{
    private readonly List<InkController> _pages = new() { new InkController() };

    public BoardBackground Background { get; set; } = BoardBackground.Black;

    /// <summary>Grid/lined spacing in device-independent pixels.</summary>
    public float GridSpacing { get; set; } = 40f;

    public int CurrentIndex { get; private set; }

    public int PageCount => _pages.Count;

    public InkController Current => _pages[CurrentIndex];

    public IReadOnlyList<InkController> Pages => _pages;

    public event Action PageChanged;

    public event Action BackgroundChanged;

    public void SetBackground(BoardBackground background)
    {
        if (Background == background) return;
        Background = background;
        BackgroundChanged?.Invoke();
    }

    public void NextPage()
    {
        if (CurrentIndex >= _pages.Count - 1) _pages.Add(new InkController());
        CurrentIndex++;
        PageChanged?.Invoke();
    }

    public void PreviousPage()
    {
        if (CurrentIndex <= 0) return;
        CurrentIndex--;
        PageChanged?.Invoke();
    }

    public void GoTo(int index)
    {
        if (index < 0 || index >= _pages.Count || index == CurrentIndex) return;
        CurrentIndex = index;
        PageChanged?.Invoke();
    }

    public void InsertPageAfterCurrent()
    {
        _pages.Insert(CurrentIndex + 1, new InkController());
        CurrentIndex++;
        PageChanged?.Invoke();
    }

    public void RemoveCurrentPage()
    {
        if (_pages.Count == 1)
        {
            _pages[0].Clear();
            PageChanged?.Invoke();
            return;
        }
        _pages.RemoveAt(CurrentIndex);
        CurrentIndex = Math.Clamp(CurrentIndex, 0, _pages.Count - 1);
        PageChanged?.Invoke();
    }
}