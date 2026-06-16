using System.Windows;
using System.Windows.Threading;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDirectories.EnsureCreated();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            ProjectLinkDiagnostics.Log($"Unhandled UI exception: {e.Exception}");
        }
        catch
        {
            /* ignore logging failures */
        }

        MessageBox.Show(
            e.Exception.Message,
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
        }
        catch
        {
            /* ignore logging failures */
        }
    }
}
