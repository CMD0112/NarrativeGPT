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

    public List<JsonImportReviewItem> JsonImportReviewQueue { get; set; } = [];

    /// <summary>Last proposed scenario.json / entities.json captured from a json import utility reply.</summary>
    public JsonImportProposedSnapshot? JsonImportProposedSnapshot { get; set; }
}

public sealed class JsonImportProposedSnapshot
{
    public string ScenarioJson { get; set; } = "";

    public string EntitiesJson { get; set; } = "";

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<string> NonCanonicalFilenames { get; set; } = [];

    public List<string> PreviewWarnings { get; set; } = [];
}

public sealed class JsonImportReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><c>scenarioField</c> or <c>entity</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Scenario property key (camelCase) when <see cref="Kind"/> is scenarioField.</summary>
    public string Field { get; set; } = "";

    /// <summary><c>person</c>, <c>place</c>, <c>concept</c>, or <c>faction</c> when Kind is entity.</summary>
    public string EntityType { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary><c>add</c>, <c>update</c>, or <c>remove</c> for entities; scenario fields always set.</summary>
    public string Action { get; set; } = "update";

    public string Value { get; set; } = "";

    public string PriorValue { get; set; } = "";

    public string Rationale { get; set; } = "";
}

public enum JsonImportConflictSeverity
{
    None,
    Supported,
    Drift,
    Unsupported,
}

public sealed class JsonImportProposalAnalysis
{
    public Guid ProposalId { get; init; }

    public JsonImportConflictSeverity Severity { get; init; }

    /// <summary>Accepting would update JSON without a matching markdown change.</summary>
    public bool WarnStaleSourcesOnAccept { get; init; }

    public string? SourceRef { get; init; }

    public string? SourceExcerpt { get; init; }

    public string? DeterministicValue { get; init; }

    public string DisplaySummary { get; init; } = "";

    public string? EntityLinkageHint { get; init; }
}

public sealed class SourceEditReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TargetFile { get; set; } = "";

    public string Operation { get; set; } = "replace";

    public string Content { get; set; } = "";

    public string Rationale { get; set; } = "";
}
