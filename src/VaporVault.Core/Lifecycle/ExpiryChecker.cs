using VaporVault.Core.Data;
using VaporVault.Core.Models;

namespace VaporVault.Core.Lifecycle;

/// <summary>
/// Checks quarantine entries for expiry and handles:
/// - Day 25: Returns entries that should trigger a toast notification
/// - Day 30: Returns entries that should be auto-deleted
/// Also handles the actual permanent deletion of expired entries.
/// </summary>
public class ExpiryChecker
{
    private readonly QuarantineIndex _quarantineIndex;

    /// <summary>
    /// Days before expiry to send the notification (25 days into the 30-day period = 5 days remaining).
    /// </summary>
    public const int NotificationDaysRemaining = 5;

    public ExpiryChecker() : this(new QuarantineIndex()) { }

    public ExpiryChecker(QuarantineIndex quarantineIndex)
    {
        _quarantineIndex = quarantineIndex;
    }

    /// <summary>
    /// Gets entries that are due for a 25-day "ready to delete?" notification.
    /// Returns entries with exactly 5 or fewer days remaining that haven't been notified yet.
    /// </summary>
    public IReadOnlyList<QuarantineEntry> GetEntriesForNotification()
    {
        return _quarantineIndex.GetActive()
            .Where(e => e.DaysRemaining <= NotificationDaysRemaining && e.DaysRemaining > 0)
            .ToList();
    }

    /// <summary>
    /// Gets entries that have expired (day 30+) and should be permanently deleted.
    /// </summary>
    public IReadOnlyList<QuarantineEntry> GetExpiredEntries()
    {
        return _quarantineIndex.GetActive()
            .Where(e => e.DaysRemaining <= 0)
            .ToList();
    }

    /// <summary>
    /// Permanently deletes an expired quarantine entry.
    /// Removes the quarantine folder from disk and logs the action.
    /// </summary>
    /// <param name="quarantineId">ID of the entry to delete.</param>
    /// <param name="historyLogger">Logger to record the permanent deletion.</param>
    /// <returns>True if the deletion was successful.</returns>
    public bool PermanentlyDelete(string quarantineId, HistoryLogger? historyLogger = null)
    {
        var entry = _quarantineIndex.GetById(quarantineId);
        if (entry == null) return false;

        // Delete the quarantine folder from disk
        try
        {
            if (Directory.Exists(entry.QuarantineFolderPath))
                Directory.Delete(entry.QuarantineFolderPath, recursive: true);
        }
        catch (Exception ex)
        {
            // Log but don't fail — the folder might already be gone
            System.Diagnostics.Debug.WriteLine($"Failed to delete quarantine folder: {ex.Message}");
        }

        // Update status in index
        _quarantineIndex.Update(quarantineId, e => e.Status = QuarantineStatus.Deleted);

        // Log to history
        historyLogger?.LogDeletion(entry);

        return true;
    }

    /// <summary>
    /// Processes all expired entries — permanently deletes them and logs.
    /// Call this on app startup and periodically.
    /// </summary>
    /// <param name="historyLogger">Logger for recording deletions.</param>
    /// <returns>Number of entries that were permanently deleted.</returns>
    public int ProcessExpiredEntries(HistoryLogger? historyLogger = null)
    {
        var expired = GetExpiredEntries();
        int deleted = 0;

        foreach (var entry in expired)
        {
            if (PermanentlyDelete(entry.QuarantineId, historyLogger))
                deleted++;
        }

        return deleted;
    }
}
