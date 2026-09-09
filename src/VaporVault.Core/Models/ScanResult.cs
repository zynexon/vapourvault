namespace VaporVault.Core.Models;

/// <summary>
/// Aggregated result of an orphan detection scan.
/// </summary>
public sealed class ScanResult
{
    /// <summary>
    /// List of orphaned applications detected.
    /// </summary>
    public required IReadOnlyList<OrphanedApp> OrphanedApps { get; init; }

    /// <summary>
    /// How long the scan took.
    /// </summary>
    public TimeSpan ScanDuration { get; init; }

    /// <summary>
    /// When the scan was performed.
    /// </summary>
    public DateTime ScannedAtUtc { get; init; }

    /// <summary>
    /// Total number of folders scanned across all locations.
    /// </summary>
    public int TotalFoldersScanned { get; init; }

    /// <summary>
    /// Total number of installed apps found in the registry.
    /// </summary>
    public int TotalInstalledAppsFound { get; init; }

    /// <summary>
    /// Total reclaimable space across all orphaned apps, in bytes.
    /// </summary>
    public long TotalReclaimableBytes => OrphanedApps.Sum(o => o.TotalSizeBytes);
}
