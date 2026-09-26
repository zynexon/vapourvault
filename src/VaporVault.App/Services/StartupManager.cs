using Windows.ApplicationModel;

namespace VaporVault_App.Services;

/// <summary>
/// Manages the "Run at startup" registration for the packaged (MSIX) app
/// using the Windows.ApplicationModel.StartupTask API.
///
/// Under MSIX packaging, the registry-based HKCU\Run approach does not work —
/// Windows ignores Run-key entries for packaged apps. Instead, the app declares
/// a &lt;uap5:StartupTask&gt; in Package.appxmanifest and manages it through
/// the WinRT StartupTask API.
///
/// The startup task appears in Settings &gt; Apps &gt; Startup and Task Manager's
/// Startup tab, giving users standard Windows controls over it.
/// </summary>
public static class StartupManager
{
    /// <summary>
    /// Must match the TaskId declared in Package.appxmanifest's
    /// &lt;uap5:StartupTask TaskId="..." /&gt; element.
    /// </summary>
    private const string TaskId = "VaporVaultStartup";

    /// <summary>
    /// Gets the current state of the startup task.
    /// Returns <see cref="StartupTaskState.Disabled"/> on any failure.
    /// </summary>
    public static async Task<StartupTaskState> GetStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartupManager.GetStateAsync failed: {ex.Message}");
            return StartupTaskState.Disabled;
        }
    }

    /// <summary>
    /// Requests Windows to enable the startup task.
    /// Returns the resulting state — callers must check this, because the result
    /// may be <see cref="StartupTaskState.DisabledByUser"/> or
    /// <see cref="StartupTaskState.DisabledByPolicy"/> rather than
    /// <see cref="StartupTaskState.Enabled"/>.
    ///
    /// <see cref="StartupTaskState.DisabledByUser"/> means the user disabled it
    /// via Task Manager or Settings &gt; Apps &gt; Startup — the app cannot
    /// re-enable it programmatically.
    /// </summary>
    public static async Task<StartupTaskState> EnableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);

            if (task.State == StartupTaskState.Disabled)
            {
                var result = await task.RequestEnableAsync();
                System.Diagnostics.Debug.WriteLine($"StartupManager: RequestEnableAsync returned {result}");
                return result;
            }

            // Already enabled, or disabled by user/policy (can't change from here)
            System.Diagnostics.Debug.WriteLine($"StartupManager: Current state is {task.State}, skipping RequestEnableAsync");
            return task.State;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartupManager.EnableAsync failed: {ex.Message}");
            return StartupTaskState.Disabled;
        }
    }

    /// <summary>
    /// Disables the startup task. This is always allowed regardless of current state.
    /// </summary>
    public static async Task DisableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            task.Disable();
            System.Diagnostics.Debug.WriteLine("StartupManager: Disabled startup.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartupManager.DisableAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if the given state represents an enabled startup task
    /// (either app-enabled or policy-enabled).
    /// </summary>
    public static bool IsEnabled(StartupTaskState state) =>
        state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    /// <summary>
    /// Returns a user-facing message explaining why the startup task cannot be
    /// enabled, or null if no explanation is needed (i.e. the task is enabled
    /// or in a normal disabled state that the app can change).
    /// </summary>
    public static string? GetDisabledReason(StartupTaskState state) => state switch
    {
        StartupTaskState.DisabledByUser =>
            "Startup has been disabled in Task Manager or Windows Settings. " +
            "Re-enable it from Settings \u2192 Apps \u2192 Startup.",
        StartupTaskState.DisabledByPolicy =>
            "Startup is disabled by your organization\u2019s group policy. " +
            "Contact your system administrator.",
        _ => null
    };
}
