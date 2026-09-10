using System.Runtime.InteropServices;

namespace VaporVault.Core.Interop;

/// <summary>
/// P/Invoke declarations for Win32 registry notification APIs.
/// Used by UninstallWatcher to receive event-driven notifications when
/// Uninstall registry subkeys are added or removed.
/// </summary>
internal static class RegistryNotificationInterop
{
    // ── Registry root key handles ──

    internal static readonly IntPtr HKEY_LOCAL_MACHINE = new(unchecked((int)0x80000002));
    internal static readonly IntPtr HKEY_CURRENT_USER = new(unchecked((int)0x80000001));

    // ── Access rights ──

    /// <summary>
    /// Required to request change notifications for a registry key.
    /// </summary>
    internal const uint KEY_NOTIFY = 0x0010;

    /// <summary>
    /// Combines STANDARD_RIGHTS_READ, KEY_QUERY_VALUE, KEY_ENUMERATE_SUB_KEYS, and KEY_NOTIFY.
    /// </summary>
    internal const uint KEY_READ = 0x20019;

    // ── Notification filter flags ──

    /// <summary>
    /// Notify the caller if a subkey is added or deleted. This is what we need
    /// to detect when an Uninstall entry is created or removed.
    /// </summary>
    internal const uint REG_NOTIFY_CHANGE_NAME = 0x00000001;

    // ── Error codes ──

    internal const int ERROR_SUCCESS = 0;

    // ── RegOpenKeyEx ──

    /// <summary>
    /// Opens the specified registry key with the requested access rights.
    /// </summary>
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int RegOpenKeyEx(
        IntPtr hKey,
        string lpSubKey,
        uint ulOptions,
        uint samDesired,
        out IntPtr phkResult);

    // ── RegNotifyChangeKeyValue ──

    /// <summary>
    /// Notifies the caller about changes to the attributes or contents of a
    /// specified registry key. When called with fAsynchronous = false (synchronous),
    /// the function does not return until a change has occurred.
    /// This is a one-shot notification — must be re-registered after each fire.
    /// </summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern int RegNotifyChangeKeyValue(
        IntPtr hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        IntPtr hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    // ── RegCloseKey ──

    /// <summary>
    /// Closes a handle to the specified registry key.
    /// </summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern int RegCloseKey(IntPtr hKey);

    // ── Helper: open a registry key for notification watching ──

    /// <summary>
    /// Opens a registry key with KEY_NOTIFY | KEY_READ access for watching.
    /// Returns the native handle, or IntPtr.Zero if the key could not be opened.
    /// </summary>
    /// <param name="rootKey">HKEY_LOCAL_MACHINE or HKEY_CURRENT_USER.</param>
    /// <param name="subKeyPath">The Uninstall key path to open.</param>
    internal static IntPtr OpenForNotification(IntPtr rootKey, string subKeyPath)
    {
        int result = RegOpenKeyEx(rootKey, subKeyPath, 0, KEY_READ | KEY_NOTIFY, out IntPtr hKey);
        return result == ERROR_SUCCESS ? hKey : IntPtr.Zero;
    }

    /// <summary>
    /// Blocks the current thread until a subkey is added or removed under the
    /// specified registry key handle. Returns true if a change was detected,
    /// false if the call failed (e.g., handle was closed from another thread).
    /// </summary>
    /// <param name="hKey">Handle from OpenForNotification.</param>
    internal static bool WaitForChange(IntPtr hKey)
    {
        int result = RegNotifyChangeKeyValue(
            hKey,
            bWatchSubtree: true,
            dwNotifyFilter: REG_NOTIFY_CHANGE_NAME,
            hEvent: IntPtr.Zero,          // synchronous — no event object
            fAsynchronous: false);         // block until change

        return result == ERROR_SUCCESS;
    }

    /// <summary>
    /// Safely closes a registry key handle. No-ops if the handle is zero.
    /// </summary>
    internal static void CloseHandle(IntPtr hKey)
    {
        if (hKey != IntPtr.Zero)
            RegCloseKey(hKey);
    }
}
