using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InstructionSourcesPolicyTests
{
    [Fact]
    public void BuildStaticInstructionsBody_excludes_world_rules()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings() },
            Scenario = new ScenarioDocument
            {
                WorldRules = "Magic is forbidden.",
                AuthorsNote = "Keep it terse.",
            },
        };

        var body = InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle);

        Assert.Contains("Perspective:", body);
        Assert.Contains("Author's note", body);
        Assert.DoesNotContain("Magic is forbidden", body);
        Assert.DoesNotContain("World rules", body);
    }

    [Fact]
    public void BuildInstructionsSnippet_matches_static_instructions_body()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings
                {
                    Tone = "Grim",
                    ContentBoundaries = ["No gore"],
                },
            },
            Scenario = new ScenarioDocument { AuthorsNote = "Noir tone." },
        };

        Assert.Equal(
            InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle),
            InstructionSourcesPolicy.BuildInstructionsSnippet(bundle));
    }

    [Fact]
    public void InstructionDomainChanged_true_when_hash_missing_or_differs()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings { Tone = "A" } },
            Scenario = new ScenarioDocument(),
        };

        Assert.True(InstructionSourcesPolicy.InstructionDomainChanged(bundle));

        InstructionSourcesPolicy.RecordInstructionsSynced(bundle);
        Assert.False(InstructionSourcesPolicy.InstructionDomainChanged(bundle));

        bundle.Metadata.Settings.Tone = "B";
        Assert.True(InstructionSourcesPolicy.InstructionDomainChanged(bundle));
    }

    [Fact]
    public void BuildProjectInstructions_delegates_to_policy()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings() },
            Scenario = new ScenarioDocument { WorldRules = "Dragons exist." },
        };

        var instructions = AdventureProjectBindingService.BuildProjectInstructions(bundle);

        Assert.Equal(InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle), instructions);
        Assert.DoesNotContain("Dragons", instructions);
    }
}
