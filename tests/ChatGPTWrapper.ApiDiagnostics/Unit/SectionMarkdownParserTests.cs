using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class SectionMarkdownParserTests
{
    [Fact]
    public void Parse_extracts_title_sections_and_entries()
    {
        const string markdown = """
            # Scenario

            ## opening
            **Setting:** A coastal town
            **Opening:** Fog rolls in at dusk.

            ## misc
            ### Harbor Watch
            Id: harbor-watch
            Aliases: Watch, Guards
            The night patrol keeps order on the docks.
            """;

        var doc = SectionMarkdownParser.Parse(markdown);

        Assert.Equal("Scenario", doc.Title);
        Assert.Equal(2, doc.Sections.Count);
        Assert.Equal("opening", doc.Sections[0].Id);
        Assert.Equal("A coastal town", SectionMarkdownParser.ExtractField(doc.Sections[0].FreeformBody, "Setting"));
        Assert.Equal("Fog rolls in at dusk.", SectionMarkdownParser.ExtractField(doc.Sections[0].FreeformBody, "Opening"));

        var misc = doc.Sections[1];
        Assert.Single(misc.Entries);
        Assert.Equal("Harbor Watch", misc.Entries[0].Title);
        Assert.Equal("harbor-watch", misc.Entries[0].Slug);
        Assert.Equal(["Watch", "Guards"], misc.Entries[0].Aliases);
        Assert.Contains("night patrol", misc.Entries[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_orphan_entry_creates_misc_section()
    {
        const string markdown = """
            ### Lone NPC
            Id: lone-npc
            A wanderer without a parent section.
            """;

        var doc = SectionMarkdownParser.Parse(markdown);

        Assert.Single(doc.Sections);
        Assert.Equal("misc", doc.Sections[0].Id);
        Assert.Equal("lone-npc", doc.Sections[0].Entries[0].Slug);
    }

    [Fact]
    public void ExtractField_matches_alternate_labels()
    {
        const string body = """
            **Player role:** Investigator
            **Player Role:** Should not win
            """;

        Assert.Equal("Investigator", SectionMarkdownParser.ExtractField(body, "Player role", "Player Role"));
    }

    [Fact]
    public void StripStructuredLines_removes_metadata_lines()
    {
        const string body = """
            **Name:** Alex
            Id: alex
            Aliases: Player
            > Flavor: weary
            Status: active
            A tired traveler with a secret.
            """;

        var stripped = SectionMarkdownParser.StripStructuredLines(body);

        Assert.DoesNotContain("**Name:**", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("Id:", stripped, StringComparison.Ordinal);
        Assert.Contains("tired traveler", stripped, StringComparison.Ordinal);
    }
}
