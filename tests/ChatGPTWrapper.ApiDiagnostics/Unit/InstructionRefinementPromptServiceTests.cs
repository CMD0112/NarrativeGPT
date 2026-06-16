using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InstructionRefinementPromptServiceTests
{
    [Fact]
    public void BuildRefinementPrompt_includes_canonical_body_and_anti_invention_rules()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Refinement test");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Coastal ruins");
        bundle.Metadata.Settings.ContentBoundaries = ["No gratuitous gore."];
        bundle.Metadata.Settings.Perspective = "second person";
        bundle.Metadata.Settings.Tense = "present";
        bundle.Metadata.Settings.DetailLevel = "medium";

        var prompt = InstructionRefinementPromptService.BuildRefinementPrompt(bundle, "Tighten boundary wording.");

        Assert.Contains("refinement only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CANONICAL MANUAL VERSION", prompt, StringComparison.Ordinal);
        Assert.Contains("# Refinement test - Instructions Snippet", prompt, StringComparison.Ordinal);
        Assert.Contains("No gratuitous gore.", prompt, StringComparison.Ordinal);
        Assert.Contains("Do **not** add new content boundaries", prompt, StringComparison.Ordinal);
        Assert.Contains("Tighten boundary wording.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("DESIGN CONTEXT", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Coastal ruins", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRefinementPrompt_uses_default_notes_when_request_empty()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Default notes");

        var prompt = InstructionRefinementPromptService.BuildRefinementPrompt(bundle);

        Assert.Contains("improve clarity and flow only", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPrompt_instructions_matches_refinement_prompt()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Prompt parity");

        var buildPrompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, "instructions-snippet.md");
        var refinement = InstructionRefinementPromptService.BuildRefinementPrompt(bundle);

        Assert.Equal(refinement, buildPrompt);
    }
}
