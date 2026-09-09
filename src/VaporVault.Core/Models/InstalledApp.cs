namespace VaporVault.Core.Models;

/// <summary>
/// Represents an application that is currently registered in the Windows Uninstall registry.
/// Populated from HKLM or HKCU \Software\Microsoft\Windows\CurrentVersion\Uninstall keys.
/// </summary>
public sealed class InstalledApp
{
    /// <summary>
    /// The display name from the registry (e.g., "Spotify" or "Google Chrome").
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The install location path, if specified in the registry. May be null.
    /// </summary>
    public string? InstallLocation { get; init; }

    /// <summary>
    /// The publisher name, if available.
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// The registry key name (subkey under Uninstall) that holds this entry.
    /// </summary>
    public required string RegistryKeyName { get; init; }

    /// <summary>
    /// Whether this entry was found under HKCU (true) or HKLM (false).
    /// </summary>
    public bool IsCurrentUser { get; init; }

    /// <summary>
    /// Normalized tokens derived from DisplayName for fuzzy matching.
    /// Computed once at construction time.
    /// </summary>
    public IReadOnlyList<string> NameTokens { get; init; } = Array.Empty<string>();
}
