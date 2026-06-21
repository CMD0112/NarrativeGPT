using System.Windows;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDirectories.EnsureCreated();
        CanonSchemaLoader.Initialize();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        var chrome = UiChromeStore.Load();
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(chrome.Theme);
        ThemeRuntime.Update(resolved);
        ThemeApplicationService.ApplyToWpf(resolved);

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
        }
        catch
        {
            /* ignore logging failures */
        }
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
