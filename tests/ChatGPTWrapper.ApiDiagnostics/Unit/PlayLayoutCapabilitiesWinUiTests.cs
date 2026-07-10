using ChatGPTWrapper.Adventure.Services.PlayLayout;
using Xunit;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayLayoutCapabilitiesWinUiTests
{
    [Fact]
    public void FromContentWidth_narrow_uses_compact_footer()
    {
        var caps = PlayLayoutCapabilities.FromContentWidth(250);
        Assert.True(caps.UseCompactFooterMore);
        Assert.False(caps.UseFullFooterLabels);
    }

    [Fact]
    public void FromContentWidth_wide_uses_full_footer_and_entity_templates()
    {
        var caps = PlayLayoutCapabilities.FromContentWidth(1600);
        Assert.True(caps.UseFullFooterLabels);
        Assert.True(caps.UseEntityWideTemplate);
        Assert.True(caps.ShowEntityDescription);
    }
}
