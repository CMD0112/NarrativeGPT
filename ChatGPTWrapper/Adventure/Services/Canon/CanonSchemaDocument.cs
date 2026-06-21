using System.Text.Json.Serialization;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal sealed class CanonSchemaDocument
{
    public int SchemaVersion { get; init; } = 1;

    public List<CanonKindDocument> Kinds { get; init; } = [];
}

internal sealed class CanonKindDocument
{
    public required string KindId { get; init; }

    public required string CollectionKey { get; init; }

    public required string SectionId { get; init; }

    public required string SourceFile { get; init; }

    public required string UiCategory { get; init; }

    public required string TypeLabel { get; init; }

    public bool ShowInPlayGrid { get; init; }

    public bool IsSingleton { get; init; }

    public string TitleProperty { get; init; } = "name";

    public string SecondaryProperty { get; init; } = "";

    public string SnippetProperty { get; init; } = "description";

    public List<CanonFieldDocument> Fields { get; init; } = [];
}

internal sealed class CanonFieldDocument
{
    public required string Label { get; init; }

    public required string JsonKey { get; init; }

    public string Format { get; init; } = nameof(CanonFieldFormat.PlainLine);

    public string Role { get; init; } = nameof(CanonFieldRole.Extra);

    public bool Multiline { get; init; }

    public string ControlType { get; init; } = nameof(CanonFieldControlType.Text);

    public List<string> AlternateLabels { get; init; } = [];
}

internal sealed class CanonSchemaCatalog
{
    public required int SchemaVersion { get; init; }

    public required IReadOnlyList<CanonEntityKindSpec> AllKinds { get; init; }

    public CanonEntityKindSpec Player { get; init; } = null!;

    public CanonEntityKindSpec Party { get; init; } = null!;

    public CanonEntityKindSpec Npc { get; init; } = null!;

    public CanonEntityKindSpec Location { get; init; } = null!;

    public CanonEntityKindSpec Faction { get; init; } = null!;

    public CanonEntityKindSpec Concept { get; init; } = null!;

    public CanonEntityKindSpec Quest { get; init; } = null!;

    public CanonEntityKindSpec Mystery { get; init; } = null!;

    public CanonEntityKindSpec Conflict { get; init; } = null!;

    public CanonEntityKindSpec Consequence { get; init; } = null!;

    public CanonEntityKindSpec Inventory { get; init; } = null!;

    public CanonEntityKindSpec Scenario { get; init; } = null!;

    public CanonEntityKindSpec Lexicon { get; init; } = null!;

    public CanonEntityKindSpec Custom { get; init; } = null!;
}
