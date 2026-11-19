using InkCanvas.Core.Update;
using InkCanvas.Core.Versioning;
using InkCanvas.Features.Update;
using Microsoft.UI.Dispatching;

namespace InkCanvas.App.Services;

/// <summary>Check, stage and install on the configured schedule, while the canvas is idle.</summary>
public sealed class UpdateCoordinator : IDisposable
{
    private readonly AppServices _services;
    private readonly UpdateService _update;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _timer;
    private bool _running, _disposed;
    private DateTime _lastCheck = DateTime.MinValue;
    private DateTime _retryAfter = DateTime.MinValue;
    private ReleaseInfo _pending;
    private string _archive, _plan;

    public UpdateCoordinator(AppServices services, UpdateService update = null)
    {
        _services = services;
        _update = update ?? new UpdateService();
        _update.StatusChanged += OnTransportStatus;
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += async (_, _) =>
        {
            if (_disposed || !services.Settings.Startup.IsAutoUpdate || DateTime.UtcNow < _retryAfter) return;
            if (_pending is not null || DateTime.UtcNow - _lastCheck > TimeSpan.FromHours(6)) await CheckAsync(false);
        };
        _timer.Start();
    }

    public Func<string, string, Task<bool>> Confirm { get; set; }
    public Func<string, string, Task> Notify { get; set; }
    public Func<IReadOnlyList<ReleaseInfo>, Task<ReleaseInfo>> ChooseRelease { get; set; }
    public Func<bool> CanInstallSilently { get; set; }
    public Func<bool, Task<bool>> PrepareForInstall { get; set; }
    public Action ExitForInstall { get; set; }
    public AppVersion CurrentVersion => _update.CurrentVersion;
    public string Status { get; private set; } = "尚未检查更新";
    public event Action StatusChanged;

    public async Task CheckAsync(bool interactive)
    {
        if (_running || _disposed || !interactive && !_services.Settings.Startup.IsAutoUpdate) return;
        _running = true;
        try
        {
            if (_pending is null || interactive || DateTime.UtcNow - _lastCheck > TimeSpan.FromHours(6))
            {
                SetStatus("正在获取 GitHub 版本列表…");
                var releases = await _update.GetReleasesAsync(_lifetime.Token);
                _lastCheck = DateTime.UtcNow;
                var release = releases.FirstOrDefault();
                if (release is null) { _pending = null; SetStatus("仓库尚无适用的稳定发行版"); if (interactive) await Say("检查更新", Status); return; }
                if (!release.IsNewerThan(CurrentVersion))
                { _pending = null; SetStatus($"已是最新版本 {CurrentVersion} · {_update.LastSource}"); if (interactive) await Say("检查更新", Status); return; }
                if (_pending?.Tag != release.Tag) { _archive = null; _plan = null; }
                _pending = release;
            }
            await InstallPendingAsync(interactive);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { await FailedAsync(exception, interactive); }
        finally { _running = false; }
    }

    public async Task ShowVersionsAsync()
    {
        if (_running || _disposed) return;
        _running = true;
        try
        {
            SetStatus("正在获取 GitHub 版本列表…");
            var releases = await _update.GetReleasesAsync(_lifetime.Token);
            SetStatus($"已获取 {releases.Count} 个稳定版本 · {_update.LastSource}");
            var selected = ChooseRelease is null ? null : await ChooseRelease(releases);
            if (selected is null) return;
            if (!selected.IsNewerThan(CurrentVersion)) { await Say("版本列表", "所选版本不高于当前版本。"); return; }
            if (_pending?.Tag != selected.Tag) { _archive = null; _plan = null; }
            _pending = selected;
            await InstallPendingAsync(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { await FailedAsync(exception, true); }
        finally { _running = false; }
    }

    private async Task InstallPendingAsync(bool interactive)
    {
        var release = _pending;
        bool silentEnabled = _services.Settings.Startup.IsAutoUpdateWithSilence;
        bool silent = !interactive && UpdatePolicy.MayInstallSilently(_services.Settings.Startup, DateTime.Now);
        if (!interactive && silentEnabled && !silent)
        { SetStatus($"发现 {release.Tag}，等待静默更新时间段"); return; }
        if (silent && CanInstallSilently?.Invoke() != true)
        { SetStatus($"发现 {release.Tag}，等待画布空闲"); return; }
        if (!silent)
        {
            string notes = release.Notes ?? "";
            if (notes.Length > 600) notes = notes[..600] + "…";
            if (Confirm is null || !await Confirm("发现更新", $"新版本 {release.Tag}，当前 {CurrentVersion}。\n\n{notes}\n\n下载校验后会保存当前画布、退出并更新，完成后重新打开。"))
            { _pending = null; SetStatus("已取消本次更新"); return; }
        }
        if (_archive is null || !File.Exists(_archive))
        {
            _plan = null;
            int lastPercent = -1;
            var progress = new Progress<double>(value =>
            {
                int percent = (int)(value * 100);
                if (percent == lastPercent) return;
                lastPercent = percent; SetStatus($"正在下载 {release.Tag} · {percent}%");
            });
            _archive = await _update.DownloadAsync(release, progress, _lifetime.Token);
        }
        if (silent && (!UpdatePolicy.MayInstallSilently(_services.Settings.Startup, DateTime.Now) || CanInstallSilently?.Invoke() != true))
        { SetStatus("安装包已校验，等待静默时段和画布空闲"); return; }
        SetStatus("正在校验并准备更新文件…");
        if (_plan is null || !File.Exists(_plan))
            _plan = await _update.PrepareAsync(release, _archive, AppContext.BaseDirectory, token: _lifetime.Token);
        if (PrepareForInstall is null || !await PrepareForInstall(silent))
        {
            if (interactive) _pending = null;
            SetStatus("安装包已准备，等待下次安装"); return;
        }
        if (silent && !UpdatePolicy.MayInstallSilently(_services.Settings.Startup, DateTime.Now)) return;
        using var helper = UpdateService.LaunchUpdate(_plan);
        SetStatus("正在退出并安装更新…");
        ExitForInstall?.Invoke();
    }

    private void OnTransportStatus(string status) => _dispatcher.TryEnqueue(() => { if (!_disposed) SetStatus(status); });
    private void SetStatus(string status) { Status = status; StatusChanged?.Invoke(); }
    private async Task FailedAsync(Exception exception, bool interactive)
    {
        _retryAfter = DateTime.UtcNow.AddMinutes(15);
        _lastCheck = DateTime.UtcNow - TimeSpan.FromHours(6);
        SetStatus("更新失败 · " + exception.Message);
        Core.Diagnostics.FileLog.WriteLogToFile(Status, Core.Diagnostics.LogType.Error);
        if (interactive) await Say("更新失败", exception.Message);
    }
    private Task Say(string title, string message) => Notify?.Invoke(title, message) ?? Task.CompletedTask;
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _timer.Stop(); _lifetime.Cancel(); _update.StatusChanged -= OnTransportStatus; _update.Dispose();
    }
}
