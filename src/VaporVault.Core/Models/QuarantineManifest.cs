using System.Text.Json.Serialization;

namespace VaporVault.Core.Models;

/// <summary>
/// Serializable manifest written to manifest.json inside each quarantine folder.
/// Records everything needed to restore the quarantined files to their original locations.
/// </summary>
public sealed class QuarantineManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("appName")]
    public required string AppName { get; set; }

    [JsonPropertyName("quarantineId")]
    public required string QuarantineId { get; set; }

    [JsonPropertyName("quarantinedAt")]
    public DateTime QuarantinedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("compressed")]
    public bool Compressed { get; set; }

    [JsonPropertyName("compressedSizeBytes")]
    public long? CompressedSizeBytes { get; set; }

    [JsonPropertyName("sources")]
    public List<QuarantineSource> Sources { get; set; } = [];

    [JsonPropertyName("pendingRebootFiles")]
    public List<PendingRebootFile> PendingRebootFiles { get; set; } = [];

    [JsonPropertyName("registryExport")]
    public RegistryExportInfo? RegistryExport { get; set; }

    [JsonPropertyName("restoreBat")]
    public string RestoreBat { get; set; } = "restore.bat";
}

/// <summary>
/// A single source directory that was quarantined.
/// </summary>
public sealed class QuarantineSource
{
    [JsonPropertyName("originalPath")]
    public required string OriginalPath { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("movedAt")]
    public DateTime MovedAt { get; set; }

    /// <summary>
    /// The subdirectory name inside the quarantine folder where this source's files live.
    /// </summary>
    [JsonPropertyName("quarantineSubDir")]
    public required string QuarantineSubDir { get; set; }
}

/// <summary>
/// A file that couldn't be moved immediately and is scheduled for reboot.
/// </summary>
public sealed class PendingRebootFile
{
    [JsonPropertyName("originalPath")]
    public required string OriginalPath { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending_reboot";
}

/// <summary>
/// Information about the exported registry key.
/// </summary>
public sealed class RegistryExportInfo
{
    [JsonPropertyName("keyPath")]
    public required string KeyPath { get; set; }

    [JsonPropertyName("fileName")]
    public required string FileName { get; set; }

    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; }
}
