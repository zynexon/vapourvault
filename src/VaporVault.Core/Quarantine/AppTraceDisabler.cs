using System.ServiceProcess;
using Microsoft.Win32.TaskScheduler;
using VaporVault.Core.Models;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Handles the side-effecting disable/re-enable operations for detected app traces.
///
/// Disable: called at quarantine time to stop scheduled tasks and services.
/// Re-enable: called at revive time to restore them.
///
/// All operations are best-effort — failures are recorded per-item, never thrown.
/// Disabling means setting Enabled = false (tasks) or stopping + disabling (services).
/// This is reversible on Revive.
/// </summary>
public class AppTraceDisabler
{
    /// <summary>
    /// Attempts to disable all detected scheduled tasks and services.
    /// Updates each item's StillActive and DisableFailed flags in place.
    /// </summary>
    /// <param name="traces">The trace result to process. Modified in place.</param>
    public void DisableTraces(AppTraceResult traces)
    {
        if (traces == null) return;

        foreach (var task in traces.ScheduledTasks)
        {
            if (!task.StillActive) continue; // Already disabled
            DisableScheduledTask(task);
        }

        foreach (var service in traces.Services)
        {
            if (!service.StillActive) continue; // Already stopped
            StopAndDisableService(service);
        }
    }

    /// <summary>
    /// Attempts to re-enable all detected scheduled tasks and services that
    /// were successfully disabled during quarantine.
    /// Only re-enables items where StillActive == false and DisableFailed == false.
    /// </summary>
    /// <param name="traces">The trace result to process.</param>
    /// <returns>Number of items successfully re-enabled.</returns>
    public int ReEnableTraces(AppTraceResult traces)
    {
        if (traces == null) return 0;

        int reEnabled = 0;

        foreach (var task in traces.ScheduledTasks)
        {
            // Only re-enable if we successfully disabled it
            if (task.StillActive || task.DisableFailed) continue;

            if (EnableScheduledTask(task))
                reEnabled++;
        }

        foreach (var service in traces.Services)
        {
            // Only re-enable if we successfully disabled it
            if (service.StillActive || service.DisableFailed) continue;

            if (EnableService(service))
                reEnabled++;
        }

        return reEnabled;
    }

    // ── Scheduled Tasks ──

    private static void DisableScheduledTask(DetectedTask task)
    {
        try
        {
            using var ts = new TaskService();
            var scheduledTask = ts.GetTask(task.Path);

            if (scheduledTask == null)
            {
                // Task no longer exists — consider it already handled
                task.StillActive = false;
                return;
            }

            scheduledTask.Enabled = false;
            task.StillActive = false;

            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Disabled scheduled task '{task.Name}'");
        }
        catch (UnauthorizedAccessException)
        {
            task.DisableFailed = true;
            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Access denied disabling task '{task.Name}'");
        }
        catch (Exception ex)
        {
            task.DisableFailed = true;
            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Failed to disable task '{task.Name}': {ex.Message}");
        }
    }

    private static bool EnableScheduledTask(DetectedTask task)
    {
        try
        {
            using var ts = new TaskService();
            var scheduledTask = ts.GetTask(task.Path);

            if (scheduledTask == null)
                return false;

            scheduledTask.Enabled = true;
            task.StillActive = true;

            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Re-enabled scheduled task '{task.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Failed to re-enable task '{task.Name}': {ex.Message}");
            return false;
        }
    }

    // ── Windows Services ──

    private static void StopAndDisableService(DetectedService service)
    {
        try
        {
            using var sc = new ServiceController(service.ServiceName);

            // Stop the service if it's running
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }

            // Disable by setting StartType to Disabled via registry
            // (ServiceController doesn't expose StartType setting directly)
            SetServiceStartType(service.ServiceName, 4); // 4 = Disabled

            service.StillActive = false;

            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Stopped and disabled service '{service.DisplayName}'");
        }
        catch (UnauthorizedAccessException)
        {
            service.DisableFailed = true;
            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Access denied disabling service '{service.DisplayName}'");
        }
        catch (InvalidOperationException)
        {
            service.DisableFailed = true;
            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Cannot stop service '{service.DisplayName}' (may be a system service)");
        }
        catch (Exception ex)
        {
            service.DisableFailed = true;
            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Failed to disable service '{service.DisplayName}': {ex.Message}");
        }
    }

    private static bool EnableService(DetectedService service)
    {
        try
        {
            // Re-enable by setting StartType to Automatic via registry
            SetServiceStartType(service.ServiceName, 2); // 2 = Automatic

            using var sc = new ServiceController(service.ServiceName);
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));

            service.StillActive = true;

            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Re-enabled service '{service.DisplayName}'");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"AppTraceDisabler: Failed to re-enable service '{service.DisplayName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sets a service's start type via the registry.
    /// Values: 2 = Automatic, 3 = Manual, 4 = Disabled.
    /// </summary>
    private static void SetServiceStartType(string serviceName, int startType)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
        key?.SetValue("Start", startType, Microsoft.Win32.RegistryValueKind.DWord);
    }
}
