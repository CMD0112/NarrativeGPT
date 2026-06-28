using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayWrapperComposerPolicyTests
{
    [Fact]
    public void Armed_play_thread_requires_wrapper_composer()
    {
        var bundle = LinkedBundle("g-p-wrap", "conv-play");
        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-wrap");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));

        Assert.True(PlayWrapperComposerPolicy.ShouldUseWrapperComposer(caps));
    }

    [Fact]
    public void Project_landing_does_not_use_wrapper_composer()
    {
        var bundle = LinkedBundle("g-p-wrap", "conv-play");
        var source = ChatGptUrls.BuildProjectUrl("g-p-wrap");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));

        Assert.False(PlayWrapperComposerPolicy.ShouldUseWrapperComposer(caps));
    }

    [Fact]
    public void Play_rotation_draft_on_project_page_uses_wrapper_composer()
    {
        var bundle = LinkedBundle("g-p-rotate", conversationId: null);
        ProjectChatDraftService.BeginPlayDraft(bundle);

        try
        {
            var source = ChatGptUrls.BuildProjectUrl("g-p-rotate");
            var caps = PlayTabCapabilityResolver.Resolve(
                PlayTabCapabilityContext.FromUrl(
                    bundle,
                    source,
                    draftKind: ProjectChatDraftKind.Play),
                PlayTabSessionFactory.FromBundle(bundle));

            Assert.True(PlayWrapperComposerPolicy.ShouldUseWrapperComposer(caps));
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }

    private static AdventureBundle LinkedBundle(string projectId, string? conversationId)
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = projectId,
                LinkedConversationId = conversationId,
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "pin-tab-1";
        return bundle;
    }
}

[Trait("Category", "Unit")]
public sealed class PlaySendPreflightTests
{
    [Fact]
    public void Blocks_send_when_capabilities_disarm()
    {
        var bundle = LinkedBundle("g-p-preflight", "conv-play");
        var source = ChatGptUrls.BuildProjectUrl("g-p-preflight");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));
        var store = new PreparedSendArtifactStore();

        var result = PlaySendPreflight.Evaluate(caps, store);

        Assert.False(result.CanProceed);
        Assert.Equal(PlayDisarmReason.ProjectLanding.ToString(), result.ReasonCode);
    }

    [Fact]
    public void Blocks_send_when_artifact_is_stale()
    {
        var bundle = LinkedBundle("g-p-stale-send", "conv-play");
        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-stale-send");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));
        var store = new PreparedSendArtifactStore();
        store.Bind(bundle);
        store.Set(PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = "hello",
            ResolvePlayerLine = (_, _, text) => text ?? "",
        }));

        bundle.Metadata.Settings.MaxPacketChars--;

        var result = PlaySendPreflight.Evaluate(caps, store);

        Assert.False(result.CanProceed);
        Assert.Equal("stale_preview", result.ReasonCode);
    }

    [Fact]
    public void Allows_send_when_armed_and_artifact_fresh()
    {
        var bundle = LinkedBundle("g-p-ok", "conv-play");
        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-ok");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));
        var store = new PreparedSendArtifactStore();
        store.Bind(bundle);
        store.Set(PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = "hello",
            ResolvePlayerLine = (_, _, text) => text ?? "",
        }));

        var result = PlaySendPreflight.Evaluate(caps, store);

        Assert.True(result.CanProceed);
    }

    [Fact]
    public void Allows_send_when_no_artifact_cached_yet()
    {
        var bundle = LinkedBundle("g-p-miss", "conv-play");
        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-miss");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));

        var result = PlaySendPreflight.Evaluate(caps, new PreparedSendArtifactStore());

        Assert.True(result.CanProceed);
    }

    private static AdventureBundle LinkedBundle(string projectId, string? conversationId)
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = projectId,
                LinkedConversationId = conversationId,
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "pin-tab-1";
        return bundle;
    }
}
