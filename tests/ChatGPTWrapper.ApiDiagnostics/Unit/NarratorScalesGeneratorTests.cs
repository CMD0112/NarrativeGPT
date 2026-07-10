using ChatGPTWrapper.Adventure.Services;

using ChatGPTWrapper.Adventure.Services.NarratorScales;



namespace ChatGPTWrapper.ApiDiagnostics.Unit;



public sealed class NarratorScalesGeneratorTests

{

    [Fact]

    public void Generate_is_catalog_only_without_adventure_specific_sections()

    {

        var markdown = NarratorScalesGenerator.Generate();



        Assert.Contains("# Narrator scales reference", markdown);

        Assert.DoesNotContain("## active-narration-scales", markdown);

        Assert.DoesNotContain("## active-combat-scales", markdown);

        Assert.Contains("## narration-scales", markdown);

        Assert.Contains("## combat-scales", markdown);

        Assert.Contains("## inspect-instructions", markdown);

        Assert.Contains("### combat-difficulty", markdown);

        Assert.Contains("## scene-profiles", markdown);

        Assert.Contains("#### balanced", markdown);

        Assert.Contains("does **not** list adventure-specific active values", markdown);

        Assert.Contains("_Schema version:", markdown);

    }



    [Fact]

    public void ParseSections_indexes_dimension_catalog_sections()

    {

        var markdown = NarratorScalesGenerator.Generate();



        var sections = NarratorScalesManifestService.ParseSections(markdown);



        Assert.Contains(sections, s => string.Equals(s.Id, "narration-scales", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(sections, s => string.Equals(s.Id, "combat-scales", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(sections, s => string.Equals(s.Id, "active-narration-scales", StringComparison.OrdinalIgnoreCase));

    }

}

