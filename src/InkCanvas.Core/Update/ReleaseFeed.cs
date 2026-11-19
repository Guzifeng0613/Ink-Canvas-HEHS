using InkCanvas.Core.Versioning;
using System.Text.Json;

namespace InkCanvas.Core.Update;

public sealed record ReleaseInfo(string Tag, AppVersion Version, string Name, string Notes,
    string PageUrl, string InstallerUrl, long InstallerSize)
{
    public string AssetName { get; init; }
    public string Sha256 { get; init; }
    public string ChecksumsUrl { get; init; }
    public bool IsPortable => AssetName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsNewerThan(AppVersion current) => Version > current;
}

public static class ReleaseFeed
{
    public static ReleaseInfo ParseLatest(string json)
    {
        using var document = Read(json);
        return document?.RootElement.ValueKind == JsonValueKind.Object ? Parse(document.RootElement) : null;
    }

    public static IReadOnlyList<ReleaseInfo> ParseStableList(string json)
    {
        using var document = Read(json);
        if (document?.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<ReleaseInfo>();
        return document.RootElement.EnumerateArray().Select(Parse).Where(release => release is not null)
            .OrderByDescending(release => release.Version).ToArray();
    }

    public static ReleaseInfo ParseNewestStable(string json) => ParseStableList(json).FirstOrDefault();

    private static JsonDocument Read(string json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static ReleaseInfo Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || Bool(element, "draft") || Bool(element, "prerelease")) return null;
        string tag = Text(element, "tag_name");
        if (!AppVersion.TryParse(tag, out var version) || !element.TryGetProperty("assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array) return null;
        var candidates = assets.EnumerateArray().Where(asset => asset.ValueKind == JsonValueKind.Object)
            .Select(asset => new { Asset = asset, Name = Text(asset, "name") }).ToArray();
        var package = candidates.Where(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && item.Name.Contains("InkCanvas", StringComparison.OrdinalIgnoreCase)
                && item.Name.Contains("WinUI", StringComparison.OrdinalIgnoreCase)
                && item.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)
                && !item.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault() ?? candidates.Where(item => item.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !item.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Name.Contains("setup", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (package is null || string.IsNullOrEmpty(Text(package.Asset, "browser_download_url"))) return null;
        string digest = Text(package.Asset, "digest");
        string hash = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
        return new(tag, version, Text(element, "name"), Text(element, "body"), Text(element, "html_url"),
            Text(package.Asset, "browser_download_url"), package.Asset.TryGetProperty("size", out var size)
                && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out long length) ? length : 0)
        {
            AssetName = package.Name,
            Sha256 = hash is { Length: 64 } && hash.All(Uri.IsHexDigit) ? hash.ToLowerInvariant() : null,
            ChecksumsUrl = candidates.Where(item => item.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                .Select(item => Text(item.Asset, "browser_download_url")).FirstOrDefault(),
        };
    }

    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : "";
    private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
