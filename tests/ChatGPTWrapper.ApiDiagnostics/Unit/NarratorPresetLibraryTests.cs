using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class NarratorPresetLibraryTests
{
    [Fact]
    public void ApplySceneProfile_sets_turn_overrides()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");

        NarratorPresetLibrary.ApplySceneProfile(bundle, "action", NarratorOverrideScope.Turn);

        Assert.Equal("brief", bundle.Metadata.Settings.PlayTurnOverrides.ResponseLength);
        Assert.Equal("low", bundle.Metadata.Settings.PlayTurnOverrides.DetailLevel);
        Assert.Equal("tense", bundle.Metadata.Settings.PlayTurnOverrides.Tone);
    }

    [Fact]
    public void BuildComboItems_includes_inherit_first()
    {
        var items = NarratorPresetLibrary.BuildComboItems(NarratorParameter.DetailLevel, "medium");

        Assert.True(items[0].IsInherit);
        Assert.Contains(items, i => string.Equals(i.Id, "high", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindSceneProfile_returns_known_profile()
    {
        var profile = NarratorPresetLibrary.FindSceneProfile("lore");

        Assert.NotNull(profile);
        Assert.Equal("Lore", profile.DisplayName);
        Assert.Equal("expansive", profile.Values[NarratorParameter.ResponseLength]);
    }
}
