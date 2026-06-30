using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityReferenceAttachmentPolicyTests
{
    [Fact]
    public void CanEmbed_json_and_markdown()
    {
        var attachments = new[]
        {
            new DomAttachmentPayload
            {
                Name = "entities.json",
                MimeType = "application/json",
                Content = """{"entities":[]}"""u8.ToArray(),
            },
            new DomAttachmentPayload
            {
                Name = "notes.md",
                MimeType = "text/markdown",
                Content = "# Lore"u8.ToArray(),
            },
        };

        Assert.True(UtilityReferenceAttachmentPolicy.CanEmbedInPacket(attachments, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void CanEmbed_false_for_png()
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

        Assert.False(UtilityReferenceAttachmentPolicy.CanEmbedInPacket(attachments, out var error));
        Assert.Equal("utility_reference_files_must_be_text", error);
    }

    [Fact]
    public void CanEmbed_false_for_oversized_text()
    {
        var attachments = new[]
        {
            new DomAttachmentPayload
            {
                Name = "huge.txt",
                MimeType = "text/plain",
                Content = new byte[UtilityReferenceAttachmentPolicy.MaxEmbedBytesPerFile + 1],
            },
        };

        Assert.False(UtilityReferenceAttachmentPolicy.CanEmbedInPacket(attachments, out var error));
        Assert.Equal("utility_reference_files_too_large", error);
    }

    [Fact]
    public void EmbedInPacket_appends_file_sections()
    {
        var body = "=== JOB ===\nDo work.";
        var result = UtilityReferenceAttachmentPolicy.EmbedInPacket(
            body,
            [
                new DomAttachmentPayload
                {
                    Name = "entities.json",
                    MimeType = "application/json",
                    Content = """{"id":"x"}"""u8.ToArray(),
                },
            ]);

        Assert.Contains("=== FILE: entities.json ===", result);
        Assert.Contains(""""{"id":"x"}"""", result);
        Assert.DoesNotContain("composer uploads", result);
    }
}
