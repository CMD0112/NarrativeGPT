using System.Text.Json;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ConversationFileParserTests
{
    [Fact]
    public void ExtractFiles_finds_metadata_attachments()
    {
        const string json = """
            {
              "mapping": {
                "n1": {
                  "message": {
                    "id": "msg-1",
                    "author": { "role": "user" },
                    "metadata": {
                      "attachments": [
                        {
                          "id": "file-abc123",
                          "name": "notes.pdf",
                          "mime_type": "application/pdf",
                          "size": 42
                        }
                      ]
                    },
                    "content": {
                      "content_type": "text",
                      "parts": ["hello"]
                    }
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var files = ConversationFileParser.ExtractFiles(doc.RootElement);

        Assert.Single(files);
        Assert.Equal("file-abc123", files[0].FileId);
        Assert.Equal("notes.pdf", files[0].Name);
        Assert.Equal("user", files[0].AuthorRole);
    }

    [Fact]
    public void ExtractFiles_finds_asset_pointer_in_parts()
    {
        const string json = """
            {
              "mapping": {
                "n1": {
                  "message": {
                    "id": "msg-2",
                    "author": { "role": "user" },
                    "content": {
                      "content_type": "multimodal_text",
                      "parts": [
                        "see image",
                        {
                          "content_type": "image_asset_pointer",
                          "asset_pointer": "file-service://file-img-1",
                          "size_bytes": 100
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var files = ConversationFileParser.ExtractFiles(doc.RootElement);

        Assert.Single(files);
        Assert.Equal("file-img-1", files[0].FileId);
        Assert.Equal("file-service://file-img-1", files[0].AssetPointer);
    }

    [Fact]
    public void ExtractFileIdFromAssetPointer_parses_file_service_uri()
    {
        Assert.Equal(
            "file-xyz",
            ConversationFileParser.ExtractFileIdFromAssetPointer("file-service://file-xyz"));
    }

    [Fact]
    public void IsPlausibleFileId_accepts_file_prefix()
    {
        Assert.True(ConversationFileParser.IsPlausibleFileId("file-abc123"));
        Assert.False(ConversationFileParser.IsPlausibleFileId("hello"));
    }
}

public sealed class ChatGptConversationAttachmentSendTests
{
    [Fact]
    public void BuildSendBodyWithAttachments_uses_multimodal_text_shape()
    {
        var attachments = new[]
        {
            new ChatAttachmentRef
            {
                FileId = "file-1",
                FileName = "map.png",
                MimeType = "image/png",
                SizeBytes = 512,
                Width = 640,
                Height = 480,
            },
        };

        var (body, messageId) = ChatGptConversationSendService.BuildSendBodyWithAttachmentsInternal(
            "conv-1",
            "parent-1",
            "g-p-test",
            "Describe this map",
            attachments);

        Assert.False(string.IsNullOrWhiteSpace(messageId));
        var json = JsonSerializer.Serialize(body);
        Assert.Contains("multimodal_text", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file-service://file-1", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"width\":640", json, StringComparison.Ordinal);
        Assert.Contains("\"height\":480", json, StringComparison.Ordinal);
        Assert.Contains("attachments", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image_asset_pointer", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MergeAttachmentSendBodyFromTemplate_converts_text_message_to_multimodal()
    {
        const string template = """
            {
              "action": "next",
              "model": "gpt-5-5-thinking",
              "client_prepare_state": "sent",
              "supports_buffering": true,
              "supported_encodings": ["v1"],
              "messages": [{
                "author": { "role": "user" },
                "content": { "content_type": "text", "parts": ["old"] },
                "metadata": {
                  "selected_sources": [],
                  "serialization_metadata": { "custom_symbol_offsets": [] }
                }
              }]
            }
            """;

        using var doc = JsonDocument.Parse(template);
        var attachments = new[]
        {
            new ChatAttachmentRef
            {
                FileId = "file-img",
                FileName = "photo.png",
                MimeType = "image/png",
                SizeBytes = 100,
                Width = 1024,
                Height = 768,
            },
        };

        var (body, messageId) = ChatGptConversationSendService.MergeAttachmentSendBodyFromTemplate(
            doc.RootElement,
            "conv-1",
            "parent-1",
            "g-p-test",
            "look at this",
            attachments);

        Assert.False(string.IsNullOrWhiteSpace(messageId));
        Assert.Equal("gpt-5-5-thinking", body["model"]?.ToString());

        var json = JsonSerializer.Serialize(body);
        Assert.Contains("multimodal_text", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("look at this", json, StringComparison.Ordinal);
        Assert.Contains("file-service://file-img", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"content_type\":\"text\"", json, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ImageAttachmentDimensionsTests
{
    [Fact]
    public void TryParse_reads_png_ihdr_dimensions()
    {
        var png = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x02, 0x80,
            0x00, 0x00, 0x01, 0xE0,
        };

        var dims = ImageAttachmentDimensions.TryParse(png, "image/png");

        Assert.NotNull(dims);
        Assert.Equal(640, dims!.Value.Width);
        Assert.Equal(480, dims.Value.Height);
    }
}

public sealed class ChatGptChatFileServiceProbeTests
{
    [Fact]
    public void ParseComposerFileUiProbe_reads_inputs_and_buttons()
    {
        const string json = """
            {
              "href": "https://chatgpt.com/c/abc",
              "fileInputs": [
                { "accept": "image/*", "multiple": true, "hidden": true, "testId": "file-input" }
              ],
              "attachButtons": [
                { "selector": "button[data-testid*=attach]", "ariaLabel": "Attach", "text": "Attach" }
              ]
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var probe = ChatGptChatFileService.ParseComposerFileUiProbe(doc.RootElement);

        Assert.True(probe.Success);
        Assert.Single(probe.FileInputs);
        Assert.Equal("image/*", probe.FileInputs[0].Accept);
        Assert.Single(probe.AttachButtons);
        Assert.Equal("Attach", probe.AttachButtons[0].AriaLabel);
    }
}
