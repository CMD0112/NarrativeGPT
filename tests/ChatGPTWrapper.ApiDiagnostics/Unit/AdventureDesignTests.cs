using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(nameof(IsolatedAppRootCollection))]
public sealed class AdventureDesignTests
{

    [Fact]
    public void CreateDesigningAdventure_persists_workspace_and_status()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Test design adventure");

        Assert.Equal(AdventureStatus.Designing, bundle.Metadata.Status);
        Assert.Equal(AdventureDesignStep.Setup, bundle.DesignWorkspace.CurrentStep);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(AdventureStatus.Designing, reloaded!.Metadata.Status);
        Assert.Equal(AdventureDesignStep.Setup, reloaded.DesignWorkspace.CurrentStep);
    }

    [Fact]
    public void TryAdvanceStep_moves_through_ordered_steps()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Step test");
        Assert.True(AdventureDesignService.TryAdvanceStep(bundle, out var next));
        Assert.Equal(AdventureDesignStep.Concept, next);
        Assert.Equal(AdventureDesignStep.Concept, bundle.DesignWorkspace.CurrentStep);
    }

    [Fact]
    public void ParseExtractResponse_maps_concept_fields()
    {
        const string json = """
            {
              "setting": "Foggy harbor town",
              "playerRole": "Dock inspector",
              "genre": "Mystery",
              "tone": "Noir",
              "openingSituation": "A crate washes ashore."
            }
            """;

        var proposals = AdventureDesignExtractionService.ParseExtractResponse(
            AdventureDesignStep.Concept,
            json);

        Assert.Equal(5, proposals.Count);
        Assert.Contains(proposals, p => p.FieldKey == "setting" && p.ProposedValue.Contains("harbor"));
    }

    [Fact]
    public void Finalize_writes_scenario_and_exports_sources()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Finalize test");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Ancient forest");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "playerRole", "Ranger");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "genre", "Fantasy");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "openingSituation", "A path closes behind you.");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.World, "worldRules", "Magic is wild.");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Plot, "plotEssentials", "Find the lost grove.");
        AdventureStore.Save(bundle);

        var result = AdventureDesignFinalizeService.Finalize(bundle);
        Assert.True(result.Success);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(AdventureStatus.Active, reloaded!.Metadata.Status);
        Assert.Equal("Ancient forest", reloaded.Scenario.Setting);
        Assert.Equal("Ranger", reloaded.Scenario.PlayerRole);
        Assert.True(File.Exists(Path.Combine(reloaded.DirectoryPath, "sources", "scenario.md")));
    }

    [Fact]
    public void ImportDraftFrameworkMarkdown_populates_sources_step()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Import test");
        AdventureDesignService.ImportDraftFrameworkMarkdown(bundle, "# Framework\n\n## scenario.md\nContent");

        var outline = AdventureDesignService.GetField(bundle, AdventureDesignStep.Sources, "sourceOutline");
        Assert.False(string.IsNullOrWhiteSpace(AdventureDesignService.GetFreeform(bundle, AdventureDesignStep.Sources)));
        Assert.Contains("framework", AdventureDesignService.GetFreeform(bundle, AdventureDesignStep.Sources), StringComparison.OrdinalIgnoreCase);
    }
}
