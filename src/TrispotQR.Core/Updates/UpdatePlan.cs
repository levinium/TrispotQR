namespace TrispotQR.Core.Updates;

/// <summary>
/// The three paths an in-place update moves between.
///
/// Windows will not let a running exe be overwritten or deleted, but it will let one be renamed.
/// So: rename the running exe to its backup name, rename the staged download into the name it
/// vacated, start it, and let the new process delete the backup. Fixed names, so a crash between
/// any two steps leaves files the next launch recognizes and removes.
/// </summary>
public sealed record UpdatePlan(string Current, string Staged, string Backup)
{
    public const string StagedSuffix = ".new";

    public const string BackupSuffix = ".old";

    public string Directory => Path.GetDirectoryName(Current) ?? string.Empty;

    public static UpdatePlan For(string currentExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExePath);
        return new UpdatePlan(currentExePath, currentExePath + StagedSuffix, currentExePath + BackupSuffix);
    }

    /// <summary>
    /// Safe to delete unconditionally at startup: an exe is running under the real name, so the
    /// backup is superseded and any staged download was never installed.
    /// </summary>
    public IEnumerable<string> Leftovers()
    {
        yield return Backup;
        yield return Staged;
    }
}
