using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class UpdatePlanAndScheduleTests
{
    [Fact]
    public void ThePlanKeepsEverythingBesideTheRunningExe()
    {
        // Beside, not in temp: a rename within one volume is atomic, and a copy between volumes
        // can fail halfway with the old exe already moved aside.
        var exe = Path.Combine(Path.GetTempPath(), "apps", "TrispotQR.exe");
        var plan = UpdatePlan.For(exe);

        Assert.Equal(exe, plan.Current);
        Assert.Equal(exe + ".new", plan.Staged);
        Assert.Equal(exe + ".old", plan.Backup);
        Assert.Equal(exe + ".partial", plan.Partial);
        Assert.Equal(Path.GetDirectoryName(exe), plan.Directory);
        Assert.Equal([plan.Backup, plan.Staged, plan.Partial], plan.Leftovers());
    }

    [Fact]
    public void APlanNeedsAPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => UpdatePlan.For(" "));
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeverCheckedIsDue() => Assert.True(UpdateSchedule.IsDue(null, Now));

    [Fact]
    public void LessThanADayAgoIsNotDue() => Assert.False(UpdateSchedule.IsDue(Now.AddHours(-23), Now));

    [Fact]
    public void ADayAgoIsDue() => Assert.True(UpdateSchedule.IsDue(Now.AddDays(-1), Now));

    [Fact]
    public void ACheckTimeInTheFutureIsDue()
    {
        // Left behind by a clock correction. Treated as "checked recently" it would stop the
        // check forever, silently.
        Assert.True(UpdateSchedule.IsDue(Now.AddDays(3), Now));
    }
}
