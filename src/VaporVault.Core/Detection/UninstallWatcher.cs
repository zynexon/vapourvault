using System.Collections.Concurrent;
using VaporVault.Core.Interop;
using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Watches the three Windows Uninstall registry roots for subkey additions/removals
/// using the native RegNotifyChangeKeyValue API (event-driven, near-zero CPU while idle).
///
/// On each change notification:
/// 1. Re-reads the current app list for the affected root.
/// 2. Diffs against a cached snapshot.
/// 3. Any entry present in the old snapshot but missing from the new one is a just-uninstalled app.
/// 4. Updates the snapshot and re-arms the one-shot notification.
///
/// Debounce: if multiple entries disappear within a short window (default 3 seconds),
/// they are batched into a single event rather than firing per-app.
///
/// This class is entirely standalone — it does not know about scanning, toasts, or quarantine.
/// Consumers subscribe to the AppsUninstalled event.
/// </summary>
public sealed class UninstallWatcher : IDisposable
{
    // ── Registry roots to watch ──

    private static readonly (IntPtr RootKey, string SubKeyPath, string Label)[] WatchedRoots =
    [
        (RegistryNotificationInterop.HKEY_LOCAL_MACHINE,
         @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
         "HKLM_Uninstall"),

        (RegistryNotificationInterop.HKEY_LOCAL_MACHINE,
         @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
         "HKLM_WOW64_Uninstall"),

        (RegistryNotificationInterop.HKEY_CURRENT_USER,
         @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
         "HKCU_Uninstall")
    ];

    // ── Debounce configuration ──

    private readonly TimeSpan _debounceWindow;

    // ── State ──

    private readonly IRegistryReader _registryReader;
    private readonly object _snapshotLock = new();

    /// <summary>
    /// Snapshot: maps RegistryKeyName → DisplayName for quick diff.
    /// Keyed by registry key name (GUID or product code), which is unique per entry.
    /// </summary>
    private Dictionary<string, string> _snapshot = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<Thread> _watcherThreads = [];
    private readonly IntPtr[] _keyHandles;
    private volatile bool _stopping;
    private bool _disposed;

    // ── Debounce state ──

    private readonly ConcurrentBag<string> _pendingRemovals = [];
    private Timer? _debounceTimer;
    private readonly object _debounceLock = new();

    // ── Events ──

    /// <summary>
    /// Fired when one or more apps have been detected as uninstalled.
    /// The list contains the DisplayName of each removed app, batched by the debounce window.
    /// This event fires on a thread pool thread — callers must marshal to the UI thread if needed.
    /// </summary>
    public event Action<IReadOnlyList<string>>? AppsUninstalled;

    /// <summary>
    /// Creates a new UninstallWatcher with the default 3-second debounce window.
    /// </summary>
    public UninstallWatcher()
        : this(new RegistryReader(), TimeSpan.FromSeconds(3))
    {
    }

    /// <summary>
    /// Creates a new UninstallWatcher with injectable dependencies (for testing).
    /// </summary>
    /// <param name="registryReader">The registry reader to use for snapshots.</param>
    /// <param name="debounceWindow">How long to wait for additional removals before firing the event.</param>
    public UninstallWatcher(IRegistryReader registryReader, TimeSpan debounceWindow)
    {
        _registryReader = registryReader;
        _debounceWindow = debounceWindow;
        _keyHandles = new IntPtr[WatchedRoots.Length];
    }

