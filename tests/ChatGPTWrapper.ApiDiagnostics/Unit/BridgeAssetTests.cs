namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class BridgeAssetTests
{
    private static string MainAppOutputDirectory =>
        Path.GetDirectoryName(typeof(ChatGPTWrapper.ChatGptApiBridgeInjection).Assembly.Location)!;

    private static string BridgeAssetPath =>
        Path.Combine(MainAppOutputDirectory, "wrapper-assets", "chatgpt-api-bridge.js");

    [Fact]
    public void Bridge_asset_exists_in_main_app_output()
    {
        Assert.True(File.Exists(BridgeAssetPath), $"Missing: {BridgeAssetPath}");
    }

    [Fact]
    public void Bridge_asset_contains_required_symbols()
    {
        var text = File.ReadAllText(BridgeAssetPath);
        Assert.Contains("__cgwApiInvoke", text);
        Assert.Contains("probeApi", text);
        Assert.Contains("getAccessToken", text);
        Assert.Contains("discoverProjectsDom", text);
        Assert.Contains("tryUploadProjectLibrary", text);
        Assert.Contains("/backend-api/files/library", text);
    }

    [Fact]
    public void Bridge_asset_contains_streaming_capture_fields()
    {
        var text = File.ReadAllText(BridgeAssetPath);
        Assert.Contains("assistantText", text);
        Assert.Contains("streamComplete", text);
        Assert.Contains("__cgwConversationStream", text);
    }

    [Fact]
    public void Bridge_asset_contains_sentinel_diagnostics()
    {
        var text = File.ReadAllText(BridgeAssetPath);
        Assert.Contains("recordSentinelDiagnostic", text);
        Assert.Contains("__CGW_LAST_SENTINEL_DIAGNOSTIC__", text);
        Assert.Contains("resolvePageSentinelSdk", text);
    }

    [Fact]
    public void Bridge_asset_contains_fresh_sentinel_flow()
    {
        var text = File.ReadAllText(BridgeAssetPath);
        Assert.Contains("refreshConversationSentinelHeaders", text);
        Assert.Contains("chat-requirements/finalize", text);
        Assert.Contains("clearSentinelCapture", text);
        Assert.Contains("tryAcquireFreshSentinelViaSdk", text);
    }

    [Fact]
    public void Bridge_kernel_asset_exists_in_main_app_output()
    {
        var path = Path.Combine(MainAppOutputDirectory, "wrapper-assets", "cgw-bridge-kernel.js");
        Assert.True(File.Exists(path), $"Missing: {path}");
        var text = File.ReadAllText(path);
        Assert.Contains("__cgwBridgeKernel", text);
        Assert.Contains("registerChannel", text);
    }
}
