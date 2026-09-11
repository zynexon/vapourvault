# VaporVault v2 — Manual End-to-End Verification Checklist

Run through this checklist once on a real Windows machine before signing off on v2.
Check each box after confirming the expected outcome. Note any deviations in the "Notes" column.

---

## Prerequisites

- [ ] VaporVault is built and runnable (`dotnet publish` or direct `dotnet run`)
- [ ] You have access to `C:\ProgramData\VaporVault\watcher.log` (or wherever `App.Log()` writes)
- [ ] You have a small throwaway app to install/uninstall, **or** you'll create a fake registry entry + AppData folder as described below

### Option A: Use a real throwaway app
Install something small like 7-Zip, Notepad++, or WinMerge.

### Option B: Fake registry entry (no installer needed)
1. Open `regedit` and navigate to:
   ```
   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall
   ```
2. Create a new key named `TestApp123`
3. Inside `TestApp123`, add a String value: `DisplayName` = `TestApp123`
4. Create a folder: `%LocalAppData%\TestApp123\` with a dummy file inside (e.g., `dummy.txt`)

---

## Checklist

### 1. Tray icon and close-to-tray behavior

| Step | Action | Expected Outcome | ✓ | Notes |
|------|--------|-------------------|---|-------|
| 1.1 | Launch VaporVault normally (no `--background` flag) | Main window opens | ☐ | |
| 1.2 | Check the system tray (notification area) | VaporVault icon visible with tooltip "VaporVault — Monitoring for uninstalls" | ☐ | |
| 1.3 | Close the main window (X button) with "Close to Tray" enabled in Settings | Window disappears but tray icon remains; app is still running (check Task Manager) | ☐ | |
| 1.4 | Double-click the tray icon | Main window reappears and is brought to the foreground | ☐ | |
| 1.5 | Right-click tray icon → "Open VaporVault" | Main window reappears and is brought to the foreground | ☐ | |
| 1.6 | Right-click tray icon → "Exit" | App fully exits; tray icon removed; process gone from Task Manager | ☐ | |

**After this step, check `watcher.log`:**
- [ ] Log contains `AUMID registered successfully for Toast Notifications.`
- [ ] Log contains `UninstallWatcher started successfully.`

---

### 2. Run at Startup toggle

| Step | Action | Expected Outcome | ✓ | Notes |
|------|--------|-------------------|---|-------|
| 2.1 | Right-click tray icon → check "Run at Startup" | Toggle becomes checked; registry entry created at `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` for VaporVault | ☐ | |
| 2.2 | Right-click tray icon again | "Run at Startup" still shows as checked (persisted) | ☐ | |
| 2.3 | Uncheck "Run at Startup" | Registry entry removed | ☐ | |

---

### 3. Single uninstall → toast notification

| Step | Action | Expected Outcome | ✓ | Notes |
|------|--------|-------------------|---|-------|
| 3.1 | Ensure the test app (or fake registry entry) is installed and has a folder in `%LocalAppData%` | Folder exists on disk | ☐ | |
| 3.2 | Uninstall the test app (or delete the registry key from `regedit`) | Within ~3-5 seconds, a Windows toast notification appears | ☐ | |
| 3.3 | Verify the toast content | Title: `"<AppName> uninstalled — <size> of leftovers found"`. Body mentions folders left behind and prompts to open VaporVault | ☐ | |
| 3.4 | Click the toast notification | VaporVault main window opens / comes to the foreground | ☐ | |

**After this step, check `watcher.log`:**
- [ ] Log contains `AppsUninstalled event received for 1 app(s): <AppName>`
- [ ] Log contains `Running targeted scan for uninstalled app: '<AppName>'`
- [ ] Log contains `Found X leftover folder(s) for '<AppName>'`
- [ ] Log contains `SendUninstallDetectedToast: Toast shown for '<AppName>'`

---

### 4. Batched uninstalls — two apps within debounce window

> **Expected behavior (confirmed as intended):** Two rapid uninstalls → one `AppsUninstalled` event containing both names → **two separate toast notifications** (one per app).

| Step | Action | Expected Outcome | ✓ | Notes |
|------|--------|-------------------|---|-------|
| 4.1 | Set up two fake registry entries (`TestApp1`, `TestApp2`) with corresponding `%LocalAppData%` folders | Both entries and folders exist | ☐ | |
| 4.2 | Delete both registry keys within ~2 seconds of each other | Two toast notifications appear (one per app) after the 3-second debounce window | ☐ | |
| 4.3 | Verify each toast names the correct app and size | Toast content matches each app individually | ☐ | |

**After this step, check `watcher.log`:**
- [ ] Log contains `AppsUninstalled event received for 2 app(s): TestApp1, TestApp2`
- [ ] Log contains separate `Running targeted scan` entries for each app
- [ ] Log contains separate `Toast shown` entries for each app

---

### 5. No false positive on install

| Step | Action | Expected Outcome | ✓ | Notes |
|------|--------|-------------------|---|-------|
| 5.1 | With VaporVault running, install a new small app (or create a new registry entry under Uninstall) | No toast notification appears; no `AppsUninstalled` event in the log | ☐ | |

**After this step, check `watcher.log`:**
- [ ] If present, log contains `Change detected but no removals found (likely an install).` — this is correct behavior
- [ ] No `AppsUninstalled event received` line for this action

---

### 6. Startup behavior — background mode

| Step | Action | Expected Outcome | ✓ | Notes |
|------|--------|-------------------|---|-------|
| 6.1 | Enable "Run at Startup" from the tray icon | Confirmed enabled | ☐ | |
| 6.2 | Sign out and sign back in (or restart the machine) | VaporVault starts automatically | ☐ | |
| 6.3 | Check the screen after login | No main window visible — app launched in `--background` mode | ☐ | |
| 6.4 | Check the system tray | VaporVault tray icon is present | ☐ | |
| 6.5 | Uninstall an app | Toast notification fires correctly (same as step 3) | ☐ | |

**After this step, check `watcher.log`:**
- [ ] Fresh log entries from the new session
- [ ] `UninstallWatcher started successfully.` present

---

### 7. Log legibility review

| Step | Action | Expected Outcome | ✓ | Notes |
|------|--------|-------------------|---|-------|
| 7.1 | Open `C:\ProgramData\VaporVault\watcher.log` in a text editor | File exists and is readable | ☐ | |
| 7.2 | Verify timestamps | Each line has a `[YYYY-MM-DD HH:MM:SS.mmm]` prefix | ☐ | |
| 7.3 | Verify coverage | Log entries present for: AUMID registration, watcher start, change detection, scan, toast send | ☐ | |
| 7.4 | Check for unhandled exceptions | No stack traces or `FAILED` entries (unless expected from a deliberately provoked error) | ☐ | |

---

## Sign-Off

| Item | Status |
|------|--------|
| All 7 checklist sections passed | ☐ |
| No unexplained log anomalies | ☐ |
| v2 approved for v3 build | ☐ |

**Tested by:** _______________  
**Date:** _______________  
**Build hash/version:** _______________
