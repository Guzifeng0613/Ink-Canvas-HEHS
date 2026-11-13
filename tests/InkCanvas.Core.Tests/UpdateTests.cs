using InkCanvas.Core.Update;
using InkCanvas.Core.Versioning;
using Xunit;

namespace InkCanvas.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("3.0.0", 3, 0, 0, 0)]
    [InlineData("v3.1.2", 3, 1, 2, 0)]
    [InlineData("V26.10.7", 26, 10, 7, 0)]
    [InlineData("1.2.3.4", 1, 2, 3, 4)]
    [InlineData("  v2.5  ", 2, 5, 0, 0)]
    public void ParsesCommonTagForms(string text, int major, int minor, int patch, int build)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new AppVersion(major, minor, patch, build), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("release")]
    [InlineData(null)]
    public void RejectsNonVersionTags(string text)
        => Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void OrdersByComponents()
    {
        AppVersion.TryParse("3.0.0", out var a);
        AppVersion.TryParse("3.0.1", out var b);
        AppVersion.TryParse("3.1.0", out var c);
        AppVersion.TryParse("4.0.0", out var d);
        Assert.True(a < b);
        Assert.True(b < c);
        Assert.True(c < d);
        Assert.True(d > a);
    }

    [Fact]
    public void BuildNumberBreaksTies()
    {
        AppVersion.TryParse("3.0.0.1", out var a);
        AppVersion.TryParse("3.0.0.2", out var b);
        Assert.True(b > a);
    }
}

public class ReleaseFeedTests
{
    private const string LatestJson = """
    {
      "tag_name": "v3.2.0",
      "name": "Ink Canvas 3.2.0",
      "body": "修复若干问题",
      "html_url": "https://github.com/example/repo/releases/tag/v3.2.0",
      "draft": false,
      "prerelease": false,
      "assets": [
        { "name": "notes.txt", "size": 12, "browser_download_url": "https://example/notes.txt" },
        { "name": "InkCanvasWinUI3-3.2.0-Setup.exe", "size": 987654, "browser_download_url": "https://example/setup.exe" }
      ]
    }
    """;

    [Fact]
    public void ParsesLatestRelease()
    {
        var r = ReleaseFeed.ParseLatest(LatestJson);
        Assert.NotNull(r);
        Assert.Equal("v3.2.0", r.Tag);
        Assert.Equal(new AppVersion(3, 2, 0, 0), r.Version);
        Assert.Equal("修复若干问题", r.Notes);
    }

    [Fact]
    public void PrefersInstallerExeAsset()
    {
        var r = ReleaseFeed.ParseLatest(LatestJson);
        Assert.Equal("https://example/setup.exe", r.InstallerUrl);
        Assert.Equal(987654, r.InstallerSize);
    }

    [Fact]
    public void ReturnsNullWhenNoInstallerAsset()
    {
        const string json = """
        { "tag_name":"v3.2.0", "assets":[ { "name":"notes.txt","size":1,"browser_download_url":"https://example/n.txt" } ] }
        """;
        Assert.Null(ReleaseFeed.ParseLatest(json));
    }

    [Fact]
    public void SkipsDraftsAndPrereleasesInList()
    {
        const string json = """
        [
          { "tag_name":"v9.0.0","draft":true,"assets":[{"name":"a-Setup.exe","size":1,"browser_download_url":"https://example/a.exe"}] },
          { "tag_name":"v8.0.0","prerelease":true,"assets":[{"name":"b-Setup.exe","size":1,"browser_download_url":"https://example/b.exe"}] },
          { "tag_name":"v3.2.0","draft":false,"prerelease":false,"assets":[{"name":"c-Setup.exe","size":1,"browser_download_url":"https://example/c.exe"}] }
        ]
        """;
        var r = ReleaseFeed.ParseNewestStable(json);
        Assert.NotNull(r);
        Assert.Equal("v3.2.0", r.Tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void MalformedFeedReturnsNull(string json)
    {
        Assert.Null(ReleaseFeed.ParseLatest(json));
        Assert.Null(ReleaseFeed.ParseNewestStable(json));
    }

    [Fact]
    public void IsNewerThanComparesAgainstCurrent()
    {
        var r = ReleaseFeed.ParseLatest(LatestJson);
        Assert.True(r.IsNewerThan(new AppVersion(3, 0, 0, 0)));
        Assert.False(r.IsNewerThan(new AppVersion(3, 2, 0, 0)));
        Assert.False(r.IsNewerThan(new AppVersion(4, 0, 0, 0)));
    }
}