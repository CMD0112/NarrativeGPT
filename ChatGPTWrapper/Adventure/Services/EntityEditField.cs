namespace ChatGPTWrapper.Adventure.Services;

using ChatGPTWrapper.Adventure.Services.Canon;

public sealed class EntityEditField
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public string Value { get; set; } = "";

    public bool Multiline { get; init; }

    public int Order { get; init; }

    public string GroupId { get; init; } = CanonFieldGroup.Story;

    public int DisplayOrder { get; init; }
}
