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



    public PacketProfile Profile { get; init; } = PacketProfile.InlineFallback;



    public PacketDelegationMode DelegationMode { get; init; } = PacketDelegationMode.InlineFallback;



    public AttachmentSendMode AttachmentSendMode { get; init; } = AttachmentSendMode.TextOnly;



    public IReadOnlyList<InjectionSection> Sections { get; init; } = [];



    public IReadOnlyList<TrimmedSection> Trimmed { get; init; } = [];

    public bool HasUtilityInjection { get; init; }

    public int UtilitySectionCount { get; init; }

    public IReadOnlyList<ContextPointer> BaselinePointers { get; init; } = [];

    public IReadOnlyList<ContextPointer> ThisTurnPointers { get; init; } = [];
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

        int priorThreadUserMessageCount = 0,

        int? packetTurnIndexOverride = null,

        bool userChoseInlineFallback = false)

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

        var packetTurnIndex = packetTurnIndexOverride

            ?? PlayTurnScopeService.ResolveNextPacketTurnIndex(bundle, priorThreadUserMessageCount);

        var profile = PacketProfileResolver.Resolve(bundle, userChoseInlineFallback);

        var ctx = PromptPacketBuilder.BuildContext(

            bundle, searchHint, contextMode, attachment, packetTurnIndex,

            userChoseInlineFallback: userChoseInlineFallback);

        var packet = PromptPacketBuilder.Build(

            bundle, trimmedUser, contextMode, attachment, packetTurnIndex,

            userChoseInlineFallback: userChoseInlineFallback);



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



        var playSnapshot = PlayPacketContextSnapshotBuilder.Build(ctx.ContextText, merged);

        var utilitySections = PlayUtilityInjectionService.BuildAndDrainUtilitySections(
            bundle,
            playSnapshot: playSnapshot);

        if (utilitySections.Count > 0)

            merged = PlayUtilityInjectionService.PrependUtilitySections(merged, utilitySections);



        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        var trimmed = new List<TrimmedSection>(packet.BudgetTrimmed);

        if (packet.WasTrimmed)

            trimmed.Add(new TrimmedSection("packet", "tail truncated (MaxPacketChars)"));



        var sections = InjectionSectionManifestBuilder.BuildSections(

            bundle, merged, ctx.ContextText, packet.Profile, readiness);



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

            Profile = packet.Profile,

            DelegationMode = InjectionSectionManifestBuilder.ResolveDelegationMode(readiness, profile),

            AttachmentSendMode = mode,

            Sections = sections,

            Trimmed = trimmed,

            HasUtilityInjection = utilitySections.Count > 0,

            UtilitySectionCount = utilitySections.Count,

            BaselinePointers = packet.BaselinePointers,

            ThisTurnPointers = packet.ThisTurnPointers,

        };

    }



    /// <summary>

    /// Uses a pre-built packet as-is (e.g. start packet pasted after "Start new play thread").

    /// </summary>

    public static PromptInjectionPrepareResult PreparePrebuiltPacket(string packetText)

    {

        var trimmed = packetText.Trim();

        var profile = trimmed.Contains("mode=\"delegated\"", StringComparison.OrdinalIgnoreCase)

                      || trimmed.Contains("mode=\"thin\"", StringComparison.OrdinalIgnoreCase)

            ? PacketProfile.SourceDelegated

            : trimmed.Contains("mode=\"minimal\"", StringComparison.OrdinalIgnoreCase)

                ? PacketProfile.MinimalLocal

                : PacketProfile.InlineFallback;



        return new PromptInjectionPrepareResult

        {

            MergedText = trimmed,

            UserText = trimmed,

            ContextText = "",

            Hash = PromptPacketBuilder.ComputeHash(trimmed),

            Mode = PacketProfileResolver.ToPacketMode(profile),

            Profile = profile,

        };

    }

}


