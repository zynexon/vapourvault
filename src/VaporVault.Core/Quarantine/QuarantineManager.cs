using VaporVault.Core.Data;
using VaporVault.Core.Detection;
using VaporVault.Core.Interop;
using VaporVault.Core.Models;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Orchestrates the full quarantine flow for an orphaned app:
/// 1. Check for running processes → offer to close them
/// 2. Check for locked file handles via Restart Manager
/// 3. Move unlocked files to quarantine folder (instant same-partition move)
/// 4. Schedule locked files for move-on-reboot via MoveFileEx
/// 5. Write manifest.json
/// 6. Generate restore.bat
/// 7. Export HKCU\Software\[App] registry key
/// 8. v3: Detect additional traces (scheduled tasks, services, associations)
/// 9. Update quarantine index
/// </summary>
public class QuarantineManager
{
    private readonly string _quarantineBasePath;
    private readonly FileMover _fileMover;
    private readonly LockedFileHandler _lockedFileHandler;
    private readonly ManifestWriter _manifestWriter;
    private readonly RestoreBatGenerator _restoreBatGenerator;
    private readonly RegistryExporter _registryExporter;
    private readonly QuarantineIndex _quarantineIndex;
    private readonly IAppTraceDetector _traceDetector;
    private readonly AppTraceDisabler _traceDisabler;

    /// <summary>
    /// Quarantine hold period in days.
    /// </summary>
    public const int QuarantineDays = 30;

    public QuarantineManager()
        : this(QuarantineIndex.DefaultQuarantineBasePath)
    {
    }

    public QuarantineManager(string quarantineBasePath)
    {
        _quarantineBasePath = quarantineBasePath;
        _fileMover = new FileMover();
        _lockedFileHandler = new LockedFileHandler();
        _manifestWriter = new ManifestWriter();
        _restoreBatGenerator = new RestoreBatGenerator();
        _registryExporter = new RegistryExporter();
        _quarantineIndex = new QuarantineIndex();
        _traceDetector = new AppTraceDetector();
        _traceDisabler = new AppTraceDisabler();
    }

    /// <summary>
    /// Constructor for dependency injection / testing.
    /// </summary>
    public QuarantineManager(
        string quarantineBasePath,
        QuarantineIndex quarantineIndex)
    {
        _quarantineBasePath = quarantineBasePath;
        _fileMover = new FileMover();
        _lockedFileHandler = new LockedFileHandler();
        _manifestWriter = new ManifestWriter();
        _restoreBatGenerator = new RestoreBatGenerator();
        _registryExporter = new RegistryExporter();
        _quarantineIndex = quarantineIndex;
        _traceDetector = new AppTraceDetector();
        _traceDisabler = new AppTraceDisabler();
    }

    /// <summary>
    /// Result of a quarantine operation.
    /// </summary>
    public record QuarantineResult(
        bool Success,
        string QuarantineId,
        string QuarantineFolderPath,
        int TotalFilesMoved,
        int PendingRebootFiles,
        string? ErrorMessage);

    /// <summary>
    /// Checks for running processes that match the app name.
    /// Call this before quarantine to give the user a chance to close them.
    /// </summary>
    public IReadOnlyList<System.Diagnostics.Process> FindRunningProcesses(string appName)
    {
        return ProcessHelper.FindProcessesByAppName(appName);
    }

    /// <summary>
    /// Checks for locked files in the specified folders.
    /// Call this after the user has closed processes to see if anything else holds locks.
    /// </summary>
    public LockedFileHandler.LockCheckResult CheckLockedFiles(IEnumerable<string> folderPaths)
    {
        var allLockedFiles = new List<string>();
        var allLockingProcesses = new List<LockedFileHandler.LockingProcessInfo>();
        var seenProcessIds = new HashSet<int>();

        foreach (var path in folderPaths)
        {
            var result = _lockedFileHandler.CheckForLockedFiles(path);
            allLockedFiles.AddRange(result.LockedFiles);

            foreach (var proc in result.LockingProcesses)
            {
                if (seenProcessIds.Add(proc.ProcessId))
                    allLockingProcesses.Add(proc);
            }
        }

        return new LockedFileHandler.LockCheckResult(allLockedFiles, allLockingProcesses);
    }

