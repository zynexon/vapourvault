using System.Runtime.InteropServices;

namespace VaporVault.Core.Interop;

/// <summary>
/// P/Invoke declaration for MoveFileEx, used to schedule locked files
/// for move-on-reboot when they cannot be moved immediately.
/// </summary>
internal static class MoveFileExInterop
{
    /// <summary>
    /// Flag: Replace destination file if it exists.
    /// </summary>
    internal const uint MOVEFILE_REPLACE_EXISTING = 0x1;

    /// <summary>
    /// Flag: Delay the move operation until the next system reboot.
    /// If lpNewFileName is null, the file is deleted on reboot.
    /// </summary>
    internal const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    /// <summary>
    /// Flag: Enable move across volumes (copy + delete).
    /// </summary>
    internal const uint MOVEFILE_COPY_ALLOWED = 0x2;

    /// <summary>
    /// Flag: Write through (ensures the move is flushed to disk).
    /// </summary>
    internal const uint MOVEFILE_WRITE_THROUGH = 0x8;

    /// <summary>
    /// Moves an existing file or directory, including its children.
    /// With MOVEFILE_DELAY_UNTIL_REBOOT, the operation is deferred to next boot.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileEx(
        string lpExistingFileName,
        string? lpNewFileName,
        uint dwFlags);

    /// <summary>
    /// Schedules a file to be moved to a new location on the next system reboot.
    /// Used for files that are currently locked by another process.
    /// </summary>
    /// <param name="sourcePath">Current path of the locked file.</param>
    /// <param name="destinationPath">Target path after reboot.</param>
    /// <returns>True if the operation was successfully scheduled.</returns>
    internal static bool ScheduleMoveOnReboot(string sourcePath, string destinationPath)
    {
        return MoveFileEx(sourcePath, destinationPath,
            MOVEFILE_DELAY_UNTIL_REBOOT | MOVEFILE_REPLACE_EXISTING);
    }

    /// <summary>
    /// Schedules a file for deletion on the next system reboot.
    /// </summary>
    /// <param name="filePath">Path of the file to delete on reboot.</param>
    /// <returns>True if the operation was successfully scheduled.</returns>
    internal static bool ScheduleDeleteOnReboot(string filePath)
    {
        return MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
    }
}
