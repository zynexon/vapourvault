using VaporVault.Core.Interop;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Handles detecting and dealing with locked files during quarantine operations.
/// Uses the Windows Restart Manager API to find which processes hold file handles,
/// and MoveFileEx to schedule locked files for move-on-reboot.
/// </summary>
public class LockedFileHandler
{
    /// <summary>
    /// Information about a process that is locking files in the target directory.
    /// </summary>
    public record LockingProcessInfo(
        int ProcessId,
        string ProcessName,
        string ApplicationType);

    /// <summary>
    /// Result of a locked-file check.
    /// </summary>
    public record LockCheckResult(
        IReadOnlyList<string> LockedFiles,
        IReadOnlyList<LockingProcessInfo> LockingProcesses);

    /// <summary>
    /// Checks for locked files in the specified directory.
    /// Uses the Restart Manager to detect which processes hold handles.
    /// </summary>
    /// <param name="directoryPath">Directory to check for locked files.</param>
    /// <returns>Information about locked files and the processes holding them.</returns>
    public LockCheckResult CheckForLockedFiles(string directoryPath)
    {
        var lockedFiles = new List<string>();
        var lockingProcesses = new List<LockingProcessInfo>();

        if (!Directory.Exists(directoryPath))
            return new LockCheckResult(lockedFiles, lockingProcesses);

        // Get all files in the directory tree
        string[] allFiles;
        try
        {
            allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            return new LockCheckResult(lockedFiles, lockingProcesses);
        }

        if (allFiles.Length == 0)
            return new LockCheckResult(lockedFiles, lockingProcesses);

        // Check in batches (Restart Manager has limits)
        const int batchSize = 64;
        var seenProcessIds = new HashSet<int>();

        for (int i = 0; i < allFiles.Length; i += batchSize)
        {
            var batch = allFiles.Skip(i).Take(batchSize).ToArray();

            var processInfos = RestartManagerInterop.GetLockingProcesses(batch);

            foreach (var pi in processInfos)
            {
                if (!seenProcessIds.Add(pi.Process.dwProcessId))
                    continue;

                lockingProcesses.Add(new LockingProcessInfo(
                    pi.Process.dwProcessId,
                    pi.strAppName ?? "Unknown",
                    GetAppTypeName(pi.ApplicationType)));
            }
        }

        // If we found locking processes, check which specific files are locked
        if (lockingProcesses.Count > 0)
        {
            foreach (var file in allFiles)
            {
                if (IsFileLocked(file))
                    lockedFiles.Add(file);
            }
        }

        return new LockCheckResult(lockedFiles, lockingProcesses);
    }

    /// <summary>
    /// Schedules a locked file to be moved on the next system reboot.
    /// </summary>
    /// <param name="sourcePath">Current file path.</param>
    /// <param name="destinationPath">Target path after reboot.</param>
    /// <returns>True if the reboot-move was successfully scheduled.</returns>
    public bool ScheduleMoveOnReboot(string sourcePath, string destinationPath)
    {
        // Ensure destination directory exists
        var destDir = Path.GetDirectoryName(destinationPath);
        if (destDir != null)
            Directory.CreateDirectory(destDir);

        return MoveFileExInterop.ScheduleMoveOnReboot(sourcePath, destinationPath);
    }

    /// <summary>
    /// Tests whether a file is currently locked by trying to open it exclusively.
    /// </summary>
    private static bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string GetAppTypeName(int appType) => appType switch
    {
        RestartManagerInterop.RmMainWindow => "Application",
        RestartManagerInterop.RmOtherWindow => "Application",
        RestartManagerInterop.RmService => "Service",
        RestartManagerInterop.RmExplorer => "Explorer",
        RestartManagerInterop.RmConsole => "Console",
        RestartManagerInterop.RmCritical => "Critical",
        _ => "Unknown"
    };
}
