using ChatGPTWrapper;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class WeaveTranscriptViewTests
{
    [Fact]
    public void Weave_asset_defines_flow_renderer()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("weave-transcript-view.js");
        Assert.Contains("function buildFlow", js);
        Assert.Contains("function renderWeaveFlow", js);
        Assert.Contains("function syncWeaveFlow", js);
        Assert.Contains("__cgwRegisterTranscriptRenderer", js);
        Assert.Contains("\"weave\"", js);
    }

    [Fact]
    public void Weave_asset_defines_embed_kind_heuristic()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("weave-transcript-view.js");
        Assert.Contains("function resolveEmbedKind", js);
        Assert.Contains("pull-quote", js);
        Assert.Contains("run-in", js);
    }

    [Fact]
    public void Weave_css_embed_has_default_margin_and_blockquote_surface()
    {
        var css = WrapperAssetTestHelpers.ReadAsset("weave-transcript-view.css");
        Assert.Contains("--cgw-weave-embed-margin-block, 0.75rem", css);
        Assert.Contains(".cgw-weave-embed--blockquote", css);
        Assert.Contains("border-radius: 0 6px 6px 0", css);
    }

    [Fact]
    public void Format_builder_emits_weave_variables()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.WeaveEmbedMarginBlockRem = 1.25;
        var css = FormatCssBuilder.BuildWeaveCssText(format);
        Assert.Contains("--cgw-weave-embed-margin-block: 1.25rem", css);
        Assert.Contains("data-cgw-transcript-mode=\"weave\"", css);
    }

    [Fact]
    public void Transcript_mode_switch_clears_weave_fingerprints()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");
        Assert.Contains("delete globalThis.__cgwWeaveViewFingerprint", js);
        Assert.Contains("delete globalThis.__cgwWeaveFlowFingerprints", js);
    }

    [Fact]
    public void Weave_apply_skips_early_exit_without_weave_markup()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("weave-transcript-view.js");
        Assert.Contains("function containerHasWeaveMarkup", js);
        Assert.Contains("containerHasWeaveMarkup(container)", js);
    }

    [Fact]
    public void Weave_asset_resolves_player_blocks_from_registry_fallback()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("weave-transcript-view.js");
        Assert.Contains("function resolveWeaveEmbedBlocks", js);
        Assert.Contains("playerSnippet", js);
        Assert.Contains("data-cgw-user-line", js);
        Assert.Contains("kind !== \"packetContext\"", js);
    }

    [Fact]
    public void Weave_embed_uses_div_not_blockquote()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("weave-transcript-view.js");
        Assert.Contains("document.createElement(\"div\")", js);
        Assert.DoesNotContain("document.createElement(\"blockquote\")", js);
    }

    [Fact]
    public void Transcript_kernel_exports_registry_helpers()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");
        Assert.Contains("__cgwSetTranscriptViewMode", js);
        Assert.Contains("__cgwTranscriptKernel", js);
        Assert.Contains("collectSegmentsFromTurns", js);
        Assert.Contains("registerTranscriptRenderer", js);
    }
}
