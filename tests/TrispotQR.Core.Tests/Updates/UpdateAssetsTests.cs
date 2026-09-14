using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class UpdateAssetsTests
{
    private static readonly ReleaseAsset Exe = new("TrispotQR.exe", "https://example.org/TrispotQR.exe", 48_000_000);
    private static readonly ReleaseAsset Sum = new("TrispotQR.exe.sha256", "https://example.org/TrispotQR.exe.sha256", 80);
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void PicksTheExecutableByItsWholeName()
    {
        // The checksum file's name CONTAINS the exe's. Matching on a substring would install 80
        // bytes of text over the app.
        Assert.Same(Exe, UpdateAssets.Executable([Sum, Exe]));
        Assert.Same(Sum, UpdateAssets.Checksum([Exe, Sum]));
    }

    [Fact]
    public void AReleaseWithoutTheFilesHasNothingToOffer()
    {
        Assert.Null(UpdateAssets.Executable([Sum]));
        Assert.Null(UpdateAssets.Checksum([Exe]));
        Assert.Null(UpdateAssets.Executable(null));
    }

    [Theory]
    [InlineData(Hash + "  TrispotQR.exe")]
    [InlineData(Hash + " *TrispotQR.exe\r\n")]
    [InlineData("\uFEFF" + Hash + "\n")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF  TrispotQR.exe")]
    public void ReadsTheChecksumInTheShapesItArrivesIn(string text)
    {
        Assert.True(UpdateAssets.TryReadChecksum(text, out var hash));
        Assert.Equal(Hash, hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("0123456789abcdef  TrispotQR.exe")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void RefusesAChecksumThatIsNotOne(string? text)
    {
        Assert.False(UpdateAssets.TryReadChecksum(text, out _));
    }

    [Fact]
    public void ComparesHashesWithoutCaringAboutCase()
    {
        // PowerShell's Get-FileHash writes upper case; an ordinal comparison would reject every
        // genuine update while looking exactly like a tampered one.
        Assert.True(UpdateAssets.Matches(Hash.ToUpperInvariant(), Hash));
        Assert.False(UpdateAssets.Matches(Hash, Hash.Replace('0', '1')));
        Assert.False(UpdateAssets.Matches(null, Hash));
        Assert.False(UpdateAssets.Matches(Hash, ""));
    }

    [Fact]
    public void FormatsAHashTheWayTheChecksumFileWritesIt()
    {
        Assert.Equal("00ff10", UpdateAssets.Format([0x00, 0xFF, 0x10]));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/levinium/TrispotQR/releases/latest", true)]
    [InlineData("http://localhost:8123/release.json", true)]
    [InlineData("http://127.0.0.1:8123/TrispotQR.exe", true)]
    [InlineData("http://[::1]:8123/TrispotQR.exe", true)]
    [InlineData("http://example.org/TrispotQR.exe", false)]
    [InlineData("ftp://example.org/TrispotQR.exe", false)]
    [InlineData("file:///C:/TrispotQR.exe", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void OnlyHttpsOrALoopbackAddressIsAllowed(string? url, bool allowed)
    {
        Assert.Equal(allowed, UpdateAssets.IsAllowedUrl(url));
    }
}
