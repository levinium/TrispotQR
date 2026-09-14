using System.Diagnostics;
using TrispotQR.Core.Updates;

namespace TrispotQR.UI.Services;

/// <summary>What a launch does first, before anything could be holding the files.</summary>
public static class UpdateStartup
{
    /// <summary>Followed by the process id of the copy that was just replaced.</summary>
    public const string UpdatedArgument = "--updated";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Waits for the replaced copy to exit, so its backup file is free to delete. The common case
    /// is that it has already gone.
    ///
    /// Never throws: this runs before the window exists, so any exception is a crash on the first
    /// launch of the new version. And by the time this runs Windows may have given the old id to
    /// an unrelated process, possibly one this user cannot open, so it only waits for a process
    /// with this app's name that started no later than this one.
    /// </summary>
    public static void WaitForPredecessor(string[] args)
    {
        var at = Array.IndexOf(args, UpdatedArgument);
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var pid))
        {
            return;
        }

        try
        {
            using var current = Process.GetCurrentProcess();
            using var previous = Process.GetProcessById(pid);

            if (!string.Equals(previous.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase)
                || StartedAfter(previous, current))
            {
                return;
            }

            previous.WaitForExit(Patience);
        }
        catch (Exception)
        {
            // Gone, unreadable, or access denied: in every case there is nothing worth waiting for.
        }
    }

    /// <summary>A start time that cannot be read is not evidence either way, so it does not rule the process out.</summary>
    private static bool StartedAfter(Process candidate, Process current)
    {
        try
        {
            return candidate.StartTime > current.StartTime;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a previous update's backup and any download that was never installed. A file still
    /// held by the exiting process is left for the next launch.
    /// </summary>
    public static void CleanUp(string? exePath = null)
    {
        var exe = exePath ?? Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        foreach (var leftover in UpdatePlan.For(exe).Leftovers())
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
