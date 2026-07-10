using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class UtilityJobAttachmentStagingTests
{
    [Fact]
    public void Stage_and_load_round_trips_attachment_bytes()
    {
        var adventureId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var payloads = new List<DomAttachmentPayload>
        {
            new()
            {
                Name = "notes.md",
                MimeType = "text/markdown",
                Content = "lore snippet"u8.ToArray(),
            },
        };

        var staged = UtilityJobAttachmentStaging.Stage(adventureId, runId, payloads);
        Assert.Single(staged);
        Assert.Equal("notes.md", staged[0].Name);

        var loaded = UtilityJobAttachmentStaging.LoadDomPayloads(adventureId, staged);
        Assert.Single(loaded);
        Assert.Equal("text/markdown", loaded[0].MimeType);
        Assert.Equal("lore snippet", System.Text.Encoding.UTF8.GetString(loaded[0].Content));

        var context = UtilityJobAttachmentStaging.ToAttachmentContext(adventureId, staged);
        Assert.True(context.HasAttachments);
        Assert.Equal("notes.md", context.Attachments[0].Name);

        UtilityJobAttachmentStaging.Cleanup(adventureId, runId);
        Assert.False(Directory.Exists(UtilityJobAttachmentStaging.StagingDirectory(adventureId, runId)));
    }

    [Fact]
    public void LoadFromPaths_reads_local_files()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cgw-attach-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "hello");
        try
        {
            var loaded = UtilityJobAttachmentStaging.LoadFromPaths([path]);
            Assert.Single(loaded);
            Assert.Equal("text/plain", loaded[0].MimeType);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(loaded[0].Content));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
