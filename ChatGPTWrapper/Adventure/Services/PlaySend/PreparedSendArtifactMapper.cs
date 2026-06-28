namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal static class PreparedSendArtifactMapper
{
    public static PromptInjectionPrepareResult ToPrepareResult(PreparedSendArtifact artifact) =>
        new()
        {
            MergedText = artifact.MergedText,
            Hash = artifact.Hash,
            WasTrimmed = artifact.WasTrimmed,
            UserText = artifact.PlayerLine,
            ContextText = "",
        };
}
