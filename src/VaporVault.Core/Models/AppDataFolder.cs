namespace VaporVault.Core.Models;

/// <summary>
/// Represents a top-level subdirectory found in one of the scanned app data locations.
/// </summary>
public sealed class AppDataFolder
{
    /// <summary>
    /// The full path to the folder.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// The folder name (last segment of the path).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Which well-known location this folder was found in.
    /// </summary>
    public required AppDataLocation Location { get; init; }

    /// <summary>
    /// Total size in bytes of all files in this folder (recursive).
    /// Computed lazily — may be -1 if not yet calculated.
    /// </summary>
    public long SizeBytes { get; set; } = -1;

    /// <summary>
    /// The most recent write time of any file in the folder (recursive).
    /// Used to estimate "how long ago" the app was active.
    /// </summary>
    public DateTime LastWriteTimeUtc { get; set; } = DateTime.MinValue;
}

/// <summary>
/// The well-known app data locations that VaporVault scans.
/// </summary>
public enum AppDataLocation
{
    LocalAppData,
    RoamingAppData,
    ProgramData
}
