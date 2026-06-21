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
    public void Weave_css_scoped_to_weave_mode()
    {
        var css = WrapperAssetTestHelpers.ReadAsset("weave-transcript-view.css");
        Assert.Contains("data-cgw-transcript-mode=\"weave\"", css);
        Assert.Contains(".cgw-weave-embed--blockquote", css);
        Assert.Contains(".cgw-weave-embed--pull-quote", css);
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
    public void Transcript_kernel_exports_registry_helpers()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");
        Assert.Contains("__cgwSetTranscriptViewMode", js);
        Assert.Contains("__cgwTranscriptKernel", js);
        Assert.Contains("collectSegmentsFromTurns", js);
        Assert.Contains("registerTranscriptRenderer", js);
    }
}
