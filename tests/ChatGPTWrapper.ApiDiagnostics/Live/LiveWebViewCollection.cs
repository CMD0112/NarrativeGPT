namespace ChatGPTWrapper.ApiDiagnostics.Live;

[CollectionDefinition("LiveWebView")]
public sealed class LiveWebViewCollection : ICollectionFixture<LiveWebViewFixture>
{
}

public sealed class LiveWebViewFixture : IAsyncLifetime
{
    public WebView2DiagnosticHost Host { get; } = new();

    public Task InitializeAsync() => Host.InitializeAsync();

    public async Task DisposeAsync() => await Host.DisposeAsync();
}
