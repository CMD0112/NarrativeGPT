namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonFieldGroup
{
    public const string Identity = "identity";

    public const string Story = "story";

    public const string Capabilities = "capabilities";

    public const string Relations = "relations";

    public const string Custom = "custom";

    public static string DisplayLabel(string groupId) => groupId switch
    {
        Identity => "Identity",
        Story => "Story",
        Capabilities => "Capabilities",
        Relations => "Relations",
        Custom => "Custom fields",
        _ => groupId,
    };
}
