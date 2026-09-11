using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VaporVault.Core.Data;
using VaporVault_App.Pages;

namespace VaporVault_App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Set a reasonable default window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 750));

        // Hook the close event for close-to-tray behavior
        AppWindow.Closing += AppWindow_Closing;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var settings = AppSettings.Load();

        // If live interception is enabled and close-to-tray is on,
        // hide the window instead of closing (minimize to tray).
        if (settings.LiveInterceptionEnabled && settings.CloseToTray)
        {
            args.Cancel = true;
            AppWindow.Hide();
            System.Diagnostics.Debug.WriteLine("MainWindow: Minimized to tray (close cancelled).");
        }
        // Otherwise, let the window close normally (app will exit).
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Shows the main window and brings it to the foreground.
    /// Called from TrayIconService when the user opens from the tray.
    /// </summary>
    public void ShowAndBringToFront()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: ShowAndBringToFront called");

            // First, get the native handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            System.Diagnostics.Debug.WriteLine($"MainWindow: hwnd = {hwnd}");

            // Use Win32 to restore and bring to front (more reliable than WinUI APIs for hidden windows)
            ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);

            // Also use WinUI APIs as backup
            AppWindow.Show(activateWindow: true);
            this.Activate();

            System.Diagnostics.Debug.WriteLine("MainWindow: ShowAndBringToFront completed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow: ShowAndBringToFront FAILED: {ex.Message}");
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "scan":
                    NavFrame.Navigate(typeof(ScanPage));
                    break;
                case "vault":
                    NavFrame.Navigate(typeof(VaultPage));
                    break;
                case "history":
                    NavFrame.Navigate(typeof(HistoryPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }
}

