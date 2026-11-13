using InkCanvas.Core.History;
using InkCanvas.Core.Ink;
using InkCanvas.Features.Persistence;
using InkCanvas.Ink;
using System.Numerics;
using System.Text.Json;

namespace InkCanvas.Features.Board;

public enum BoardBackground { Black, White, Grid, Lined }

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
    };
}

public readonly record struct BoardImageGeometry(float Left, float Top, float Width, float Height);

/// <summary>
/// Multi-page blackboard/whiteboard. Every page owns its ink controller, undo history and image
/// collection. Coordinates and sizes in both collections are stored in WinUI device-independent
/// pixels, not physical screen pixels.
/// </summary>
public sealed class BoardService
{
    public const int MaximumPages = 99;
    private const string BoardFormat = "inkcanvas-board";
    private const int BoardFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly List<InkController> _pages = new() { new InkController() };
    private readonly List<List<BoardImageAsset>> _images = new() { new List<BoardImageAsset>() };

    public BoardBackground Background { get; set; } = BoardBackground.Black;

    /// <summary>Grid/lined spacing in device-independent pixels.</summary>
    public float GridSpacing { get; set; } = 40f;

    public int CurrentIndex { get; private set; }

    public int PageCount => _pages.Count;

    public bool HasContent => _pages.Any(page => page.Document.Count > 0)
        || _images.Any(images => images.Count > 0);

    public InkController Current => _pages[CurrentIndex];

    public IReadOnlyList<InkController> Pages => _pages;

    public IReadOnlyList<BoardImageAsset> CurrentImages => _images[CurrentIndex];

    public event Action PageChanged;

    public event Action BackgroundChanged;

    public event Action ImagesChanged;

    public void SetBackground(BoardBackground background)
    {
        if (Background == background) return;
        Background = background;
        BackgroundChanged?.Invoke();
    }

    public void NextPage()
    {
        if (CurrentIndex < _pages.Count - 1)
        {
            CurrentIndex++;
            PageChanged?.Invoke();
            return;
        }
        if (_pages.Count >= MaximumPages) return;
        AddPageAt(_pages.Count);
        CurrentIndex = _pages.Count - 1;
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

    public bool InsertPageAfterCurrent()
    {
        if (_pages.Count >= MaximumPages) return false;
        AddPageAt(CurrentIndex + 1);
        CurrentIndex++;
        PageChanged?.Invoke();
        return true;
    }

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

        _pages[CurrentIndex].Dispose();
        _pages.RemoveAt(CurrentIndex);
        _images.RemoveAt(CurrentIndex);
        CurrentIndex = Math.Clamp(CurrentIndex, 0, _pages.Count - 1);
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
        BoardBackground background = Background;
        float gridSpacing = GridSpacing;
        if (TryReadBoardFile(path, out var model))
        {
            loadedPages = model.Pages ?? new List<BoardPageFile>();
            if (loadedPages.Count == 0) loadedPages.Add(new BoardPageFile());
            if (loadedPages.Count > MaximumPages)
                throw new InvalidDataException($"A board can contain at most {MaximumPages} pages.");
            if (Enum.TryParse(model.Background, ignoreCase: true, out BoardBackground parsed)) background = parsed;
            if (model.GridSpacing > 0) gridSpacing = model.GridSpacing;
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
        foreach (var page in loadedPages)
        {
            var controller = new InkController();
            controller.Document.AddRange((page.Strokes ?? new List<StrokeFile>()).Select(FromStrokeFile));
            _pages.Add(controller);
            _images.Add((page.Images ?? new List<BoardImageAsset>()).Where(IsValidImage).Select(image => image.Copy()).ToList());
        }
        CurrentIndex = 0;
        GridSpacing = Math.Clamp(gridSpacing, 12f, 160f);
        if (Background != background)
        {
            Background = background;
            BackgroundChanged?.Invoke();
        }
        PageChanged?.Invoke();
        ImagesChanged?.Invoke();
    }

    private void SaveBoardFile(string path, IEnumerable<int> pageIndices)
    {
        var file = new BoardFile
        {
            Format = BoardFormat,
            Version = BoardFormatVersion,
            Background = Background.ToString(),
            GridSpacing = GridSpacing,
            Pages = pageIndices.Select(index => new BoardPageFile
            {
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
        if (model.Version != BoardFormatVersion)
            throw new InvalidDataException($"Unsupported board file version {model.Version}.");
        return true;
    }

    private void ReplacePage(int index, BoardPageFile page)
    {
        var controller = _pages[index];
        controller.Document.Clear();
        controller.History.Clear();
        controller.Selection.Clear();
        controller.ViewTransform = Matrix3x2.Identity;
        controller.Document.AddRange((page.Strokes ?? new List<StrokeFile>()).Select(FromStrokeFile));
        _images[index] = (page.Images ?? new List<BoardImageAsset>()).Where(IsValidImage).Select(image => image.Copy()).ToList();
    }

    private void AddPageAt(int index)
    {
        _pages.Insert(index, new InkController());
        _images.Insert(index, new List<BoardImageAsset>());
    }

    private static bool IsValidImage(BoardImageAsset image)
        => image is not null && image.Data is { Length: > 0 } && image.Width > 0 && image.Height > 0;

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
