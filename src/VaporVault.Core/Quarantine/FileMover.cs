namespace VaporVault.Core.Quarantine;

/// <summary>
/// Handles moving directories from their original locations to the quarantine folder.
/// Prefers instant same-partition Directory.Move(), falls back to copy+delete for cross-partition.
/// </summary>
public class FileMover
{
    /// <summary>
    /// Result of a file move operation.
    /// </summary>
    public record MoveResult(
        bool Success,
        int FilesMoved,
        int FilesFailedToMove,
        IReadOnlyList<string> LockedFiles,
        string? ErrorMessage);

    /// <summary>
    /// Moves a directory to the quarantine destination.
    /// Tries instant Directory.Move() first (same partition, near-instant).
    /// Falls back to file-by-file copy+delete if cross-partition or partial failure.
    /// </summary>
    /// <param name="sourcePath">Full path of the source directory.</param>
    /// <param name="destinationPath">Full path of the quarantine destination.</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0).</param>
    /// <returns>Result of the move operation.</returns>
    public MoveResult MoveDirectory(string sourcePath, string destinationPath, Action<double>? progress = null)
    {
        if (!Directory.Exists(sourcePath))
            return new MoveResult(false, 0, 0, [], $"Source directory does not exist: {sourcePath}");

        // Ensure parent of destination exists
        var destParent = Path.GetDirectoryName(destinationPath);
        if (destParent != null)
            Directory.CreateDirectory(destParent);

        // Try instant same-partition move first
        try
        {
            if (!Directory.Exists(destinationPath))
            {
                Directory.Move(sourcePath, destinationPath);
                progress?.Invoke(1.0);

                var fileCount = Directory.GetFiles(destinationPath, "*", SearchOption.AllDirectories).Length;
                return new MoveResult(true, fileCount, 0, [], null);
            }
        }
        catch (IOException)
        {
            // Cross-partition or destination exists — fall through to file-by-file copy
        }

        // Fall back to file-by-file copy + delete
        return CopyAndDelete(sourcePath, destinationPath, progress);
    }

    /// <summary>
    /// Copies all files from source to destination, then deletes source files.
    /// Used when Directory.Move() fails (cross-partition or partial locks).
    /// </summary>
    private static MoveResult CopyAndDelete(string sourcePath, string destinationPath, Action<double>? progress)
    {
        var lockedFiles = new List<string>();
        int filesMoved = 0;
        int filesFailed = 0;

        try
        {
            var allFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            int totalFiles = allFiles.Length;

            for (int i = 0; i < allFiles.Length; i++)
            {
                var file = allFiles[i];
                var relativePath = Path.GetRelativePath(sourcePath, file);
                var destFile = Path.Combine(destinationPath, relativePath);

                var destDir = Path.GetDirectoryName(destFile);
                if (destDir != null)
                    Directory.CreateDirectory(destDir);

                try
                {
                    File.Copy(file, destFile, overwrite: true);
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // File is locked — we copied it but can't delete the source
                        lockedFiles.Add(file);
                    }
                    filesMoved++;
                }
                catch (IOException)
                {
                    lockedFiles.Add(file);
                    filesFailed++;
                }
                catch (UnauthorizedAccessException)
                {
                    lockedFiles.Add(file);
                    filesFailed++;
                }

                progress?.Invoke((double)(i + 1) / totalFiles);
            }

            // Try to remove now-empty source directories
            TryDeleteEmptyDirectories(sourcePath);

            return new MoveResult(
                Success: filesFailed == 0,
                FilesMoved: filesMoved,
                FilesFailedToMove: filesFailed,
                LockedFiles: lockedFiles,
                ErrorMessage: filesFailed > 0 ? $"{filesFailed} file(s) could not be moved (locked or access denied)" : null);
        }
        catch (Exception ex)
        {
            return new MoveResult(false, filesMoved, filesFailed, lockedFiles, ex.Message);
        }
    }

    /// <summary>
    /// Recursively removes empty directories from the bottom up.
    /// </summary>
    private static void TryDeleteEmptyDirectories(string path)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                TryDeleteEmptyDirectories(dir);
            }

            if (Directory.GetFileSystemEntries(path).Length == 0)
            {
                Directory.Delete(path);
            }
        }
        catch { /* Best-effort cleanup */ }
    }
}
