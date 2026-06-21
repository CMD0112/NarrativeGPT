using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class AdventureDesignSourcePromptTests
{
    [Fact]
    public void AllDefinitions_covers_core_lore_files_plus_instructions()
    {
        var paths = AdventureDesignSourcePromptService.AllDefinitions
            .Select(d => d.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(SectionSchema.ScenarioFile, paths);
        Assert.Contains(SectionSchema.WorldFile, paths);
        Assert.Contains(SectionSchema.PlotFile, paths);
        Assert.Contains(SectionSchema.CastFile, paths);
        Assert.Contains(SectionSchema.LexiconFile, paths);
        Assert.Contains("instructions-snippet.md", paths);
    }

    [Theory]
    [InlineData(SectionSchema.ScenarioFile, "## opening")]
    [InlineData(SectionSchema.WorldFile, "# World")]
    [InlineData(SectionSchema.PlotFile, "# Plot")]
    [InlineData(SectionSchema.CastFile, "# Cast")]
    [InlineData(SectionSchema.LexiconFile, "# Lexicon")]
    public void BuildPrompt_includes_file_shape_and_context(
        string relativePath,
        string expectedFragment)
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Prompt test adventure");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Coastal ruins");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.World, "worldRules", "Tides reveal hidden paths.");

        var prompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, relativePath);
        var prefixedName = AdventureDesignSourcePromptService.BuildPrefixedFileName(
            "Prompt test adventure",
            relativePath);

        Assert.Contains("Prompt test adventure", prompt, StringComparison.Ordinal);
        Assert.Contains(expectedFragment, prompt, StringComparison.Ordinal);
        Assert.Contains(AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
                "Prompt test adventure",
                relativePath),
            prompt,
            StringComparison.Ordinal);
        Assert.Contains($"--- begin {prefixedName} ---", prompt, StringComparison.Ordinal);
        Assert.Contains("DELIVERABLE", prompt, StringComparison.Ordinal);
        Assert.Contains("Downloadable file", prompt, StringComparison.Ordinal);
        Assert.Contains("DESIGN CONTEXT", prompt, StringComparison.Ordinal);
        Assert.Contains("Coastal ruins", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPrompt_instructions_uses_refinement_not_design_context()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Instructions refine");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Coastal ruins");
        bundle.Metadata.Settings.ContentBoundaries = ["No exploitation."];

        var prompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, "instructions-snippet.md");

        Assert.Contains("refinement only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CANONICAL MANUAL VERSION", prompt, StringComparison.Ordinal);
        Assert.Contains("No exploitation.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("DESIGN CONTEXT", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Coastal ruins", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPrefixedFileName_sanitizes_invalid_filename_characters()
    {
        var name = AdventureDesignSourcePromptService.BuildPrefixedFileName(
            "Test: Adventure",
            SectionSchema.ScenarioFile);

        Assert.Equal("Test- Adventure - scenario.md", name);
    }

    [Fact]
    public void ForDesignStep_Sources_returns_all_prompts()
    {
        Assert.Equal(6, AdventureDesignSourcePromptService.ForDesignStep(AdventureDesignStep.Sources).Count());
    }

    [Fact]
    public void ForDesignStep_Lexicon_returns_lexicon_prompt_only()
    {
        var prompts = AdventureDesignSourcePromptService.ForDesignStep(AdventureDesignStep.Lexicon).ToList();
        Assert.Single(prompts);
        Assert.Equal(SectionSchema.LexiconFile, prompts[0].RelativePath);
    }

    [Fact]
    public void ForDesignStep_World_returns_world_prompt_only()
    {
        var prompts = AdventureDesignSourcePromptService.ForDesignStep(AdventureDesignStep.World).ToList();
        Assert.Single(prompts);
        Assert.Equal(SectionSchema.WorldFile, prompts[0].RelativePath);
    }

    [Fact]
    public void NormalizeSelectedPaths_uses_pipeline_order_cast_first()
    {
        var paths = AdventureDesignSourcePromptService.NormalizeSelectedPaths(
        [
            SectionSchema.CastFile,
            SectionSchema.ScenarioFile,
            SectionSchema.WorldFile,
        ]);

        Assert.Equal(
            [SectionSchema.CastFile, SectionSchema.ScenarioFile, SectionSchema.WorldFile],
            paths);
    }

    [Fact]
    public void PromptPipelineOrder_starts_with_cast_not_scenario()
    {
        Assert.Equal(SectionSchema.CastFile, AdventureDesignSourcePromptService.PromptPipelineOrder[0]);
        Assert.Equal(SectionSchema.ScenarioFile, AdventureDesignSourcePromptService.PromptPipelineOrder[1]);
    }

    [Fact]
    public void BuildPrompt_includes_adventure_title_block()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Title block test");

        var prompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, SectionSchema.ScenarioFile);

        Assert.Contains("Adventure identity (mandatory)", prompt, StringComparison.Ordinal);
        Assert.Contains("Wrapper adventure title: **Title block test**", prompt, StringComparison.Ordinal);
        Assert.Contains("Do **not** use a different adventure name", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPrompt_world_includes_prior_block_when_dependencies_sent()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Prior block test");
        AdventureDesignService.MarkSourceFilePromptSent(bundle, SectionSchema.CastFile, "Cast excerpt");
        AdventureDesignService.MarkSourceFilePromptSent(bundle, SectionSchema.ScenarioFile, "Scenario excerpt");

        var prompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, SectionSchema.WorldFile);

        Assert.Contains("PRIOR SOURCE FILES", prompt, StringComparison.Ordinal);
        Assert.Contains(SectionSchema.CastFile, prompt, StringComparison.Ordinal);
        Assert.Contains(SectionSchema.ScenarioFile, prompt, StringComparison.Ordinal);
        Assert.Contains("Cast excerpt", prompt, StringComparison.Ordinal);
        Assert.Contains("Scenario excerpt", prompt, StringComparison.Ordinal);
        Assert.Contains("role-only placeholders", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCombinedPrompt_includes_coherence_block_and_cast_first_file_order()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Combined prompt test");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Harbor town");

        var prompt = AdventureDesignSourcePromptService.BuildCombinedPrompt(
            bundle,
            [SectionSchema.WorldFile, SectionSchema.CastFile, SectionSchema.ScenarioFile]);

        Assert.Contains("Coherence rules", prompt, StringComparison.Ordinal);
        Assert.Contains("Adventure identity (mandatory)", prompt, StringComparison.Ordinal);
        Assert.Contains("multiple Project source files (3)", prompt, StringComparison.Ordinal);

        var castIdx = prompt.IndexOf("=== FILE: sources/Combined prompt test - cast.md ===", StringComparison.Ordinal);
        var scenarioIdx = prompt.IndexOf("=== FILE: sources/Combined prompt test - scenario.md ===", StringComparison.Ordinal);
        var worldIdx = prompt.IndexOf("=== FILE: sources/Combined prompt test - world.md ===", StringComparison.Ordinal);

        Assert.True(castIdx >= 0);
        Assert.True(scenarioIdx > castIdx);
        Assert.True(worldIdx > scenarioIdx);
    }

    [Fact]
    public void BuildPrompt_instructions_requires_title_header()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Instructions header test");

        var prompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, "instructions-snippet.md");

        Assert.Contains("# Instructions header test - Instructions Snippet", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNextRecommendedPath_returns_first_unsent_pipeline_file()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Next path test");
        AdventureDesignService.MarkSourceFilePromptSent(bundle, SectionSchema.CastFile);

        Assert.Equal(SectionSchema.ScenarioFile, AdventureDesignSourcePromptService.GetNextRecommendedPath(bundle));
    }

    [Fact]
    public void MarkSourceFilePromptSent_and_GetSentSourceFiles_track_pipeline_order()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Sent state test");
        AdventureDesignService.MarkSourceFilePromptSent(bundle, SectionSchema.WorldFile, "world reply");
        AdventureDesignService.MarkSourceFilePromptSent(bundle, SectionSchema.CastFile, "cast reply");

        Assert.True(AdventureDesignService.IsSourceFilePromptSent(bundle, SectionSchema.CastFile));
        Assert.Equal(
            [SectionSchema.CastFile, SectionSchema.WorldFile],
            AdventureDesignService.GetSentSourceFiles(bundle));
    }

    [Fact]
    public void IsOutOfOrder_true_when_dependency_not_sent()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Order test");

        Assert.True(AdventureDesignSourcePromptService.IsOutOfOrder(bundle, SectionSchema.WorldFile));
        Assert.False(AdventureDesignSourcePromptService.IsOutOfOrder(bundle, SectionSchema.CastFile));
    }

    [Fact]
    public void BuildCombinedPrompt_describes_multiple_files()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Combined prompt test");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "Harbor town");

        var prompt = AdventureDesignSourcePromptService.BuildCombinedPrompt(
            bundle,
            [SectionSchema.ScenarioFile, SectionSchema.WorldFile]);

        Assert.Contains("multiple Project source files (2)", prompt, StringComparison.Ordinal);
        Assert.Contains("Coherence rules", prompt, StringComparison.Ordinal);
        Assert.Contains("=== FILE: sources/Combined prompt test - scenario.md ===", prompt, StringComparison.Ordinal);
        Assert.Contains("=== FILE: sources/Combined prompt test - world.md ===", prompt, StringComparison.Ordinal);
        Assert.Contains("Combined prompt test - scenario.md", prompt, StringComparison.Ordinal);
        Assert.Contains("Combined prompt test - world.md", prompt, StringComparison.Ordinal);
        Assert.Contains("multiple separate markdown files", prompt, StringComparison.Ordinal);
        Assert.Contains("DESIGN CONTEXT (shared", prompt, StringComparison.Ordinal);
        Assert.Contains("Harbor town", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCombinedPrompt_single_selection_matches_single_prompt()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Single via combined");

        var single = AdventureDesignSourcePromptService.BuildPrompt(bundle, SectionSchema.PlotFile);
        var combined = AdventureDesignSourcePromptService.BuildCombinedPrompt(bundle, [SectionSchema.PlotFile]);

        Assert.Equal(single, combined);
    }

    [Fact]
    public void ResolveSourceFilePromptMessage_returns_prompt_unchanged()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Resolve test");
        const string prompt = "=== DELIVERABLE: sources/Resolve test - scenario.md ===\nBody";

        var resolved = AdventureDesignChatService.ResolveSourceFilePromptMessage(bundle, prompt);

        Assert.Equal(prompt, resolved);
        Assert.True(AdventureDesignService.GetOrCreateStep(bundle, AdventureDesignStep.Sources).StepSeedSent);
    }

    [Fact]
    public void FormatSendError_maps_composer_not_found()
    {
        var message = AdventureDesignDomChatService.FormatSendError("composer_not_found");
        Assert.Contains("composer", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatPinError_describes_missing_conversation_tab()
    {
        var message = AdventureDesignDomChatService.FormatPinError("design_tab_not_on_conversation");
        Assert.Contains("design thread", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPipelineChecklist_marks_next_recommended_and_blocked_rows()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Checklist test");
        AdventureDesignService.MarkSourceFilePromptSent(bundle, SectionSchema.CastFile);

        var rows = AdventureDesignSourcePromptService.BuildPipelineChecklist(bundle);

        Assert.Equal(7, rows.Count);
        var canonFormat = rows.Single(r => r.RelativePath == SectionSchema.CanonFormatFile);
        Assert.True(canonFormat.IsReferenceFile);
        var cast = rows.Single(r => r.RelativePath == SectionSchema.CastFile);
        var scenario = rows.Single(r => r.RelativePath == SectionSchema.ScenarioFile);
        var world = rows.Single(r => r.RelativePath == SectionSchema.WorldFile);

        Assert.True(cast.PromptSent);
        Assert.False(cast.IsNextRecommended);
        Assert.False(cast.IsBlocked);

        Assert.True(scenario.IsNextRecommended);
        Assert.False(scenario.IsBlocked);

        Assert.True(world.IsBlocked);
        Assert.Contains("scenario.md", world.BlockedReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPipelineChecklist_reflects_on_disk_state()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Disk state test");
        AdventureTestData.WriteLocalSources(bundle);

        var rows = AdventureDesignSourcePromptService.BuildPipelineChecklist(bundle);

        Assert.Contains(rows, r => r.RelativePath == SectionSchema.CastFile && r.PresentOnDisk);
    }

    [Fact]
    public void GetCombinedSelectionWarning_requires_dependencies_in_selection_or_sent()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Combined warning test");

        var warning = AdventureDesignSourcePromptService.GetCombinedSelectionWarning(
            bundle,
            [SectionSchema.WorldFile]);

        Assert.NotNull(warning);
        Assert.Contains("cast.md", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scenario.md", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCombinedSelectionWarning_allows_selection_when_dependencies_included()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Combined ok test");

        var warning = AdventureDesignSourcePromptService.GetCombinedSelectionWarning(
            bundle,
            [SectionSchema.CastFile, SectionSchema.ScenarioFile, SectionSchema.WorldFile]);

        Assert.Null(warning);
    }
}
