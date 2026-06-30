using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>CMD-424: packet preparation and lane routing for ephemeral utility attachments.</summary>
internal static class UtilityEphemeralAttachmentSendService
{
    internal sealed record PreparedPacket(
        string Wrapped,
        string PromptHash,
        UtilityAttachmentDeliveryLane Lane,
        IReadOnlyList<DomAttachmentPayload>? DomRequired,
        bool ForceDomAttach);

    public static PreparedPacket? TryPrepare(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context)
    {
        if (!LocalUtilityInferencePolicy.HasStagedWorkerAttachments(context, entry))
            return null;

        if (!context.UtilityContextAssembled)
        {
            UtilityMessagePushService.ApplyLegacyReferenceFirstDefaults(
                bundle,
                context,
                context.StoryContextHasTranscript);
        }

        var domAttachments = UtilityJobAttachmentStaging.LoadDomPayloads(bundle.Metadata.Id, entry.Attachments);
        var forceDomAttach = UtilityEphemeralWorkerPolicy.ForceDomAttach(bundle);
        var deliveryLane = forceDomAttach && domAttachments is { Count: > 0 }
            ? UtilityAttachmentDeliveryLane.DomComposer
            : UtilityAttachmentDeliveryClassifier.ResolveLane(domAttachments);
        var useDomLane = deliveryLane is UtilityAttachmentDeliveryLane.DomComposer
            or UtilityAttachmentDeliveryLane.Mixed;

        if (forceDomAttach && domAttachments is { Count: > 0 })
        {
            ProjectLinkDiagnostics.Log(
                $"ephemeral_attach_force_dom_lane files={domAttachments.Count}");
        }

        var jobBody = UtilityJobPromptBuilder.BuildCoreJobBody(bundle, entry.JobId, context);
        jobBody = UtilityJobPacketAttachmentEnricher.Append(
            bundle,
            jobBody,
            context.JobAttachments,
            context.AttachmentReferenceNote,
            deliveryLane);

        IReadOnlyList<DomAttachmentPayload>? domRequired = null;

        if (forceDomAttach && domAttachments is { Count: > 0 })
        {
            domRequired = domAttachments;
        }
        else if (domAttachments is { Count: > 0 } && !useDomLane)
        {
            if (!UtilityReferenceAttachmentPolicy.CanEmbedInPacket(domAttachments, out _))
                return null;

            jobBody = UtilityReferenceAttachmentPolicy.EmbedInPacket(jobBody, domAttachments);
        }
        else if (useDomLane)
        {
            UtilityAttachmentDeliveryClassifier.Partition(domAttachments, out var embeddable, out var required);
            if (embeddable.Count > 0)
                jobBody = UtilityReferenceAttachmentPolicy.EmbedInPacket(jobBody, embeddable);

            if (required.Count == 0)
                return null;

            domRequired = required;
        }

        jobBody = UtilityResponseSchemaRegistry.AppendResponseContract(jobBody, entry.JobId);
        var wrapped = ContextTagFormat.WrapUtilityJob(entry.JobId, jobBody, "worker", entry.RunId);
        return new PreparedPacket(
            wrapped,
            UtilityMessagePushService.ComputeHash(wrapped),
            deliveryLane,
            domRequired,
            forceDomAttach);
    }

    public static bool RequiresDomHost(PreparedPacket packet) =>
        packet.DomRequired is { Count: > 0 };

    public static string FormatAttachStatus(UtilityOutboxEntry entry, PreparedPacket packet)
    {
        if (packet.DomRequired is { Count: > 0 } files)
        {
            var forced = packet.ForceDomAttach ? "forced DOM " : "";
            return $"{entry.JobId}: ephemeral {forced}attach ({files.Count} file{(files.Count == 1 ? "" : "s")})…";
        }

        return $"{entry.JobId}: ephemeral embed attach…";
    }
}
