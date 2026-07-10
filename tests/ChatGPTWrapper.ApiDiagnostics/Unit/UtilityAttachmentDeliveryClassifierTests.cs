using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityAttachmentDeliveryClassifierTests
{
    [Fact]
    public void ShouldUseDomAttach_false_for_json_only()
    {
        var attachments = new[]
        {
            new DomAttachmentPayload
            {
                Name = "entities.json",
                MimeType = "application/json",
                Content = """{"entities":[]}"""u8.ToArray(),
            },
        };

        Assert.False(UtilityAttachmentDeliveryClassifier.ShouldUseDomAttachLane(attachments));
        Assert.Equal(UtilityAttachmentDeliveryLane.PacketEmbed, UtilityAttachmentDeliveryClassifier.ResolveLane(attachments));
    }

    [Fact]
    public void ShouldUseDomAttach_true_for_png()
    {
        var attachments = new[]
        {
            new DomAttachmentPayload
            {
                Name = "map.png",
                MimeType = "image/png",
                Content = [0x89, 0x50, 0x4E, 0x47],
            },
        };

        Assert.True(UtilityAttachmentDeliveryClassifier.ShouldUseDomAttachLane(attachments));
    }

    [Fact]
    public void Partition_splits_mixed_attachments()
    {
        var json = new DomAttachmentPayload
        {
            Name = "entities.json",
            MimeType = "application/json",
            Content = """{"id":"x"}"""u8.ToArray(),
        };
        var png = new DomAttachmentPayload
        {
            Name = "map.png",
            MimeType = "image/png",
            Content = [0x89, 0x50, 0x4E, 0x47],
        };

        UtilityAttachmentDeliveryClassifier.Partition([json, png], out var embeddable, out var domRequired);

        Assert.Single(embeddable);
        Assert.Equal("entities.json", embeddable[0].Name);
        Assert.Single(domRequired);
        Assert.Equal("map.png", domRequired[0].Name);
    }
}
