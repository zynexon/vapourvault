using Microsoft.Win32;
using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Reads installed application entries from the Windows Uninstall registry keys.
/// Scans both HKLM and HKCU Uninstall locations to build a complete picture.
/// </summary>
public class RegistryReader : IRegistryReader
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    // Characters used to split display names into matchable tokens
    private static readonly char[] TokenSeparators = [' ', '-', '_', '.', '(', ')', ','];

    /// <summary>
    /// Reads all installed application entries from the Uninstall registry keys.
    /// </summary>
    public IReadOnlyList<InstalledApp> GetInstalledApps()
    {
        var apps = new List<InstalledApp>();

        // HKLM — machine-wide installs
        ReadUninstallKey(Registry.LocalMachine, UninstallKeyPath, isCurrentUser: false, apps);

        // HKCU — per-user installs
        ReadUninstallKey(Registry.CurrentUser, UninstallKeyPath, isCurrentUser: true, apps);

        // Also check the 32-bit Wow6432Node on 64-bit systems
        const string wow64KeyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        ReadUninstallKey(Registry.LocalMachine, wow64KeyPath, isCurrentUser: false, apps);

        return apps;
    }

    private static void ReadUninstallKey(RegistryKey rootKey, string keyPath, bool isCurrentUser, List<InstalledApp> results)
    {
        using var uninstallKey = rootKey.OpenSubKey(keyPath);
        if (uninstallKey is null) return;

        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            try
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                if (subKey is null) continue;

                var displayName = subKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                // Skip system components (they clutter matching without being relevant)
                var systemComponent = subKey.GetValue("SystemComponent");
                if (systemComponent is int sc && sc == 1) continue;

                var installLocation = subKey.GetValue("InstallLocation") as string;
                var publisher = subKey.GetValue("Publisher") as string;

                var nameTokens = TokenizeName(displayName);

                results.Add(new InstalledApp
                {
                    DisplayName = displayName,
                    InstallLocation = string.IsNullOrWhiteSpace(installLocation) ? null : installLocation,
                    Publisher = publisher,
                    RegistryKeyName = subKeyName,
                    IsCurrentUser = isCurrentUser,
                    NameTokens = nameTokens
                });
            }
            catch (System.Security.SecurityException)
            {
                // Some keys may not be readable without elevation — skip silently
            }
            catch (UnauthorizedAccessException)
            {
                // Same — skip
            }
        }
    }

    /// <summary>
    /// Splits a display name into normalized lowercase tokens for fuzzy matching.
    /// Example: "Google Chrome Beta" → ["google", "chrome", "beta"]
    /// </summary>
    internal static IReadOnlyList<string> TokenizeName(string displayName)
    {
        return displayName
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 1) // Skip single-char fragments
            .Distinct()
            .ToList();
    }
}

/// <summary>
/// Abstraction for reading installed apps from registry, to support unit testing.
/// </summary>
public interface IRegistryReader
{
    IReadOnlyList<InstalledApp> GetInstalledApps();
}
