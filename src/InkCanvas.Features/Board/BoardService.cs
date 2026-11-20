using InkCanvas.Core.History;
using InkCanvas.Core.Ink;
using InkCanvas.Features.Persistence;
using InkCanvas.Ink;
using System.Numerics;
using System.Text.Json;

namespace InkCanvas.Features.Board;

public enum BoardBackground { Black, White, Grid, Lined, Green, Cream, Coordinate }

/// <summary>A raster image placed on a board page; position and size are device-independent pixels.</summary>
public sealed class BoardImageAsset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Image";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "image/png";
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    /// <summary>Optional camelCase JSON payload (for example AI paste source); empty for plain images.</summary>
    public string Metadata { get; set; } = "";

    public BoardImageAsset Copy() => new()
    {
        Id = Id,
        Name = Name,
        Data = Data.ToArray(),
        ContentType = ContentType,
        Left = Left,
        Top = Top,
        Width = Width,
        Height = Height,
        Metadata = Metadata ?? "",
    };
}

public readonly record struct BoardImageGeometry(float Left, float Top, float Width, float Height);

/// <summary>Lightweight page summary for thumbnail/list UIs (no bitmap rendering required).</summary>
public readonly record struct BoardPageThumbnail(
    int Index, string Name, BoardBackground Background, int StrokeCount, int ImageCount);

/// <summary>
/// Multi-page blackboard/whiteboard. Every page owns its ink controller, undo history, image
/// collection and <see cref="BoardPageState"/> (name, background, grid spacing, view transform).
/// Coordinates and sizes in both collections are stored in WinUI device-independent pixels,
/// not physical screen pixels.
/// </summary>
public sealed class BoardService
{
    public const int MaximumPages = 99;
    private const string BoardFormat = "inkcanvas-board";
    private const int BoardFormatVersion = 3;
    private const float MinimumGridSpacing = 12f;
    private const float MaximumGridSpacing = 160f;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly List<InkController> _pages = new() { new InkController() };
    private readonly List<List<BoardImageAsset>> _images = new() { new List<BoardImageAsset>() };
    private readonly List<BoardPageState> _states = new() { new BoardPageState { Name = "页面 1" } };

    /// <summary>Page-level undo/redo for structural operations (insert, copy, move, rename, delete).</summary>
    private readonly UndoStack _pageHistory = new(100);

    public int CurrentIndex { get; private set; }

    public int PageCount => _pages.Count;

    public bool HasContent => _pages.Any(page => page.Document.Count > 0)
        || _images.Any(images => images.Count > 0);

    public InkController Current => _pages[CurrentIndex];

    public IReadOnlyList<InkController> Pages => _pages;

    /// <summary>Visual state (name/background/grid/view) of the current page.</summary>
    public BoardPageState CurrentState => _states[CurrentIndex];

    /// <summary>Background of the current page. Kept for backward compatibility with callers.</summary>
    public BoardBackground Background
    {
        get => CurrentState.Background;
        set => CurrentState.Background = value;
    }

    /// <summary>Grid/lined/coordinate spacing of the current page, in device-independent pixels.</summary>
    public float GridSpacing
    {
        get => CurrentState.GridSpacing;
        set => CurrentState.GridSpacing = Math.Clamp(value, MinimumGridSpacing, MaximumGridSpacing);
    }

    public IReadOnlyList<BoardImageAsset> CurrentImages => _images[CurrentIndex];

    public bool CanUndoPage => _pageHistory.CanUndo;

    public bool CanRedoPage => _pageHistory.CanRedo;

    public event Action PageChanged;

    public event Action BackgroundChanged;

    public event Action ImagesChanged;

    public bool UndoPage() => _pageHistory.Undo();

    public bool RedoPage() => _pageHistory.Redo();

    public void SetBackground(BoardBackground background)
    {
        if (CurrentState.Background == background) return;
        CurrentState.Background = background;
        BackgroundChanged?.Invoke();
    }

    public void NextPage()
    {
        if (CurrentIndex < _pages.Count - 1)
        {
            GoTo(CurrentIndex + 1);
            return;
        }
        if (_pages.Count >= MaximumPages) return;
        SaveCurrentViewState();
        AddPageAt(_pages.Count);
        CurrentIndex = _pages.Count - 1;
        ApplyCurrentViewState();
        PageChanged?.Invoke();
    }

