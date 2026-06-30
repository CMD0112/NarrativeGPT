using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InstructionContractServiceTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void ApplyFromDesignStep_maps_fields_to_settings_not_authors_note()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Contract test");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Instructions, "authorsNote", "Terse prose.");
        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            InstructionContractService.GlobalBoundariesFieldKey,
            "No sexual content involving minors.");
        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            InstructionContractService.CharacterPortrayalFieldKey,
            "Mara: Avoid treating as passive.");
        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            InstructionContractService.InstructionAddendumFieldKey,
            "Keep war horror dark but not gratuitous.");

        InstructionContractService.ApplyFromDesignStep(bundle);

        Assert.Equal("Terse prose.", bundle.Scenario.AuthorsNote);
        Assert.Contains("No sexual content involving minors.", bundle.Metadata.Settings.ContentBoundaries);
        Assert.Single(bundle.Metadata.Settings.CharacterPortrayalRules);
        Assert.Equal("Mara", bundle.Metadata.Settings.CharacterPortrayalRules[0].Subject);
        Assert.Equal("Avoid treating as passive.", bundle.Metadata.Settings.CharacterPortrayalRules[0].Rule);
        Assert.Equal("Keep war horror dark but not gratuitous.", bundle.Metadata.Settings.InstructionAddendum);
    }

    [Fact]
    public void BuildContractSections_includes_global_portrayal_and_addendum()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings
                {
                    ContentBoundaries = ["No gore"],
                    CharacterPortrayalRules =
                    [
                        new CharacterPortrayalRule { Subject = "Bram", Rule = "Not a cartoon villain." },
                    ],
                    InstructionAddendum = "Prefer moral ambiguity.",
                },
            },
            Scenario = new ScenarioDocument(),
        };

        var sections = InstructionContractService.BuildContractSections(bundle);

        Assert.Contains("Content boundaries:", sections);
        Assert.Contains("No gore", sections);
        Assert.Contains("Character portrayal:", sections);
        Assert.Contains("Bram: Not a cartoon villain.", sections);
        Assert.Contains("Instruction addendum:", sections);
        Assert.Contains("Prefer moral ambiguity.", sections);
    }

    [Fact]
    public void BuildAuthorDefinedContractBlock_uses_design_fields_when_settings_empty()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design contract");
        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            InstructionContractService.GlobalBoundariesFieldKey,
            "No exploitation of child NPCs.");
        AdventureDesignService.SetField(
            bundle,
            AdventureDesignStep.Instructions,
            InstructionContractService.CharacterPortrayalFieldKey,
            "Nessa: Avoid prophecy-child tropes.");

        var block = InstructionContractService.BuildAuthorDefinedContractBlock(bundle);

        Assert.Contains("No exploitation of child NPCs.", block);
        Assert.Contains("Nessa: Avoid prophecy-child tropes.", block);
        Assert.Contains("do not invent others", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryApplyFromInstructionsBody_parses_sections_back_into_settings()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings() },
            Scenario = new ScenarioDocument(),
        };

        const string markdown = """
            # Adventure - Instructions Snippet

            Perspective: second person. Tense: present. Detail: medium.
            Tone: somber

            Content boundaries:
            No sexual content involving minors.
            Avoid gratuitous gore.

            Character portrayal:
            Mara: Avoid treating as passive.
            Bram: Avoid treating as a simple villain.

            Instruction addendum:
            Keep grief honest but not exploitative.
            """;

        Assert.True(InstructionContractService.TryApplyFromInstructionsBody(bundle, markdown));
        Assert.Equal(2, bundle.Metadata.Settings.ContentBoundaries.Count);
        Assert.Equal(2, bundle.Metadata.Settings.CharacterPortrayalRules.Count);
        Assert.Equal("Mara", bundle.Metadata.Settings.CharacterPortrayalRules[0].Subject);
        Assert.Contains("grief honest", bundle.Metadata.Settings.InstructionAddendum);
    }

    [Fact]
    public void BuildStaticInstructionsBody_includes_character_portrayal_section()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings
                {
                    ContentBoundaries = ["No gore"],
                    CharacterPortrayalRules =
                    [
                        new CharacterPortrayalRule { Subject = "Mara", Rule = "Not passive by default." },
                    ],
                },
            },
            Scenario = new ScenarioDocument { AuthorsNote = "Noir tone." },
        };

        var body = InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle);

        Assert.Contains("Content boundaries:", body);
        Assert.Contains("Character portrayal:", body);
        Assert.Contains("Mara: Not passive by default.", body);
    }

    [Fact]
    public void HydrateDesignInstructionFields_round_trips_settings()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Round trip");
        bundle.Metadata.Settings.ContentBoundaries = ["Line A"];
        bundle.Metadata.Settings.CharacterPortrayalRules =
        [
            new CharacterPortrayalRule { Subject = "Crownward", Rule = "Do not eroticize magical control." },
        ];
        bundle.Metadata.Settings.InstructionAddendum = "Extra note.";

        InstructionContractService.HydrateDesignInstructionFields(bundle);

        Assert.Equal("Line A", AdventureDesignService.GetField(
            bundle,
            AdventureDesignStep.Instructions,
            InstructionContractService.GlobalBoundariesFieldKey));
        Assert.Contains(
            "Crownward: Do not eroticize magical control.",
            AdventureDesignService.GetField(
                bundle,
                AdventureDesignStep.Instructions,
                InstructionContractService.CharacterPortrayalFieldKey) ?? "");
    }
}

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class InstructionContractDesignerTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public InstructionContractDesignerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-InstrDesigner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void BuildInstructionsSnippetFileContent_includes_title_header()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Snippet header");
        bundle.Metadata.Settings.Perspective = "second person";

        var content = InstructionContractService.BuildInstructionsSnippetFileContent(bundle);

        Assert.StartsWith("# Snippet header - Instructions Snippet", content, StringComparison.Ordinal);
        Assert.Contains("Perspective: second person", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyDesignerFields_updates_settings_and_design_fields()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Designer fields");

        InstructionContractService.ApplyDesignerFields(
            bundle,
            "second person",
            "present",
            "medium",
            "somber",
            "Terse noir.",
            ["No gore."],
            [new CharacterPortrayalRule { Subject = "Mara", Rule = "Not passive." }],
            "Keep ambiguity.",
            "hard",
            "moderate",
            "balanced",
            "lasting");

        Assert.Equal("second person", bundle.Metadata.Settings.Perspective);
        Assert.Equal("hard", bundle.Metadata.Settings.Difficulty);
        Assert.Equal("moderate", bundle.Metadata.Settings.ViolenceLevel);
        Assert.Equal("Terse noir.", bundle.Scenario.AuthorsNote);
        Assert.Contains("No gore.", bundle.Metadata.Settings.ContentBoundaries);
        Assert.Equal("Mara", bundle.Metadata.Settings.CharacterPortrayalRules[0].Subject);
        Assert.Equal(
            "No gore.",
            AdventureDesignService.GetField(
                bundle,
                AdventureDesignStep.Instructions,
                InstructionContractService.GlobalBoundariesFieldKey));
    }

    [Fact]
    public void GenerateInstructionsSnippetFile_writes_canonical_content()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Generate file");
        bundle.Metadata.Settings.ContentBoundaries = ["Boundary line."];

        AdventureDesignService.EnsureWorkspace(bundle);
        Assert.True(InstructionContractService.GenerateInstructionsSnippetFile(bundle));

        var path = AdventureSourceFileService.ResolveAbsolutePath(
            bundle,
            InstructionContractService.InstructionsSnippetFile);
        var text = File.ReadAllText(path);
        Assert.Contains("# Generate file - Instructions Snippet", text, StringComparison.Ordinal);
        Assert.Contains("Boundary line.", text, StringComparison.Ordinal);
    }
}
