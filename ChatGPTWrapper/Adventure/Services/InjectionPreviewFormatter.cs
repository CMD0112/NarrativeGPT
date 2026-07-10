using ChatGPTWrapper.Adventure.Models;



namespace ChatGPTWrapper.Adventure.Services;



internal static class InjectionPreviewFormatter

{

    public static string FormatMetaLine(

        AdventureBundle bundle,

        PromptInjectionPrepareResult prepared,

        ProjectSourceReadiness readiness,

        AttachmentContext? attachment = null)

    {

        var delegationLabel = PacketProfileResolver.DisplayLabel(prepared.Profile, readiness);



        var pointerLabel = "Sections";

        var pointers = prepared.ResolvedSectionPointers.Count > 0

            ? prepared.ResolvedSectionPointers

            : prepared.TriggeredCardNames;

        var pointerText = pointers.Count > 0 ? string.Join(", ", pointers) : "none";



        var attachMode = bundle.Metadata.Settings.AttachmentContextMode;

        var attachNote = attachment is { HasAttachments: true }

            ? $" | Attachments: {attachment.Attachments.Count} ({attachMode}, send={prepared.AttachmentSendMode})"

            : "";



        var nextTurn = PlayTurnScopeService.GetNextPacketTurnIndex(bundle);

        var scopedAccepted = PlayTurnScopeService.GetPacketAcceptedTurns(bundle).Count;

        var metaAttrs = ContextTagFormat.ExtractTagAttributes(prepared.MergedText, "meta");

        var metaTurn = metaAttrs.TryGetValue("turn", out var turnAttr) && !string.IsNullOrWhiteSpace(turnAttr)

            ? turnAttr

            : nextTurn.ToString();

        var metaMode = metaAttrs.TryGetValue("mode", out var modeAttr) && !string.IsNullOrWhiteSpace(modeAttr)

            ? modeAttr

            : PacketProfileResolver.ProfileMetaMode(prepared.Profile);



        return

            $"Turn: {metaTurn} (scoped accepted: {scopedAccepted}) | Meta mode: {metaMode}\n" +

            $"Packet: {prepared.Profile} ({delegationLabel}) | Chars: {prepared.MergedText.Length} | Hash: {prepared.Hash} | Trimmed: {prepared.WasTrimmed}{attachNote}\n" +

            $"Project: {bundle.Metadata.LinkedProjectId ?? "none"} | {pointerLabel}: {pointerText}";

    }



    public static string FormatManifestSummary(PromptInjectionPrepareResult prepared)

    {

        var sectionSummary = InjectionSectionManifestBuilder.FormatSectionSummary(prepared.Sections);

        var trimmedSummary = InjectionSectionManifestBuilder.FormatTrimmedSummary(prepared.Trimmed);

        return string.Join("\n",

            new[] { sectionSummary, trimmedSummary }.Where(s => !string.IsNullOrWhiteSpace(s)));

    }



    public static string ResolvePreviewPlayerLine(

        AdventureBundle bundle,

        Func<string?>? resolveComposerText,

        string fallbackPlayerLine)

    {

        var compose = resolveComposerText?.Invoke()?.Trim();

        if (!string.IsNullOrWhiteSpace(compose))

            return compose;



        var fallback = fallbackPlayerLine.Trim();

        if (!string.IsNullOrWhiteSpace(fallback))

            return fallback;



        if (AdventureBootstrapService.IsFreshAdventure(bundle)

            && bundle.Metadata.Settings.OfferStartOnPlay)

        {

            return AdventureBootstrapService.BuildStartPlayerDirective(bundle);

        }



        return "";

    }

}