    public void PreviousPage()
    {
        if (CurrentIndex <= 0) return;
        GoTo(CurrentIndex - 1);
    }

    public void GoTo(int index)
    {
        if (index < 0 || index >= _pages.Count || index == CurrentIndex) return;
        SaveCurrentViewState();
        CurrentIndex = index;
        ApplyCurrentViewState();
        PageChanged?.Invoke();
    }

    public bool InsertPageAfterCurrent()
    {
        if (_pages.Count >= MaximumPages) return false;
        SaveCurrentViewState();
        AddPageAt(CurrentIndex + 1);
        CurrentIndex++;
        ApplyCurrentViewState();
        PageChanged?.Invoke();
        return true;
    }

    /// <summary>Deep-copy the current page (strokes, images and visual state) into a page right after it.</summary>
    public bool CopyCurrentPage()
    {
        if (_pages.Count >= MaximumPages) return false;
        SaveCurrentViewState();

        int source = CurrentIndex;
        var sourceState = _states[source];

        var controller = new InkController();
        var strokeCopies = _pages[source].Document.Strokes
            .Select(stroke => FromStrokeFile(ToStrokeFile(stroke)))
            .ToList();
        controller.Document.AddRange(strokeCopies);
        controller.ViewTransform = sourceState.ViewTransform;

        var state = new BoardPageState
        {
            Name = $"{sourceState.Name} 副本",
            Background = sourceState.Background,
            GridSpacing = sourceState.GridSpacing,
            ViewTransform = sourceState.ViewTransform,
        };
        var images = _images[source].Select(image => image.Copy()).ToList();

        int insertAt = source + 1;
        _pages.Insert(insertAt, controller);
        _images.Insert(insertAt, images);
        _states.Insert(insertAt, state);
        CurrentIndex = insertAt;
        ApplyCurrentViewState();

        _pageHistory.Push(new DelegateAction("复制页面",
            () =>
            {
                _pages.Insert(insertAt, controller);
                _images.Insert(insertAt, images);
                _states.Insert(insertAt, state);
                CurrentIndex = insertAt;
                ApplyCurrentViewState();
                PageChanged?.Invoke();
                ImagesChanged?.Invoke();
            },
            () =>
            {
                int at = _pages.IndexOf(controller);
                if (at < 0) return;
                _pages.RemoveAt(at);
                _images.RemoveAt(at);
                _states.RemoveAt(at);
                CurrentIndex = Math.Clamp(at, 0, _pages.Count - 1);
                ApplyCurrentViewState();
                PageChanged?.Invoke();
                ImagesChanged?.Invoke();
            }));

        PageChanged?.Invoke();
        ImagesChanged?.Invoke();
        return true;
    }

    /// <summary>Move a page to a new position. <paramref name="toIndex"/> is the final index.</summary>
    public bool MovePage(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _pages.Count) return false;
        toIndex = Math.Clamp(toIndex, 0, _pages.Count - 1);
        if (fromIndex == toIndex) return false;

        SaveCurrentViewState();
        var currentPage = _pages[CurrentIndex];
        MovePageCore(fromIndex, toIndex);
        CurrentIndex = _pages.IndexOf(currentPage);
        ApplyCurrentViewState();

        _pageHistory.Push(new DelegateAction("移动页面",
            () =>
            {
                MovePageCore(fromIndex, toIndex);
                CurrentIndex = Math.Clamp(CurrentIndex, 0, _pages.Count - 1);
                ApplyCurrentViewState();
                PageChanged?.Invoke();
            },
            () =>
            {
                MovePageCore(toIndex, fromIndex);
                CurrentIndex = Math.Clamp(CurrentIndex, 0, _pages.Count - 1);
                ApplyCurrentViewState();
                PageChanged?.Invoke();
            }));

