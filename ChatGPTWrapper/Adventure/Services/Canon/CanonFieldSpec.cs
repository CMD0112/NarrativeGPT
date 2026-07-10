namespace ChatGPTWrapper.Adventure.Services.Canon;

internal sealed class CanonFieldSpec
{
    public required string Label { get; init; }

    public required string JsonKey { get; init; }

    public CanonFieldFormat Format { get; init; } = CanonFieldFormat.PlainLine;

    public int? PositionalIndex { get; init; }

    public bool Multiline { get; init; }

    public CanonFieldControlType ControlType { get; init; } = CanonFieldControlType.Text;

    public CanonFieldRole Role { get; init; } = CanonFieldRole.Extra;

    public string FieldGroup { get; init; } = CanonFieldGroup.Story;

    public bool IsTypedProperty { get; init; } = true;

    public IReadOnlyList<string> AlternateLabels { get; init; } = [];
}
