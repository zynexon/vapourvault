using VaporVault.Core.Models;
using VaporVault.Core.Quarantine;

namespace VaporVault.Core.Tests.Quarantine;

/// <summary>
/// Tests for ManifestWriter — verifies manifest.json serialization and deserialization.
/// </summary>
public class ManifestWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ManifestWriter _writer;

    public ManifestWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VaporVault_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _writer = new ManifestWriter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void WriteManifest_CreatesValidJsonFile()
    {
        var manifest = CreateTestManifest();

        _writer.WriteManifest(_tempDir, manifest);

        var manifestPath = Path.Combine(_tempDir, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        var json = File.ReadAllText(manifestPath);
        Assert.Contains("Spotify", json);
        Assert.Contains("schemaVersion", json);
    }

    [Fact]
    public void ReadManifest_ReturnsDeserializedManifest()
    {
        var original = CreateTestManifest();
        _writer.WriteManifest(_tempDir, original);

        var loaded = _writer.ReadManifest(_tempDir);

        Assert.NotNull(loaded);
        Assert.Equal(original.AppName, loaded.AppName);
        Assert.Equal(original.QuarantineId, loaded.QuarantineId);
        Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(original.Sources.Count, loaded.Sources.Count);
        Assert.Equal(original.Sources[0].OriginalPath, loaded.Sources[0].OriginalPath);
    }

    [Fact]
    public void ReadManifest_MissingFile_ReturnsNull()
    {
        var result = _writer.ReadManifest(Path.Combine(_tempDir, "nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void UpdateManifest_ModifiesExistingManifest()
    {
        var manifest = CreateTestManifest();
        _writer.WriteManifest(_tempDir, manifest);

        var updated = _writer.UpdateManifest(_tempDir, m =>
        {
            m.Compressed = true;
            m.CompressedSizeBytes = 500_000;
        });

        Assert.True(updated);

        var loaded = _writer.ReadManifest(_tempDir);
        Assert.NotNull(loaded);
        Assert.True(loaded.Compressed);
        Assert.Equal(500_000, loaded.CompressedSizeBytes);
    }

    [Fact]
    public void WriteManifest_PendingRebootFiles_Serialized()
    {
        var manifest = CreateTestManifest();
        manifest.PendingRebootFiles.Add(new PendingRebootFile
        {
            OriginalPath = @"C:\Users\Test\AppData\Local\Spotify\locked.dll",
            Status = "pending_reboot"
        });

        _writer.WriteManifest(_tempDir, manifest);
        var loaded = _writer.ReadManifest(_tempDir);

        Assert.NotNull(loaded);
        Assert.Single(loaded.PendingRebootFiles);
        Assert.Equal("pending_reboot", loaded.PendingRebootFiles[0].Status);
    }

    [Fact]
    public void WriteManifest_RegistryExport_Serialized()
    {
        var manifest = CreateTestManifest();
        manifest.RegistryExport = new RegistryExportInfo
        {
            KeyPath = @"HKCU\Software\Spotify",
            FileName = "registry_backup.reg",
            ExportedAt = DateTime.UtcNow
        };

        _writer.WriteManifest(_tempDir, manifest);
        var loaded = _writer.ReadManifest(_tempDir);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.RegistryExport);
        Assert.Equal(@"HKCU\Software\Spotify", loaded.RegistryExport.KeyPath);
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
