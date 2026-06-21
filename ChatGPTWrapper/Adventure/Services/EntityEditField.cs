namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityEditField
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public string Value { get; set; } = "";

    public bool Multiline { get; init; }

    public int Order { get; init; }
}
