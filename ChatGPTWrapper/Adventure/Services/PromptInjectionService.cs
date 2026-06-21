using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class PromptInjectionPrepareResult
{
    public required string MergedText { get; init; }

    public required string UserText { get; init; }

    public required string ContextText { get; init; }

    public required string Hash { get; init; }

    public bool WasTrimmed { get; init; }

    public List<string> TriggeredCardNames { get; init; } = [];

    public List<string> ResolvedSectionPointers { get; init; } = [];

    public PacketMode Mode { get; init; } = PacketMode.Fat;
}

internal static class PromptInjectionService
{
    public static string BuildInvalidationMarker(string domTurnId) =>
        $"[[cgw:invalidation turn=\"{domTurnId}\"]]";

    public static string StripInvalidationMarkers(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? text
            : System.Text.RegularExpressions.Regex.Replace(
                text,
                @"\[\[cgw:invalidation[^\]]*\]\]\s*",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static PromptInjectionPrepareResult PrepareSend(
        AdventureBundle bundle,
        string userText,
        AttachmentContext? attachment = null,
        int priorThreadUserMessageCount = 0)
    {
        var mode = AttachmentSendPolicy.Classify(userText, attachment);
        var trimmedUser = userText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUser) && attachment?.HasAttachments == true)
            trimmedUser = AttachmentSendPolicy.ResolveDisplayPlayerLine(bundle, trimmedUser, attachment);

        var searchHint = trimmedUser;
        var fileTokens = AttachmentSendPolicy.FilenameSearchTokens(attachment);
        if (!string.IsNullOrWhiteSpace(fileTokens))
            searchHint = $"{searchHint} {fileTokens}".Trim();

        var contextMode = AttachmentSendPolicy.ResolveContextMode(bundle, mode);
        var packetTurnIndex = PlayTurnScopeService.ResolveNextPacketTurnIndex(bundle, priorThreadUserMessageCount);
        var ctx = PromptPacketBuilder.BuildContext(bundle, searchHint, contextMode, attachment, packetTurnIndex);
        var packet = PromptPacketBuilder.Build(bundle, trimmedUser, contextMode, attachment, packetTurnIndex);

        var merged = packet.Text;
        if (bundle.Metadata.Settings.InjectAttachmentGuidance)
        {
            var guidance = AttachmentSendPolicy.BuildAttachmentGuidance(mode);
            if (!string.IsNullOrWhiteSpace(guidance))
                merged = guidance + Environment.NewLine + Environment.NewLine + merged;
        }

        var manifest = AttachmentSendPolicy.BuildAttachmentManifestSection(attachment);
        if (!string.IsNullOrWhiteSpace(manifest))
            merged = manifest + Environment.NewLine + Environment.NewLine + merged;

        merged = NarratorOverrideResolver.AppendOverrideBlocks(bundle, merged);
        merged = CanonReconciliationService.AppendNotifyBlock(bundle, merged);

        return new PromptInjectionPrepareResult
        {
            MergedText = merged,
            UserText = trimmedUser,
            ContextText = ctx.ContextText,
            Hash = packet.Hash,
            WasTrimmed = packet.WasTrimmed,
            TriggeredCardNames = packet.TriggeredCardNames,
            ResolvedSectionPointers = packet.ResolvedSectionPointers,
            Mode = packet.Mode,
        };
    }

    /// <summary>
    /// Uses a pre-built packet as-is (e.g. start packet pasted after "Start new play thread").
    /// </summary>
    public static PromptInjectionPrepareResult PreparePrebuiltPacket(string packetText)
    {
        var trimmed = packetText.Trim();
        return new PromptInjectionPrepareResult
        {
            MergedText = trimmed,
            UserText = trimmed,
            ContextText = "",
            Hash = PromptPacketBuilder.ComputeHash(trimmed),
            Mode = trimmed.Contains("mode=\"thin\"", StringComparison.OrdinalIgnoreCase)
                ? PacketMode.Thin
                : PacketMode.Fat,
        };
    }
}
