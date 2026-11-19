using System.Runtime.InteropServices;
using InkCanvas.Core.Update;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace InkCanvas.App.Services;

public sealed partial class ShellController
{
    private bool _installingUpdate;
    private bool CanInstallUpdateSilently()
    {
        if (_exiting || _disposed || _board is not null || _documentWorkspace is not null || _settings is not null
            || _countdown is not null || _lottery is not null || _replay is not null || _imagePicking || _lastSlideShowState) return false;
        if (Services.OverlayInk.HasActiveInteraction || Services.OverlayInk.Playback is not null
            || Services.OverlayInk.Document.Count > 0 || Services.ScreenImages.CurrentImages.Count > 0) return false;
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.Time) >= 60000;
    }

    private async Task<bool> PrepareForUpdateAsync(bool silent)
    {
        if (silent) return CanInstallUpdateSilently();
        if (ActiveController.HasActiveInteraction) { await NotifyAsync("更新", "请先完成当前笔画或拖动，再安装更新。"); return false; }
        if (_documentWorkspace is not null && !await _documentWorkspace.CloseAsync()) return false;
        string directory = Path.Combine(Services.Store.Directory, "BeforeUpdate", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        if (Services.OverlayInk.Document.Count > 0 || Services.ScreenImages.CurrentImages.Count > 0)
        { Directory.CreateDirectory(directory); Services.ScreenImages.SavePage(Path.Combine(directory, "桌面批注.icink")); }
        if (_board is not null)
        { Directory.CreateDirectory(directory); Services.Board.SaveBoard(Path.Combine(directory, "画板.icink")); _board.Close(); }
        Services.SaveSettings();
        return true;
    }

    private async Task<ReleaseInfo> ChooseUpdateReleaseAsync(IReadOnlyList<ReleaseInfo> releases)
    {
        var owner = _settings as Window ?? _bar;
        var list = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 300, MinHeight = 44 };
        foreach (var release in releases) list.Items.Add(release.Tag + " · " + release.Name);
        list.SelectedIndex = releases.Count > 0 ? 0 : -1;
        var notes = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void UpdateNotes() => notes.Text = list.SelectedIndex >= 0 ? releases[list.SelectedIndex].Notes : "仓库没有适用的稳定版本。";
        list.SelectionChanged += (_, _) => UpdateNotes(); UpdateNotes();
        var dialog = new ContentDialog
        {
            Title = "GitHub 稳定版本", PrimaryButtonText = "更新到所选版本", CloseButtonText = "关闭",
            IsPrimaryButtonEnabled = releases.Count > 0, XamlRoot = ((FrameworkElement)owner.Content).XamlRoot,
            Content = new StackPanel { Spacing = 12, Children = { list, new ScrollViewer { Content = notes, MaxHeight = 240 } } },
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedIndex >= 0 ? releases[list.SelectedIndex] : null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
}
