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
            ContextText = artifact.ContextText,
            Profile = artifact.Profile,
            DelegationMode = artifact.DelegationMode,
            AttachmentSendMode = artifact.AttachmentSendMode,
            Sections = artifact.Sections.ToList(),
            Trimmed = artifact.Trimmed.ToList(),
            HasUtilityInjection = artifact.HasUtilityInjection,
            UtilitySectionCount = artifact.UtilitySectionCount,
            BaselinePointers = artifact.BaselinePointers.ToList(),
            ThisTurnPointers = artifact.ThisTurnPointers.ToList(),
        };
}
