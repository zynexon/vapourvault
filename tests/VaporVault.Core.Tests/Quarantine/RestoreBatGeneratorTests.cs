using VaporVault.Core.Models;
using VaporVault.Core.Quarantine;

namespace VaporVault.Core.Tests.Quarantine;

/// <summary>
/// Tests for RestoreBatGenerator — verifies the generated batch file content.
/// </summary>
public class RestoreBatGeneratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RestoreBatGenerator _generator;

    public RestoreBatGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VaporVault_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _generator = new RestoreBatGenerator();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GenerateRestoreBat_CreatesFile()
    {
        var manifest = CreateTestManifest();

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var batPath = Path.Combine(_tempDir, "restore.bat");
        Assert.True(File.Exists(batPath));
    }

    [Fact]
    public void GenerateRestoreBat_ContainsRobocopyCommands()
    {
        var manifest = CreateTestManifest();

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var content = File.ReadAllText(Path.Combine(_tempDir, "restore.bat"));
        Assert.Contains("robocopy", content);
        Assert.Contains("/MOVE", content);
        Assert.Contains("/E", content);
    }

    [Fact]
    public void GenerateRestoreBat_ContainsOriginalPaths()
    {
        var manifest = CreateTestManifest();

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var content = File.ReadAllText(Path.Combine(_tempDir, "restore.bat"));
        Assert.Contains(@"C:\Users\Test\AppData\Local\Spotify", content);
        Assert.Contains(@"C:\Users\Test\AppData\Roaming\Spotify", content);
    }

    [Fact]
    public void GenerateRestoreBat_ContainsAppName()
    {
        var manifest = CreateTestManifest();

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var content = File.ReadAllText(Path.Combine(_tempDir, "restore.bat"));
        Assert.Contains("Spotify", content);
    }

    [Fact]
    public void GenerateRestoreBat_ContainsEchoOff()
    {
        var manifest = CreateTestManifest();

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var content = File.ReadAllText(Path.Combine(_tempDir, "restore.bat"));
        Assert.StartsWith("@echo off", content);
    }

    [Fact]
    public void GenerateRestoreBat_ContainsRegistryNote_WhenExportExists()
    {
        var manifest = CreateTestManifest();
        manifest.RegistryExport = new RegistryExportInfo
        {
            KeyPath = @"HKCU\Software\Spotify",
            FileName = "registry_backup.reg",
            ExportedAt = DateTime.UtcNow
        };

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var content = File.ReadAllText(Path.Combine(_tempDir, "restore.bat"));
        Assert.Contains("registry_backup.reg", content);
        Assert.Contains("does NOT automatically import", content);
    }

    [Fact]
    public void GenerateRestoreBat_HasErrorHandling()
    {
        var manifest = CreateTestManifest();

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var content = File.ReadAllText(Path.Combine(_tempDir, "restore.bat"));
        Assert.Contains("ERRORLEVEL", content);
        Assert.Contains("ERRORS", content);
    }

    [Fact]
    public void GenerateRestoreBat_OneRobocopyPerSource()
    {
        var manifest = CreateTestManifest();

        _generator.GenerateRestoreBat(_tempDir, manifest);

        var content = File.ReadAllText(Path.Combine(_tempDir, "restore.bat"));
        // Count actual robocopy command lines (start with "robocopy"), not mentions in comments
        var robocopyCommandCount = content
            .Split('\n')
            .Count(line => line.TrimStart().StartsWith("robocopy"));
        Assert.Equal(manifest.Sources.Count, robocopyCommandCount);
    }

    private static QuarantineManifest CreateTestManifest()
    {
        return new QuarantineManifest
        {
            AppName = "Spotify",
            QuarantineId = "Spotify_test1234",
            QuarantinedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            TotalSizeBytes = 4_000_000_000,
            Sources =
            [
                new QuarantineSource
                {
                    OriginalPath = @"C:\Users\Test\AppData\Local\Spotify",
                    Type = "LocalAppData",
                    FileCount = 342,
                    SizeBytes = 3_500_000_000,
                    MovedAt = DateTime.UtcNow,
                    QuarantineSubDir = "LocalAppData"
                },
                new QuarantineSource
                {
                    OriginalPath = @"C:\Users\Test\AppData\Roaming\Spotify",
                    Type = "RoamingAppData",
                    FileCount = 18,
                    SizeBytes = 500_000_000,
                    MovedAt = DateTime.UtcNow,
                    QuarantineSubDir = "RoamingAppData"
                }
            ]
        };
    }
}
