namespace TrispotQR.Core.Updates;

/// <summary>
/// The paths an in-place update moves between.
///
/// Windows will not let a running exe be overwritten or deleted, but it will let one be renamed.
/// So: download to the partial name, rename it to the staged name only once it has verified,
/// rename the running exe to its backup name, rename the staged download into the name it
/// vacated, start it, and let the new process delete the backup. Fixed names, so a crash between
/// any two steps leaves files the next launch recognizes and removes.
/// </summary>
public sealed record UpdatePlan(string Current, string Staged, string Backup, string Partial)
{
    public const string StagedSuffix = ".new";

    public const string BackupSuffix = ".old";

    /// <summary>A download still being written or checked. Never installed from.</summary>
    public const string PartialSuffix = ".partial";

    public string Directory => Path.GetDirectoryName(Current) ?? string.Empty;

    public static UpdatePlan For(string currentExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExePath);
        return new UpdatePlan(
            currentExePath,
            currentExePath + StagedSuffix,
            currentExePath + BackupSuffix,
            currentExePath + PartialSuffix);
    }

    /// <summary>
    /// Every file an update can leave beside the exe. Once an exe runs under the real name the
    /// backup is superseded, but a staged or partial download may belong to another copy that is
    /// still running, so startup deletes those only when no other copy is.
    /// </summary>
    public IEnumerable<string> Leftovers()
    {
        yield return Backup;
        yield return Staged;
        yield return Partial;
    }
}
