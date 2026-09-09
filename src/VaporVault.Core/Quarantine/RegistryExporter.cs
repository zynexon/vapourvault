using System.Diagnostics;
using System.Text.RegularExpressions;
using VaporVault.Core.Models;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Exports a single HKCU\Software\[AppName] registry branch to a .reg file.
/// Only exports the single per-user key — no HKLM, no other keys.
/// The .reg file is human-readable and can be manually merged to restore.
/// </summary>
public class RegistryExporter
{
    // Allow only safe characters in app names passed to reg.exe
    // Letters, digits, spaces, dots, dashes, underscores, plus signs
    private static readonly Regex SafeAppNamePattern = new(@"^[\w\s.\-+()]+$", RegexOptions.Compiled);

    /// <summary>
    /// Exports the HKCU\Software\[AppName] registry key to a .reg file
    /// inside the quarantine folder.
    /// </summary>
    /// <param name="appName">Application name (used to find the registry key).</param>
    /// <param name="quarantineFolderPath">Path to the quarantine folder where the .reg file will be saved.</param>
    /// <returns>Registry export info if successful, null if the key doesn't exist or export failed.</returns>
    public RegistryExportInfo? ExportRegistryKey(string appName, string quarantineFolderPath)
    {
        // Sanitize app name to prevent command injection via reg.exe arguments
        var sanitizedName = SanitizeAppName(appName);
        if (string.IsNullOrEmpty(sanitizedName))
            return null;

        var keyPath = $@"HKCU\Software\{sanitizedName}";
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
    /// Sanitizes the app name to prevent command injection when passed to reg.exe.
    /// Strips any characters that are not alphanumeric, spaces, dots, dashes,
    /// underscores, plus signs, or parentheses.
    /// Returns null if the result is empty or the original contains suspicious patterns.
    /// </summary>
    internal static string? SanitizeAppName(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return null;

        // Reject names containing shell metacharacters that could escape the quoted argument
        if (appName.Contains('"') || appName.Contains('`') || appName.Contains('$') ||
            appName.Contains('|') || appName.Contains('&') || appName.Contains('>') ||
            appName.Contains('<') || appName.Contains(';') || appName.Contains('%'))
        {
            return null;
        }

        // Strip any remaining characters outside the safe set
        var sanitized = Regex.Replace(appName, @"[^\w\s.\-+()]", "").Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
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
