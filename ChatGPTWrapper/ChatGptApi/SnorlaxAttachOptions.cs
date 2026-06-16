namespace ChatGPTWrapper.ChatGptApi;

public sealed record SnorlaxAttachOptions(
    bool SkipOwnershipVerify = false,
    bool SkipPostAttachSidebar = false)
{
    public static SnorlaxAttachOptions Default { get; } = new();
}
