using InkCanvas.Core.Versioning;
using System.Text.Json;

namespace InkCanvas.Core.Update;

/// <summary>A published release with a downloadable installer.</summary>
public sealed record ReleaseInfo(
    string Tag,
    AppVersion Version,
    string Name,
    string Notes,
    string PageUrl,
    string InstallerUrl,
    long InstallerSize)
{
    public bool IsNewerThan(AppVersion current) => Version > current;
}

/// <summary>
/// Parses GitHub release payloads. Pure JSON handling — the HTTP call lives in the features
/// layer so this stays unit-testable and offline.
/// </summary>
public static class ReleaseFeed
{
    /// <summary>Parses the <c>/releases/latest</c> payload (GitHub already excludes drafts).</summary>
    public static ReleaseInfo ParseLatest(string json)
    {
        var element = TryRead(json);
        if (element is null || element.Value.ValueKind != JsonValueKind.Object) return null;
        return FromElement(element.Value);
    }

    /// <summary>Parses a <c>/releases</c> array and returns the highest stable version.</summary>
    public static ReleaseInfo ParseNewestStable(string json)
    {
        var element = TryRead(json);
        if (element is null || element.Value.ValueKind != JsonValueKind.Array) return null;

        ReleaseInfo best = null;
        foreach (var item in element.Value.EnumerateArray())
        {
            if (Bool(item, "draft") || Bool(item, "prerelease")) continue;
            var info = FromElement(item);
            if (info is null) continue;
            if (best is null || info.Version > best.Version) best = info;
        }
        return best;
    }

    private static JsonElement? TryRead(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // The document must outlive the element, so copy the root out of a cloned document.
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReleaseInfo FromElement(JsonElement e)
    {
        string tag = Str(e, "tag_name");
        if (!AppVersion.TryParse(tag, out var version)) return null;

        var (url, size) = PickInstaller(e);
        if (url is null) return null;

        return new ReleaseInfo(tag, version, Str(e, "name"), Str(e, "body"),
            Str(e, "html_url"), url, size);
    }

    /// <summary>Prefers an installer-looking .exe over any other executable asset.</summary>
    private static (string url, long size) PickInstaller(JsonElement e)
    {
        if (!e.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, 0);

        string fallbackUrl = null;
        long fallbackSize = 0;
        foreach (var a in assets.EnumerateArray())
        {
            string name = Str(a, "name");
            if (string.IsNullOrEmpty(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            string url = Str(a, "browser_download_url");
            if (string.IsNullOrEmpty(url)) continue;
            long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;

            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                return (url, size);
            fallbackUrl ??= url;
            if (fallbackSize == 0) fallbackSize = size;
        }
        return (fallbackUrl, fallbackSize);
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : string.Empty;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}