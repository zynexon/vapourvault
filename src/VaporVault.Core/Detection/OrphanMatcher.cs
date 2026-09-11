using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Matches app data folders against installed applications to identify orphans.
/// A folder is considered orphaned if no installed app's display name matches it.
/// 
/// Matching strategy:
/// 1. Case-insensitive exact match of folder name to any installed app's DisplayName.
/// 2. Case-insensitive check if folder name starts with any installed app's DisplayName (first word or full name).
/// 3. Token overlap: if the folder name contains a significant token from an installed app's name.
/// 4. Alias matching: known folder-name-to-app-name mappings (e.g., npm → Node.js).
/// 5. PATH fallback: verify whether the tool's executable is reachable via PATH.
/// 6. Exclusion list: known Windows/Microsoft system folders are always excluded from orphan results.
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
        "Comms", "Backup", "PlaceholderTileLogoFolder",
        "Temporary Internet Files", "VEDetector",
        
        // System config
        "SystemCertificates", "CertificateRevocationList",
        "Adobe", "Sun", "Oracle", "Java",
        
        // VaporVault itself and its dependencies
        "VaporVault", "ToastNotificationManagerCompat",
        
        // Temporary and cache folders
        "Local", "LocalLow", "Roaming", "Caches", "Cache",
        "ApplicationCache", "GrShaderCache", "GPUCache",
        "ShaderCache", "Code Cache", "DawnCache",
        
        // Common OS-level folders found in ProgramData
        "Desktop", "Documents", "Templates", "Application Data",
        "Start Menu", "regid.1991-06.com.microsoft",
        "SoftwareDistribution", "USOPrivate", "USOShared",
        "WindowsHolographicDevices", "ssh",
        
        // Microsoft dev/test tools
        "ms-playwright-go",
        
        // Package managers / frameworks / dev tools that aren't user apps
        "pip", "npm", "yarn", "chocolatey", "scoop", "winget",
        "jupyter", "ipython", "ngrok"
    };

    /// <summary>
    /// Dev-tool folder name prefixes that should be excluded via prefix matching,
    /// not just exact matching. This catches variants like "npm-cache", "pip-cache", etc.
    /// Only entries that are known package-manager/dev-tool cache directories are listed here.
    /// </summary>
    private static readonly string[] DevToolPrefixExclusions =
    [
        "npm", "pip", "yarn", "nuget", "chocolatey", "scoop", "winget", "dotnet",
        "jupyter", "ipython", "ms-playwright"
    ];

    /// <summary>
    /// Maps known AppData folder name patterns to the installed app names they belong to.
    /// This extends what a folder name is allowed to match against — it does NOT change
    /// what "installed" means; registry presence remains the source of truth.
    /// Keys are matched case-insensitively, with prefix matching (e.g., "npm-cache-2024" → "npm-cache" → "npm").
    /// </summary>
    private static readonly Dictionary<string, string[]> FolderNameAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["npm"] = ["node.js", "node", "nvm for windows", "nvm"],
        ["npm-cache"] = ["node.js", "node", "nvm for windows", "nvm"],
        ["pip"] = ["python", "python launcher"],
        ["nuget"] = ["visual studio", ".net sdk", ".net"],
        ["docker"] = ["docker desktop", "docker"],
        ["composer"] = ["php"],
        ["cargo"] = ["rust", "rustup"],
        ["gradle"] = ["java", "openjdk"],
        ["maven"] = ["java", "openjdk"],
        ["jupyter"] = ["python", "python launcher"],
        ["ipython"] = ["python", "python launcher"],
    };

    /// <summary>
    /// Maps folder name prefixes to executable names for PATH-based fallback detection.
    /// Only used as a secondary signal when alias + token matching fails.
    /// </summary>
    private static readonly Dictionary<string, string[]> FolderToExecutables =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["npm"] = ["node", "npm"],
        ["npm-cache"] = ["node", "npm"],
        ["pip"] = ["python", "pip"],
        ["nuget"] = ["dotnet", "nuget"],
        ["docker"] = ["docker"],
        ["composer"] = ["php", "composer"],
        ["cargo"] = ["rustc", "cargo"],
        ["gradle"] = ["gradle", "java"],
        ["maven"] = ["mvn", "java"],
        ["yarn"] = ["yarn", "node"],
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
            // Skip excluded system folders (exact match + dev-tool prefix match)
            if (IsExcluded(folder.Name))
                continue;

            // Skip hidden/system folders (starting with .)
            if (folder.Name.StartsWith('.'))
                continue;

            // Check if this folder matches any installed app (includes alias matching)
            if (IsMatchedByInstalledApp(folder.Name, installedApps, appNameSet, appTokens))
                continue;

            // Part 3 fallback: check if the tool is reachable via PATH
            if (IsToolReachableViaPath(folder.Name))
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
    /// Uses a multi-tier matching strategy from strict to loose, including alias matching.
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

        // Tier 4: Alias matching — check if folder name maps to a known installed app
        if (IsMatchedByAlias(folderName, appNameSet, installedApps))
            return true;

        return false;
    }

    /// <summary>
    /// Checks the alias table for the folder name (with prefix matching).
    /// If the folder name maps to known app names via the alias table, checks those
    /// aliases against the installed apps using the same fuzzy matching tiers 1 & 2.
    /// </summary>
    internal static bool IsMatchedByAlias(
        string folderName,
        HashSet<string> appNameSet,
        IReadOnlyList<InstalledApp> installedApps)
    {
        var aliasedNames = ResolveAliases(folderName);
        if (aliasedNames == null)
            return false;

        foreach (var aliasName in aliasedNames)
        {
            // Tier 1 for alias: exact match
            if (appNameSet.Contains(aliasName))
                return true;

            var aliasLower = aliasName.ToLowerInvariant();

            // Tier 2 for alias: prefix match
            foreach (var app in installedApps)
            {
                var appLower = app.DisplayName.ToLowerInvariant();
                if (appLower.StartsWith(aliasLower) || aliasLower.StartsWith(appLower))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves folder name to alias entries using prefix matching.
    /// E.g., "npm-cache-2024" first checks "npm-cache-2024", then "npm-cache", then "npm".
    /// Returns null if no alias is found.
    /// </summary>
    internal static string[]? ResolveAliases(string folderName)
    {
        // Direct match first (most specific)
        if (FolderNameAliases.TryGetValue(folderName, out var direct))
            return direct;

        // Prefix matching: try progressively shorter prefixes
        // E.g., "npm-cache-2024" → check "npm-cache" → check "npm"
        var folderLower = folderName.ToLowerInvariant();
        foreach (var kvp in FolderNameAliases)
        {
            if (folderLower.StartsWith(kvp.Key.ToLowerInvariant()))
                return kvp.Value;
        }

        return null;
    }

    /// <summary>
    /// Checks if a folder name is in the exclusion list.
    /// Uses exact matching for most entries, plus prefix matching for known
    /// dev-tool cache directories (e.g., "npm-cache" matches the "npm" prefix exclusion).
    /// </summary>
    internal static bool IsExcluded(string folderName)
    {
        // Exact match first
        if (ExcludedFolderNames.Contains(folderName))
            return true;

        // Prefix match for dev-tool entries only
        var folderLower = folderName.ToLowerInvariant();
        foreach (var prefix in DevToolPrefixExclusions)
        {
            if (folderLower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                folderLower.Length > prefix.Length &&
                (folderLower[prefix.Length] == '-' || folderLower[prefix.Length] == '_' || folderLower[prefix.Length] == '.'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Part 3 fallback: Checks if a tool associated with the folder name is reachable via PATH.
    /// This is a secondary signal only — it should never override a clear registry-based match
    /// or mismatch. Used only to reduce false positives in genuinely ambiguous cases.
    /// </summary>
    internal static bool IsToolReachableViaPath(string folderName)
    {
        var executables = ResolveExecutables(folderName);
        if (executables == null)
            return false;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var exe in executables)
        {
            var exeFileName = exe + ".exe";
            foreach (var dir in pathDirs)
            {
                try
                {
                    var fullPath = Path.Combine(dir, exeFileName);
                    if (File.Exists(fullPath))
                        return true;
                }
                catch
                {
                    // Skip inaccessible directories
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves folder name to known executable names for PATH checking.
    /// Uses the same prefix-matching approach as alias resolution.
    /// </summary>
    internal static string[]? ResolveExecutables(string folderName)
    {
        // Direct match first
        if (FolderToExecutables.TryGetValue(folderName, out var direct))
            return direct;

        // Prefix matching
        var folderLower = folderName.ToLowerInvariant();
        foreach (var kvp in FolderToExecutables)
        {
            if (folderLower.StartsWith(kvp.Key.ToLowerInvariant()))
                return kvp.Value;
        }

        return null;
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
