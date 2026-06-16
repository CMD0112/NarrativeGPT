using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>
/// Single STA WinForms message loop shared by play composer integration tests (avoids WPF Application conflicts).
/// </summary>
internal static class PlayComposeUiEnvironment
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TaskCompletionSource<(Form Form, WebView2 WebView)> Ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Thread? _uiThread;
    private static bool _started;

    public static async Task<(Form Form, WebView2 WebView)> GetShellAsync()
    {
        if (_started)
            return await Ready.Task;

        await Gate.WaitAsync();
        try
        {
            if (_started)
                return await Ready.Task;

            _uiThread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var form = new Form
                {
                    Text = "Play Compose Tests",
                    Width = 800,
                    Height = 600,
                    ShowInTaskbar = false,
                    WindowState = FormWindowState.Minimized,
                };
                var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                form.Show();
                Ready.TrySetResult((form, webView));
                Application.Run(form);
            })
            {
                IsBackground = true,
                Name = "PlayComposeSharedUi",
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            _started = true;
        }
        finally
        {
            Gate.Release();
        }

        return await Ready.Task;
    }

    public static void Shutdown()
    {
        if (!_started)
            return;

        try
        {
            if (Ready.Task.IsCompletedSuccessfully)
            {
                var (form, _) = Ready.Task.Result;
                if (!form.IsDisposed)
                {
                    form.Invoke(() =>
                    {
                        form.Close();
                        Application.ExitThread();
                    });
                }
            }
        }
        catch
        {
            /* ignore shutdown races */
        }

        _uiThread?.Join(TimeSpan.FromSeconds(5));
        _started = false;
    }
}
