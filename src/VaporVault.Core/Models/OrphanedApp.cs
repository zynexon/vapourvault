namespace VaporVault.Core.Models;

/// <summary>
/// Represents an orphaned application: one or more app data folders that have no
/// corresponding entry in the Windows Uninstall registry. This is a candidate for quarantine.
/// </summary>
public sealed class OrphanedApp
{
    /// <summary>
    /// The inferred application name, derived from the folder name.
    /// </summary>
    public required string AppName { get; init; }

    /// <summary>
    /// All folders across the scanned locations that belong to this orphaned app.
    /// Typically one per location (LocalAppData, RoamingAppData, ProgramData).
    /// </summary>
    public required IReadOnlyList<AppDataFolder> Folders { get; init; }

    /// <summary>
    /// Total size in bytes across all folders.
    /// </summary>
    public long TotalSizeBytes => Folders.Sum(f => Math.Max(0, f.SizeBytes));

    /// <summary>
    /// Estimated time since the app was last active, based on the most recent
    /// file write time across all folders.
    /// </summary>
    public DateTime EstimatedLastActiveUtc =>
        Folders.Max(f => f.LastWriteTimeUtc);

    /// <summary>
    /// Human-readable description for the UI card.
    /// Example: "Found 4.2 GB left behind by Spotify (uninstalled ~4 months ago)"
    /// </summary>
    public string GetCardDescription()
    {
        var sizeStr = FormatSize(TotalSizeBytes);
        var agoStr = FormatTimeAgo(DateTime.UtcNow - EstimatedLastActiveUtc);
        return $"Found {sizeStr} left behind by {AppName} (uninstalled ~{agoStr} ago)";
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }

    private static string FormatTimeAgo(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 365)
            return $"{(int)(elapsed.TotalDays / 365)} year{((int)(elapsed.TotalDays / 365) == 1 ? "" : "s")}";
        if (elapsed.TotalDays >= 30)
            return $"{(int)(elapsed.TotalDays / 30)} month{((int)(elapsed.TotalDays / 30) == 1 ? "" : "s")}";
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays} day{((int)elapsed.TotalDays == 1 ? "" : "s")}";
        return "less than a day";
    }
}
