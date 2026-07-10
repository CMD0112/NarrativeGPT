using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityEphemeralAttachmentSendServiceTests
{
    [Fact]
    public void TryPrepare_returns_null_when_no_attachments()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var entry = CreateEntry();
        var context = new GenerationJobContext();

        var prepared = UtilityEphemeralAttachmentSendService.TryPrepare(bundle, entry, context);

        Assert.Null(prepared);
    }

    [Fact]
    public void TryPrepare_packet_embed_for_json_attachment()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var entry = CreateEntry();
        var staged = StageJson(bundle.Metadata.Id, entry.RunId);
        var context = new GenerationJobContext
        {
            UserPrompt = "ping",
            JobAttachments = UtilityJobAttachmentStaging.ToAttachmentContext(bundle.Metadata.Id, staged),
        };
        entry.Attachments = staged.ToList();

        var prepared = UtilityEphemeralAttachmentSendService.TryPrepare(bundle, entry, context);

        Assert.NotNull(prepared);
        Assert.Equal(UtilityAttachmentDeliveryLane.PacketEmbed, prepared!.Lane);
        Assert.Null(prepared.DomRequired);
        Assert.Contains("utility_worker_ping", prepared.Wrapped, StringComparison.Ordinal);

        UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
    }

    [Fact]
    public void TryPrepare_dom_composer_when_force_dom_attach_enabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.ForceUtilityWorkerDomAttach = true;
        var entry = CreateEntry();
        var staged = StageJson(bundle.Metadata.Id, entry.RunId);
        var context = new GenerationJobContext
        {
            UserPrompt = "ping",
            JobAttachments = UtilityJobAttachmentStaging.ToAttachmentContext(bundle.Metadata.Id, staged),
        };
        entry.Attachments = staged.ToList();

        var prepared = UtilityEphemeralAttachmentSendService.TryPrepare(bundle, entry, context);

        Assert.NotNull(prepared);
        Assert.Equal(UtilityAttachmentDeliveryLane.DomComposer, prepared!.Lane);
        Assert.NotNull(prepared.DomRequired);
        Assert.Single(prepared.DomRequired!);
        Assert.True(prepared.ForceDomAttach);
        Assert.DoesNotContain("entities.json", prepared.Wrapped, StringComparison.Ordinal);

        UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
    }

    [Fact]
    public void RequiresDomHost_true_when_dom_required_present()
    {
        var packet = new UtilityEphemeralAttachmentSendService.PreparedPacket(
            "wrapped",
            "hash",
            UtilityAttachmentDeliveryLane.DomComposer,
            [new DomAttachmentPayload { Name = "a.png", MimeType = "image/png", Content = [1, 2, 3] }],
            ForceDomAttach: false);

        Assert.True(UtilityEphemeralAttachmentSendService.RequiresDomHost(packet));
    }

    [Fact]
    public void TryPrepare_skips_dom_attach_for_source_file_io_jobs()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.ForceUtilityWorkerDomAttach = true;
        var entry = CreateEntry(GenerationJobId.ExtractEntities);
        var staged = StageJson(bundle.Metadata.Id, entry.RunId);
        var context = new GenerationJobContext
        {
            JobAttachments = UtilityJobAttachmentStaging.ToAttachmentContext(bundle.Metadata.Id, staged),
        };
        entry.Attachments = staged.ToList();

        var prepared = UtilityEphemeralAttachmentSendService.TryPrepare(bundle, entry, context);

        Assert.Null(prepared);

        UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
    }

    private static UtilityOutboxEntry CreateEntry(string jobId = GenerationJobId.UtilityWorkerPing) =>
        new()
        {
            RunId = Guid.NewGuid(),
            JobId = jobId,
            Channel = UtilityExecutionChannel.ManualBackground,
            State = UtilityJobRunState.Queued,
            QueuedAt = DateTimeOffset.UtcNow,
        };

    private static IReadOnlyList<UtilityOutboxAttachment> StageJson(Guid adventureId, Guid runId)
    {
        var json = """{"entities":[{"id":"e1","name":"Hero"}]}""";
        var payloads = new List<DomAttachmentPayload>
        {
            new()
            {
                Name = "entities.json",
                MimeType = "application/json",
                Content = System.Text.Encoding.UTF8.GetBytes(json),
            },
        };
        return UtilityJobAttachmentStaging.Stage(adventureId, runId, payloads);
    }
}
