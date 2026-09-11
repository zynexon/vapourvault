using VaporVault.Core.Detection;
using VaporVault.Core.Models;

namespace VaporVault.Core.Tests.Detection;

/// <summary>
/// Tests for OrphanMatcher — the core matching logic that determines
/// which app data folders are orphaned vs. still belonging to installed apps.
/// </summary>
public class OrphanMatcherTests
{
    private readonly OrphanMatcher _matcher = new();

    #region Helper Methods

    private static InstalledApp MakeApp(string displayName, string? installLocation = null)
    {
        return new InstalledApp
        {
            DisplayName = displayName,
            InstallLocation = installLocation,
            RegistryKeyName = displayName.Replace(" ", ""),
            NameTokens = RegistryReader.TokenizeName(displayName)
        };
    }

    private static AppDataFolder MakeFolder(string name, AppDataLocation location = AppDataLocation.LocalAppData)
    {
        return new AppDataFolder
        {
            FullPath = $@"C:\Users\Test\AppData\Local\{name}",
            Name = name,
            Location = location,
            SizeBytes = 1024 * 1024, // 1 MB
            LastWriteTimeUtc = DateTime.UtcNow.AddDays(-30)
        };
    }

    #endregion

    #region Exact Match Tests

    [Fact]
    public void ExactMatch_FolderMatchesInstalledApp_NotOrphaned()
    {
        var apps = new[] { MakeApp("Spotify") };
        var folders = new[] { MakeFolder("Spotify") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    [Fact]
    public void ExactMatch_CaseInsensitive_NotOrphaned()
    {
        var apps = new[] { MakeApp("Spotify") };
        var folders = new[] { MakeFolder("spotify") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    #endregion

    #region Prefix Match Tests

    [Fact]
    public void PrefixMatch_FolderStartsWithAppName_NotOrphaned()
    {
        var apps = new[] { MakeApp("Spotify") };
        var folders = new[] { MakeFolder("SpotifyAB") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    [Fact]
    public void PrefixMatch_AppNameStartsWithFolderName_NotOrphaned()
    {
        var apps = new[] { MakeApp("Google Chrome Beta") };
        var folders = new[] { MakeFolder("Google Chrome") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    #endregion

    #region Token Match Tests

    [Fact]
    public void TokenMatch_FolderContainsAppToken_NotOrphaned()
    {
        var apps = new[] { MakeApp("Discord") };
        var folders = new[] { MakeFolder("DiscordPTB") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    #endregion

    #region Orphan Detection Tests

    [Fact]
    public void NoMatch_FolderIsOrphaned()
    {
        var apps = new[] { MakeApp("Spotify") };
        var folders = new[] { MakeFolder("OBSStudio") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Single(orphans);
        Assert.Equal("OBSStudio", orphans[0].AppName);
    }

    [Fact]
    public void EmptyInstalledApps_AllFoldersAreOrphaned()
    {
        var apps = Array.Empty<InstalledApp>();
        var folders = new[] { MakeFolder("Spotify"), MakeFolder("Discord") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Equal(2, orphans.Count);
    }

    [Fact]
    public void EmptyFolders_NoOrphans()
    {
        var apps = new[] { MakeApp("Spotify") };
        var folders = Array.Empty<AppDataFolder>();

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    [Fact]
    public void MultipleFoldersSameApp_GroupedIntoSingleOrphan()
    {
        var apps = new[] { MakeApp("Chrome") };
        var folders = new[]
        {
            MakeFolder("Spotify", AppDataLocation.LocalAppData),
            MakeFolder("Spotify", AppDataLocation.RoamingAppData)
        };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Single(orphans);
        Assert.Equal("Spotify", orphans[0].AppName);
        Assert.Equal(2, orphans[0].Folders.Count);
    }

    [Fact]
    public void Orphans_SortedByTotalSizeDescending()
    {
        var apps = Array.Empty<InstalledApp>();
        var smallFolder = MakeFolder("SmallApp");
        smallFolder.SizeBytes = 100;
        var bigFolder = MakeFolder("BigApp");
        bigFolder.SizeBytes = 999_999_999;

        var orphans = _matcher.FindOrphans(apps, new[] { smallFolder, bigFolder });

        Assert.Equal(2, orphans.Count);
        Assert.Equal("BigApp", orphans[0].AppName);
        Assert.Equal("SmallApp", orphans[1].AppName);
    }

    #endregion

    #region Exclusion List Tests

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("Windows")]
    [InlineData("Temp")]
    [InlineData("Package Cache")]
    [InlineData("VaporVault")]
    [InlineData("NuGet")]
    [InlineData("pip")]
    public void ExcludedFolders_NeverFlaggedAsOrphaned(string folderName)
    {
        var apps = Array.Empty<InstalledApp>();
        var folders = new[] { MakeFolder(folderName) };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    [Fact]
    public void HiddenFolders_StartingWithDot_NeverFlaggedAsOrphaned()
    {
        var apps = Array.Empty<InstalledApp>();
        var folders = new[] { MakeFolder(".config") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    #endregion

    #region IsExcluded Tests

    [Fact]
    public void IsExcluded_CaseInsensitive()
    {
        Assert.True(OrphanMatcher.IsExcluded("microsoft"));
        Assert.True(OrphanMatcher.IsExcluded("MICROSOFT"));
        Assert.True(OrphanMatcher.IsExcluded("Microsoft"));
    }

    [Fact]
    public void IsExcluded_UnknownFolder_NotExcluded()
    {
        Assert.False(OrphanMatcher.IsExcluded("Spotify"));
        Assert.False(OrphanMatcher.IsExcluded("CustomApp123"));
    }

    #endregion

    #region IsMatchedByInstalledApp Tests

    [Fact]
    public void IsMatched_InstallLocationContainsFolderName_Matched()
    {
        var apps = new[] { MakeApp("My Custom App", @"C:\Program Files\MyCustomApp") };
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "My Custom App" };
        var appTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "custom", "app" };

        var result = OrphanMatcher.IsMatchedByInstalledApp("MyCustomApp", apps, appNameSet, appTokens);

        Assert.True(result);
    }

    #endregion

    #region Alias Matching Tests

    [Fact]
    public void AliasMatch_NpmCacheFolder_WithNodeJsInstalled_NotOrphaned()
    {
        // npm-cache should be recognized as belonging to Node.js via alias table
        var apps = new[] { MakeApp("Node.js") };
        var folders = new[] { MakeFolder("npm-cache") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    [Fact]
    public void AliasMatch_NpmCacheFolder_WithNodeJsAbsent_IsOrphaned()
    {
        // Without Node.js installed, npm-cache IS orphaned...
        // BUT npm-cache is excluded by the dev-tool prefix exclusion (Part 2), so
        // it won't appear in results either way. Test the alias matching directly.
        var apps = new[] { MakeApp("Spotify") };
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Spotify" };
        var appTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "spotify" };

        var result = OrphanMatcher.IsMatchedByInstalledApp("npm-cache", apps, appNameSet, appTokens);

        Assert.False(result);
    }

    [Fact]
    public void AliasMatch_NpmFolder_WithNodeJsInstalled_MatchedByAlias()
    {
        var apps = new[] { MakeApp("Node.js") };
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Node.js" };
        var appTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node" };

        var result = OrphanMatcher.IsMatchedByAlias("npm", appNameSet, apps);

        Assert.True(result);
    }

    [Fact]
    public void AliasMatch_PipFolder_WithPythonInstalled_MatchedByAlias()
    {
        var apps = new[] { MakeApp("Python 3.12.0") };
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Python 3.12.0" };

        // Alias for "pip" includes "python" — prefix match against "Python 3.12.0"
        var result = OrphanMatcher.IsMatchedByAlias("pip", appNameSet, apps);

        Assert.True(result);
    }

    [Fact]
    public void AliasMatch_NpmCacheSuffix_PrefixMatchHitsAlias()
    {
        // "npm-cache-2024" should prefix-match to "npm-cache" alias
        var apps = new[] { MakeApp("Node.js") };
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Node.js" };

        var result = OrphanMatcher.IsMatchedByAlias("npm-cache-2024", appNameSet, apps);

        Assert.True(result);
    }

    [Fact]
    public void AliasMatch_FolderNotInAliasTable_Unaffected()
    {
        // A folder not in the alias table should return false from alias matching
        var apps = new[] { MakeApp("Spotify") };
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Spotify" };

        var result = OrphanMatcher.IsMatchedByAlias("OBSStudio", appNameSet, apps);

        Assert.False(result);
    }

    [Fact]
    public void AliasMatch_NuGetFolder_WithVisualStudioInstalled_MatchedByAlias()
    {
        var apps = new[] { MakeApp("Visual Studio Professional 2022") };
        var appNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Visual Studio Professional 2022" };

        var result = OrphanMatcher.IsMatchedByAlias("nuget", appNameSet, apps);

        Assert.True(result);
    }

    #endregion

    #region Dev-Tool Prefix Exclusion Tests (Part 2)

    [Theory]
    [InlineData("npm-cache")]
    [InlineData("npm-cache-folder")]
    [InlineData("npm_global")]
    [InlineData("pip-cache")]
    [InlineData("yarn-cache")]
    [InlineData("nuget-packages")]
    [InlineData("dotnet-tools")]
    public void IsExcluded_DevToolPrefixVariants_AreExcluded(string folderName)
    {
        Assert.True(OrphanMatcher.IsExcluded(folderName));
    }

    [Theory]
    [InlineData("npmapp")]        // No separator after prefix — not a dev-tool variant
    [InlineData("pipelines")]     // "pip" is prefix but "pipelines" has no separator
    [InlineData("yarnball")]      // No separator
    public void IsExcluded_NonDevToolPrefixMatches_NotExcluded(string folderName)
    {
        Assert.False(OrphanMatcher.IsExcluded(folderName));
    }

    [Fact]
    public void FindOrphans_NpmCacheFolder_ExcludedByPrefix_NeverOrphaned()
    {
        // Even with no installed apps, npm-cache should be excluded by the prefix rule
        var apps = Array.Empty<InstalledApp>();
        var folders = new[] { MakeFolder("npm-cache") };

        var orphans = _matcher.FindOrphans(apps, folders);

        Assert.Empty(orphans);
    }

    #endregion

    #region ResolveAliases Tests

    [Fact]
    public void ResolveAliases_DirectMatch_ReturnsAliases()
    {
        var result = OrphanMatcher.ResolveAliases("npm");
        Assert.NotNull(result);
        Assert.Contains("node.js", result);
    }

    [Fact]
    public void ResolveAliases_PrefixMatch_ReturnsAliases()
    {
        var result = OrphanMatcher.ResolveAliases("npm-cache-2024");
        Assert.NotNull(result);
        Assert.Contains("node.js", result);
    }

    [Fact]
    public void ResolveAliases_NoMatch_ReturnsNull()
    {
        var result = OrphanMatcher.ResolveAliases("SomeRandomFolder");
        Assert.Null(result);
    }

    #endregion
}
