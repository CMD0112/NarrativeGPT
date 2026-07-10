using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlayLayoutPresetTests
{
    [Theory]
    [InlineData("writer", "Reference", PlayPanelSide.Left)]
    [InlineData("writer", "State", PlayPanelSide.Right)]
    [InlineData("writer", "Warnings", PlayPanelSide.Hidden)]
    [InlineData("writer", "Notes", PlayPanelSide.Right)]
    [InlineData("gm", "Notes", PlayPanelSide.Right)]
    [InlineData("minimal", "Reference", PlayPanelSide.Hidden)]
    public void ApplyPreset_writes_expected_tab_placement(string presetId, string tab, string expectedSide)
    {
        var settings = new AdventureSettings();
        PlayPanelLayoutService.ApplyPreset(settings, presetId);

        Assert.Equal(presetId, settings.PlayLayoutPresetId);
        Assert.Equal(expectedSide, PlayPanelLayoutService.ResolveTabPlacement(settings, tab));
    }

    [Fact]
    public void ResolveTabPlacement_defaults_notes_to_right()
    {
        var settings = new AdventureSettings();
        Assert.Equal(PlayPanelSide.Right, PlayPanelLayoutService.ResolveTabPlacement(settings, "Notes"));
    }

    [Fact]
    public void NormalizeTabPlacement_coerces_notes_left_to_right()
    {
        Assert.Equal(
            PlayPanelSide.Right,
            PlayPanelLayoutService.NormalizeTabPlacement("Notes", PlayPanelSide.Left));
    }
}
