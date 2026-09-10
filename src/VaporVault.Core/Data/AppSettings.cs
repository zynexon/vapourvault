using System.Text.Json;

namespace VaporVault.Core.Data;

/// <summary>
/// Persisted application settings for VaporVault.
/// Stored as JSON at C:\ProgramData\VaporVault\settings.json.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VaporVault", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Whether live uninstall interception is enabled.
    /// When true, the UninstallWatcher monitors registry changes in the background.
    /// </summary>
    public bool LiveInterceptionEnabled { get; set; } = true;

    /// <summary>
    /// Whether VaporVault starts automatically with Windows.
    /// Implemented via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    /// </summary>
    public bool RunAtStartup { get; set; }

    /// <summary>
    /// Whether closing the main window minimizes to the system tray
    /// instead of fully exiting. Only effective when LiveInterceptionEnabled is true.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Loads settings from disk. Returns defaults if the file doesn't exist or is corrupt.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings.Load failed: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>
    /// Saves current settings to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (dir != null)
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettings.Save failed: {ex.Message}");
        }
    }
}
