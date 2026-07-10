using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class NotesSectionParserTests
{
    [Fact]
    public void Parse_empty_returns_empty()
    {
        Assert.Empty(NotesSectionParser.Parse(null));
        Assert.Empty(NotesSectionParser.Parse(""));
    }

    [Fact]
    public void Parse_single_heading()
    {
        var text = "intro\n## Act 2\nbody";
        var sections = NotesSectionParser.Parse(text);
        Assert.Single(sections);
        Assert.Equal("Act 2", sections[0].Title);
        Assert.Equal(1, sections[0].LineIndex);
        Assert.Equal(text.IndexOf("## Act 2", StringComparison.Ordinal), sections[0].CharOffset);
    }

    [Fact]
    public void Parse_duplicate_titles_keeps_both()
    {
        var text = "## Scene\nx\n## Scene\ny";
        var sections = NotesSectionParser.Parse(text);
        Assert.Equal(2, sections.Count);
        Assert.All(sections, s => Assert.Equal("Scene", s.Title));
    }

    [Fact]
    public void Parse_ignores_hash_and_triple_hash_lines()
    {
        var text = "# Title\n### Sub\n## Real section";
        var sections = NotesSectionParser.Parse(text);
        Assert.Single(sections);
        Assert.Equal("Real section", sections[0].Title);
    }

    [Fact]
    public void Parse_handles_crlf()
    {
        var text = "## Act 1\r\nline";
        var sections = NotesSectionParser.Parse(text);
        Assert.Single(sections);
        Assert.Equal(0, sections[0].LineIndex);
        Assert.Equal(0, sections[0].CharOffset);
    }

    [Fact]
    public void GetSectionIndexForOffset_returns_current_section()
    {
        var text = "intro\n## Act 2\nbody\n## Act 3\nend";
        var sections = NotesSectionParser.Parse(text);
        Assert.Equal(0, NotesSectionParser.GetSectionIndexForOffset(sections, 0));
        Assert.Equal(0, NotesSectionParser.GetSectionIndexForOffset(sections, text.IndexOf("## Act 2", StringComparison.Ordinal)));
        Assert.Equal(0, NotesSectionParser.GetSectionIndexForOffset(sections, text.IndexOf("body", StringComparison.Ordinal)));
        Assert.Equal(1, NotesSectionParser.GetSectionIndexForOffset(sections, text.IndexOf("## Act 3", StringComparison.Ordinal)));
        Assert.Equal(1, NotesSectionParser.GetSectionIndexForOffset(sections, text.Length - 1));
    }
}
