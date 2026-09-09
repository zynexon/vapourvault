namespace VaporVault.Core.Models;

/// <summary>
/// Represents a single entry in the quarantine index (quarantine-index.json).
/// Tracks all currently quarantined apps for the Vault page display.
/// </summary>
public sealed class QuarantineEntry
{
    /// <summary>
    /// Unique identifier for this quarantine operation (e.g., "Spotify_a1b2c3d4").
    /// </summary>
    public required string QuarantineId { get; set; }

    /// <summary>
    /// The application name.
    /// </summary>
    public required string AppName { get; set; }

    /// <summary>
    /// When the quarantine was performed.
    /// </summary>
    public DateTime QuarantinedAt { get; set; }

    /// <summary>
    /// When the quarantine expires (auto-delete date, typically 30 days after quarantine).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Total original size in bytes.
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Whether background compression has completed.
    /// </summary>
    public bool Compressed { get; set; }

    /// <summary>
    /// Size after compression, if applicable.
    /// </summary>
    public long? CompressedSizeBytes { get; set; }

    /// <summary>
    /// Full path to the quarantine folder.
    /// </summary>
    public required string QuarantineFolderPath { get; set; }

    /// <summary>
    /// Whether there are files pending move-on-reboot.
    /// </summary>
    public bool HasPendingRebootFiles { get; set; }

    /// <summary>
    /// Days remaining until auto-deletion.
    /// </summary>
    public int DaysRemaining => Math.Max(0, (int)(ExpiresAt - DateTime.UtcNow).TotalDays);

    /// <summary>
    /// Current status of this quarantine entry.
    /// </summary>
    public QuarantineStatus Status { get; set; } = QuarantineStatus.Active;
}

/// <summary>
/// Status of a quarantine entry.
/// </summary>
public enum QuarantineStatus
{
    Active,
    Expired,
    Revived,
    Deleted
}
