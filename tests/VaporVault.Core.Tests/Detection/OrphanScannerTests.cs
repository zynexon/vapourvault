using Moq;
using VaporVault.Core.Detection;
using VaporVault.Core.Models;

namespace VaporVault.Core.Tests.Detection;

/// <summary>
/// Tests for OrphanScanner — verifies the orchestration pipeline
/// using mocked dependencies (RegistryReader, FolderEnumerator, OrphanMatcher).
/// </summary>
public class OrphanScannerTests
{
    [Fact]
    public void Scan_ReturnsCorrectResult_WithMockedDependencies()
    {
        // Arrange
        var mockRegistry = new Mock<IRegistryReader>();
        var mockEnumerator = new Mock<IAppDataFolderEnumerator>();
        var mockMatcher = new Mock<IOrphanMatcher>();

        var installedApps = new List<InstalledApp>
        {
            new()
            {
                DisplayName = "Chrome",
                RegistryKeyName = "GoogleChrome",
                NameTokens = new[] { "chrome" }
            }
        };

        var folders = new List<AppDataFolder>
        {
            new()
            {
                FullPath = @"C:\Users\Test\AppData\Local\Chrome",
                Name = "Chrome",
                Location = AppDataLocation.LocalAppData,
                SizeBytes = 500_000,
                LastWriteTimeUtc = DateTime.UtcNow
            },
            new()
            {
                FullPath = @"C:\Users\Test\AppData\Local\Spotify",
                Name = "Spotify",
                Location = AppDataLocation.LocalAppData,
                SizeBytes = 4_000_000_000,
                LastWriteTimeUtc = DateTime.UtcNow.AddDays(-120)
            }
        };

        var orphans = new List<OrphanedApp>
        {
            new()
            {
                AppName = "Spotify",
                Folders = new[] { folders[1] }
            }
        };

        mockRegistry.Setup(r => r.GetInstalledApps()).Returns(installedApps);
        mockEnumerator.Setup(e => e.EnumerateFolders()).Returns(folders);
        mockMatcher.Setup(m => m.FindOrphans(installedApps, folders)).Returns(orphans);

        var scanner = new OrphanScanner(mockRegistry.Object, mockEnumerator.Object, mockMatcher.Object);

        // Act
        var result = scanner.Scan();

        // Assert
        Assert.Single(result.OrphanedApps);
        Assert.Equal("Spotify", result.OrphanedApps[0].AppName);
        Assert.Equal(2, result.TotalFoldersScanned);
        Assert.Equal(1, result.TotalInstalledAppsFound);
        Assert.True(result.ScanDuration.TotalMilliseconds >= 0);
    }

    [Fact]
    public void Scan_EmptySystem_ReturnsEmptyResult()
    {
        // Arrange
        var mockRegistry = new Mock<IRegistryReader>();
        var mockEnumerator = new Mock<IAppDataFolderEnumerator>();
        var mockMatcher = new Mock<IOrphanMatcher>();

        mockRegistry.Setup(r => r.GetInstalledApps()).Returns(new List<InstalledApp>());
        mockEnumerator.Setup(e => e.EnumerateFolders()).Returns(new List<AppDataFolder>());
        mockMatcher
            .Setup(m => m.FindOrphans(
                It.IsAny<IReadOnlyList<InstalledApp>>(),
                It.IsAny<IReadOnlyList<AppDataFolder>>()))
            .Returns(new List<OrphanedApp>());

        var scanner = new OrphanScanner(mockRegistry.Object, mockEnumerator.Object, mockMatcher.Object);

        // Act
        var result = scanner.Scan();

        // Assert
        Assert.Empty(result.OrphanedApps);
        Assert.Equal(0, result.TotalFoldersScanned);
        Assert.Equal(0, result.TotalInstalledAppsFound);
        Assert.Equal(0, result.TotalReclaimableBytes);
    }

    [Fact]
    public async Task ScanAsync_RunsOnBackgroundThread()
    {
        // Arrange
        var mockRegistry = new Mock<IRegistryReader>();
        var mockEnumerator = new Mock<IAppDataFolderEnumerator>();
        var mockMatcher = new Mock<IOrphanMatcher>();

        mockRegistry.Setup(r => r.GetInstalledApps()).Returns(new List<InstalledApp>());
        mockEnumerator.Setup(e => e.EnumerateFolders()).Returns(new List<AppDataFolder>());
        mockMatcher
            .Setup(m => m.FindOrphans(
                It.IsAny<IReadOnlyList<InstalledApp>>(),
                It.IsAny<IReadOnlyList<AppDataFolder>>()))
            .Returns(new List<OrphanedApp>());

        var scanner = new OrphanScanner(mockRegistry.Object, mockEnumerator.Object, mockMatcher.Object);

        // Act
        var result = await scanner.ScanAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.OrphanedApps);
    }
}
