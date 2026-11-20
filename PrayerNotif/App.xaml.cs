using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PrayerNotif.Services;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PrayerNotif;
/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    //TODO: maybe make this a user preference?
    private bool RunInBackgroundEnabled = true;
    private TaskbarIcon? _trayIcon;
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();

        _window.AppWindow.Closing += (sender, e) =>
        {
            if (RunInBackgroundEnabled)
            {
                e.Cancel = true;
                sender.Hide();
            }
        };

        InitializeTrayIcon();

        DataHandeler<UserPrefrences> _userPrefrencesHandler = new();
        if(_userPrefrencesHandler.GetData()?.SpawnWindowOnStartup == true)
        { 
            _window.Activate();
        }
    }
    private void InitializeTrayIcon()
    {
        // Built-in WinUI commands (works with H.NotifyIcon's native right-click menu)
        var openCommand = new XamlUICommand();
        openCommand.ExecuteRequested += (_, _) => ShowMainWindow();

        var exitCommand = new XamlUICommand();
        exitCommand.ExecuteRequested += (_, _) => ExitApp();

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open", Command = openCommand });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = exitCommand });

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "My App",

            // Zero-asset icon using an emoji or Segoe Fluent Icon glyph (e.g. "\uE707")
            IconSource = new GeneratedIconSource
            {
                Text = "📍",
                Foreground = new SolidColorBrush(Colors.White)
            },

            ContextFlyout = menu,
            LeftClickCommand = openCommand,
            NoLeftClickDelay = true // Open immediately on left-click without waiting for double-click
        };

        // Required so the icon appears even when MainWindow has never been activated!
        // Set enablesEfficiencyMode: true if your app does no active work while idle.
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
    }
    private void ShowMainWindow()
    {
        if (_window is null)
            return;

        // If the user minimized it earlier, restore it before activating
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        _window.Activate();
    }

    private void ExitApp()
    {
        // Dispose first so Windows removes the tray icon immediately instead of leaving a ghost icon
        _trayIcon?.Dispose();
        Application.Current.Exit();
    }
}
