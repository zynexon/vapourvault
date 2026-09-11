using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VaporVault.Core.Data;

namespace VaporVault_App.Services;

/// <summary>
/// Manages the system tray icon for VaporVault's background presence.
/// Uses H.NotifyIcon.WinUI (a native WinUI 3 tray icon library).
///
/// The tray icon provides:
/// - Visual indicator that VaporVault is monitoring for uninstalls
/// - Context menu: "Open VaporVault", "Run at Startup" (toggle), "Exit"
/// - Double-click to open the main window
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _taskbarIcon;
    private bool _disposed;

    /// <summary>
    /// Raised when the user clicks "Open VaporVault" or double-clicks the tray icon.
    /// </summary>
    public event Action? OpenRequested;

    /// <summary>
    /// Raised when the user clicks "Exit" from the tray context menu.
    /// </summary>
    public event Action? ExitRequested;

    /// <summary>
    /// Creates and shows the system tray icon with a context menu.
    /// Must be called from the UI thread.
    /// </summary>
    public void Show()
    {
        if (_taskbarIcon != null) return;

        // Build context menu
        var contextMenu = new MenuFlyout();

        var openItem = new MenuFlyoutItem { Text = "Open VaporVault" };
        openItem.Click += (_, _) =>
        {
            System.Diagnostics.Debug.WriteLine("TrayIcon: Open VaporVault clicked");
            OpenRequested?.Invoke();
        };
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        var startupItem = new ToggleMenuFlyoutItem
        {
            Text = "Run at Startup",
            IsChecked = StartupManager.IsEnabled
        };
        startupItem.Click += (_, _) =>
        {
            if (startupItem.IsChecked)
                StartupManager.Enable();
            else
                StartupManager.Disable();

            var settings = AppSettings.Load();
            settings.RunAtStartup = startupItem.IsChecked;
            settings.Save();
        };
        contextMenu.Items.Add(startupItem);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) =>
        {
            System.Diagnostics.Debug.WriteLine("TrayIcon: Exit clicked");
            ExitRequested?.Invoke();
        };
        contextMenu.Items.Add(exitItem);

        var openCommand = new RelayCommand(() =>
        {
            System.Diagnostics.Debug.WriteLine("TrayIcon: Left/double click → OpenRequested");
            OpenRequested?.Invoke();
        });

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "VaporVault — Monitoring for uninstalls",
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = contextMenu,
            NoLeftClickDelay = true,
            LeftClickCommand = openCommand,
            DoubleClickCommand = openCommand,
            IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico"))
        };

        _taskbarIcon.ForceCreate();
    }

    /// <summary>
    /// Hides and removes the system tray icon.
    /// </summary>
    public void Hide()
    {
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hide();
    }

    /// <summary>
    /// Minimal ICommand implementation for the DoubleClickCommand.
    /// </summary>
    private sealed class RelayCommand(Action execute) : System.Windows.Input.ICommand
    {
#pragma warning disable CS0067 // Required by ICommand, but we never raise it
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
