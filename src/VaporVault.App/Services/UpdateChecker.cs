using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VaporVault_App.Services;

/// <summary>
/// Checks for newer versions of VaporVault on app startup.
///
/// v4 scope: detection + link-out only, no in-app silent update installation.
///
/// Strategy:
/// - On app startup, once per day (not every launch), fetch a version manifest
///   from the configured update URL.
/// - Compare against the running app's package version.
/// - If newer, raise <see cref="UpdateAvailable"/> so the UI can show a
///   non-blocking banner with a link to the release page.
/// - Fail silently (log only) on network errors — a broken network shouldn't
///   produce an alarming message in a disk-cleanup tool.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>
    /// The URL to fetch the latest version manifest from.
    /// Expected format: a JSON file with "version" and "releaseUrl" fields.
    /// 
    /// For GitHub Releases, host a <c>latest.json</c> alongside your release assets, e.g.:
    /// <c>https://github.com/yourorg/vaporvault/releases/latest/download/latest.json</c>
    /// 
    /// Or use the GitHub Releases API:
    /// <c>https://api.github.com/repos/yourorg/vaporvault/releases/latest</c>
    /// (in which case, override the parsing in <see cref="FetchLatestVersionAsync"/>).
    /// </summary>
    private const string UpdateManifestUrl =
        "https://raw.githubusercontent.com/yourorg/vaporvault/main/latest.json";

    /// <summary>
    /// Settings key for persisting the last check timestamp.
    /// Stored in the app's local data so we only check once per day.
    /// </summary>
    private static readonly string LastCheckFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VaporVault", "last_update_check.txt");

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// Raised when a newer version is detected.
    /// Contains the new version string and a URL to download it.
    /// </summary>
    public event Action<UpdateInfo>? UpdateAvailable;

    /// <summary>
    /// Checks for updates if at least 24 hours have passed since the last check.
    /// Call this once from App.OnLaunched. Runs entirely in the background;
    /// never throws to the caller.
    /// </summary>
    public async Task CheckOncePerDayAsync()
    {
        try
        {
            if (!ShouldCheckToday())
            {
                App.Log("UpdateChecker: Skipping — already checked within the last 24 hours.");
                return;
            }

            App.Log("UpdateChecker: Checking for updates...");

            var latest = await FetchLatestVersionAsync();
            if (latest is null)
            {
                App.Log("UpdateChecker: Could not fetch latest version info.");
                return;
            }

            var current = GetCurrentVersion();
            App.Log($"UpdateChecker: Current = {current}, Latest = {latest.Version}");

            if (latest.Version > current)
            {
                App.Log($"UpdateChecker: Newer version available: {latest.Version}");
                UpdateAvailable?.Invoke(new UpdateInfo
                {
                    VersionString = FormatVersion(latest.Version),
                    ReleaseUrl = latest.ReleaseUrl ?? "https://github.com/yourorg/vaporvault/releases/latest"
                });
            }
            else
            {
                App.Log("UpdateChecker: App is up to date.");
            }

            RecordCheckTimestamp();
        }
        catch (Exception ex)
        {
            // Fail silently — log only, no user-facing error
            App.Log($"UpdateChecker: Check failed (silent): {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the running app's version from the MSIX package identity.
    /// Falls back to the assembly version if running unpackaged.
    /// </summary>
    private static Version GetCurrentVersion()
    {
        try
        {
            var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
            return new Version(
                packageVersion.Major,
                packageVersion.Minor,
                packageVersion.Build,
                packageVersion.Revision);
        }
        catch
        {
            // Unpackaged fallback (development)
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return asm ?? new Version(1, 0, 0, 0);
        }
    }

    /// <summary>
    /// Fetches the latest version manifest from the update URL.
    /// </summary>
    private static async Task<VersionManifest?> FetchLatestVersionAsync()
    {
        try
        {
            var manifest = await Http.GetFromJsonAsync<VersionManifestJson>(UpdateManifestUrl);
            if (manifest?.Version is null) return null;

            if (!Version.TryParse(manifest.Version, out var version))
                return null;

            return new VersionManifest
            {
                Version = version,
                ReleaseUrl = manifest.ReleaseUrl
            };
        }
        catch (Exception ex)
        {
            App.Log($"UpdateChecker.FetchLatestVersionAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns true if 24+ hours have passed since the last check.
    /// </summary>
    private static bool ShouldCheckToday()
    {
        try
        {
            if (!File.Exists(LastCheckFile)) return true;

            var text = File.ReadAllText(LastCheckFile).Trim();
            if (DateTimeOffset.TryParse(text, out var lastCheck))
            {
                return DateTimeOffset.UtcNow - lastCheck > TimeSpan.FromHours(24);
            }

            return true; // Corrupt file — check anyway
        }
        catch
        {
            return true; // Can't read file — check anyway
        }
    }

    /// <summary>
    /// Records the current UTC timestamp as the last check time.
    /// </summary>
    private static void RecordCheckTimestamp()
    {
        try
        {
            var dir = Path.GetDirectoryName(LastCheckFile);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(LastCheckFile, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            App.Log($"UpdateChecker: Failed to record check timestamp: {ex.Message}");
        }
    }

    private static string FormatVersion(Version v) =>
        v.Revision > 0 ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}" : $"{v.Major}.{v.Minor}.{v.Build}";

    /// <summary>
    /// JSON shape of the <c>latest.json</c> manifest file.
    /// </summary>
    private sealed class VersionManifestJson
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("releaseUrl")]
        public string? ReleaseUrl { get; set; }
    }

    private sealed class VersionManifest
    {
        public required Version Version { get; init; }
        public string? ReleaseUrl { get; init; }
    }
}

/// <summary>
/// Information about an available update, passed to the <see cref="UpdateChecker.UpdateAvailable"/> event.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>Formatted version string, e.g. "4.1.0".</summary>
    public required string VersionString { get; init; }

    /// <summary>URL to the release page — opened in the default browser when the user clicks the banner.</summary>
    public required string ReleaseUrl { get; init; }
}
