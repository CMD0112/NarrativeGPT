using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(nameof(IsolatedAppRootCollection))]
public sealed class AdventureSessionModePolicyTests
{
    private static void RemoveImportableLoreSources(AdventureBundle bundle)
    {
        foreach (var fileName in ProjectSourceImportService.ImportableLoreFileNames)
        {
            var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
    [Fact]
    public void CanSwitchToPlay_is_true_for_any_loaded_adventure()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Play switch");
        Assert.True(AdventureSessionModePolicy.CanSwitchToPlay(bundle));
    }

    [Fact]
    public void GetDesignAvailability_returns_Ready_for_designing_adventure()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Designing");
        Assert.Equal(
            AdventureSessionDesignAvailability.Ready,
            AdventureSessionModePolicy.GetDesignAvailability(bundle));
    }

    [Fact]
    public void GetDesignAvailability_returns_UnavailableHasPlayTurns_when_active_with_turns()
    {
        var bundle = AdventureStore.CreateNew("Active play", designing: false);
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            PlayerText = "Hello",
            NarratorText = "Hi",
            Status = TurnStatus.Accepted,
        });
        RemoveImportableLoreSources(bundle);
        AdventureStore.Save(bundle);

        Assert.Equal(
            AdventureSessionDesignAvailability.UnavailableHasPlayTurns,
            AdventureSessionModePolicy.GetDesignAvailability(bundle));
        Assert.False(AdventureSessionModePolicy.CanSwitchToDesign(bundle));
    }

    [Fact]
    public void GetDesignAvailability_returns_ReadyLocalSources_when_active_with_turns_and_local_sources()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Post-finalize sources");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Forest");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "playerRole", "Ranger");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "genre", "Fantasy");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "openingSituation", "Start.");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.World, "worldRules", "Wild magic.");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Plot, "plotEssentials", "Find grove.");
        AdventureStore.Save(bundle);
        AdventureDesignFinalizeService.Finalize(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);
        reloaded!.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            PlayerText = "Hello",
            NarratorText = "Hi",
            Status = TurnStatus.Accepted,
        });
        AdventureStore.Save(reloaded);

        Assert.Equal(AdventureStatus.Active, reloaded.Metadata.Status);
        Assert.True(AdventureSourceFileService.HasLocalLoreSourceFiles(reloaded));
        Assert.Equal(
            AdventureSessionDesignAvailability.ReadyLocalSources,
            AdventureSessionModePolicy.GetDesignAvailability(reloaded));
        Assert.True(AdventureSessionModePolicy.CanSwitchToDesign(reloaded));
    }

    [Fact]
    public void GetDesignAvailability_returns_NeedsWizard_for_fresh_active_adventure()
    {
        var bundle = AdventureStore.CreateNew("Fresh", designing: false);
        RemoveImportableLoreSources(bundle);
        AdventureStore.Save(bundle);

        Assert.Equal(
            AdventureSessionDesignAvailability.NeedsWizard,
            AdventureSessionModePolicy.GetDesignAvailability(bundle));
        Assert.True(AdventureSessionModePolicy.CanSwitchToDesign(bundle));
    }

    [Fact]
    public void ResolveDesignEntryIntent_uses_local_sources_when_post_finalize()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Finalize path");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Forest");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "playerRole", "Ranger");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "genre", "Fantasy");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "openingSituation", "Start.");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.World, "worldRules", "Wild magic.");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Plot, "plotEssentials", "Find grove.");
        AdventureStore.Save(bundle);
        AdventureDesignFinalizeService.Finalize(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(AdventureStatus.Active, reloaded!.Metadata.Status);

        Assert.Equal(
            DesignModeEntryIntent.LocalSourcesEdit,
            AdventureSessionModePolicy.ResolveDesignEntryIntent(reloaded));
    }
}
