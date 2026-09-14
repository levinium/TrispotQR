using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData(" V1.2.3 ", 1, 2, 3, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("1", 1, 0, 0, null)]
    [InlineData("1.2.3-rc.1", 1, 2, 3, "rc.1")]
    [InlineData("1.2.3+abc123", 1, 2, 3, null)]
    public void ParsesTheFormsATagCanTake(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(new ReleaseVersion(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("-1.2.3")]
    [InlineData("1.0.0.1")]
    public void RefusesWhatItCannotOrderSafely(string? text)
    {
        // A four-part tag is refused rather than truncated: 1.0.0.1 and 1.0.0.2 would compare
        // equal, and an update between them would be silently missed.
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void ComparesNumbersAsNumbers()
    {
        // As text, "1.10.0" sorts before "1.9.0", which would tell someone on 1.9.0 they are
        // up to date forever.
        Assert.True(Parse("1.10.0").IsNewerThan(Parse("1.9.0")));
    }

    [Fact]
    public void AFinishedReleaseIsNewerThanItsOwnReleaseCandidate()
    {
        Assert.True(Parse("1.2.0").IsNewerThan(Parse("1.2.0-rc.1")));
        Assert.False(Parse("1.2.0-rc.1").IsNewerThan(Parse("1.2.0")));
    }

    [Fact]
    public void PreReleaseIdentifiersOrderNumericallyAndByLength()
    {
        Assert.True(Parse("1.0.0-rc.10").IsNewerThan(Parse("1.0.0-rc.2")));
        Assert.True(Parse("1.0.0-rc.1.2").IsNewerThan(Parse("1.0.0-rc.1")));
        Assert.True(Parse("1.0.0-beta").IsNewerThan(Parse("1.0.0-1")));
    }

    [Fact]
    public void BuildMetadataTakesNoPartInOrdering()
    {
        Assert.Equal(0, Parse("1.2.0+one").CompareTo(Parse("1.2.0+two")));
    }

    [Fact]
    public void PrintsWithoutTheLeadingV()
    {
        Assert.Equal("1.2.0", Parse("v1.2.0").ToString());
        Assert.Equal("1.2.0-rc.1", Parse("v1.2.0-rc.1").ToString());
    }

    private static ReleaseVersion Parse(string text) =>
        ReleaseVersion.TryParse(text, out var v) ? v : throw new ArgumentException(text);
}
