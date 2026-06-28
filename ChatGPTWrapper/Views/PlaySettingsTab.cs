namespace ChatGPTWrapper.Views;

public enum PlaySettingsTab
{
    Injection,
    NextSend,
    World,
    Session,
    AiTools,

    /// <summary>Legacy alias — use <see cref="AiTools"/>.</summary>
    AiActions = AiTools,
    PlaySurface,
    Settings,
    Sources,
    MemoryCards,
}
