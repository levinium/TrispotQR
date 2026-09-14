using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class ReleaseFeedTests
{
    private const string Release = """
        {
          "tag_name": "v1.3.0",
          "name": "Trispot QR v1.3.0",
          "html_url": "https://github.com/levinium/TrispotQR/releases/tag/v1.3.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "TrispotQR.exe", "browser_download_url": "https://example.org/TrispotQR.exe", "size": 48000000, "state": "uploaded" },
            { "name": "TrispotQR.exe.sha256", "browser_download_url": "https://example.org/TrispotQR.exe.sha256", "size": 80, "state": "uploaded" },
            { "name": "Partial.exe", "browser_download_url": "https://example.org/Partial.exe", "size": 10, "state": "starter" }
          ]
        }
        """;

    [Fact]
    public void ReadsTheFieldsThatMatter()
    {
        var info = ReleaseFeed.Parse(Release);

        Assert.NotNull(info);
        Assert.Equal("v1.3.0", info.Tag);
        Assert.Equal("https://github.com/levinium/TrispotQR/releases/tag/v1.3.0", info.Url);
        Assert.False(info.IsDraft);
        Assert.False(info.IsPreRelease);
        Assert.Equal(48000000, info.Assets!.Single(a => a.Name == "TrispotQR.exe").Size);
    }

    [Fact]
    public void SkipsAnAssetThatIsStillUploading()
    {
        // GitHub lists an asset from the moment its upload starts. Downloading one of those
        // yields a truncated file.
        Assert.DoesNotContain(ReleaseFeed.Parse(Release)!.Assets!, a => a.Name == "Partial.exe");
    }

    [Fact]
    public void ReadsTheDraftAndPreReleaseFlags()
    {
        var info = ReleaseFeed.Parse("""{ "tag_name": "v2.0.0", "draft": true, "prerelease": true }""");

        Assert.True(info!.IsDraft);
        Assert.True(info.IsPreRelease);
        Assert.Empty(info.Assets!);
    }

    [Fact]
    public void FallsBackToTheNameWhenThereIsNoTag()
    {
        Assert.Equal("1.4.0", ReleaseFeed.Parse("""{ "name": "1.4.0" }""")!.Tag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>Sign in to continue</body></html>")]
    [InlineData("[]")]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    public void AnythingThatIsNotAReleaseReadsAsNothing(string? json)
    {
        // A captive portal answering with HTML is the usual cause, and it means exactly what a
        // failed request means.
        Assert.Null(ReleaseFeed.Parse(json));
    }

    [Fact]
    public void AnUnreadableSizeIsReadAsZeroNotThrown()
    {
        var info = ReleaseFeed.Parse("""{ "tag_name": "v1.0.0", "assets": [{ "name": "test.exe", "browser_download_url": "https://example.org/test.exe", "size": 1.5, "state": "uploaded" }] }""");

        Assert.NotNull(info);
        Assert.Single(info.Assets!);
        Assert.Equal(0, info.Assets.Single().Size);
    }
}
