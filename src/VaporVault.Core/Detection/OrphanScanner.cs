using System.Diagnostics;
using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Orchestrates the full orphan detection pipeline:
/// 1. Read installed apps from registry
/// 2. Enumerate app data folders on disk
/// 3. Match folders against installed apps
/// 4. Return unmatched folders as orphan candidates
/// 
/// This is purely read-only — no files are moved or modified.
/// </summary>
public class OrphanScanner
{
    private readonly IRegistryReader _registryReader;
    private readonly IAppDataFolderEnumerator _folderEnumerator;
    private readonly IOrphanMatcher _orphanMatcher;

    public OrphanScanner(
        IRegistryReader registryReader,
        IAppDataFolderEnumerator folderEnumerator,
        IOrphanMatcher orphanMatcher)
    {
        _registryReader = registryReader;
        _folderEnumerator = folderEnumerator;
        _orphanMatcher = orphanMatcher;
    }

    /// <summary>
    /// Default constructor using real implementations.
    /// </summary>
    public OrphanScanner()
        : this(new RegistryReader(), new AppDataFolderEnumerator(), new OrphanMatcher())
    {
    }

    /// <summary>
    /// Performs a full scan and returns orphan detection results.
    /// This is a potentially long-running operation (disk enumeration + size calculation).
    /// </summary>
    public ScanResult Scan()
    {
        var stopwatch = Stopwatch.StartNew();

        // Step 1: Read installed apps from registry
        var installedApps = _registryReader.GetInstalledApps();

        // Step 2: Enumerate app data folders
        var folders = _folderEnumerator.EnumerateFolders();

        // Step 3: Find orphans
        var orphans = _orphanMatcher.FindOrphans(installedApps, folders);

        stopwatch.Stop();

        return new ScanResult
        {
            OrphanedApps = orphans,
            ScanDuration = stopwatch.Elapsed,
            ScannedAtUtc = DateTime.UtcNow,
            TotalFoldersScanned = folders.Count,
            TotalInstalledAppsFound = installedApps.Count
        };
    }

    /// <summary>
    /// Async wrapper for Scan() that runs the scan on a background thread.
    /// </summary>
    public Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(), cancellationToken);
    }

    /// <summary>
    /// Performs a targeted scan for leftovers of a specific app that was just uninstalled.
    /// Much faster than a full scan — only checks folders matching the given app name.
    ///
    /// This does NOT read the registry (the app is already gone from there).
    /// Instead, it enumerates AppData folders and checks which ones match the
    /// given display name using the same fuzzy-matching logic as the full scan.
    /// </summary>
    /// <param name="appDisplayName">The display name of the just-uninstalled app (from the old snapshot).</param>
    /// <returns>Orphaned app entries matching this specific app, or empty if no leftovers found.</returns>
    public IReadOnlyList<OrphanedApp> ScanForApp(string appDisplayName)
    {
        if (string.IsNullOrWhiteSpace(appDisplayName))
            return [];

        // Enumerate all AppData folders (fast — just top-level directory listing)
        var folders = _folderEnumerator.EnumerateFolders();

        // Tokenize the app name for matching
        var appNameLower = appDisplayName.ToLowerInvariant();
        var appTokens = RegistryReader.TokenizeName(appDisplayName);

        // Find folders that match this app name
        var matchingFolders = new List<AppDataFolder>();

        foreach (var folder in folders)
        {
            if (OrphanMatcher.IsExcluded(folder.Name))
                continue;
            if (folder.Name.StartsWith('.'))
                continue;

            if (FolderMatchesApp(folder.Name, appNameLower, appTokens))
            {
                matchingFolders.Add(folder);
            }
        }

        if (matchingFolders.Count == 0)
            return [];

        // Group into a single OrphanedApp
        return
        [
            new OrphanedApp
            {
                AppName = appDisplayName,
                Folders = matchingFolders
            }
        ];
    }

    /// <summary>
    /// Async wrapper for ScanForApp().
    /// </summary>
    public Task<IReadOnlyList<OrphanedApp>> ScanForAppAsync(string appDisplayName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ScanForApp(appDisplayName), cancellationToken);
    }

    /// <summary>
    /// Checks if a folder name matches a specific app's display name.
    /// Uses the same multi-tier matching strategy as OrphanMatcher, but in reverse:
    /// instead of "does any installed app own this folder?", we ask
    /// "does this folder belong to this specific uninstalled app?".
    /// </summary>
    private static bool FolderMatchesApp(string folderName, string appNameLower,
        IReadOnlyList<string> appTokens)
    {
        var folderLower = folderName.ToLowerInvariant();

        // Tier 1: Exact match
        if (folderLower == appNameLower)
            return true;

        // Tier 2: Prefix match (either direction)
        if (folderLower.StartsWith(appNameLower) || appNameLower.StartsWith(folderLower))
            return true;

        // Tier 3: Token overlap — folder contains a significant token from the app name
        if (appTokens.Count > 0)
        {
            var folderTokens = folderLower
                .Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .ToList();

            var appTokenSet = new HashSet<string>(appTokens, StringComparer.OrdinalIgnoreCase);
            foreach (var token in folderTokens)
            {
                if (appTokenSet.Contains(token))
                    return true;
            }
        }

        return false;
    }
}
