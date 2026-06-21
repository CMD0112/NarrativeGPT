namespace ChatGPTWrapper.Adventure.Services.Canon;

internal sealed class CanonEntityKindSpec
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

    public IReadOnlyList<CanonFieldSpec> Fields { get; init; } = [];

    public IReadOnlyList<CanonFieldSpec> ShellFields =>
        Fields.Where(f => f.Role == CanonFieldRole.Shell).ToList();

    public IReadOnlyList<CanonFieldSpec> BodyFields =>
        Fields.Where(f => f.Role != CanonFieldRole.Shell).ToList();

    public IReadOnlyList<CanonFieldSpec> EditorFields =>
        Fields.Where(f => f.Role == CanonFieldRole.Extra).ToList();
}
