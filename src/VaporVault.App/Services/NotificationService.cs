using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using VaporVault.Core.Data;
using VaporVault.Core.Lifecycle;
using VaporVault.Core.Models;

namespace VaporVault_App.Services;

/// <summary>
/// Handles toast notifications and background expiry processing.
/// - Fires Day-25 "ready to delete?" toast notifications
/// - Auto-deletes expired entries at Day 30
/// - Runs on app startup + periodic timer
/// </summary>
public class NotificationService
{
    private readonly ExpiryChecker _expiryChecker;
    private readonly HistoryLogger _historyLogger;
    private readonly QuarantineIndex _quarantineIndex;
    private System.Timers.Timer? _expiryTimer;

    public NotificationService()
    {
        _quarantineIndex = new QuarantineIndex();
        _expiryChecker = new ExpiryChecker(_quarantineIndex);
        _historyLogger = new HistoryLogger();
    }

    /// <summary>
    /// Initializes the notification service.
    /// Call once on app startup from App.xaml.cs.
    /// </summary>
    public void Initialize()
    {
        // Process expired entries immediately on startup
        ProcessExpiredAndNotify();

        // Set up a 6-hour recurring timer to check again while the app is running
        _expiryTimer = new System.Timers.Timer(TimeSpan.FromHours(6));
        _expiryTimer.Elapsed += (_, _) => ProcessExpiredAndNotify();
        _expiryTimer.AutoReset = true;
        _expiryTimer.Start();
    }

    /// <summary>
    /// Processes expired entries and sends toast notifications for entries nearing expiry.
    /// </summary>
    private void ProcessExpiredAndNotify()
    {
        try
        {
            // Step 1: Auto-delete expired entries (Day 30+)
            int deleted = _expiryChecker.ProcessExpiredEntries(_historyLogger);

            // Step 2: Send Day-25 toast notifications for entries nearing expiry
            var nearingExpiry = _expiryChecker.GetEntriesForNotification();

            foreach (var entry in nearingExpiry)
            {
                SendExpiryToast(entry);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NotificationService error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a Windows toast notification for an entry nearing expiry.
    /// </summary>
    private static void SendExpiryToast(QuarantineEntry entry)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText($"'{entry.AppName}' quarantine expires soon")
                .AddText($"{entry.DaysRemaining} day(s) remaining before permanent deletion.")
                .AddText("Open VaporVault to revive it, or let it auto-delete.");

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            // Toast may fail on systems without notification support — non-fatal
            System.Diagnostics.Debug.WriteLine($"Toast notification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the background timer.
    /// </summary>
    public void Stop()
    {
        _expiryTimer?.Stop();
        _expiryTimer?.Dispose();
        _expiryTimer = null;
    }
}
