using InkCanvas.Core.Ink;
using InkCanvas.Features.Persistence;
using System.Text.Json;

namespace InkCanvas.Features.Ppt;

/// <summary>
/// File-backed per-presentation ink archive. The directory shape mirrors the old WPF layout
/// (&lt;root&gt;/Auto Saved - Presentations/&lt;name&gt;_&lt;slide-count&gt;), but stroke data is stored as
/// the current app's .icink plus a manifest, never as legacy WPF .icstk/.icart ISF.
/// </summary>
public sealed class PptInkFileStore : IPptInkStore
{
    private const string ManifestName = "InkCanvas-Ppt.json";
    private const string PositionName = "Position";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;

    public PptInkFileStore(string autoSaveRoot)
    {
        _root = string.IsNullOrWhiteSpace(autoSaveRoot)
            ? throw new ArgumentException("An auto-save root directory is required.", nameof(autoSaveRoot))
            : Path.GetFullPath(autoSaveRoot);
    }

    public PptInkArchive Load(PptPresentationInfo presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        string directory = GetPresentationDirectory(presentation);
        if (!Directory.Exists(directory)) return new PptInkArchive(presentation, null, null, Array.Empty<PptStoredSlide>());

        var slides = new List<PptStoredSlide>();
        int? lastIndex = null;
        int? lastPosition = null;
        bool hasLegacy = Directory.EnumerateFiles(directory, "*.icstk", SearchOption.TopDirectoryOnly).Any() ||
            Directory.EnumerateFiles(directory, "*.icart", SearchOption.TopDirectoryOnly).Any();
        string manifestPath = Path.Combine(directory, ManifestName);
        if (File.Exists(manifestPath))
        {
            try
            {
                Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), JsonOptions);
                if (manifest?.Slides is not null)
                {
                    lastIndex = manifest.LastSlideIndex;
                    lastPosition = manifest.LastShowPosition;
                    foreach (var entry in manifest.Slides)
                    {
                        string file = SafeManifestFile(directory, entry.File);
                        if (file is null || !File.Exists(file)) continue;
                        try { slides.Add(new PptStoredSlide(entry.SlideId, entry.SlideIndex, InkFileStore.Load(file))); }
                        catch { /* Other slides remain available when one file is damaged. */ }
                    }
                }
            }
            catch { /* Preserve legacy-file diagnosis when a manifest is damaged. */ }
        }
        string positionPath = Path.Combine(directory, PositionName);
        if (lastIndex is null && File.Exists(positionPath) &&
            int.TryParse(File.ReadAllText(positionPath), out int legacyPosition) && legacyPosition > 0)
        {
            lastIndex = legacyPosition;
            lastPosition = legacyPosition;
        }
        return new PptInkArchive(presentation, lastIndex, lastPosition, slides, hasLegacy);
    }

    public void Save(PptInkArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        string directory = GetPresentationDirectory(archive.Presentation);
        Directory.CreateDirectory(directory);
        string manifestPath = Path.Combine(directory, ManifestName);
        Manifest previous = null;
        try { if (File.Exists(manifestPath)) previous = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), JsonOptions); }
        catch { /* The new manifest may replace an unreadable one; old data files stay untouched. */ }

        var manifest = new Manifest
        {
            LastSlideIndex = archive.LastSlideIndex,
            LastShowPosition = archive.LastShowPosition,
            Slides = new List<ManifestSlide>(),
        };
        foreach (PptStoredSlide item in archive.Slides ?? Array.Empty<PptStoredSlide>())
        {
            if (item is null || item.SlideId <= 0 || item.SlideIndex <= 0 || item.Strokes is null || item.Strokes.Count == 0)
                continue;
            string fileName = $"slide-{item.SlideId}.icink";
            InkFileStore.Save(Path.Combine(directory, fileName), item.Strokes);
            manifest.Slides.Add(new ManifestSlide { SlideId = item.SlideId, SlideIndex = item.SlideIndex, File = fileName });
        }

        string temp = manifestPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temp, manifestPath, overwrite: true);
        if (archive.LastSlideIndex is > 0)
            File.WriteAllText(Path.Combine(directory, PositionName), archive.LastSlideIndex.Value.ToString());

        var retained = manifest.Slides.Select(s => s.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestSlide old in previous?.Slides ?? new List<ManifestSlide>())
        {
            if (retained.Contains(old.File)) continue;
            string obsolete = SafeManifestFile(directory, old.File);
            if (obsolete is not null && File.Exists(obsolete)) File.Delete(obsolete);
        }
    }

    private string GetPresentationDirectory(PptPresentationInfo presentation) =>
        Path.Combine(_root, "Auto Saved - Presentations",
            $"{SafeFilePart(presentation.Name)}_{Math.Max(0, presentation.SlideCount)}");

    private static string SafeManifestFile(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ||
            !fileName.EndsWith(InkFileStore.Extension, StringComparison.OrdinalIgnoreCase)) return null;
        string full = Path.GetFullPath(Path.Combine(directory, fileName));
        return full.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full : null;
    }

    private static string SafeFilePart(string value)
    {
        string result = string.IsNullOrWhiteSpace(value) ? "Presentation" : value;
        foreach (char c in Path.GetInvalidFileNameChars()) result = result.Replace(c, '_');
        return result.Replace('/', '_').Replace('\\', '_');
    }

    private sealed class Manifest
    {
        public int Version { get; set; } = 1;
        public int? LastSlideIndex { get; set; }
        public int? LastShowPosition { get; set; }
        public List<ManifestSlide> Slides { get; set; } = new();
    }
    private sealed class ManifestSlide
    {
        public int SlideId { get; set; }
        public int SlideIndex { get; set; }
        public string File { get; set; }
    }
}
