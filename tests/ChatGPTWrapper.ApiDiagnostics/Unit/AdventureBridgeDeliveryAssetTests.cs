namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class AdventureBridgeDeliveryAssetTests
{
    [Fact]
    public void Adventure_bridge_includes_fill_readback_and_delivery_audit()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "ChatGPT_files",
            "adventure-bridge.js");
        path = Path.GetFullPath(path);
        Assert.True(File.Exists(path), $"Missing adventure-bridge.js at {path}");

        var text = File.ReadAllText(path);
        Assert.Contains("bridge_fill_readback", text);
        Assert.Contains("bridge_delivery_audit", text);
        Assert.Contains("injection_delivery_mismatch", text);
        Assert.Contains("waitForStableComposerText", text);
        Assert.Contains("options.displayUserLine", text);
        Assert.DoesNotContain("verifiedBy: \"composer_shortened\"", text);
    }

    [Fact]
    public void Play_compose_allows_native_send_when_intercept_blocked()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "ChatGPT_files",
            "cgw-play-compose.js");
        path = Path.GetFullPath(path);
        Assert.True(File.Exists(path), $"Missing cgw-play-compose.js at {path}");

        var text = File.ReadAllText(path);
        Assert.Contains("compose_native_submit_click", text);
        Assert.Contains("allowing ChatGPT default", text);
    }
}
