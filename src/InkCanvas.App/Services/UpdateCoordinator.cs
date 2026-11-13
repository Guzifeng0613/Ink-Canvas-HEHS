using InkCanvas.Core.Update;
using InkCanvas.Core.Versioning;
using InkCanvas.Features.Update;

namespace InkCanvas.App.Services;

/// <summary>
/// Owns the update flow: check, ask, download, verify, hand off to the installer.
/// Shared by the startup check and the manual button in settings so there is exactly one
/// place that decides whether an installer may run.
/// </summary>
public sealed class UpdateCoordinator : IDisposable
{
    private readonly AppServices _services;
    private readonly UpdateService _update = new();
    private bool _running;

    public UpdateCoordinator(AppServices services)
    {
        _services = services;
    }

    /// <summary>Ask the user yes/no. Supplied by the shell so dialogs get a valid XamlRoot.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; }

    /// <summary>Show an informational message.</summary>
    public Func<string, string, Task> Notify { get; set; }

    /// <summary>Called after the installer starts; the app should exit so files can be replaced.</summary>
    public Action ExitForInstall { get; set; }

    public AppVersion CurrentVersion => _update.CurrentVersion;

    /// <param name="interactive">True for a user-initiated check, which reports "up to date".</param>
    public async Task CheckAsync(bool interactive)
    {
        if (_running) return;
        _running = true;
        try
        {
            var release = await _update.CheckAsync();
            if (release is null)
            {
                if (interactive) await Say("检查更新", "无法获取更新信息，请检查网络或稍后再试。");
                return;
            }

            if (!release.IsNewerThan(_update.CurrentVersion))
            {
                if (interactive) await Say("检查更新", $"已是最新版本（{_update.CurrentVersion}）。");
                return;
            }

            bool silent = UpdatePolicy.MayInstallSilently(_services.Settings.Startup, DateTime.Now);
            if (!silent)
            {
                if (Confirm is null) return;
                string message = $"发现新版本 {release.Tag}（当前 {_update.CurrentVersion}）。\n\n"
                               + Trim(release.Notes)
                               + "\n\n下载并安装？安装前会关闭本程序。";
                if (!await Confirm("发现更新", message)) return;
            }

            string path = await _update.DownloadAsync(release);
            if (path is null)
            {
                await Say("更新失败", "安装包下载失败或校验不通过，未执行任何安装。");
                return;
            }

            if (!_update.LaunchInstaller(path))
            {
                await Say("更新失败", "无法启动安装程序。");
                return;
            }
            ExitForInstall?.Invoke();
        }
        finally
        {
            _running = false;
        }
    }

    private static string Trim(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return string.Empty;
        notes = notes.ReplaceLineEndings("\n").Trim();
        return notes.Length <= 600 ? notes : notes[..600] + "…";
    }

    private async Task Say(string title, string message)
    {
        if (Notify is not null) await Notify(title, message);
    }

    public void Dispose() => _update.Dispose();
}