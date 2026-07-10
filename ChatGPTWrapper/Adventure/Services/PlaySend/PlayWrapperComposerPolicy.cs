namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// When the host owns play draft input (wrapper composer mandatory).
/// </summary>
internal static class PlayWrapperComposerPolicy
{
    public static bool ShouldUseWrapperComposer(PlayTabCapabilities capabilities) =>
        capabilities.AcceptPlayDraft && !capabilities.AllowNativeComposerInput;
}
