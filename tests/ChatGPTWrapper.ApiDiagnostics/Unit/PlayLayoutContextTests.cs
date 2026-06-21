using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlayLayoutContextTests
{
    [Theory]
    [InlineData(0, PlayLayoutTier.Compact)]
    [InlineData(200, PlayLayoutTier.Compact)]
    [InlineData(220, PlayLayoutTier.Cozy)]
    [InlineData(260, PlayLayoutTier.Standard)]
    [InlineData(300, PlayLayoutTier.Comfortable)]
    [InlineData(360, PlayLayoutTier.Wide)]
    [InlineData(420, PlayLayoutTier.ExtraWide)]
    public void ResolveTier_maps_content_width_bands(double contentWidth, PlayLayoutTier expected) =>
        Assert.Equal(expected, PlayLayoutContext.ResolveTier(contentWidth));

    [Fact]
    public void FromPanel_shell_side_uses_tiered_margin()
    {
        var narrow = PlayLayoutContext.FromPanel(PlayPanelSide.Left, 250);
        Assert.Equal(PlayResponsiveTiers.CompactMargin, narrow.Margin);

        var wide = PlayLayoutContext.FromPanel(PlayPanelSide.Left, 384);
        Assert.Equal(PlayResponsiveTiers.NormalMargin, wide.Margin);
        Assert.Equal(360, wide.ContentWidth);
        Assert.Equal(PlayLayoutTier.Wide, wide.Tier);
    }

    [Fact]
    public void Coordinator_resolves_tab_context_by_placement()
    {
        var settings = new AdventureSettings();
        PlayPanelLayoutService.ApplyPreset(settings, "writer");

        var snapshot = PlayLayoutCoordinator.CreateSnapshot(384, 424);
        var reference = PlayLayoutCoordinator.ResolveTabContext(snapshot, settings, "Reference");
        var state = PlayLayoutCoordinator.ResolveTabContext(snapshot, settings, "State");

        Assert.Equal(384, reference.PanelWidth);
        Assert.Equal(424, state.PanelWidth);
    }

    [Fact]
    public void Capabilities_enable_wide_reference_at_writer_optimal_shell_width()
    {
        var context = PlayLayoutContext.FromPanel(PlayPanelSide.Left, 384);

        Assert.True(context.Capabilities.UseEntityWideTemplate);
        Assert.False(context.Capabilities.UseCompactEntityMore);
        Assert.False(context.Capabilities.UseShellHeaderFlyouts);
    }

    [Fact]
    public void Capabilities_on_right_panel_use_companion_width_for_state_tab()
    {
        var settings = new AdventureSettings();
        PlayPanelLayoutService.ApplyPreset(settings, "writer");

        var snapshot = PlayLayoutCoordinator.CreateSnapshot(384, 300);
        var state = PlayLayoutCoordinator.ResolveTabContext(snapshot, settings, "State");

        Assert.False(state.Capabilities.UseWideStatePreview);
        Assert.True(state.Capabilities.ShowStateAllFields);
    }
}
