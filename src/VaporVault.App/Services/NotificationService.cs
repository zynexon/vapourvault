using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using VaporVault.Core.Data;
using VaporVault.Core.Lifecycle;
using VaporVault.Core.Models;

namespace VaporVault_App.Services;

/// <summary>
/// Handles toast notifications and background expiry processing.
/// 
/// Uses the raw Windows.UI.Notifications WinRT API for toast delivery.
/// This works in unpackaged WinUI 3 apps without any COM registration or
/// additional NuGet packages. The app identity is provided by the
/// Windows App SDK's winapp tooling.
/// 
/// - Fires Day-25 "ready to delete?" toast notifications
/// - Fires uninstall-detected toast notifications (v2)
/// - Auto-deletes expired entries at Day 30
/// - Runs on app startup + periodic timer
/// </summary>
public class NotificationService
{
    /// <summary>
    /// Raised when the user clicks a native toast notification.
    /// </summary>
    public static event Action? ToastClicked;

    private readonly ExpiryChecker _expiryChecker;
    private readonly HistoryLogger _historyLogger;
    private readonly QuarantineIndex _quarantineIndex;
    private System.Timers.Timer? _expiryTimer;

    // App User Model ID — required for toast notifications in unpackaged apps.
    // This must match the value registered by the Windows App SDK build tooling.
    private const string AppId = "VaporVault";

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
        try
        {
            // Register AUMID for unpackaged toast notifications
            var registryKey = $@"Software\Classes\AppUserModelId\{AppId}";
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryKey);
            key.SetValue("DisplayName", "VaporVault");
            
            var iconPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            if (System.IO.File.Exists(iconPath))
            {
                key.SetValue("IconUri", iconPath);
            }
            
            App.Log("AUMID registered successfully for Toast Notifications.");
        }
        catch (Exception ex)
        {
            App.Log($"Failed to register AUMID: {ex.Message}");
        }

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
            ShowToast(
                $"'{entry.AppName}' quarantine expires soon",
                $"{entry.DaysRemaining} day(s) remaining before permanent deletion.\nOpen VaporVault to revive it, or let it auto-delete.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toast notification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a toast notification when an app uninstall is detected and leftovers are found.
    /// </summary>
    public static void SendUninstallDetectedToast(string appName, long leftoverSizeBytes, int folderCount)
    {
        try
        {
            var sizeStr = FormatSize(leftoverSizeBytes);
            var folderText = folderCount == 1 ? "1 folder" : $"{folderCount} folders";

            App.Log($"SendUninstallDetectedToast: Sending toast for '{appName}' ({sizeStr}, {folderText})...");

            ShowToast(
                $"{appName} uninstalled — {sizeStr} of leftovers found",
                $"{folderText} left behind. Open VaporVault to quarantine and reclaim space.");

            App.Log($"SendUninstallDetectedToast: Toast shown for '{appName}'.");
        }
        catch (Exception ex)
        {
            App.Log($"SendUninstallDetectedToast FAILED for '{appName}': {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Shows a Windows toast notification using the raw WinRT API.
    /// Works in unpackaged apps without COM registration.
    /// </summary>
    private static void ShowToast(string title, string body)
    {
        // Build the toast XML manually using the standard toast template
        var toastXml = new XmlDocument();
        toastXml.LoadXml($"""
            <toast>
                <visual>
                    <binding template="ToastGeneric">
                        <text>{EscapeXml(title)}</text>
                        <text>{EscapeXml(body)}</text>
                    </binding>
                </visual>
            </toast>
            """);

        var toast = new ToastNotification(toastXml);
        
        toast.Activated += (sender, args) => ToastClicked?.Invoke();

        // Use the explicit App User Model ID that we registered in Initialize()
        try
        {
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
        }
        catch (Exception ex)
        {
            App.Log($"ShowToast failed: {ex.Message}");
        }
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };

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
