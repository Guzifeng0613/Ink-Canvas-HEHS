using InkCanvas.Core.Settings;

namespace InkCanvas.Core.Update;

/// <summary>Policy for unattended installs. Kept pure so the gating logic is testable.</summary>
public static class UpdatePolicy
{
    /// <summary>
    /// True only when auto-update and silent mode are both on and <paramref name="now"/> falls
    /// inside the configured window. Unparseable times mean "ask the user".
    /// <para>
    /// The window is start-inclusive, end-exclusive, and may cross midnight. Equal start and
    /// end means "no restriction", which is what the default 00:00–00:00 configuration implies.
    /// </para>
    /// </summary>
    public static bool MayInstallSilently(StartupSettings settings, DateTime now)
    {
        if (settings is null || !settings.IsAutoUpdate || !settings.IsAutoUpdateWithSilence) return false;
        if (!TryMinutes(settings.AutoUpdateWithSilenceStartTime, out int start)) return false;
        if (!TryMinutes(settings.AutoUpdateWithSilenceEndTime, out int end)) return false;

        if (start == end) return true;

        int minutes = now.Hour * 60 + now.Minute;
        return start < end
            ? minutes >= start && minutes < end
            : minutes >= start || minutes < end;   // window crosses midnight
    }

    private static bool TryMinutes(string text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        minutes = h * 60 + m;
        return true;
    }
}