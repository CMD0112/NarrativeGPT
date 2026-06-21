using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EntityReferenceEditModeResolverTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(280)]
    [InlineData(400)]
    public void Auto_uses_modal_at_all_widths(int width)
    {
        var layout = PlayLayoutCapabilities.FromContentWidth(width);
        var mode = EntityReferenceEditModeResolver.Resolve(EntityReferenceEditMode.Auto, layout);
        Assert.Equal(EntityReferenceEditMode.Modal, mode);
    }

    [Fact]
    public void Modal_stays_modal_regardless_of_width()
    {
        var layout = PlayLayoutCapabilities.FromContentWidth(400);
        var mode = EntityReferenceEditModeResolver.Resolve(EntityReferenceEditMode.Modal, layout);
        Assert.Equal(EntityReferenceEditMode.Modal, mode);
    }

    [Fact]
    public void SidePanel_stays_side_panel_when_explicit()
    {
        var layout = PlayLayoutCapabilities.FromContentWidth(400);
        var mode = EntityReferenceEditModeResolver.Resolve(EntityReferenceEditMode.SidePanel, layout);
        Assert.Equal(EntityReferenceEditMode.SidePanel, mode);
    }
}
