using System.Diagnostics;

namespace VaporVault.Core.Interop;

/// <summary>
/// Utility for finding and gracefully closing orphaned processes by name pattern.
/// Used before quarantine to close any still-running processes from an uninstalled app.
/// </summary>
public class ProcessHelper
{
    /// <summary>
    /// Finds all running processes whose names match the given app name pattern.
    /// Matches process names that start with or contain the app name (case-insensitive).
    /// </summary>
    /// <param name="appName">The application name to search for (e.g., "Spotify").</param>
    /// <returns>List of matching processes.</returns>
    public static IReadOnlyList<Process> FindProcessesByAppName(string appName)
    {
        var result = new List<Process>();
        var appLower = appName.ToLowerInvariant();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var processNameLower = process.ProcessName.ToLowerInvariant();
                if (processNameLower.Contains(appLower) || appLower.Contains(processNameLower))
                {
                    result.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                // Process may have exited between enumeration and name access
                process.Dispose();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied — skip
                process.Dispose();
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to gracefully close a process by sending WM_CLOSE to its main window.
    /// Falls back to Kill() if the process doesn't close within the timeout.
    /// </summary>
    /// <param name="process">The process to close.</param>
    /// <param name="gracefulTimeoutMs">Time to wait for graceful close before killing.</param>
    /// <returns>True if the process was successfully stopped.</returns>
    public static bool CloseProcess(Process process, int gracefulTimeoutMs = 5000)
    {
        try
        {
            if (process.HasExited)
                return true;

            // Try graceful close first (sends WM_CLOSE to main window)
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                process.CloseMainWindow();
                if (process.WaitForExit(gracefulTimeoutMs))
                    return true;
            }

            // Graceful close failed or no main window — force kill
            process.Kill();
            return process.WaitForExit(3000);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied (e.g., elevated process)
            return false;
        }
    }

    /// <summary>
    /// Attempts to close all processes matching an app name.
    /// </summary>
    /// <param name="appName">The application name.</param>
    /// <param name="gracefulTimeoutMs">Time to wait per process for graceful close.</param>
    /// <returns>Number of processes that were successfully closed.</returns>
    public static int CloseAllProcessesForApp(string appName, int gracefulTimeoutMs = 5000)
    {
        var processes = FindProcessesByAppName(appName);
        int closed = 0;

        foreach (var process in processes)
        {
            using (process)
            {
                if (CloseProcess(process, gracefulTimeoutMs))
                    closed++;
            }
        }

        return closed;
    }
}
