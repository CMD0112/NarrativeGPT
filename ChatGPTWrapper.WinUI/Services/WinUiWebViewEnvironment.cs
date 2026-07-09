namespace ChatGPTWrapper.WinUI.Services;

/// <summary>
/// Ensures WebView2 user-data folder is configured before any tab initializes.
/// WinUI uses default environment creation via <c>EnsureCoreWebView2Async()</c>.
/// </summary>
internal static class WinUiWebViewEnvironment
{
    private static bool _configured;

    public static Task ReadyTask
    {
        get
        {
            EnsureConfigured();
            return Task.CompletedTask;
        }
    }

    public static Task GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConfigured();
        return Task.CompletedTask;
    }

    private static void EnsureConfigured()
    {
        if (_configured)
            return;

        _configured = true;
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "WebView2UserData");
        Directory.CreateDirectory(userDataFolder);
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER",
            userDataFolder,
            EnvironmentVariableTarget.Process);
    }
}
