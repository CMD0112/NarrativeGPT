using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Builds injected packets for repairing a play turn via ChatGPT user-message edit
/// (e.g. when native send bypassed wrapper injection).
/// </summary>
internal static class PlaySendRepairService
{
    public const string CopyForEditRepairButton = "Copy packet for edit repair";

    public const string NextSendRepairHelpText =
        PlayPacketPanelCopy.RepairHint;

    public static string FormatRepairCopiedMessage(int repairTurnIndex) =>
        $"Repair packet for turn {repairTurnIndex} copied.\n\n"
        + "1. In the pinned Play tab, open the last user message (⋯ → Edit).\n"
        + "2. Select all, paste (Ctrl+V), and save/send.\n"
        + "3. If the model already replied, the invalidation marker tells it to disregard that reply.";

    /// <summary>
    /// 1-based turn index of the user message being repaired in the live thread.
    /// </summary>
    public static int ResolveRepairTurnIndex(AdventureBundle bundle, int threadUserMessageCount)
    {
        if (threadUserMessageCount > 0)
            return threadUserMessageCount;

        var logged = PlayTurnScopeService.GetPacketContextTurns(bundle).Count;
        return Math.Max(1, logged);
    }

    public static PromptInjectionPrepareResult PrepareRepairPacket(
        AdventureBundle bundle,
        string playerLine,
        int repairTurnIndex,
        AttachmentContext? attachment = null)
    {
        var priorCount = Math.Max(0, repairTurnIndex - 1);
        return PromptInjectionService.PrepareSend(
            bundle,
            playerLine,
            attachment,
            priorCount,
            repairTurnIndex);
    }

    public static string AssembleRepairClipboardText(string mergedPacket, int repairTurnIndex)
    {
        var marker = PromptInjectionService.BuildInvalidationMarker(repairTurnIndex.ToString());
        return string.IsNullOrWhiteSpace(mergedPacket)
            ? marker
            : marker + Environment.NewLine + Environment.NewLine + mergedPacket;
    }
}
