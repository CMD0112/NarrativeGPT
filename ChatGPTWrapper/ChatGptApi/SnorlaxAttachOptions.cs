namespace ChatGPTWrapper.ChatGptApi;

public sealed record SnorlaxAttachOptions(
    bool SkipOwnershipVerify = false,
    bool SkipPostAttachSidebar = false)
{
    public static SnorlaxAttachOptions Default { get; } = new();

    /// <summary>Publication bind: project-files attach only; strict integrity verify runs later.</summary>
    public static SnorlaxAttachOptions Publication { get; } = new(
        SkipOwnershipVerify: true,
        SkipPostAttachSidebar: true);
}
