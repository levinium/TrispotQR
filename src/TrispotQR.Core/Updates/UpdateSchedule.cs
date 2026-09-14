namespace TrispotQR.Core.Updates;

/// <summary>How often the app is allowed to go and look.</summary>
public static class UpdateSchedule
{
    /// <summary>
    /// Once a day. Releases arrive weeks apart, so more often learns the same answer at someone
    /// else's expense; less often lets a fix sit unnoticed.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>
    /// A stored time in the future counts as due. It should be impossible, but a clock correction
    /// leaves exactly that, and the naive comparison would never check again.
    /// </summary>
    public static bool IsDue(DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc)
    {
        if (lastCheckUtc is not { } last || last > nowUtc)
        {
            return true;
        }

        return nowUtc - last >= Interval;
    }
}
