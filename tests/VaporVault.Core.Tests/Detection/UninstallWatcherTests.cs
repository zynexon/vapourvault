using Moq;
using VaporVault.Core.Detection;
using VaporVault.Core.Models;

namespace VaporVault.Core.Tests.Detection;

/// <summary>
/// Tests for UninstallWatcher — the diff, debounce, deduplication, and dispose logic.
///
/// These tests exercise the extracted internal APIs without spinning up real
/// watcher threads or opening native registry handles:
///   - DiffSnapshots (pure static function)
///   - ProcessChange (triggers diff + debounce pipeline via mocked IRegistryReader)
///   - SetSnapshotForTesting (sets baseline state without Start())
/// </summary>
public class UninstallWatcherTests
{
    #region Helpers

    /// <summary>
    /// Creates a mock IRegistryReader that returns the given list of (RegistryKeyName, DisplayName) pairs.
    /// </summary>
    private static Mock<IRegistryReader> MockRegistry(params (string keyName, string displayName)[] apps)
    {
        var mock = new Mock<IRegistryReader>();
        var list = apps.Select(a => new InstalledApp
        {
            RegistryKeyName = a.keyName,
            DisplayName = a.displayName,
            NameTokens = Array.Empty<string>()
        }).ToList();
        mock.Setup(r => r.GetInstalledApps()).Returns(list);
        return mock;
    }

    /// <summary>
    /// Subscribes to AppsUninstalled and captures all fired events into a list.
    /// Returns a TaskCompletionSource that completes when the first event fires.
    /// </summary>
    private static (List<IReadOnlyList<string>> events, TaskCompletionSource<IReadOnlyList<string>> firstEvent)
        CaptureEvents(UninstallWatcher watcher)
    {
        var events = new List<IReadOnlyList<string>>();
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        watcher.AppsUninstalled += batch =>
        {
            events.Add(batch);
            tcs.TrySetResult(batch);
        };

        return (events, tcs);
    }

    #endregion

    #region 1. Snapshot diff correctness