    /// <summary>
    /// Takes the initial snapshot of installed apps and starts background watcher threads.
    /// Call once on app startup. Safe to call from any thread.
    /// </summary>
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UninstallWatcher));

        _stopping = false;

        // Take the initial snapshot
        RefreshSnapshot();

        // Open handles and start watcher threads
        for (int i = 0; i < WatchedRoots.Length; i++)
        {
            var (rootKey, subKeyPath, label) = WatchedRoots[i];

            IntPtr hKey = RegistryNotificationInterop.OpenForNotification(rootKey, subKeyPath);
            _keyHandles[i] = hKey;

            if (hKey == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"UninstallWatcher: Could not open {label} for notification (access denied or key missing). Skipping.");
                continue;
            }

            int index = i; // capture for closure
            var thread = new Thread(() => WatcherLoop(index, label))
            {
                IsBackground = true,
                Name = $"UninstallWatcher_{label}",
                Priority = ThreadPriority.BelowNormal
            };
            _watcherThreads.Add(thread);
            thread.Start();
        }

        System.Diagnostics.Debug.WriteLine(
            $"UninstallWatcher: Started with {_watcherThreads.Count} watcher thread(s), " +
            $"{_snapshot.Count} apps in initial snapshot.");
    }

    /// <summary>
    /// Signals all watcher threads to stop and waits briefly for them to exit.
    /// The threads will unblock when their registry handles are closed.
    /// </summary>
    public void Stop()
    {
        _stopping = true;

        // Close all handles — this will cause the blocking RegNotifyChangeKeyValue calls to fail/return,
        // unblocking the watcher threads.
        for (int i = 0; i < _keyHandles.Length; i++)
        {
            if (_keyHandles[i] != IntPtr.Zero)
            {
                RegistryNotificationInterop.CloseHandle(_keyHandles[i]);
                _keyHandles[i] = IntPtr.Zero;
            }
        }

        // Wait briefly for threads to exit
        foreach (var thread in _watcherThreads)
        {
            thread.Join(timeout: TimeSpan.FromSeconds(2));
        }
        _watcherThreads.Clear();

        // Dispose debounce timer
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        System.Diagnostics.Debug.WriteLine("UninstallWatcher: Stopped.");
    }

    // ── Private: watcher loop per root ──

    private void WatcherLoop(int handleIndex, string label)
    {
        System.Diagnostics.Debug.WriteLine($"UninstallWatcher: {label} watcher thread started.");

        while (!_stopping)
        {
            IntPtr hKey = _keyHandles[handleIndex];
            if (hKey == IntPtr.Zero) break;

            // Block until a subkey is added or removed (or handle is closed)
            bool changed = RegistryNotificationInterop.WaitForChange(hKey);

            if (_stopping) break;
            if (!changed)
            {
                // Handle was likely closed (during Stop()) — exit the loop
                System.Diagnostics.Debug.WriteLine($"UninstallWatcher: {label} WaitForChange returned false.");
                break;
            }

            System.Diagnostics.Debug.WriteLine($"UninstallWatcher: {label} change detected.");

            // A change happened — diff the snapshot
            try
            {
                ProcessChange();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"UninstallWatcher: Error processing change on {label}: {ex.Message}");
            }

            // Re-arm: we need a fresh handle for the next notification.
            // RegNotifyChangeKeyValue is one-shot per registration, but
            // we can re-call it on the same open handle.
            // Actually — re-calling WaitForChange on the same handle works fine
            // because the API re-registers internally. The loop will block again
            // at the top on the next iteration.
        }

        System.Diagnostics.Debug.WriteLine($"UninstallWatcher: {label} watcher thread exiting.");
    }

    // ── Internal: diff logic (exposed for testing) ──

    /// <summary>
    /// Pure diff: returns DisplayNames that are in oldSnapshot but missing from newSnapshot.
    /// No side effects — safe to call from tests without threads or registry handles.
    /// </summary>
    internal static List<string> DiffSnapshots(
        Dictionary<string, string> oldSnapshot,
        Dictionary<string, string> newSnapshot)
    {
        return oldSnapshot
            .Where(kvp => !newSnapshot.ContainsKey(kvp.Key))
            .Select(kvp => kvp.Value)
            .ToList();
    }

    /// <summary>
    /// Re-reads the registry via IRegistryReader, diffs against the cached snapshot,
    /// and feeds any removals into the debounce pipeline.
    /// Made internal (was private) so tests can trigger diff+debounce without real watcher threads.
    /// </summary>
    internal void ProcessChange()
    {
        // Build new snapshot from current registry state
        var currentApps = _registryReader.GetInstalledApps();
        var newSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in currentApps)
        {
            // Use RegistryKeyName as the key — it's the unique subkey name
            newSnapshot.TryAdd(app.RegistryKeyName, app.DisplayName);
        }

        // Diff: find entries that were in old snapshot but missing from new
        List<string> removedNames;
        lock (_snapshotLock)
        {
            removedNames = DiffSnapshots(_snapshot, newSnapshot);

            // Update the snapshot
            _snapshot = newSnapshot;
        }

        if (removedNames.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("UninstallWatcher: Change detected but no removals found (likely an install).");
            return;
        }

        System.Diagnostics.Debug.WriteLine(
            $"UninstallWatcher: Detected {removedNames.Count} removal(s): {string.Join(", ", removedNames)}");

        // Add to pending batch and reset debounce timer
        foreach (var name in removedNames)
        {
            _pendingRemovals.Add(name);
        }

        ResetDebounceTimer();
    }

    // ── Private: debounce ──

    private void ResetDebounceTimer()
    {
        lock (_debounceLock)
        {
            // Dispose existing timer and create a fresh one.
            // This effectively resets the countdown each time a new removal arrives.
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(OnDebounceElapsed, null, _debounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_stopping) return;

        // Drain all pending removals
        var batch = new List<string>();
        while (_pendingRemovals.TryTake(out var name))
        {
            batch.Add(name);
        }

        if (batch.Count == 0) return;

        // Deduplicate (same app could be removed from multiple roots)
        var distinct = batch.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        System.Diagnostics.Debug.WriteLine(
            $"UninstallWatcher: Debounce fired — raising AppsUninstalled for {distinct.Count} app(s): " +
            string.Join(", ", distinct));

        try
        {
            AppsUninstalled?.Invoke(distinct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"UninstallWatcher: Error in AppsUninstalled handler: {ex.Message}");
        }
    }

    // ── Snapshot management ──

    private void RefreshSnapshot()
    {
        var apps = _registryReader.GetInstalledApps();
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            snapshot.TryAdd(app.RegistryKeyName, app.DisplayName);
        }

        lock (_snapshotLock)
        {
            _snapshot = snapshot;
        }
    }

    /// <summary>
    /// Sets the internal snapshot directly — for testing only.
    /// Allows tests to establish baseline state without calling Start() (which opens native handles).
    /// </summary>
    internal void SetSnapshotForTesting(Dictionary<string, string> snapshot)
    {
        lock (_snapshotLock)
        {
            _snapshot = new Dictionary<string, string>(snapshot, StringComparer.OrdinalIgnoreCase);
        }
    }

    // ── IDisposable ──

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
    }
}
