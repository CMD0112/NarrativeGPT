using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class PlayThreadRegistrySendTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void GetActiveConversationId_survives_schema6_save_without_legacy_field()
    {
        var bundle = AdventureStore.CreateNew("Registry send");
        bundle.Metadata.LinkedProjectId = "g-p-registry";
        PlayThreadBindingService.MarkVerified(bundle, "thread-registry");
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Null(reloaded.Metadata.LinkedConversationId);
        Assert.Equal("thread-registry", PlayThreadBindingService.GetActiveConversationId(reloaded));
    }

    [Fact]
    public void PrepareSend_uses_registry_conversation_for_prebuilt_detection()
    {
        var bundle = AdventureStore.CreateNew("Prebuilt gate");
        bundle.Metadata.LinkedProjectId = "g-p-prebuilt";
        PlayThreadBindingService.MarkVerified(bundle, "thread-1");
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var injectedLine = "[[cgw:meta mode=\"thin\" turn=\"1\"]]\n\nBegin";
        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = reloaded,
                ComposeText = injectedLine,
                ApplySurfaceActions = false,
            },
            (_, _, text) => text ?? "");

        Assert.False(session.UsePrebuiltPacket);
        Assert.NotEqual(injectedLine, session.Prepared.MergedText);
    }
}
