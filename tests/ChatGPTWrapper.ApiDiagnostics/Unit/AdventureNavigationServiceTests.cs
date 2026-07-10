using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
[Trait("Category", "Unit")]
public sealed class AdventureNavigationServiceTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void ResolveTrustedFallbackUrl_uses_project_page_not_homepage_when_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-nav" },
        };

        Assert.Equal(
            ChatGptUrls.BuildProjectUrl("g-p-nav"),
            AdventureNavigationService.ResolveTrustedFallbackUrl(bundle));
    }

    [Fact]
    public void ResolveTrustedFallbackUrl_uses_play_conversation_when_available()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
            },
        };
        PlayThreadBindingService.MarkVerified(bundle, "conv-play");

        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-nav"),
            AdventureNavigationService.ResolveTrustedFallbackUrl(bundle));
    }

    [Fact]
    public void ResolveDesignBrowseUrl_prefers_design_thread_when_session_exists()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design nav");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.BindActiveConversation(bundle, AdventureThreadKind.Design, "design-conv");

        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("design-conv", "g-p-design"),
            AdventureNavigationService.ResolveDesignBrowseUrl(bundle));
    }

    [Fact]
    public void ShouldNavigateToPlayTarget_true_from_homepage_when_project_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
            },
        };
        PlayThreadBindingService.MarkVerified(bundle, "conv-1");

        var target = AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!;

        Assert.True(AdventureNavigationService.ShouldNavigateToPlayTarget("https://chatgpt.com/", bundle, target));
    }

    [Fact]
    public void ShouldNavigateToPlayTarget_false_when_already_on_play_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
            },
        };
        PlayThreadBindingService.MarkVerified(bundle, "conv-1");

        var source = ChatGptUrls.BuildProjectConversationUrl("conv-1", "g-p-nav");
        var target = AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!;

        Assert.False(AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, target));
    }

    [Fact]
    public void ShouldNavigateToPlayTarget_false_on_plain_conversation_url_when_thread_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
            },
        };
        PlayThreadBindingService.MarkVerified(bundle, "conv-1");

        var source = ChatGptUrls.BuildConversationUrl("conv-1");
        var target = AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!;

        Assert.False(AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, target));
    }

    [Fact]
    public void IsOnPlayTarget_false_on_other_project_conversation_url()
    {
        const string convId = "conv-1";
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                LinkedConversationId = convId,
            },
        };

        var otherProjectUrl = ChatGptUrls.BuildProjectConversationUrl(convId, "g-p-other");

        Assert.False(PlayTabPinService.IsOnPlayTarget(otherProjectUrl, bundle));
    }

    [Fact]
    public void IsOnPlayTarget_true_on_plain_conversation_url_when_thread_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                LinkedConversationId = "conv-1",
            },
        };

        Assert.True(PlayTabPinService.IsOnPlayTarget(
            ChatGptUrls.BuildConversationUrl("conv-1"),
            bundle));
    }

    [Fact]
    public void ShouldNavigateToPlayTarget_false_on_project_page_when_no_conversation_yet()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-nav" },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-nav");
        var target = AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!;

        Assert.False(AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, target));
    }

    [Fact]
    public void ShouldNavigateToDesignTarget_true_from_homepage_when_design_session_exists()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design target");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        bundle.Metadata.UtilitySessions = new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase)
        {
            [GenerationJobId.DesignAdventure] = new GenerationUtilitySession
            {
                ConversationId = "design-1",
                Sequence = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
            },
        };

        var target = AdventureNavigationService.ResolveDesignBrowseUrl(bundle)!;

        Assert.True(
            AdventureNavigationService.ShouldNavigateToDesignTarget("https://chatgpt.com/", bundle, target));
    }

    [Fact]
    public void ShouldNavigateToPlayTarget_false_on_project_page_when_stored_play_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
            },
        };
        PlayThreadBindingService.MarkVerified(bundle, "conv-1");

        var source = ChatGptUrls.BuildProjectUrl("g-p-nav");
        var target = AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!;

        Assert.False(AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, target));
    }

    [Fact]
    public void ShouldNavigateToDesignTarget_false_on_project_page_when_design_session_exists()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design project page");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        bundle.Metadata.UtilitySessions = new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase)
        {
            [GenerationJobId.DesignAdventure] = new GenerationUtilitySession
            {
                ConversationId = "design-1",
                Sequence = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
            },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-design");
        var target = AdventureNavigationService.ResolveDesignBrowseUrl(bundle)!;

        Assert.False(AdventureNavigationService.ShouldNavigateToDesignTarget(source, bundle, target));
    }

    [Fact]
    public void ShouldNavigateToDesignTarget_false_on_project_page_when_no_design_thread_yet()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design browse only");
        bundle.Metadata.LinkedProjectId = "g-p-design";

        var source = ChatGptUrls.BuildProjectUrl("g-p-design");
        var target = AdventureNavigationService.ResolveDesignBrowseUrl(bundle)!;

        Assert.False(AdventureNavigationService.ShouldNavigateToDesignTarget(source, bundle, target));
    }

    [Fact]
    public void GetDesignTargetUrl_uses_project_link_when_linked_project_id_unset()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Legacy project link");
        bundle.Metadata.LinkedProjectId = null;
        bundle.Metadata.ProjectLink = new ProjectLink
        {
            GizmoId = "g-p-legacy",
            CanonicalUrl = ChatGptUrls.BuildProjectUrl("g-p-legacy"),
        };
        AdventureThreadRegistryService.BindActiveConversation(bundle, AdventureThreadKind.Design, "design-legacy");

        AdventureNavigationService.SyncLinkedFields(bundle);
        var url = DesignTabPinService.GetDesignTargetUrl(bundle);

        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("design-legacy", "g-p-legacy"),
            url);
    }

    [Fact]
    public void HasLinkedProject_true_when_only_project_link_record_exists()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                ProjectLink = new ProjectLink
                {
                    GizmoId = "g-p-legacy",
                    CanonicalUrl = ChatGptUrls.BuildProjectUrl("g-p-legacy"),
                },
            },
        };

        AdventureNavigationService.SyncLinkedFields(bundle);

        Assert.True(AdventureNavigationService.HasLinkedProject(bundle));
        Assert.Equal("g-p-legacy", bundle.Metadata.LinkedProjectId);
    }

    [Fact]
    public void IsGenericHomepage_false_for_project_query_entry()
    {
        Assert.False(AdventureNavigationService.IsGenericHomepage(
            "https://chatgpt.com/?project=g-p-test"));
        Assert.True(AdventureNavigationService.IsGenericHomepage("https://chatgpt.com/"));
    }

    [Fact]
    public void DescribeNavigationState_detects_homepage_and_play_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                LinkedConversationId = "conv-1",
            },
        };

        Assert.Equal(
            "homepage",
            AdventureNavigationService.DescribeNavigationState(
                "https://chatgpt.com/",
                bundle,
                AdventureNavigationIntent.Play));

        Assert.Equal(
            "play thread",
            AdventureNavigationService.DescribeNavigationState(
                ChatGptUrls.BuildProjectConversationUrl("conv-1", "g-p-nav"),
                bundle,
                AdventureNavigationIntent.Play));
    }

    [Fact]
    public void RequiresHomepageRecovery_true_on_homepage_when_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-nav" },
        };

        Assert.True(AdventureNavigationService.RequiresHomepageRecovery("https://chatgpt.com/", bundle));
    }

    [Fact]
    public void RequiresHomepageRecovery_false_on_project_page()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-nav" },
        };

        Assert.False(
            AdventureNavigationService.RequiresHomepageRecovery(
                ChatGptUrls.BuildProjectUrl("g-p-nav"),
                bundle));
    }

    [Fact]
    public void RequiresHomepageRecovery_false_without_linked_project()
    {
        var bundle = new AdventureBundle { Metadata = new AdventureMetadata() };

        Assert.False(AdventureNavigationService.RequiresHomepageRecovery("https://chatgpt.com/", bundle));
    }

    [Fact]
    public void RequiresNavigationRecovery_true_for_homepage_with_wrong_project_query()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-mine" },
        };

        Assert.True(
            AdventureNavigationService.RequiresNavigationRecovery(
                "https://chatgpt.com/?project=g-p-other",
                bundle));
    }

    [Fact]
    public void RequiresNavigationRecovery_false_for_homepage_with_linked_project_query()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-mine" },
        };

        Assert.False(
            AdventureNavigationService.RequiresNavigationRecovery(
                "https://chatgpt.com/?project=g-p-mine",
                bundle));
    }

    [Fact]
    public void IsOnValidAdventureWebTarget_true_on_linked_project_page_when_no_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-nav" },
        };

        Assert.True(
            AdventureNavigationService.IsOnValidAdventureWebTarget(
                ChatGptUrls.BuildProjectUrl("g-p-nav"),
                bundle,
                AdventureNavigationIntent.Play));
    }

    [Fact]
    public void IsOnValidAdventureWebTarget_true_on_project_page_when_play_thread_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                LinkedConversationId = "conv-1",
            },
        };

        Assert.True(
            AdventureNavigationService.IsOnValidAdventureWebTarget(
                ChatGptUrls.BuildProjectUrl("g-p-nav"),
                bundle,
                AdventureNavigationIntent.Play));
    }

    [Fact]
    public void ResolveRecoveryUrl_returns_play_thread_when_linked_with_accepted_turns()
    {
        var bundle = AdventureStore.CreateNew("Recovery play");
        bundle.Metadata.LinkedProjectId = "g-p-nav";
        PlayThreadBindingService.MarkVerified(bundle, "conv-1");
        var session = AdventureSessionService.EnsureSession(bundle);
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            SessionId = session.Id,
            ConversationId = "conv-1",
            Status = TurnStatus.Accepted,
            PlayerText = "look around",
            NarratorText = "You see a hallway.",
        });

        Assert.False(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("conv-1", "g-p-nav"),
            AdventureNavigationService.ResolveRecoveryUrl(bundle, AdventureNavigationIntent.Play));
    }

    [Fact]
    public void ResolveRecoveryUrl_returns_project_page_for_play_when_no_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
            },
        };

        Assert.Equal(
            ChatGptUrls.BuildProjectUrl("g-p-nav"),
            AdventureNavigationService.ResolveRecoveryUrl(bundle, AdventureNavigationIntent.Play));
    }

    [Fact]
    public void ResolveRecoveryUrl_returns_project_page_when_play_context_deferred_despite_thread_id()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
            },
        };
        PlayThreadBindingService.MarkPendingPin(bundle, "bootstrap-conv");

        Assert.True(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
        Assert.Equal(
            ChatGptUrls.BuildProjectUrl("g-p-nav"),
            AdventureNavigationService.ResolveRecoveryUrl(bundle, AdventureNavigationIntent.Play));
    }

    [Fact]
    public void ResolveRecoveryUrl_returns_play_thread_when_pinned_and_not_deferred()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                PinnedPlayTabKey = "tab-1",
            },
        };
        PlayThreadBindingService.MarkVerified(bundle, "conv-1");

        Assert.False(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("conv-1", "g-p-nav"),
            AdventureNavigationService.ResolveRecoveryUrl(bundle, AdventureNavigationIntent.Play));
    }

    [Fact]
    public void ResolveRecoveryUrl_returns_play_thread_when_pinned_pending_pin_not_verified()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                PinnedPlayTabKey = "tab-1",
            },
        };
        PlayThreadBindingService.MarkPendingPin(bundle, "conv-pending");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play).PinnedTabUrl =
            ChatGptUrls.BuildProjectConversationUrl("conv-pending", "g-p-nav");

        Assert.False(PlayThreadBindingService.IsVerified(bundle));
        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("conv-pending", "g-p-nav"),
            AdventureNavigationService.ResolveRecoveryUrl(bundle, AdventureNavigationIntent.Play));
    }

    [Fact]
    public void ResolvePlayBrowseUrl_returns_conversation_when_pinned_pending_pin()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                PinnedPlayTabKey = "tab-1",
            },
        };
        PlayThreadBindingService.MarkPendingPin(bundle, "conv-pending");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play).PinnedTabUrl =
            ChatGptUrls.BuildProjectConversationUrl("conv-pending", "g-p-nav");

        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("conv-pending", "g-p-nav"),
            AdventureNavigationService.ResolvePlayBrowseUrl(bundle));
    }

    [Fact]
    public void ShouldNavigateToPlayTarget_false_on_conversation_when_pinned_pending_pin()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-nav",
                PinnedPlayTabKey = "tab-1",
            },
        };
        PlayThreadBindingService.MarkPendingPin(bundle, "conv-pending");

        var source = ChatGptUrls.BuildProjectConversationUrl("conv-pending", "g-p-nav");
        var target = AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!;

        Assert.False(AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, target));
    }

    [Fact]
    public void ResolveRecoveryUrl_returns_design_thread_when_design_session_exists()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Recovery design");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.BindActiveConversation(bundle, AdventureThreadKind.Design, "design-conv");

        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("design-conv", "g-p-design"),
            AdventureNavigationService.ResolveRecoveryUrl(bundle, AdventureNavigationIntent.Design));
    }
}
