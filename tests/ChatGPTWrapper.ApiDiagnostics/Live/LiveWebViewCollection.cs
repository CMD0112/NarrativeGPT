using ChatGPTWrapper.ApiDiagnostics.Infrastructure;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

[CollectionDefinition("LiveWebView", DisableParallelization = true)]
public sealed class LiveWebViewCollection : ICollectionFixture<LiveWebViewFixture>;

public sealed class LiveWebViewFixture : IAsyncLifetime
{
    private IDisposable? _webViewProfileLock;

    public WebView2DiagnosticHost Host { get; } = new();

    public async Task InitializeAsync()
    {
        _webViewProfileLock = FileLockGate.AcquireWebViewProfile(nameof(LiveWebViewFixture));
        await Host.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await Host.DisposeAsync();
        }
        finally
        {
            _webViewProfileLock?.Dispose();
            _webViewProfileLock = null;
        }
    }
}
