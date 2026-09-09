using System.Text.Json;
using VaporVault.Core.Models;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Writes and reads manifest.json files inside quarantine folders.
/// The manifest records everything needed to restore files to their original locations.
/// </summary>
public class ManifestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Writes a manifest.json file to the specified quarantine folder.
    /// </summary>
    /// <param name="quarantineFolderPath">Path to the quarantine folder.</param>
    /// <param name="manifest">The manifest data to write.</param>
    public void WriteManifest(string quarantineFolderPath, QuarantineManifest manifest)
    {
        Directory.CreateDirectory(quarantineFolderPath);
        var manifestPath = Path.Combine(quarantineFolderPath, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(manifestPath, json);
    }

    /// <summary>
    /// Reads a manifest.json file from a quarantine folder.
    /// </summary>
    /// <param name="quarantineFolderPath">Path to the quarantine folder.</param>
    /// <returns>The deserialized manifest, or null if the file doesn't exist or is invalid.</returns>
    public QuarantineManifest? ReadManifest(string quarantineFolderPath)
    {
        var manifestPath = Path.Combine(quarantineFolderPath, "manifest.json");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<QuarantineManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Updates an existing manifest (e.g., to mark compression complete).
    /// </summary>
    /// <param name="quarantineFolderPath">Path to the quarantine folder.</param>
    /// <param name="updateAction">Action to modify the manifest.</param>
    /// <returns>True if the manifest was updated successfully.</returns>
    public bool UpdateManifest(string quarantineFolderPath, Action<QuarantineManifest> updateAction)
    {
        var manifest = ReadManifest(quarantineFolderPath);
        if (manifest == null) return false;

        updateAction(manifest);
        WriteManifest(quarantineFolderPath, manifest);
        return true;
    }
}
