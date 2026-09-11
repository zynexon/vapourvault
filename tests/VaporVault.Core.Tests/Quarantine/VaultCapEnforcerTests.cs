using VaporVault.Core.Data;
using VaporVault.Core.Models;
using VaporVault.Core.Quarantine;

namespace VaporVault.Core.Tests.Quarantine;

/// <summary>
/// Tests for VaultCapEnforcer — the vault storage cap logic.
/// Uses a temporary QuarantineIndex backed by a temp file to avoid
/// interfering with the real quarantine index.
/// </summary>
public class VaultCapEnforcerTests : IDisposable
{
    private readonly string _tempIndexPath;
    private readonly QuarantineIndex _index;

    public VaultCapEnforcerTests()
    {
        _tempIndexPath = Path.GetTempFileName();
        _index = new QuarantineIndex(_tempIndexPath);
    }

    public void Dispose()
    {
        try { File.Delete(_tempIndexPath); } catch { }
    }

    #region Helpers

    private void AddEntry(string name, long sizeBytes, int daysAgo = 0)
    {
        var now = DateTime.UtcNow.AddDays(-daysAgo);
        _index.Add(new QuarantineEntry
        {
            QuarantineId = $"{name}_{Guid.NewGuid():N}",
            AppName = name,
            QuarantinedAt = now,
            ExpiresAt = now.AddDays(30),
            TotalSizeBytes = sizeBytes,
            QuarantineFolderPath = $@"C:\Test\{name}",
            Status = QuarantineStatus.Active
        });
    }

    #endregion

    [Fact]
    public void CheckCap_UnderCap_NoIssue()
    {
        // 5 GB in vault, 15 GB cap, adding 1 GB
        AddEntry("App1", 5L * 1024 * 1024 * 1024);

        var enforcer = new VaultCapEnforcer(_index);
        var result = enforcer.CheckCap(1L * 1024 * 1024 * 1024);

        Assert.False(result.WouldExceedCap);
        Assert.Null(result.OldestEntry);
        Assert.Equal(0, result.SpaceNeededBytes);
    }

    [Fact]
    public void CheckCap_WouldExceedCap_ReturnsOldestEntry()
    {
        // 14 GB already in vault, adding 2 GB would exceed 15 GB cap
        AddEntry("OldApp", 6L * 1024 * 1024 * 1024, daysAgo: 20);
        AddEntry("NewApp", 8L * 1024 * 1024 * 1024, daysAgo: 5);

        var enforcer = new VaultCapEnforcer(_index);
        var result = enforcer.CheckCap(2L * 1024 * 1024 * 1024);

        Assert.True(result.WouldExceedCap);
        Assert.NotNull(result.OldestEntry);
        Assert.Equal("OldApp", result.OldestEntry!.AppName);
        Assert.True(result.SpaceNeededBytes > 0);
    }

    [Fact]
    public void CheckCap_EmptyVault_NoIssue()
    {
        var enforcer = new VaultCapEnforcer(_index);
        var result = enforcer.CheckCap(1L * 1024 * 1024 * 1024);

        Assert.False(result.WouldExceedCap);
        Assert.Null(result.OldestEntry);
    }

    [Fact]
    public void CheckCap_ExactlyAtCap_NoIssue()
    {
        // 14 GB in vault, adding 1 GB would be exactly 15 GB (at cap, not over)
        AddEntry("App1", 14L * 1024 * 1024 * 1024);

        var enforcer = new VaultCapEnforcer(_index);
        var result = enforcer.CheckCap(1L * 1024 * 1024 * 1024);

        Assert.False(result.WouldExceedCap);
    }

    [Fact]
    public void CheckCap_MultipleEntries_IdentifiesCorrectOldest()
    {
        AddEntry("First", 3L * 1024 * 1024 * 1024, daysAgo: 25);
        AddEntry("Second", 3L * 1024 * 1024 * 1024, daysAgo: 15);
        AddEntry("Third", 3L * 1024 * 1024 * 1024, daysAgo: 5);

        // 9 GB in vault, adding 7 GB exceeds 15 GB
        var enforcer = new VaultCapEnforcer(_index);
        var result = enforcer.CheckCap(7L * 1024 * 1024 * 1024);

        Assert.True(result.WouldExceedCap);
        Assert.NotNull(result.OldestEntry);
        Assert.Equal("First", result.OldestEntry!.AppName);
    }

    [Fact]
    public void CheckCap_ReportsCorrectUsageAndCap()
    {
        var size = 5L * 1024 * 1024 * 1024;
        AddEntry("App1", size);

        var enforcer = new VaultCapEnforcer(_index);
        var incomingSize = 2L * 1024 * 1024 * 1024;
        var result = enforcer.CheckCap(incomingSize);

        Assert.Equal(size, result.CurrentUsageBytes);
        Assert.Equal(15L * 1024 * 1024 * 1024, result.CapBytes);
        Assert.Equal(incomingSize, result.IncomingSizeBytes);
    }

    [Fact]
    public void CheckCap_OnlyCountsActiveEntries()
    {
        // Add a deleted entry — should not count toward usage
        var now = DateTime.UtcNow;
        _index.Add(new QuarantineEntry
        {
            QuarantineId = "deleted_1",
            AppName = "DeletedApp",
            QuarantinedAt = now.AddDays(-20),
            ExpiresAt = now.AddDays(10),
            TotalSizeBytes = 10L * 1024 * 1024 * 1024,
            QuarantineFolderPath = @"C:\Test\Deleted",
            Status = QuarantineStatus.Deleted
        });

        // Add an active entry
        AddEntry("ActiveApp", 5L * 1024 * 1024 * 1024);

        var enforcer = new VaultCapEnforcer(_index);
        var result = enforcer.CheckCap(1L * 1024 * 1024 * 1024);

        // Only 5 GB active, adding 1 GB = 6 GB — well under 15 GB cap
        Assert.False(result.WouldExceedCap);
        Assert.Equal(5L * 1024 * 1024 * 1024, result.CurrentUsageBytes);
    }
}
