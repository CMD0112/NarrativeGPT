namespace ChatGPTWrapper.Adventure.Models;

public sealed class ScenarioDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public string Setting { get; set; } = "";

    public string PlayerRole { get; set; } = "";

    public string Genre { get; set; } = "";

    public string Tone { get; set; } = "";

    public string OpeningSituation { get; set; } = "";

    public string MajorConflicts { get; set; } = "";

    public string StartingConstraints { get; set; } = "";

    public string PlotEssentials { get; set; } = "";

    public string WorldRules { get; set; } = "";

    public string AuthorsNote { get; set; } = "";

    /// <summary>Naming, tone, and anti-repetition rules for lexicon.md.</summary>
    public string LexiconRules { get; set; } = "";

    /// <summary>Setting-appropriate name pools (people, places, realms) for lexicon.md.</summary>
    public string LexiconPools { get; set; } = "";

    /// <summary>Overused names and phrases to avoid; exported to lexicon.md.</summary>
    public string LexiconAvoid { get; set; } = "";

    public List<SourceEditReviewItem> SourceEditReviewQueue { get; set; } = [];
}

public sealed class SourceEditReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TargetFile { get; set; } = "";

    public string Operation { get; set; } = "replace";

    public string Content { get; set; } = "";

    public string Rationale { get; set; } = "";
}
