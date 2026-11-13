using InkCanvas.Core.Ink;
using InkCanvas.Core.Settings;
using System.Text.Json;

namespace InkCanvas.Features.Persistence;

/// <summary>Loads and saves settings, tolerating a missing or corrupt file.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public SettingsStore(string directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InkCanvas");
    }

    public string Directory { get; }

    public string SettingsPath => Path.Combine(Directory, "Settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            // A corrupt settings file must never stop the app from starting.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch
        {
            // Disk full / locked file: keep running with in-memory settings.
        }
    }
}

/// <summary>
/// Stroke persistence.
/// <para>
/// The WPF original stored WPF's Ink Serialized Format (ISF), which depends on
/// <c>System.Windows.Ink</c> and does not exist on WinUI 3. This is a deliberate compatibility
/// break: files use our own compact JSON (<c>.icink</c>) and old <c>.isf</c> archives cannot be
/// read. Per-point widths are stored so a reloaded stroke renders exactly as it was drawn.
/// </para>
/// </summary>
public static class InkFileStore
{
    public const string Extension = ".icink";
    private const int FormatVersion = 1;

    private sealed class FileModel
    {
        public int Version { get; set; } = FormatVersion;
        public List<StrokeModel> Strokes { get; set; } = new();
    }

    private sealed class StrokeModel
    {
        public uint Color { get; set; }
        public float Width { get; set; }
        public bool Highlighter { get; set; }
        public byte HighlighterAlpha { get; set; } = 96;
        public int Taper { get; set; }
        public bool Shape { get; set; }
        /// <summary>Rows of [x, y, pressure, width, timeMs].</summary>
        public List<float[]> Points { get; set; } = new();
    }

    public static void Save(string path, IEnumerable<InkStroke> strokes)
    {
        var model = new FileModel();
        foreach (var s in strokes)
        {
            var sm = new StrokeModel
            {
                Color = s.Attributes.Color.PackedArgb,
                Width = s.Attributes.BaseWidth,
                Highlighter = s.Attributes.IsHighlighter,
                HighlighterAlpha = s.Attributes.HighlighterAlpha,
                Taper = (int)s.TaperKind,
                Shape = s.IsRecognizedShape,
            };
            foreach (var p in s.Points)
                sm.Points.Add(new[] { p.X, p.Y, p.Pressure, p.Width, p.TimeMs });
            model.Strokes.Add(sm);
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(model));
    }

    public static List<InkStroke> Load(string path)
    {
        var result = new List<InkStroke>();
        if (!File.Exists(path)) return result;
        var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path));
        if (model?.Strokes is null) return result;

        foreach (var sm in model.Strokes)
        {
            var attrs = new StrokeAttributes
            {
                Color = InkColor.FromPackedArgb(sm.Color),
                BaseWidth = sm.Width,
                IsHighlighter = sm.Highlighter,
                HighlighterAlpha = sm.HighlighterAlpha,
                TaperKind = (TaperKind)sm.Taper,
            };
            var stroke = new InkStroke(attrs) { IsRecognizedShape = sm.Shape };
            // Restore stored widths verbatim: re-running the taper here would change history.
            stroke.RestorePoints(sm.Points
                .Where(r => r.Length >= 4)
                .Select(r => new InkPoint(r[0], r[1], r[2], r[3], r.Length > 4 ? r[4] : 0f)));
            stroke.Complete();
            result.Add(stroke);
        }
        return result;
    }
}

/// <summary>Writes stroke snapshots on clear/screenshot/exit, and prunes old ones.</summary>
public sealed class AutoSaveService
{
    public AutoSaveService(Core.Settings.AutomationSettings settings)
    {
        Settings = settings;
    }

    public Core.Settings.AutomationSettings Settings { get; }

    public string SaveSnapshot(IEnumerable<InkStroke> strokes, string reason)
    {
        var list = strokes.ToList();
        if (list.Count < Math.Max(0, Settings.MinimumAutomationStrokeNumber)) return null;
        try
        {
            string root = Settings.AutoSavedStrokesLocation;
            if (string.IsNullOrWhiteSpace(root)) return null;
            string dir = Settings.IsSaveScreenshotsInDateFolders
                ? Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd"))
                : root;
            System.IO.Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{DateTime.Now:HHmmss}-{reason}{InkFileStore.Extension}");
            InkFileStore.Save(path, list);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes snapshots older than the configured threshold. Opt-in.</summary>
    public int Prune()
    {
        if (!Settings.AutoDelSavedFiles) return 0;
        int removed = 0;
        try
        {
            string root = Settings.AutoSavedStrokesLocation;
            if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root)) return 0;
            var cutoff = DateTime.Now.AddDays(-Math.Max(1, Settings.AutoDelSavedFilesDaysThreshold));
            foreach (var file in System.IO.Directory.EnumerateFiles(root, "*" + InkFileStore.Extension, SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff) { File.Delete(file); removed++; }
                }
                catch { /* individual file may be locked */ }
            }
        }
        catch { }
        return removed;
    }
}