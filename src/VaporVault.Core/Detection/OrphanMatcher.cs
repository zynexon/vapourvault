using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Matches app data folders against installed applications to identify orphans.
/// A folder is considered orphaned if no installed app's display name matches it.
/// 
/// Matching strategy:
/// 1. Case-insensitive exact match of folder name to any installed app's DisplayName.
/// 2. Case-insensitive check if folder name starts with any installed app's DisplayName (first word or full name).
/// 3. Token overlap: if the folder name contains a significant token from any installed app's name.
/// 4. Exclusion list: known Windows/Microsoft system folders are always excluded from orphan results.
/// </summary>
public class OrphanMatcher : IOrphanMatcher
{
    /// <summary>
    /// Well-known system/Microsoft folder names that should never be flagged as orphans.
    /// This prevents false positives on Windows infrastructure folders.
    /// </summary>
    private static readonly HashSet<string> ExcludedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows core
        "Microsoft", "Windows", "Temp", "Packages", "Package Cache",
        "Microsoft Corporation", "WindowsApps", "Microsoft SDKs",
        "Programs", "ProgramData", "Windows Defender",
        
        // .NET / dev tooling
        ".NET", "dotnet", "NuGet", "IISExpress", "MSBuild",
        "Microsoft.NET", "asp.net", "vs", "Visual Studio",
        
        // Common Windows infrastructure
        "D3DSCache", "ElevatedDiagnostics", "ConnectedDevicesPlatform",
        "CrashDumps", "Diagnostics", "History", "VirtualStore",
        "Publishers", "CryptonetURLCache", "IconCache",
        "MicrosoftEdge", "Microsoft Edge", "Edge",
        
        // System config
        "SystemCertificates", "CertificateRevocationList",
        "Adobe", "Sun", "Oracle", "Java",
        
        // VaporVault itself
        "VaporVault",
        
        // Temporary and cache folders
        "Local", "LocalLow", "Roaming", "Caches", "Cache",
        "ApplicationCache", "GrShaderCache", "GPUCache",
        "ShaderCache", "Code Cache", "DawnCache",
        
        // Common OS-level folders found in ProgramData
        "Desktop", "Documents", "Templates", "Application Data",
        "Start Menu", "regid.1991-06.com.microsoft",
        "SoftwareDistribution", "USOPrivate", "USOShared",
        "WindowsHolographicDevices", "ssh",
        
        // Package managers / frameworks that aren't user apps
        "pip", "npm", "yarn", "chocolatey", "scoop", "winget"
    };

    /// <summary>
    /// Minimum token length to consider meaningful for matching.
    /// Prevents false matches on very short tokens like "on", "de", etc.
    /// </summary>
    private const int MinMeaningfulTokenLength = 3;

    /// <summary>
    /// Identifies folders that are orphaned (no matching installed app).
    /// </summary>
    /// <param name="installedApps">Currently installed applications from the registry.</param>
    /// <param name="folders">App data folders found on disk.</param>
    /// <returns>List of orphaned applications with their associated folders.</returns>
    public IReadOnlyList<OrphanedApp> FindOrphans(
        IReadOnlyList<InstalledApp> installedApps,
        IReadOnlyList<AppDataFolder> folders)
    {
        // Build lookup structures for installed apps
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in installedApps)
        {
            appNameSet.Add(app.DisplayName);

            foreach (var token in app.NameTokens)
            {
                if (token.Length >= MinMeaningfulTokenLength)
                    appTokens.Add(token);
            }
        }

        // Group folders that are not matched by any installed app
        var orphanFolders = new Dictionary<string, List<AppDataFolder>>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            // Skip excluded system folders
            if (IsExcluded(folder.Name))
                continue;

            // Skip hidden/system folders (starting with .)
            if (folder.Name.StartsWith('.'))
                continue;

            // Check if this folder matches any installed app
            if (IsMatchedByInstalledApp(folder.Name, installedApps, appNameSet, appTokens))
                continue;

            // This folder is orphaned — group by folder name
            if (!orphanFolders.TryGetValue(folder.Name, out var list))
            {
                list = [];
                orphanFolders[folder.Name] = list;
            }
            list.Add(folder);
        }

        // Convert grouped folders into OrphanedApp instances
        return orphanFolders
            .Select(kvp => new OrphanedApp
            {
                AppName = kvp.Key,
                Folders = kvp.Value
            })
            .OrderByDescending(o => o.TotalSizeBytes) // Biggest first for user impact
            .ToList();
    }

    /// <summary>
    /// Determines whether a folder name matches any installed application.
    /// Uses a multi-tier matching strategy from strict to loose.
    /// </summary>
    internal static bool IsMatchedByInstalledApp(
        string folderName,
        IReadOnlyList<InstalledApp> installedApps,
        HashSet<string> appNameSet,
        HashSet<string> appTokens)
    {
        // Tier 1: Exact match (folder name == display name)
        if (appNameSet.Contains(folderName))
            return true;

        var folderLower = folderName.ToLowerInvariant();

        // Tier 2: Folder name starts with or is contained in an installed app name
        foreach (var app in installedApps)
        {
            var appLower = app.DisplayName.ToLowerInvariant();

            // "SpotifyAB" matches "Spotify", "Spotify Beta" matches "Spotify"
            if (folderLower.StartsWith(appLower) || appLower.StartsWith(folderLower))
                return true;

            // Check install location — if the folder path is within the install location
            if (!string.IsNullOrEmpty(app.InstallLocation))
            {
                var installLower = app.InstallLocation.ToLowerInvariant().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (installLower.Contains(folderLower) || folderLower.Contains(Path.GetFileName(installLower)))
                    return true;
            }
        }

        // Tier 3: Significant token overlap
        // The folder name must contain a "meaningful" token from an installed app
        // (at least 3 chars to avoid false positives like matching "on" or "to")
        var folderTokens = folderLower
            .Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= MinMeaningfulTokenLength)
            .ToList();

        foreach (var token in folderTokens)
        {
            if (appTokens.Contains(token))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a folder name is in the exclusion list.
    /// </summary>
    internal static bool IsExcluded(string folderName)
    {
        return ExcludedFolderNames.Contains(folderName);
    }
}

/// <summary>
/// Abstraction for orphan matching, to support unit testing.
/// </summary>
public interface IOrphanMatcher
{
    IReadOnlyList<OrphanedApp> FindOrphans(
        IReadOnlyList<InstalledApp> installedApps,
        IReadOnlyList<AppDataFolder> folders);
}
