using System.Text.Json.Serialization;

namespace VaporVault.Core.Models;

/// <summary>
/// Result of scanning for additional traces (scheduled tasks, services, file associations,
/// dead uninstall entries) associated with a quarantined app.
/// This is stored in the quarantine manifest for revive/restore purposes.
/// </summary>
public sealed class AppTraceResult
{
    [JsonPropertyName("scheduledTasks")]
    public List<DetectedTask> ScheduledTasks { get; set; } = [];

    [JsonPropertyName("services")]
    public List<DetectedService> Services { get; set; } = [];

    [JsonPropertyName("fileAssociations")]
    public List<DetectedAssociation> FileAssociations { get; set; } = [];

    [JsonPropertyName("deadUninstallEntries")]
    public List<DetectedDeadUninstallEntry> DeadUninstallEntries { get; set; } = [];

    /// <summary>
    /// Plain-English summary of what was found, for display in the manifest/UI.
    /// Empty if nothing was found.
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Whether any traces were found at all.
    /// </summary>
    [JsonIgnore]
    public bool HasAnyTraces =>
        ScheduledTasks.Count > 0 ||
        Services.Count > 0 ||
        FileAssociations.Count > 0 ||
        DeadUninstallEntries.Count > 0;
}

/// <summary>
/// A scheduled task detected as belonging to a quarantined app.
/// </summary>
public sealed class DetectedTask
{
    /// <summary>
    /// Full path of the task in Task Scheduler (e.g., "\Microsoft\EdgeUpdate\MicrosoftEdgeUpdateTaskMachineCore").
    /// </summary>
    [JsonPropertyName("path")]
    public required string Path { get; set; }

    /// <summary>
    /// Display name of the task.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Author field from the task definition, if available.
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// Whether the task is still enabled/active. False if we successfully disabled it.
    /// </summary>
    [JsonPropertyName("stillActive")]
    public bool StillActive { get; set; } = true;

    /// <summary>
    /// True if we attempted to disable the task but failed (e.g., access denied).
    /// </summary>
    [JsonPropertyName("disableFailed")]
    public bool DisableFailed { get; set; }
}

/// <summary>
/// A Windows service detected as belonging to a quarantined app.
/// </summary>
public sealed class DetectedService
{
    /// <summary>
    /// Internal service name (used by ServiceController).
    /// </summary>
    [JsonPropertyName("serviceName")]
    public required string ServiceName { get; set; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Path to the service binary, if available.
    /// </summary>
    [JsonPropertyName("binaryPath")]
    public string? BinaryPath { get; set; }

    /// <summary>
    /// Whether the service is still running/enabled. False if we successfully stopped it.
    /// </summary>
    [JsonPropertyName("stillActive")]
    public bool StillActive { get; set; } = true;

    /// <summary>
    /// True if we attempted to stop/disable the service but failed (e.g., access denied).
    /// </summary>
    [JsonPropertyName("disableFailed")]
    public bool DisableFailed { get; set; }
}

/// <summary>
/// A file association or shell handler detected as belonging to a quarantined app.
/// Detection only — no modification.
/// </summary>
public sealed class DetectedAssociation
{
    /// <summary>
    /// The file extension (e.g., ".mp3") or protocol (e.g., "spotify:").
    /// </summary>
    [JsonPropertyName("extension")]
    public required string Extension { get; set; }

    /// <summary>
    /// The command path that points to the app's binary.
    /// </summary>
    [JsonPropertyName("commandPath")]
    public required string CommandPath { get; set; }

    /// <summary>
    /// Type of handler: "FileType", "Protocol", or "ShellExtension".
    /// </summary>
    [JsonPropertyName("handlerType")]
    public required string HandlerType { get; set; }
}

/// <summary>
/// An orphaned Uninstall registry entry that has no working UninstallString.
/// </summary>
public sealed class DetectedDeadUninstallEntry
{
    /// <summary>
    /// The registry subkey name under Uninstall.
    /// </summary>
    [JsonPropertyName("registryKeyName")]
    public required string RegistryKeyName { get; set; }

    /// <summary>
    /// The DisplayName value from the entry.
    /// </summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Which registry root this was found in (e.g., "HKLM", "HKCU", "HKLM_WOW64").
    /// </summary>
    [JsonPropertyName("registryRoot")]
    public required string RegistryRoot { get; set; }
}