    /// <summary>
    /// Performs the full quarantine operation on an orphaned app.
    /// </summary>
    /// <param name="orphan">The orphaned app to quarantine.</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0).</param>
    /// <returns>Result of the quarantine operation.</returns>
    public QuarantineResult Quarantine(OrphanedApp orphan, Action<double>? progress = null)
    {
        var quarantineId = $"{SanitizeFolderName(orphan.AppName)}_{Guid.NewGuid().ToString("N")[..8]}";
        var quarantineFolderPath = Path.Combine(_quarantineBasePath, quarantineId);
        var now = DateTime.UtcNow;

        Directory.CreateDirectory(quarantineFolderPath);

        var manifest = new QuarantineManifest
        {
            AppName = orphan.AppName,
            QuarantineId = quarantineId,
            QuarantinedAt = now,
            ExpiresAt = now.AddDays(QuarantineDays),
            TotalSizeBytes = orphan.TotalSizeBytes
        };

        int totalFilesMoved = 0;
        var pendingRebootFiles = new List<PendingRebootFile>();
        int folderIndex = 0;

        foreach (var folder in orphan.Folders)
        {
            folderIndex++;
            var subDirName = folder.Location.ToString();
            var destPath = Path.Combine(quarantineFolderPath, subDirName);

            // Move the folder
            var moveResult = _fileMover.MoveDirectory(folder.FullPath, destPath, p =>
            {
                var overallProgress = ((folderIndex - 1) + p) / orphan.Folders.Count;
                progress?.Invoke(overallProgress);
            });

            // Count files
            int fileCount;
            try
            {
                fileCount = Directory.Exists(destPath)
                    ? Directory.GetFiles(destPath, "*", SearchOption.AllDirectories).Length
                    : 0;
            }
            catch
            {
                fileCount = moveResult.FilesMoved;
            }

            totalFilesMoved += moveResult.FilesMoved;

            // Handle locked files — schedule for reboot move
            foreach (var lockedFile in moveResult.LockedFiles)
            {
                var relativePath = Path.GetRelativePath(folder.FullPath, lockedFile);
                var destFile = Path.Combine(destPath, relativePath);

                if (_lockedFileHandler.ScheduleMoveOnReboot(lockedFile, destFile))
                {
                    pendingRebootFiles.Add(new PendingRebootFile
                    {
                        OriginalPath = lockedFile
                    });
                }
            }

            manifest.Sources.Add(new QuarantineSource
            {
                OriginalPath = folder.FullPath,
                Type = folder.Location.ToString(),
                FileCount = fileCount,
                SizeBytes = folder.SizeBytes,
                MovedAt = DateTime.UtcNow,
                QuarantineSubDir = subDirName
            });
        }

        manifest.PendingRebootFiles = pendingRebootFiles;

        // Export registry key (best-effort, not a failure if it doesn't exist)
        manifest.RegistryExport = _registryExporter.ExportRegistryKey(orphan.AppName, quarantineFolderPath);

        // v3: Detect additional traces (scheduled tasks, services, associations, dead entries)
        try
        {
            var traces = _traceDetector.DetectTraces(orphan.AppName);
            if (traces.HasAnyTraces)
            {
                manifest.AdditionalTraces = traces;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"QuarantineManager: Trace detection failed for '{orphan.AppName}': {ex.Message}");
        }

        // Write manifest
        _manifestWriter.WriteManifest(quarantineFolderPath, manifest);

        // Generate restore.bat
        _restoreBatGenerator.GenerateRestoreBat(quarantineFolderPath, manifest);

        // Update quarantine index
        _quarantineIndex.Add(new QuarantineEntry
        {
            QuarantineId = quarantineId,
            AppName = orphan.AppName,
            QuarantinedAt = now,
            ExpiresAt = now.AddDays(QuarantineDays),
            TotalSizeBytes = orphan.TotalSizeBytes,
            QuarantineFolderPath = quarantineFolderPath,
            HasPendingRebootFiles = pendingRebootFiles.Count > 0,
            Status = QuarantineStatus.Active
        });

        progress?.Invoke(1.0);

        return new QuarantineResult(
            Success: true,
            QuarantineId: quarantineId,
            QuarantineFolderPath: quarantineFolderPath,
            TotalFilesMoved: totalFilesMoved,
            PendingRebootFiles: pendingRebootFiles.Count,
            ErrorMessage: pendingRebootFiles.Count > 0
                ? $"{pendingRebootFiles.Count} file(s) will move on next restart."
                : null);
    }

    /// <summary>
    /// v3: Disables detected traces (scheduled tasks, services) after user confirmation.
    /// Call this after Quarantine() completes, if the user agrees to disable.
    /// Updates the manifest in place.
    /// </summary>
    /// <param name="quarantineFolderPath">Path to the quarantine folder.</param>
    public void DisableDetectedTraces(string quarantineFolderPath)
    {
        var manifest = _manifestWriter.ReadManifest(quarantineFolderPath);
        if (manifest?.AdditionalTraces == null) return;

        _traceDisabler.DisableTraces(manifest.AdditionalTraces);

        // Re-write manifest with updated StillActive/DisableFailed flags
        _manifestWriter.WriteManifest(quarantineFolderPath, manifest);
    }

    /// <summary>
    /// v3: Checks whether quarantining an app of the given size would exceed the vault cap.
    /// </summary>
    /// <param name="incomingSizeBytes">Size of the app about to be quarantined.</param>
    /// <returns>Cap check result with current usage, cap, and oldest entry if applicable.</returns>
    public VaultCapEnforcer.CapCheckResult CheckVaultCap(long incomingSizeBytes)
    {
        var enforcer = new VaultCapEnforcer(_quarantineIndex);
        return enforcer.CheckCap(incomingSizeBytes);
    }

    /// <summary>
    /// v3: Returns the total size of all active quarantine entries.
    /// </summary>
    public long GetTotalActiveVaultSize()
    {
        return _quarantineIndex.GetActive().Sum(e => e.TotalSizeBytes);
    }

    /// <summary>
    /// Async wrapper for the quarantine operation.
    /// </summary>
    public Task<QuarantineResult> QuarantineAsync(OrphanedApp orphan, Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Quarantine(orphan, progress), cancellationToken);
    }

    /// <summary>
    /// Sanitizes a string for use as a folder name.
    /// </summary>
    private static string SanitizeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
