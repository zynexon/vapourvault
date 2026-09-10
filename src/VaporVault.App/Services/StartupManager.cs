using Microsoft.Win32;

namespace VaporVault_App.Services;

/// <summary>
/// Manages the "Run at startup" toggle by reading/writing the standard
/// per-user HKCU\Software\Microsoft\Windows\CurrentVersion\Run registry value.
///
/// No elevation required — this is a per-user setting.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VaporVault";

    /// <summary>
    /// Checks whether VaporVault is currently registered to run at startup.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Registers VaporVault to run at startup by adding a registry value
    /// pointing to the current executable path.
    /// </summary>
    public static void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            // Pass --background flag so the app starts minimized to tray
            key?.SetValue(ValueName, $"\"{exePath}\" --background");

            System.Diagnostics.Debug.WriteLine($"StartupManager: Enabled startup with path: {exePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartupManager.Enable failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes VaporVault from the startup registry.
    /// </summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);

            System.Diagnostics.Debug.WriteLine("StartupManager: Disabled startup.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartupManager.Disable failed: {ex.Message}");
        }
    }
}
