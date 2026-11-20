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
    public const int CurrentManifestVersion = 2;
    private const string ManifestName = "InkCanvas-Ppt.json";
    private const string PositionName = "Position";
    private const string SlideFilePrefix = "slide-";
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
        int? lastSlideId = null;
        int? lastPosition = null;
        int manifestVersion = 1;
        bool manifestRead = false;
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
                    manifestRead = true;
                    manifestVersion = manifest.Version;
                    lastIndex = manifest.LastSlideIndex;
                    lastSlideId = manifest.LastSlideId;
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
        // Index-named files are the pre-manifest layout; only look for them when no manifest
        // described this directory (id-named files must never be misread as page numbers).
        if (!manifestRead && slides.Count == 0) slides.AddRange(LoadIndexNamedSlides(directory));
        string positionPath = Path.Combine(directory, PositionName);
        if (lastIndex is null && File.Exists(positionPath) &&
            int.TryParse(File.ReadAllText(positionPath), out int legacyPosition) && legacyPosition > 0)
        {
            lastIndex = legacyPosition;
            lastPosition = legacyPosition;
        }
        return new PptInkArchive(presentation, lastIndex, lastPosition, slides, hasLegacy,
            lastSlideId, manifestVersion);
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
            Version = CurrentManifestVersion,
            LastSlideIndex = archive.LastSlideIndex,
            LastSlideId = archive.LastSlideId is > 0 ? archive.LastSlideId : null,
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
        // Legacy (pre-v2) files are never pruned: they may be the only copy the migrator left
        // behind, and the migration contract forbids deleting old data.
        if (previous?.Version >= CurrentManifestVersion)
        {
            foreach (ManifestSlide old in previous.Slides ?? new List<ManifestSlide>())
            {
                if (retained.Contains(old.File)) continue;
                string obsolete = SafeManifestFile(directory, old.File);
                if (obsolete is not null && File.Exists(obsolete)) File.Delete(obsolete);
            }
        }
    }

    /// <summary>
    /// Reads a single slide's ink straight from the manifest file layout. Used by the neighbor
    /// prefetcher so it never has to touch the session's in-memory state from a worker thread.
    /// Returns <c>null</c> when the slide has no file or it is damaged.
    /// </summary>
    public IReadOnlyList<InkStroke> TryLoadSlide(PptPresentationInfo presentation, int slideId)
    {
        if (presentation is null || slideId <= 0) return null;
        try
        {
            string directory = GetPresentationDirectory(presentation);
            string file = Path.Combine(directory, $"{SlideFilePrefix}{slideId}{InkFileStore.Extension}");
            if (!File.Exists(file)) return null;
            List<InkStroke> strokes = InkFileStore.Load(file);
            return strokes is { Count: > 0 } ? strokes : null;
        }
        catch { return null; }
    }

    /// <summary>Public so callers can back up or migrate exactly the directory Load reads.</summary>
    public string GetPresentationDirectory(PptPresentationInfo presentation) =>
        Path.Combine(_root, "Auto Saved - Presentations",
            $"{SafeFilePart(presentation.Name)}_{Math.Max(0, presentation.SlideCount)}");

    /// <summary>
    /// Legacy archives stored one file per page number (<c>slide-3.icink</c>) with no manifest.
    /// Returns them with SlideId 0 and the page number as SlideIndex so the migrator can map
    /// them onto live slide ids by index.
    /// </summary>
    private static IEnumerable<PptStoredSlide> LoadIndexNamedSlides(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, SlideFilePrefix + "*" + InkFileStore.Extension,
                     SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith(SlideFilePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(name.AsSpan(SlideFilePrefix.Length), out int index) || index <= 0) continue;
            List<InkStroke> strokes;
            try { strokes = InkFileStore.Load(file); }
            catch { continue; }
            if (strokes.Count == 0) continue;
            yield return new PptStoredSlide(0, index, strokes);
        }
    }

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
        public int? LastSlideId { get; set; }
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
