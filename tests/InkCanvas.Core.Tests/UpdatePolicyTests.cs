using InkCanvas.Core.Settings;
using InkCanvas.Core.Update;
using Xunit;

namespace InkCanvas.Core.Tests;

/// <summary>
/// Decides whether an update may install without asking. This gates running an installer, so
/// the window logic (including windows that cross midnight) is pinned by tests.
/// </summary>
public class UpdatePolicyTests
{
    private static StartupSettings Settings(bool silent, string start, string end) => new()
    {
        IsAutoUpdate = true,
        IsAutoUpdateWithSilence = silent,
        AutoUpdateWithSilenceStartTime = start,
        AutoUpdateWithSilenceEndTime = end,
    };

    private static DateTime At(int hour, int minute = 0) => new(2026, 11, 13, hour, minute, 0);

    [Fact]
    public void NeverSilentWhenSilenceDisabled()
        => Assert.False(UpdatePolicy.MayInstallSilently(Settings(false, "00:00", "23:59"), At(3)));

    [Fact]
    public void NeverSilentWhenAutoUpdateDisabled()
    {
        var s = Settings(true, "00:00", "23:59");
        s.IsAutoUpdate = false;
        Assert.False(UpdatePolicy.MayInstallSilently(s, At(3)));
    }

    [Fact]
    public void SilentInsideSameDayWindow()
    {
        var s = Settings(true, "01:00", "05:00");
        Assert.True(UpdatePolicy.MayInstallSilently(s, At(3)));
        Assert.False(UpdatePolicy.MayInstallSilently(s, At(6)));
        Assert.False(UpdatePolicy.MayInstallSilently(s, At(0)));
    }

    [Fact]
    public void HandlesWindowCrossingMidnight()
    {
        var s = Settings(true, "22:00", "06:00");
        Assert.True(UpdatePolicy.MayInstallSilently(s, At(23)));
        Assert.True(UpdatePolicy.MayInstallSilently(s, At(5)));
        Assert.False(UpdatePolicy.MayInstallSilently(s, At(12)));
    }

    [Fact]
    public void EqualStartAndEndMeansNoRestriction()
        => Assert.True(UpdatePolicy.MayInstallSilently(Settings(true, "00:00", "00:00"), At(12)));

    [Fact]
    public void WindowBoundariesAreInclusiveAtStartExclusiveAtEnd()
    {
        var s = Settings(true, "02:00", "04:00");
        Assert.True(UpdatePolicy.MayInstallSilently(s, At(2)));
        Assert.False(UpdatePolicy.MayInstallSilently(s, At(4)));
    }

    [Fact]
    public void MalformedTimesFallBackToAskingTheUser()
        => Assert.False(UpdatePolicy.MayInstallSilently(Settings(true, "nonsense", "also-bad"), At(3)));
}