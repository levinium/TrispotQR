namespace TrispotQR.Core.Updates;

public enum UpdateOutcome
{
    /// <summary>Nothing newer is published.</summary>
    UpToDate,

    /// <summary>A newer release exists and is worth mentioning.</summary>
    Available,

    /// <summary>The question could not be answered: offline, refused, unreadable.</summary>
    Unknown,
}

/// <summary>What a check concluded, and the release it is about.</summary>
public readonly record struct UpdateVerdict(
    UpdateOutcome Outcome,
    ReleaseVersion Version,
    string? Url,
    ReleaseInfo? Release = null)
{
    public bool IsAvailable => Outcome == UpdateOutcome.Available;
}

/// <summary>
/// Whether a published release is one to tell the user about.
///
/// Every uncertain case resolves to Unknown rather than to either confident answer. A prompt
/// that fires on a version it could not read sends people to download something they may already
/// have; a false "up to date" is quieter but still untrue. Drafts and pre-releases are never
/// offered.
/// </summary>
public static class UpdateDecision
{
    public static UpdateVerdict For(string? currentVersion, ReleaseInfo? latest)
    {
        if (latest is null)
        {
            return new UpdateVerdict(UpdateOutcome.Unknown, default, null);
        }

        if (latest.IsDraft || latest.IsPreRelease)
        {
            return new UpdateVerdict(UpdateOutcome.UpToDate, default, null);
        }

        if (!ReleaseVersion.TryParse(latest.Tag, out var published))
        {
            return new UpdateVerdict(UpdateOutcome.Unknown, default, null);
        }

        if (published.IsPreRelease)
        {
            return new UpdateVerdict(UpdateOutcome.UpToDate, default, null);
        }

        if (!ReleaseVersion.TryParse(currentVersion, out var running))
        {
            return new UpdateVerdict(UpdateOutcome.Unknown, published, latest.Url);
        }

        return published.IsNewerThan(running)
            ? new UpdateVerdict(UpdateOutcome.Available, published, latest.Url, latest)
            : new UpdateVerdict(UpdateOutcome.UpToDate, published, latest.Url, latest);
    }
}
