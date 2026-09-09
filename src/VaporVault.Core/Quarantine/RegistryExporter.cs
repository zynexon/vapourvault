using System.Diagnostics;
using VaporVault.Core.Models;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Exports a single HKCU\Software\[AppName] registry branch to a .reg file.
/// Only exports the single per-user key — no HKLM, no other keys.
/// The .reg file is human-readable and can be manually merged to restore.
/// </summary>
public class RegistryExporter
{
    /// <summary>
    /// Exports the HKCU\Software\[AppName] registry key to a .reg file
    /// inside the quarantine folder.
    /// </summary>
    /// <param name="appName">Application name (used to find the registry key).</param>
    /// <param name="quarantineFolderPath">Path to the quarantine folder where the .reg file will be saved.</param>
    /// <returns>Registry export info if successful, null if the key doesn't exist or export failed.</returns>
    public RegistryExportInfo? ExportRegistryKey(string appName, string quarantineFolderPath)
    {
        var keyPath = $@"HKCU\Software\{appName}";
        var fileName = "registry_backup.reg";
        var fullExportPath = Path.Combine(quarantineFolderPath, fileName);

        // Check if the registry key actually exists
        if (!RegistryKeyExists(keyPath))
            return null;

        try
        {
            Directory.CreateDirectory(quarantineFolderPath);

            // Use reg.exe to export (it's always available on Windows)
            var startInfo = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"export \"{keyPath}\" \"{fullExportPath}\" /y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            process.WaitForExit(10_000); // 10 second timeout

            if (process.ExitCode == 0 && File.Exists(fullExportPath))
            {
                return new RegistryExportInfo
                {
                    KeyPath = keyPath,
                    FileName = fileName,
                    ExportedAt = DateTime.UtcNow
                };
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if a registry key exists using reg.exe query.
    /// </summary>
    private static bool RegistryKeyExists(string keyPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"query \"{keyPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
