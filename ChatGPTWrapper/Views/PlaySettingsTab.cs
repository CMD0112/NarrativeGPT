namespace ChatGPTWrapper.Views;

public enum PlaySettingsTab
{
    Injection,
    NextSend,
    World,
    Session,
    UtilityJobs,

    /// <summary>Legacy alias — use <see cref="UtilityJobs"/>.</summary>
    AiTools = UtilityJobs,

    /// <summary>Legacy alias — use <see cref="UtilityJobs"/>.</summary>
    AiActions = UtilityJobs,
    PlaySurface,
    Settings,
    Sources,
    MemoryCards,
    History,
    Preview,
}
