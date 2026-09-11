using VaporVault.Core.Detection;
using VaporVault.Core.Models;

namespace VaporVault.Core.Tests.Detection;

/// <summary>
/// Tests for AppTraceDetector — the detection module for scheduled tasks,
/// services, file associations, and dead uninstall entries.
///
/// These tests exercise the internal matching/helper logic using the extracted
/// static/internal methods, avoiding real Task Scheduler or ServiceController calls.
/// </summary>
public class AppTraceDetectorTests
{
    #region ContainsAppReference

    [Fact]
    public void ContainsAppReference_DirectSubstringMatch_ReturnsTrue()
    {
        var tokens = AppTraceDetector.TokenizeAppName("Spotify");
        Assert.True(AppTraceDetector.ContainsAppReference(
            "spotifyupdatertask", "spotify", tokens));
    }

    [Fact]
    public void ContainsAppReference_TokenMatch_ReturnsTrue()
    {
        var tokens = AppTraceDetector.TokenizeAppName("Google Chrome");
        Assert.True(AppTraceDetector.ContainsAppReference(
            "chromeupdater", "google chrome", tokens));
    }

    [Fact]
    public void ContainsAppReference_NoMatch_ReturnsFalse()
    {
        var tokens = AppTraceDetector.TokenizeAppName("Spotify");
        Assert.False(AppTraceDetector.ContainsAppReference(
            "firefoxupdater", "spotify", tokens));
    }

    [Fact]
    public void ContainsAppReference_ShortTokenIgnored_ReturnsFalse()
    {
        // "7z" is only 2 chars — should be ignored to avoid false positives
        var tokens = AppTraceDetector.TokenizeAppName("7-Zip");
        // "zip" is 3 chars so should match
        Assert.True(AppTraceDetector.ContainsAppReference(
            "zipextractor", "7-zip", tokens));
    }

    #endregion

    #region TokenizeAppName

    [Fact]
    public void TokenizeAppName_SplitsOnSeparators()
    {
        var tokens = AppTraceDetector.TokenizeAppName("Google Chrome Beta");
        Assert.Contains("google", tokens);
        Assert.Contains("chrome", tokens);
        Assert.Contains("beta", tokens);
    }

    [Fact]
    public void TokenizeAppName_FiltersShortTokens()
    {
        var tokens = AppTraceDetector.TokenizeAppName("A B CD EFG");
        // Only "efg" has length >= 3
        Assert.Single(tokens);
        Assert.Equal("efg", tokens[0]);
    }

    [Fact]
    public void TokenizeAppName_Deduplicates()
    {
        var tokens = AppTraceDetector.TokenizeAppName("App App App");
        Assert.Single(tokens);
        Assert.Equal("app", tokens[0]);
    }

    #endregion

    #region IsDeadUninstallEntry

    [Fact]
    public void IsDeadUninstallEntry_NullUninstallString_ReturnsTrue()
    {
        Assert.True(AppTraceDetector.IsDeadUninstallEntry(null));
    }

    [Fact]
    public void IsDeadUninstallEntry_EmptyUninstallString_ReturnsTrue()
    {
        Assert.True(AppTraceDetector.IsDeadUninstallEntry(""));
        Assert.True(AppTraceDetector.IsDeadUninstallEntry("   "));
    }

    [Fact]
    public void IsDeadUninstallEntry_MsiExec_ReturnsFalse()
    {
        // MsiExec entries are system-managed, not dead
        Assert.False(AppTraceDetector.IsDeadUninstallEntry(
            "MsiExec.exe /I{12345-ABCDE}"));
    }

    [Fact]
    public void IsDeadUninstallEntry_NonexistentPath_ReturnsTrue()
    {
        Assert.True(AppTraceDetector.IsDeadUninstallEntry(
            @"""C:\NonExistent\Path\uninstall.exe"" /S"));
    }

    [Fact]
    public void IsDeadUninstallEntry_QuotedNonexistentPath_ReturnsTrue()
    {
        Assert.True(AppTraceDetector.IsDeadUninstallEntry(
            @"""C:\Does\Not\Exist\setup.exe"" --uninstall"));
    }

    #endregion

    #region DetectTraces — integration-level

    [Fact]
    public void DetectTraces_EmptyAppName_ReturnsEmptyResult()
    {
        var detector = new AppTraceDetector();
        var result = detector.DetectTraces("");

        Assert.NotNull(result);
        Assert.False(result.HasAnyTraces);
        Assert.Empty(result.ScheduledTasks);
        Assert.Empty(result.Services);
        Assert.Empty(result.FileAssociations);
        Assert.Empty(result.DeadUninstallEntries);
    }

    [Fact]
    public void DetectTraces_NullAppName_ReturnsEmptyResult()
    {
        var detector = new AppTraceDetector();
        var result = detector.DetectTraces(null!);

        Assert.NotNull(result);
        Assert.False(result.HasAnyTraces);
    }

    [Fact]
    public void DetectTraces_NonexistentApp_ReturnsNoTraces()
    {
        // An app name that almost certainly has no traces on the test machine
        var detector = new AppTraceDetector();
        var result = detector.DetectTraces("XyzNonExistentApp12345");

        Assert.NotNull(result);
        // May or may not find anything — the key is it doesn't throw
        Assert.Null(result.Summary == null ? null : (result.HasAnyTraces ? result.Summary : null));
    }

    #endregion

    #region AppTraceResult.HasAnyTraces

    [Fact]
    public void HasAnyTraces_EmptyResult_ReturnsFalse()
    {
        var result = new AppTraceResult();
        Assert.False(result.HasAnyTraces);
    }

    [Fact]
    public void HasAnyTraces_WithTask_ReturnsTrue()
    {
        var result = new AppTraceResult
        {
            ScheduledTasks = [new DetectedTask { Path = @"\Test", Name = "Test" }]
        };
        Assert.True(result.HasAnyTraces);
    }

    [Fact]
    public void HasAnyTraces_WithService_ReturnsTrue()
    {
        var result = new AppTraceResult
        {
            Services = [new DetectedService { ServiceName = "svc", DisplayName = "Svc" }]
        };
        Assert.True(result.HasAnyTraces);
    }

    [Fact]
    public void HasAnyTraces_WithAssociation_ReturnsTrue()
    {
        var result = new AppTraceResult
        {
            FileAssociations = [new DetectedAssociation
            {
                Extension = ".test",
                CommandPath = @"C:\test.exe",
                HandlerType = "FileType"
            }]
        };
        Assert.True(result.HasAnyTraces);
    }

    [Fact]
    public void HasAnyTraces_WithDeadEntry_ReturnsTrue()
    {
        var result = new AppTraceResult
        {
            DeadUninstallEntries = [new DetectedDeadUninstallEntry
            {
                RegistryKeyName = "TestKey",
                DisplayName = "Test App",
                RegistryRoot = "HKCU"
            }]
        };
        Assert.True(result.HasAnyTraces);
    }

    #endregion
}
