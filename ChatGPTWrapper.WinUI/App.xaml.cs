using System.Reflection;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.WinUI.Services;
using ChatGPTWrapper.WinUI.Theme;
using Microsoft.UI.Xaml;

namespace ChatGPTWrapper.WinUI;

public partial class App : Application
{
    internal static MainWindow? CurrentMainWindow { get; set; }

    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WpfStaProjectHostBridge.RunOnStaThreadAsync = task => WpfStaHost.InvokeTaskAsync(task);

        var startupArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        DiagnosticsOptions.Initialize(startupArgs);
        DiagnosticsPaths.EnsureLogDirectory();
        AdventureRootPaths.AdventureDirectoryResolver = id =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChatGPTWrapper",
                "adventures",
                id.ToString("D"));
        RegisterWinUiDiagnosticsHost();
        DiagnosticsSession.WriteExtendedHeader(startupArgs);

        WinUiThemeApplication.Register();
        WinUiThemeApplication.ApplyStartupTheme();

        AppDirectories.EnsureCreated();

        _window = new MainWindow();
        _window.Activate();

        if (DiagnosticsOptions.Extended || DiagnosticsOptions.LogUiEvents)
        {
            WinUiEventLogger.Info(
                "app_startup",
                "Diagnostics enabled",
                new
                {
                    extended = DiagnosticsOptions.Extended,
                    logUiEvents = DiagnosticsOptions.LogUiEvents,
                    sessionId = DiagnosticsLog.SessionId,
                    unifiedLog = DiagnosticsOptions.Extended ? DiagnosticsLog.UnifiedTracePath : null,
                    host = "winui",
                });
        }
    }

    private static void RegisterWinUiDiagnosticsHost()
    {
        DiagnosticsHostContext.GetAppVersion = () =>
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            DiagnosticsMirror.LogException("winui_unhandled", e.Exception);
        }
        catch
        {
            /* ignore logging failures */
        }
    }
}
