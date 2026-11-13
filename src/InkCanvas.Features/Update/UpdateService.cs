using InkCanvas.Core.Update;
using InkCanvas.Core.Versioning;
using System.Diagnostics;
using System.Reflection;

namespace InkCanvas.Features.Update;

/// <summary>
/// Checks GitHub releases and downloads an installer.
/// <para>
/// Downloading and running an installer is a user-visible, hard-to-reverse action, so this
/// service never launches anything on its own: <see cref="LaunchInstaller"/> is only called
/// after the shell has obtained consent (or the silent-window policy allows it).
/// </para>
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly HttpClient _http;

    public UpdateService(string repository = "QiXinia/Ink-Canvas-HEHS")
    {
        Repository = repository;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Generic client name: no personal or machine information leaves the device.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("InkCanvas-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string Repository { get; set; }

    public AppVersion CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(3, 0, 0);
            return new AppVersion(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build, v.Revision < 0 ? 0 : v.Revision);
        }
    }

    /// <summary>Returns the newest stable release, or null when offline / unavailable.</summary>
    public async Task<ReleaseInfo> CheckAsync(CancellationToken cancellation = default)
    {
        try
        {
            string url = $"https://api.github.com/repos/{Repository}/releases?per_page=20";
            using var response = await _http.GetAsync(url, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            string json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return ReleaseFeed.ParseNewestStable(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Downloads the installer to a temp folder and verifies its length.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress = null,
        CancellationToken cancellation = default)
    {
        if (release is null || string.IsNullOrEmpty(release.InstallerUrl)) return null;

        string dir = Path.Combine(Path.GetTempPath(), "InkCanvas", "update");
        Directory.CreateDirectory(dir);
        string fileName = Path.GetFileName(new Uri(release.InstallerUrl).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"InkCanvas-{release.Tag}-Setup.exe";
        string path = Path.Combine(dir, fileName);

        using var response = await _http.GetAsync(release.InstallerUrl,
            HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        long total = response.Content.Headers.ContentLength ?? release.InstallerSize;
        await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        await using var destination = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, n), cancellation).ConfigureAwait(false);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        await destination.FlushAsync(cancellation).ConfigureAwait(false);

        // A truncated download must never be executed.
        if (release.InstallerSize > 0 && read != release.InstallerSize)
        {
            try { File.Delete(path); } catch { }
            return null;
        }
        return path;
    }

    /// <summary>Starts the downloaded installer. Caller must already have consent.</summary>
    public bool LaunchInstaller(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}