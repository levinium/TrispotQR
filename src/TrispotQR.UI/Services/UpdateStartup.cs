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
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(Patience);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
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