    [Fact]
    public void DiffSnapshots_IdentifiesRemovedApp()
    {
        // Arrange: old snapshot has A and B, new snapshot has only A
        var oldSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KeyA"] = "App Alpha",
            ["KeyB"] = "App Beta"
        };
        var newSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KeyA"] = "App Alpha"
        };

        // Act
        var removed = UninstallWatcher.DiffSnapshots(oldSnapshot, newSnapshot);

        // Assert
        Assert.Single(removed);
        Assert.Equal("App Beta", removed[0]);
    }

    [Fact]
    public void DiffSnapshots_NoRemovals_ReturnsEmpty()
    {
        var oldSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KeyA"] = "App Alpha"
        };
        var newSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KeyA"] = "App Alpha",
            ["KeyB"] = "App Beta"
        };

        var removed = UninstallWatcher.DiffSnapshots(oldSnapshot, newSnapshot);

        Assert.Empty(removed);
    }

    [Fact]
    public void DiffSnapshots_MultipleRemovals_ReturnsAll()
    {
        var oldSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KeyA"] = "App Alpha",
            ["KeyB"] = "App Beta",
            ["KeyC"] = "App Gamma"
        };
        var newSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KeyA"] = "App Alpha"
        };

        var removed = UninstallWatcher.DiffSnapshots(oldSnapshot, newSnapshot);

        Assert.Equal(2, removed.Count);
        Assert.Contains("App Beta", removed);
        Assert.Contains("App Gamma", removed);
    }

    #endregion

    #region 2. Debounce batching

    [Fact]
    public async Task Debounce_BatchesMultipleRemovals_IntoSingleEvent()
    {
        // Arrange: short debounce window for fast test
        var debounce = TimeSpan.FromMilliseconds(200);

        // First call: registry returns A + B. Second call: registry returns only A (B removed).
        // Third call: registry returns empty (A also removed).
        var mockRegistry = new Mock<IRegistryReader>();
        var callCount = 0;
        mockRegistry.Setup(r => r.GetInstalledApps()).Returns(() =>
        {
            callCount++;
            return callCount switch
            {
                // After first ProcessChange: B is gone
                1 => new List<InstalledApp>
                {
                    new() { RegistryKeyName = "KeyA", DisplayName = "App Alpha", NameTokens = Array.Empty<string>() }
                },
                // After second ProcessChange: A is also gone
                _ => new List<InstalledApp>
                {
                    new() { RegistryKeyName = "KeyA", DisplayName = "App Alpha", NameTokens = Array.Empty<string>() }
                }
            };
        });

        using var watcher = new UninstallWatcher(mockRegistry.Object, debounce);

        // Set initial snapshot: A + B installed
        watcher.SetSnapshotForTesting(new Dictionary<string, string>
        {
            ["KeyA"] = "App Alpha",
            ["KeyB"] = "App Beta"
        });

        var (events, firstEvent) = CaptureEvents(watcher);

        // Act: simulate two ProcessChange calls within the debounce window.
        // First removes B, mock now returns only A.
        watcher.ProcessChange();

        // Update mock for second call: now A is also gone
        callCount = 0;
        mockRegistry.Setup(r => r.GetInstalledApps()).Returns(new List<InstalledApp>());
        watcher.ProcessChange();

        // Wait for the debounce to fire
        var batch = await Task.WhenAny(firstEvent.Task, Task.Delay(2000));

        // Assert: single event with both apps
        Assert.True(firstEvent.Task.IsCompleted, "AppsUninstalled should have fired");
        Assert.Single(events);
        Assert.Contains("App Beta", events[0]);
        Assert.Contains("App Alpha", events[0]);
    }

    #endregion

    #region 3. Debounce timing — separate events after window elapsed

    [Fact]
    public async Task Debounce_SeparateEventsAfterWindowElapsed()
    {
        var debounce = TimeSpan.FromMilliseconds(150);

        // First call returns only A (B removed from initial snapshot).
        // After debounce elapses, second call returns empty (A also removed).
        var mockRegistry = new Mock<IRegistryReader>();
        var callCount = 0;
        mockRegistry.Setup(r => r.GetInstalledApps()).Returns(() =>
        {
            callCount++;
            return callCount switch
            {
                1 => new List<InstalledApp>
                {
                    new() { RegistryKeyName = "KeyA", DisplayName = "App Alpha", NameTokens = Array.Empty<string>() }
                },
                _ => new List<InstalledApp>()
            };
        });

        using var watcher = new UninstallWatcher(mockRegistry.Object, debounce);
        watcher.SetSnapshotForTesting(new Dictionary<string, string>
        {
            ["KeyA"] = "App Alpha",
            ["KeyB"] = "App Beta"
        });

        var (events, firstEvent) = CaptureEvents(watcher);

        // Act: first removal
        watcher.ProcessChange();

        // Wait for first debounce to fire
        await Task.WhenAny(firstEvent.Task, Task.Delay(2000));
        Assert.True(firstEvent.Task.IsCompleted, "First AppsUninstalled should have fired");

        // Wait a bit more to ensure we're past the debounce window
        await Task.Delay(100);

        // Second removal (well after first debounce window)
        var secondEvent = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.AppsUninstalled += batch => secondEvent.TrySetResult(batch);

        watcher.ProcessChange();

        await Task.WhenAny(secondEvent.Task, Task.Delay(2000));
        Assert.True(secondEvent.Task.IsCompleted, "Second AppsUninstalled should have fired");

        // Assert: two separate events
        Assert.True(events.Count >= 2, $"Expected 2 events but got {events.Count}");
        Assert.Contains("App Beta", events[0]);
        Assert.Contains("App Alpha", events[1]);
    }

    #endregion

    #region 4. Deduplication — same app from multiple roots

    [Fact]
    public async Task Deduplication_SameAppFromMultipleRoots_AppearsOnce()
    {
        var debounce = TimeSpan.FromMilliseconds(200);

        // Simulate the same app name appearing as removed from two different roots
        // by calling ProcessChange twice where both times the same DisplayName disappears.
        var callCount = 0;
        var mockRegistry = new Mock<IRegistryReader>();
        mockRegistry.Setup(r => r.GetInstalledApps()).Returns(() =>
        {
            callCount++;
            return callCount switch
            {
                // First call: "7-Zip" removed from HKLM root (KeyA gone)
                1 => new List<InstalledApp>
                {
                    new() { RegistryKeyName = "KeyB", DisplayName = "7-Zip", NameTokens = Array.Empty<string>() }
                },
                // Second call: "7-Zip" also removed from WOW64 root (KeyB also gone)
                _ => new List<InstalledApp>()
            };
        });

        using var watcher = new UninstallWatcher(mockRegistry.Object, debounce);
        // Initial: same app registered under two different registry keys (two roots)
        watcher.SetSnapshotForTesting(new Dictionary<string, string>
        {
            ["KeyA"] = "7-Zip",
            ["KeyB"] = "7-Zip"
        });

        var (events, firstEvent) = CaptureEvents(watcher);

        // Act: two ProcessChange calls within debounce window
        watcher.ProcessChange(); // removes KeyA → "7-Zip"
        watcher.ProcessChange(); // removes KeyB → "7-Zip"

        await Task.WhenAny(firstEvent.Task, Task.Delay(2000));

        // Assert: single event, "7-Zip" appears exactly once (deduplicated)
        Assert.True(firstEvent.Task.IsCompleted, "AppsUninstalled should have fired");
        Assert.Single(events);
        Assert.Single(events[0]); // only one distinct name
        Assert.Equal("7-Zip", events[0][0]);
    }

    #endregion

    #region 5. No-op on install — no event fired

    [Fact]
    public async Task NoOpOnInstall_NoEventFired()
    {
        var debounce = TimeSpan.FromMilliseconds(100);

        // New snapshot has MORE entries than old — this is an install, not an uninstall
        var mockRegistry = MockRegistry(
            ("KeyA", "App Alpha"),
            ("KeyB", "App Beta"),
            ("KeyC", "App Gamma"));

        using var watcher = new UninstallWatcher(mockRegistry.Object, debounce);
        watcher.SetSnapshotForTesting(new Dictionary<string, string>
        {
            ["KeyA"] = "App Alpha",
            ["KeyB"] = "App Beta"
        });

        var (events, _) = CaptureEvents(watcher);

        // Act
        watcher.ProcessChange();

        // Wait longer than the debounce window to ensure nothing fires
        await Task.Delay((int)debounce.TotalMilliseconds * 3);

        // Assert: no events fired
        Assert.Empty(events);
    }

    #endregion

    #region 6. Dispose safety

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var mockRegistry = MockRegistry(("KeyA", "App Alpha"));
        var watcher = new UninstallWatcher(mockRegistry.Object, TimeSpan.FromSeconds(3));

        // Act & Assert: Dispose should not throw
        var ex = Record.Exception(() => watcher.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DoubleDispose_DoesNotThrow()
    {
        var mockRegistry = MockRegistry(("KeyA", "App Alpha"));
        var watcher = new UninstallWatcher(mockRegistry.Object, TimeSpan.FromSeconds(3));

        watcher.Dispose();

        // Second dispose should be a no-op
        var ex = Record.Exception(() => watcher.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Start_AfterDispose_ThrowsObjectDisposedException()
    {
        var mockRegistry = MockRegistry(("KeyA", "App Alpha"));
        var watcher = new UninstallWatcher(mockRegistry.Object, TimeSpan.FromSeconds(3));

        watcher.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => watcher.Start());
    }

    #endregion
}
