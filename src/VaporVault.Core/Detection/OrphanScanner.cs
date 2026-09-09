using System.Diagnostics;
using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Orchestrates the full orphan detection pipeline:
/// 1. Read installed apps from registry
/// 2. Enumerate app data folders on disk
/// 3. Match folders against installed apps
/// 4. Return unmatched folders as orphan candidates
/// 
/// This is purely read-only — no files are moved or modified.
/// </summary>
public class OrphanScanner
{
    private readonly IRegistryReader _registryReader;
    private readonly IAppDataFolderEnumerator _folderEnumerator;
    private readonly IOrphanMatcher _orphanMatcher;

    public OrphanScanner(
        IRegistryReader registryReader,
        IAppDataFolderEnumerator folderEnumerator,
        IOrphanMatcher orphanMatcher)
    {
        _registryReader = registryReader;
        _folderEnumerator = folderEnumerator;
        _orphanMatcher = orphanMatcher;
    }

    /// <summary>
    /// Default constructor using real implementations.
    /// </summary>
    public OrphanScanner()
        : this(new RegistryReader(), new AppDataFolderEnumerator(), new OrphanMatcher())
    {
    }

    /// <summary>
    /// Performs a full scan and returns orphan detection results.
    /// This is a potentially long-running operation (disk enumeration + size calculation).
    /// </summary>
    public ScanResult Scan()
    {
        var stopwatch = Stopwatch.StartNew();

        // Step 1: Read installed apps from registry
        var installedApps = _registryReader.GetInstalledApps();

        // Step 2: Enumerate app data folders
        var folders = _folderEnumerator.EnumerateFolders();

        // Step 3: Find orphans
        var orphans = _orphanMatcher.FindOrphans(installedApps, folders);

        stopwatch.Stop();

        return new ScanResult
        {
            OrphanedApps = orphans,
            ScanDuration = stopwatch.Elapsed,
            ScannedAtUtc = DateTime.UtcNow,
            TotalFoldersScanned = folders.Count,
            TotalInstalledAppsFound = installedApps.Count
        };
    }

    /// <summary>
    /// Async wrapper for Scan() that runs the scan on a background thread.
    /// </summary>
    public Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(), cancellationToken);
    }
}
