using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DiagnosticsOptions.Initialize(e.Args);
        AppDirectories.EnsureCreated();
        WpfDiagnosticsHost.Register();
        CanonSchemaLoader.Initialize();
        DiagnosticsSession.WriteExtendedHeader(e.Args);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        if (DiagnosticsOptions.Extended || DiagnosticsOptions.LogUiEvents)
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var chrome = UiChromeStore.Load();
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(chrome.Theme);
        ThemeRuntime.Update(resolved);
        ThemeApplicationService.ApplyToWpf(resolved);

        if (DiagnosticsOptions.Extended || DiagnosticsOptions.LogUiEvents)
        {
            UiEventLogger.Info(
                "app_startup",
                "Diagnostics enabled",
                new
                {
                    extended = DiagnosticsOptions.Extended,
                    logUiEvents = DiagnosticsOptions.LogUiEvents,
                    sessionId = DiagnosticsLog.SessionId,
                    unifiedLog = DiagnosticsOptions.Extended ? DiagnosticsLog.UnifiedTracePath : null,
                });
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticsSession.WriteExtendedShutdown();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            ProjectLinkDiagnostics.Log($"Unhandled UI exception: {e.Exception}");
            DiagnosticsMirror.LogException("dispatcher", e.Exception);
        }
        catch
        {
            /* ignore logging failures */
        }

        MessageBox.Show(
            FormatExceptionMessage(e.Exception),
            "ChatGPT Wrapper error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception ex)
            return;

        try
        {
            ProjectLinkDiagnostics.Log($"Unhandled domain exception: {ex}");
            DiagnosticsMirror.LogException("appdomain", ex);
        }
        catch
        {
            /* ignore logging failures */
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var ex = e.Exception?.GetBaseException();
        if (ex is null)
            return;

        try
        {
            DiagnosticsMirror.LogException("unobserved_task", ex);
            UiEventLogger.Error(
                "async_task_failed",
                ex.Message,
                new { operation = "unobserved_task", exceptionType = ex.GetType().Name });
        }
        catch
        {
            /* ignore logging failures */
        }

        e.SetObserved();
    }

    private static string FormatExceptionMessage(Exception ex)
    {
        var lines = new System.Text.StringBuilder();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (lines.Length > 0)
                lines.AppendLine();
            lines.Append(current.Message);
        }

        return lines.ToString();
    }
}
