using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class UpdateDecisionTests
{
    private static ReleaseInfo Release(string tag, bool draft = false, bool pre = false) =>
        new(tag, "https://example.org/release", draft, pre, []);

    [Fact]
    public void ANewerReleaseIsAvailableAndCarriesTheRelease()
    {
        var release = Release("v1.3.0");
        var verdict = UpdateDecision.For("1.2.0", release);

        Assert.Equal(UpdateOutcome.Available, verdict.Outcome);
        Assert.True(verdict.IsAvailable);
        Assert.Equal("1.3.0", verdict.Version.ToString());
        Assert.Same(release, verdict.Release);
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.3.0")]
    public void TheSameOrAnOlderReleaseIsUpToDate(string running)
    {
        Assert.Equal(UpdateOutcome.UpToDate, UpdateDecision.For(running, Release("v1.2.0")).Outcome);
    }

    [Fact]
    public void NoReleaseReadIsUnknown()
    {
        Assert.Equal(UpdateOutcome.Unknown, UpdateDecision.For("1.2.0", null).Outcome);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1.0.0.1")]
    public void AnUnreadableTagIsUnknown(string tag)
    {
        Assert.Equal(UpdateOutcome.Unknown, UpdateDecision.For("1.2.0", Release(tag)).Outcome);
    }

    [Fact]
    public void AnUnreadableRunningVersionIsUnknown()
    {
        Assert.Equal(UpdateOutcome.Unknown, UpdateDecision.For("dev", Release("v9.0.0")).Outcome);
    }

    [Fact]
    public void DraftsAndPreReleasesAreNeverOffered()
    {
        Assert.False(UpdateDecision.For("1.0.0", Release("v2.0.0", draft: true)).IsAvailable);
        Assert.False(UpdateDecision.For("1.0.0", Release("v2.0.0", pre: true)).IsAvailable);

        // Unlabelled, but the tag says what it is.
        Assert.False(UpdateDecision.For("1.0.0", Release("v2.0.0-rc.1")).IsAvailable);
    }
}
