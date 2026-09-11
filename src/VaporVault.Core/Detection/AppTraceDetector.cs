using System.ServiceProcess;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Detects additional traces (scheduled tasks, services, file associations,
/// dead uninstall entries) associated with a given app name.
///
/// This is a read-only detection module — no side effects, no modifications.
/// Each detection category is wrapped in try-catch so one failing category
/// doesn't block the others.
///
/// Follows the same pattern as OrphanMatcher: standalone, testable, injectable.
/// </summary>
public class AppTraceDetector : IAppTraceDetector
{
    /// <summary>
    /// Minimum token length for fuzzy matching to avoid false positives
    /// on short common words.
    /// </summary>
    private const int MinTokenLength = 3;

    /// <inheritdoc />
    public AppTraceResult DetectTraces(string appName, string? installLocation = null)
    {
        var result = new AppTraceResult();

        if (string.IsNullOrWhiteSpace(appName))
            return result;

        var appNameLower = appName.ToLowerInvariant();
        var tokens = TokenizeAppName(appName);

        // 1. Scheduled tasks
        try
        {
            result.ScheduledTasks = DetectScheduledTasks(appNameLower, tokens, installLocation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppTraceDetector: Task detection failed: {ex.Message}");
        }

        // 2. Windows services
        try
        {
            result.Services = DetectServices(appNameLower, tokens, installLocation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppTraceDetector: Service detection failed: {ex.Message}");
        }

        // 3. File associations
        try
        {
            result.FileAssociations = DetectFileAssociations(appNameLower, tokens, installLocation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppTraceDetector: Association detection failed: {ex.Message}");
        }

        // 4. Dead uninstall entries
        try
        {
            result.DeadUninstallEntries = DetectDeadUninstallEntries(appNameLower, tokens);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppTraceDetector: Dead uninstall detection failed: {ex.Message}");
        }

        // Build plain-English summary
        result.Summary = BuildSummary(result);

        return result;
    }

    // ── Scheduled Tasks ──

    private static List<DetectedTask> DetectScheduledTasks(
        string appNameLower, IReadOnlyList<string> tokens, string? installLocation)
    {
        var detected = new List<DetectedTask>();

        using var ts = new TaskService();
        EnumerateTaskFolder(ts.RootFolder, appNameLower, tokens, installLocation, detected);

        return detected;
    }

    private static void EnumerateTaskFolder(
        TaskFolder folder,
        string appNameLower,
        IReadOnlyList<string> tokens,
        string? installLocation,
        List<DetectedTask> results)
    {
        try
        {
            foreach (var task in folder.Tasks)
            {
                try
                {
                    if (TaskMatchesApp(task, appNameLower, tokens, installLocation))
                    {
                        results.Add(new DetectedTask
                        {
                            Path = task.Path,
                            Name = task.Name,
                            Author = task.Definition?.RegistrationInfo?.Author,
                            StillActive = task.Enabled
                        });
                    }
                }
                catch (UnauthorizedAccessException) { /* Skip inaccessible tasks */ }
                catch (System.Runtime.InteropServices.COMException) { /* Skip COM errors */ }
            }

            foreach (var subFolder in folder.SubFolders)
            {
                try
                {
                    EnumerateTaskFolder(subFolder, appNameLower, tokens, installLocation, results);
                }
                catch (UnauthorizedAccessException) { /* Skip inaccessible folders */ }
                catch (System.Runtime.InteropServices.COMException) { /* Skip COM errors */ }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>
    /// Checks if a scheduled task is associated with the given app.
    /// Matches by task name, path, author, or action executable path.
    /// </summary>
    internal static bool TaskMatchesApp(
        Microsoft.Win32.TaskScheduler.Task task,
        string appNameLower,
        IReadOnlyList<string> tokens,
        string? installLocation)
    {
        // Match by name
        var taskNameLower = task.Name.ToLowerInvariant();
        if (ContainsAppReference(taskNameLower, appNameLower, tokens))
            return true;

        // Match by path
        var taskPathLower = task.Path.ToLowerInvariant();
        if (ContainsAppReference(taskPathLower, appNameLower, tokens))
            return true;

        // Match by author
        var author = task.Definition?.RegistrationInfo?.Author;
        if (!string.IsNullOrEmpty(author) &&
            ContainsAppReference(author.ToLowerInvariant(), appNameLower, tokens))
            return true;

        // Match by action executable path
        if (task.Definition?.Actions != null)
        {
            foreach (var action in task.Definition.Actions)
            {
                if (action is ExecAction execAction && !string.IsNullOrEmpty(execAction.Path))
                {
                    var execPathLower = execAction.Path.ToLowerInvariant();

                    if (ContainsAppReference(execPathLower, appNameLower, tokens))
                        return true;

                    // Check against install location
                    if (!string.IsNullOrEmpty(installLocation) &&
                        execPathLower.Contains(installLocation.ToLowerInvariant()))
                        return true;
                }
            }
        }

        return false;
    }

    // ── Windows Services ──

    private static List<DetectedService> DetectServices(
        string appNameLower, IReadOnlyList<string> tokens, string? installLocation)
    {
        var detected = new List<DetectedService>();
        var services = ServiceController.GetServices();

        foreach (var svc in services)
        {
            try
            {
                var displayNameLower = svc.DisplayName.ToLowerInvariant();
                var serviceNameLower = svc.ServiceName.ToLowerInvariant();

                bool matched = ContainsAppReference(displayNameLower, appNameLower, tokens) ||
                               ContainsAppReference(serviceNameLower, appNameLower, tokens);

                // Check binary path via registry if display name doesn't match
                string? binaryPath = null;
                if (!matched)
                {
                    binaryPath = GetServiceBinaryPath(svc.ServiceName);
                    if (!string.IsNullOrEmpty(binaryPath))
                    {
                        var binaryPathLower = binaryPath.ToLowerInvariant();
                        if (ContainsAppReference(binaryPathLower, appNameLower, tokens))
                            matched = true;

                        if (!matched && !string.IsNullOrEmpty(installLocation) &&
                            binaryPathLower.Contains(installLocation.ToLowerInvariant()))
                            matched = true;
                    }
                }

                if (matched)
                {
                    binaryPath ??= GetServiceBinaryPath(svc.ServiceName);
                    detected.Add(new DetectedService
                    {
                        ServiceName = svc.ServiceName,
                        DisplayName = svc.DisplayName,
                        BinaryPath = binaryPath,
                        StillActive = svc.Status == ServiceControllerStatus.Running
                    });
                }
            }
            catch (Exception) { /* Skip inaccessible services */ }
            finally
            {
                svc.Dispose();
            }
        }

        return detected;
    }

    /// <summary>
    /// Reads the service binary path from the registry.
    /// ServiceController doesn't expose this directly.
    /// </summary>
    private static string? GetServiceBinaryPath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("ImagePathName") as string;
        }
        catch
        {
            return null;
        }
    }

    // ── File Associations ──

    private static List<DetectedAssociation> DetectFileAssociations(
        string appNameLower, IReadOnlyList<string> tokens, string? installLocation)
    {
        var detected = new List<DetectedAssociation>();

        // Check HKCU\Software\Classes for per-user file type associations
        try
        {
            using var classesKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
            if (classesKey != null)
            {
                foreach (var subKeyName in classesKey.GetSubKeyNames())
                {
                    try
                    {
                        // Look at extensions (start with .) and protocols
                        if (!subKeyName.StartsWith('.') && !subKeyName.Contains(':'))
                            continue;

                        using var extKey = classesKey.OpenSubKey(subKeyName);
                        var progId = extKey?.GetValue(null) as string;
                        if (string.IsNullOrEmpty(progId)) continue;

                        // Follow progId to find the command
                        var commandPath = GetShellCommandPath(classesKey, progId);
                        if (string.IsNullOrEmpty(commandPath)) continue;

                        var commandLower = commandPath.ToLowerInvariant();
                        if (ContainsAppReference(commandLower, appNameLower, tokens) ||
                            (!string.IsNullOrEmpty(installLocation) &&
                             commandLower.Contains(installLocation.ToLowerInvariant())))
                        {
                            detected.Add(new DetectedAssociation
                            {
                                Extension = subKeyName,
                                CommandPath = commandPath,
                                HandlerType = subKeyName.StartsWith('.') ? "FileType" : "Protocol"
                            });
                        }
                    }
                    catch { /* Skip inaccessible keys */ }
                }
            }
        }
        catch { /* Skip if HKCU\Software\Classes isn't accessible */ }

        return detected;
    }

    /// <summary>
    /// Follows a ProgID to find the shell\open\command path.
    /// </summary>
    private static string? GetShellCommandPath(RegistryKey classesKey, string progId)
    {
        try
        {
            using var progIdKey = classesKey.OpenSubKey($@"{progId}\shell\open\command");
            return progIdKey?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }

    // ── Dead Uninstall Entries ──

    private static List<DetectedDeadUninstallEntry> DetectDeadUninstallEntries(
        string appNameLower, IReadOnlyList<string> tokens)
    {
        var detected = new List<DetectedDeadUninstallEntry>();

        var roots = new (RegistryKey root, string path, string label)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM_WOW64"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU")
        };

        foreach (var (root, path, label) in roots)
        {
            try
            {
                using var uninstallKey = root.OpenSubKey(path);
                if (uninstallKey == null) continue;

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = uninstallKey.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var displayName = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // Check if this entry matches the app name
                        var displayNameLower = displayName.ToLowerInvariant();
                        if (!ContainsAppReference(displayNameLower, appNameLower, tokens) &&
                            !ContainsAppReference(subKeyName.ToLowerInvariant(), appNameLower, tokens))
                            continue;

                        // Check if the UninstallString points to a nonexistent file
                        var uninstallString = subKey.GetValue("UninstallString") as string;
                        if (IsDeadUninstallEntry(uninstallString))
                        {
                            detected.Add(new DetectedDeadUninstallEntry
                            {
                                RegistryKeyName = subKeyName,
                                DisplayName = displayName,
                                RegistryRoot = label
                            });
                        }
                    }
                    catch { /* Skip inaccessible entries */ }
                }
            }
            catch { /* Skip inaccessible registry roots */ }
        }

        return detected;
    }

    /// <summary>
    /// Checks if an uninstall entry is "dead" — either has no UninstallString,
    /// or the string points to a file/path that no longer exists.
    /// </summary>
    internal static bool IsDeadUninstallEntry(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
            return true;

        // Try to extract the executable path from the uninstall string
        var path = uninstallString.Trim();

        // Handle quoted paths: "C:\path\uninstall.exe" /S
        if (path.StartsWith('"'))
        {
            var endQuote = path.IndexOf('"', 1);
            if (endQuote > 1)
                path = path[1..endQuote];
        }
        else
        {
            // Unquoted: take everything up to the first space (rough heuristic)
            var spaceIndex = path.IndexOf(' ');
            if (spaceIndex > 0)
                path = path[..spaceIndex];
        }

        // MsiExec entries are system-managed, not dead
        if (path.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check if the file exists
        try
        {
            return !File.Exists(path);
        }
        catch
        {
            return true; // If we can't check, assume dead
        }
    }

    // ── Matching helpers ──

    /// <summary>
    /// Checks if a target string contains a reference to the app by name or tokens.
    /// </summary>
    internal static bool ContainsAppReference(
        string target, string appNameLower, IReadOnlyList<string> tokens)
    {
        // Direct substring match
        if (target.Contains(appNameLower))
            return true;

        // Token match — any significant token from the app name appears in the target
        foreach (var token in tokens)
        {
            if (token.Length >= MinTokenLength && target.Contains(token))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Splits an app name into lowercase tokens for fuzzy matching.
    /// </summary>
    internal static IReadOnlyList<string> TokenizeAppName(string appName)
    {
        return appName.ToLowerInvariant()
            .Split([' ', '-', '_', '.', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= MinTokenLength)
            .Distinct()
            .ToList();
    }

    // ── Summary builder ──

    private static string? BuildSummary(AppTraceResult result)
    {
        if (!result.HasAnyTraces)
            return null;

        var parts = new List<string>();

        if (result.ScheduledTasks.Count > 0)
        {
            var names = string.Join(", ", result.ScheduledTasks.Select(t => t.Name));
            parts.Add($"{result.ScheduledTasks.Count} scheduled task(s) ({names})");
        }

        if (result.Services.Count > 0)
        {
            var names = string.Join(", ", result.Services.Select(s => s.DisplayName));
            parts.Add($"{result.Services.Count} background service(s) ({names})");
        }

        if (result.FileAssociations.Count > 0)
        {
            parts.Add($"{result.FileAssociations.Count} file association(s)");
        }

        if (result.DeadUninstallEntries.Count > 0)
        {
            parts.Add($"{result.DeadUninstallEntries.Count} orphaned registry entry/entries");
        }

        return $"Also found: {string.Join(", ", parts)}.";
    }
}