        PageChanged?.Invoke();
        return true;
    }

    /// <summary>Rename a page. Empty/whitespace names are rejected.</summary>
    public bool RenamePage(int index, string name)
    {
        if (index < 0 || index >= _states.Count) return false;
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return false;
        var oldName = _states[index].Name;
        if (string.Equals(oldName, trimmed, StringComparison.Ordinal)) return false;

        _states[index].Name = trimmed;
        _pageHistory.Push(new DelegateAction("重命名页面",
            () => { _states[index].Name = trimmed; PageChanged?.Invoke(); },
            () => { _states[index].Name = oldName; PageChanged?.Invoke(); }));
        PageChanged?.Invoke();
        return true;
    }

    public string PageName(int index)
        => index >= 0 && index < _states.Count ? _states[index].Name : string.Empty;

    public BoardBackground PageBackground(int index)
        => index >= 0 && index < _states.Count ? _states[index].Background : BoardBackground.Black;

    public float PageGridSpacing(int index)
        => index >= 0 && index < _states.Count ? _states[index].GridSpacing : 0f;

    /// <summary>Metadata for one page, suitable for a text/colour thumbnail in the page list.</summary>
    public BoardPageThumbnail GetPageThumbnail(int index)
    {
        if (index < 0 || index >= _pages.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return new BoardPageThumbnail(
            index, _states[index].Name, _states[index].Background,
            _pages[index].Document.Count, _images[index].Count);
    }

    public IReadOnlyList<BoardPageThumbnail> GetPageThumbnails()
        => Enumerable.Range(0, _pages.Count).Select(GetPageThumbnail).ToList();

    public void RemoveCurrentPage()
    {
        if (_pages.Count == 1)
        {
            Current.Clear();
            Current.Selection.Clear();
            Current.History.Clear();
            _images[0].Clear();
            ImagesChanged?.Invoke();
            PageChanged?.Invoke();
            return;
        }

        SaveCurrentViewState();
        int index = CurrentIndex;
        var controller = _pages[index];
        var images = _images[index];
        var state = _states[index];

        _pages.RemoveAt(index);
        _images.RemoveAt(index);
        _states.RemoveAt(index);
        CurrentIndex = Math.Clamp(index, 0, _pages.Count - 1);
        ApplyCurrentViewState();

        // Do not dispose the removed controller: undo needs to reattach the exact same instance.
        _pageHistory.Push(new DelegateAction("删除页面",
            () =>
            {
                int at = _pages.IndexOf(controller);
                if (at < 0) return;
                _pages.RemoveAt(at);
                _images.RemoveAt(at);
                _states.RemoveAt(at);
                CurrentIndex = Math.Clamp(at, 0, _pages.Count - 1);
                ApplyCurrentViewState();
                PageChanged?.Invoke();
                ImagesChanged?.Invoke();
            },
            () =>
            {
                _pages.Insert(index, controller);
                _images.Insert(index, images);
                _states.Insert(index, state);
                CurrentIndex = index;
                ApplyCurrentViewState();
                PageChanged?.Invoke();
                ImagesChanged?.Invoke();
            }));

        PageChanged?.Invoke();
        ImagesChanged?.Invoke();
    }

    public bool AddImage(BoardImageAsset image)
    {
        if (image is null || image.Data is null || image.Data.Length == 0 || image.Width <= 0 || image.Height <= 0)
            return false;

        var pageImages = _images[CurrentIndex];
        int index = pageImages.Count;
        pageImages.Add(image);
        Current.History.Push(new DelegateAction("insert image",
            () => { pageImages.Insert(Math.Clamp(index, 0, pageImages.Count), image); ImagesChanged?.Invoke(); },
            () => { pageImages.Remove(image); ImagesChanged?.Invoke(); }));
        ImagesChanged?.Invoke();
        return true;
    }

    public void RemoveImage(BoardImageAsset image)
    {
        if (image is null) return;
        var pageImages = _images[CurrentIndex];
        int index = pageImages.IndexOf(image);
        if (index < 0) return;
        pageImages.RemoveAt(index);
        Current.History.Push(new DelegateAction("delete image",
            () => { pageImages.Remove(image); ImagesChanged?.Invoke(); },
            () => { pageImages.Insert(Math.Clamp(index, 0, pageImages.Count), image); ImagesChanged?.Invoke(); }));
        ImagesChanged?.Invoke();
    }

    /// <summary>
    /// Replace an existing image's raster data and metadata (for example after re-rendering an AI
    /// paste card), keeping its id, position and size. The change is undoable and raises
    /// <see cref="ImagesChanged"/>.
    /// </summary>
    public bool UpdateImage(BoardImageAsset image, byte[] newData, string newMetadata)
    {
        if (image is null || newData is null || newData.Length == 0) return false;
        var pageImages = _images[CurrentIndex];
        var target = pageImages.FirstOrDefault(candidate => candidate.Id == image.Id);
        if (target is null) return false;

        var previousData = target.Data;
        var previousMetadata = target.Metadata ?? "";
        string metadata = newMetadata ?? "";
        target.Data = newData;
        target.Metadata = metadata;

        Current.History.Push(new DelegateAction("update image",
            () => SetImageContent(target, newData, metadata),
            () => SetImageContent(target, previousData, previousMetadata)));
        ImagesChanged?.Invoke();
        return true;
    }

    /// <summary>Record one completed image move/resize as a single page-local undo step.</summary>
    public void RecordImageTransform(BoardImageAsset image, BoardImageGeometry before, BoardImageGeometry after)
    {
        if (image is null || !_images[CurrentIndex].Contains(image) || before == after) return;
        Current.History.Push(new DelegateAction("transform image",
            () => SetImageGeometry(image, after),
            () => SetImageGeometry(image, before)));
        ImagesChanged?.Invoke();
    }

    public void SavePage(string path)
    {
        if (_images[CurrentIndex].Count == 0)
        {
            InkFileStore.Save(path, Current.Document.Strokes);
            return;
        }
        SaveBoardFile(path, new[] { CurrentIndex });
    }

    public void SaveBoard(string path) => SaveBoardFile(path, Enumerable.Range(0, _pages.Count));

    /// <summary>Load a legacy single-page .icink or a board/page package saved by this service.</summary>
    public void LoadPage(string path)
    {
        if (TryReadBoardFile(path, out var model))
        {
            var page = model.Pages?.FirstOrDefault() ?? new BoardPageFile();
            ReplacePage(CurrentIndex, page);
        }
        else
        {
            ReplacePage(CurrentIndex, new BoardPageFile
            {
                Strokes = InkFileStore.Load(path).Select(ToStrokeFile).ToList(),
            });
        }
        PageChanged?.Invoke();
        ImagesChanged?.Invoke();
    }

    /// <summary>Open a board package, or treat a legacy .icink as a one-page board.</summary>
    public void LoadBoard(string path)
    {
        List<BoardPageFile> loadedPages;
        BoardBackground fallbackBackground = CurrentState.Background;
        float fallbackGridSpacing = CurrentState.GridSpacing;
        if (TryReadBoardFile(path, out var model))
        {
            loadedPages = model.Pages ?? new List<BoardPageFile>();
            if (loadedPages.Count == 0) loadedPages.Add(new BoardPageFile());
            if (loadedPages.Count > MaximumPages)
                throw new InvalidDataException($"A board can contain at most {MaximumPages} pages.");
            if (Enum.TryParse(model.Background, ignoreCase: true, out BoardBackground parsed)) fallbackBackground = parsed;
            if (model.GridSpacing > 0) fallbackGridSpacing = model.GridSpacing;
        }
        else
        {
            loadedPages = new List<BoardPageFile>
            {
                new() { Strokes = InkFileStore.Load(path).Select(ToStrokeFile).ToList() },
            };
        }

        foreach (var page in _pages) page.Dispose();
        _pages.Clear();
        _images.Clear();
        _states.Clear();

        for (int i = 0; i < loadedPages.Count; i++)
        {
            var page = loadedPages[i];
            var controller = new InkController();
            controller.Document.AddRange((page.Strokes ?? new List<StrokeFile>()).Select(FromStrokeFile));

            var pageBackground = Enum.TryParse(page.Background, ignoreCase: true, out BoardBackground parsedPage)
                ? parsedPage : fallbackBackground;
            float pageGridSpacing = page.GridSpacing > 0 ? page.GridSpacing : fallbackGridSpacing;
            var viewTransform = FromTransformArray(page.ViewTransform);
            controller.ViewTransform = viewTransform;

            _pages.Add(controller);
            _images.Add((page.Images ?? new List<BoardImageAsset>()).Where(IsValidImage).Select(image => image.Copy()).ToList());
            _states.Add(new BoardPageState
            {
                Name = string.IsNullOrWhiteSpace(page.Name) ? $"页面 {i + 1}" : page.Name,
                Background = pageBackground,
                GridSpacing = Math.Clamp(pageGridSpacing, MinimumGridSpacing, MaximumGridSpacing),
                ViewTransform = viewTransform,
            });
        }

        CurrentIndex = 0;
        ApplyCurrentViewState();
        BackgroundChanged?.Invoke();
        PageChanged?.Invoke();
        ImagesChanged?.Invoke();
    }

    private void SaveBoardFile(string path, IEnumerable<int> pageIndices)
    {
        SaveCurrentViewState();
        var indices = pageIndices?.ToList() ?? new List<int>();
        if (indices.Count == 0) indices.Add(CurrentIndex);
        int first = indices[0];

        var file = new BoardFile
        {
            Format = BoardFormat,
            Version = BoardFormatVersion,
            Background = _states[first].Background.ToString(),
            GridSpacing = _states[first].GridSpacing,
            Pages = indices.Select(index => new BoardPageFile
            {
                Name = _states[index].Name,
                Background = _states[index].Background.ToString(),
                GridSpacing = _states[index].GridSpacing,
                ViewTransform = ToTransformArray(_states[index].ViewTransform),
                Strokes = _pages[index].Document.Strokes.Select(ToStrokeFile).ToList(),
                Images = _images[index].Select(image => image.Copy()).ToList(),
            }).ToList(),
        };
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool TryReadBoardFile(string path, out BoardFile model)
    {
        model = null;
        if (!File.Exists(path)) return false;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Format", out var format) || format.GetString() != BoardFormat)
            return false;
        model = JsonSerializer.Deserialize<BoardFile>(document.RootElement.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("The board file is empty or invalid.");
        // Version 1 predates per-page metadata; its pages simply fall back to defaults on load.
        if (model.Version < 1 || model.Version > BoardFormatVersion)
            throw new InvalidDataException($"Unsupported board file version {model.Version}.");
        return true;
    }

    private void ReplacePage(int index, BoardPageFile page)
    {
        var controller = _pages[index];
        controller.Document.Clear();
        controller.History.Clear();
        controller.Selection.Clear();
        var viewTransform = FromTransformArray(page.ViewTransform);
        controller.ViewTransform = viewTransform;
        controller.Document.AddRange((page.Strokes ?? new List<StrokeFile>()).Select(FromStrokeFile));
        _images[index] = (page.Images ?? new List<BoardImageAsset>()).Where(IsValidImage).Select(image => image.Copy()).ToList();

        if (!string.IsNullOrWhiteSpace(page.Name)) _states[index].Name = page.Name;
        if (Enum.TryParse(page.Background, ignoreCase: true, out BoardBackground parsed)) _states[index].Background = parsed;
        if (page.GridSpacing > 0) _states[index].GridSpacing = Math.Clamp(page.GridSpacing, MinimumGridSpacing, MaximumGridSpacing);
        _states[index].ViewTransform = viewTransform;
    }

    private void AddPageAt(int index)
    {
        var basis = _states[Math.Clamp(CurrentIndex, 0, _states.Count - 1)];
        _pages.Insert(index, new InkController());
        _images.Insert(index, new List<BoardImageAsset>());
        _states.Insert(index, new BoardPageState
        {
            Name = NextPageName(),
            Background = basis.Background,
            GridSpacing = basis.GridSpacing,
            ViewTransform = Matrix3x2.Identity,
        });
    }

    private void MovePageCore(int fromIndex, int toIndex)
    {
        var page = _pages[fromIndex];
        _pages.RemoveAt(fromIndex);
        _pages.Insert(toIndex, page);

        var images = _images[fromIndex];
        _images.RemoveAt(fromIndex);
        _images.Insert(toIndex, images);

        var state = _states[fromIndex];
        _states.RemoveAt(fromIndex);
        _states.Insert(toIndex, state);
    }

    private string NextPageName()
    {
        int number = _states.Count + 1;
        string name = $"页面 {number}";
        while (_states.Any(state => string.Equals(state.Name, name, StringComparison.Ordinal)))
        {
            number++;
            name = $"页面 {number}";
        }
        return name;
    }

    private void SaveCurrentViewState()
    {
        if (CurrentIndex >= 0 && CurrentIndex < _states.Count && CurrentIndex < _pages.Count)
            _states[CurrentIndex].ViewTransform = _pages[CurrentIndex].ViewTransform;
    }

    private void ApplyCurrentViewState()
    {
        if (CurrentIndex >= 0 && CurrentIndex < _states.Count && CurrentIndex < _pages.Count)
            _pages[CurrentIndex].ViewTransform = _states[CurrentIndex].ViewTransform;
    }

    private static float[] ToTransformArray(Matrix3x2 transform)
        => new[] { transform.M11, transform.M12, transform.M21, transform.M22, transform.M31, transform.M32 };

    private static Matrix3x2 FromTransformArray(float[] values)
    {
        if (values is null || values.Length < 6) return Matrix3x2.Identity;
        return new Matrix3x2(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    private static bool IsValidImage(BoardImageAsset image)
        => image is not null && image.Data is { Length: > 0 } && image.Width > 0 && image.Height > 0;

    private void SetImageContent(BoardImageAsset image, byte[] data, string metadata)
    {
        image.Data = data;
        image.Metadata = metadata ?? "";
        ImagesChanged?.Invoke();
    }

    private void SetImageGeometry(BoardImageAsset image, BoardImageGeometry geometry)
    {
        image.Left = geometry.Left;
        image.Top = geometry.Top;
        image.Width = geometry.Width;
        image.Height = geometry.Height;
        ImagesChanged?.Invoke();
    }

    private static StrokeFile ToStrokeFile(InkStroke stroke) => new()
    {
        Color = stroke.Attributes.Color.PackedArgb,
        Width = stroke.Attributes.BaseWidth,
        Highlighter = stroke.Attributes.IsHighlighter,
        Taper = (int)stroke.Attributes.TaperKind,
        Shape = stroke.IsRecognizedShape,
        Points = stroke.Points.Select(point => new[]
        {
            point.X, point.Y, point.Pressure, point.Width, point.TimeMs,
        }).ToList(),
    };

    private static InkStroke FromStrokeFile(StrokeFile strokeFile)
    {
        var attributes = new StrokeAttributes
        {
            Color = InkColor.FromPackedArgb(strokeFile.Color),
            BaseWidth = strokeFile.Width,
            IsHighlighter = strokeFile.Highlighter,
            TaperKind = Enum.IsDefined(typeof(TaperKind), strokeFile.Taper)
                ? (TaperKind)strokeFile.Taper : TaperKind.None,
        };
        var stroke = new InkStroke(attributes) { IsRecognizedShape = strokeFile.Shape };
        stroke.RestorePoints((strokeFile.Points ?? new List<float[]>())
            .Where(row => row.Length >= 4)
            .Select(row => new InkPoint(row[0], row[1], row[2], row[3], row.Length > 4 ? row[4] : 0f)));
        stroke.Complete();
        return stroke;
    }

    private sealed class BoardFile
    {
        public string Format { get; set; }
        public int Version { get; set; }
        public string Background { get; set; }
        public float GridSpacing { get; set; }
        public List<BoardPageFile> Pages { get; set; } = new();
    }

    private sealed class BoardPageFile
    {
        public string Name { get; set; }
        public string Background { get; set; }
        public float GridSpacing { get; set; }
        public float[] ViewTransform { get; set; }
        public List<StrokeFile> Strokes { get; set; } = new();
        public List<BoardImageAsset> Images { get; set; } = new();
    }

    private sealed class StrokeFile
    {
        public uint Color { get; set; }
        public float Width { get; set; }
        public bool Highlighter { get; set; }
        public int Taper { get; set; }
        public bool Shape { get; set; }
        public List<float[]> Points { get; set; } = new();
    }
}
