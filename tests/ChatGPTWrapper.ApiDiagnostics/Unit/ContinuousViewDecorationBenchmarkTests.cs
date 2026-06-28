using System.Globalization;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection("PlayComposeWebView")]
[Trait("Category", "Performance")]
[Trait("Category", "Integration")]
public sealed class ContinuousViewDecorationBenchmarkTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.InitializeAsync();

    public Task DisposeAsync() => host.DisposeAsync();

    [Fact]
    public async Task DecorateTurnBlocks_completes_under_200ms_for_50_turns()
    {
        var script = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");
        await host.EvalVoidAsync(script);

        var raw = await host.EvalStringAsync("String(globalThis.__cgwBenchmarkDecorateTurnBlocks(50))");
        Assert.True(double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var elapsedMs));
        Assert.True(elapsedMs < 200, $"Decoration benchmark took {elapsedMs:F1}ms (limit 200ms)");
    }

    [Fact]
    public void Continuous_view_asset_wires_ordinal_map_and_role_interleave()
    {
        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");
        Assert.Contains("interleaveGroupedTurnRoots", text);
        Assert.Contains("__cgwThreadOrdinalMap", text);
        Assert.Contains("scheduleContinuousViewDecorationOnly", text);
        Assert.Contains("segmentsNeedPhraseHighlightRefresh", text);
    }
}
