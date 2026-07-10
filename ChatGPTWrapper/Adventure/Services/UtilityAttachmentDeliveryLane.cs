namespace ChatGPTWrapper.Adventure.Services;

/// <summary>How utility worker reference files are delivered to ChatGPT.</summary>
internal enum UtilityAttachmentDeliveryLane
{
    None,
    PacketEmbed,
    DomComposer,
    Mixed,
    AttachWorker,
}
