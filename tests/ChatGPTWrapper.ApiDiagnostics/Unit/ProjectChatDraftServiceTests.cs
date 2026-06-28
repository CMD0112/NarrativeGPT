using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProjectChatDraftServiceTests
{
    [Fact]
    public void ShouldNavigateToPlayTarget_false_on_project_page_when_draft_active_with_stored_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-draft",
                LinkedConversationId = "conv-play",
            },
        };

        ProjectChatDraftService.BeginUtilityDraft(bundle);

        try
        {
            var source = ChatGptUrls.BuildProjectUrl("g-p-draft");
            var target = AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!;

            Assert.False(AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, target));
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }

    [Fact]
    public void ShouldNavigateToDesignTarget_false_on_project_page_when_draft_active()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Draft design");
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

        ProjectChatDraftService.BeginDesignDraft(bundle);

        try
        {
            var source = ChatGptUrls.BuildProjectUrl("g-p-design");
            var target = AdventureNavigationService.ResolveDesignBrowseUrl(bundle)!;

            Assert.False(AdventureNavigationService.ShouldNavigateToDesignTarget(source, bundle, target));
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }

    [Fact]
    public void Cancel_play_draft_restores_prior_binding()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-restore",
                LinkedConversationId = "conv-old",
                PinnedPlayTabKey = "tab-1",
                PinnedPlayTabTitle = "Play",
            },
        };

        ProjectChatDraftService.BeginPlayDraft(bundle);
        bundle.Metadata.LinkedConversationId = null;
        bundle.Metadata.PinnedPlayTabKey = null;

        ProjectChatDraftService.Cancel(bundle);

        Assert.Equal("conv-old", PlayThreadBindingService.GetActiveConversationId(bundle));
        Assert.Equal(
            "tab-1",
            AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)?.PinnedTabKey);
        Assert.False(ProjectChatDraftService.IsActive(bundle));
    }

    [Fact]
    public void ShouldSuppressPlayAutomation_on_project_page_when_stored_play_thread_without_draft()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-util",
                LinkedConversationId = "conv-play",
            },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-util");

        Assert.True(ProjectChatDraftService.ShouldSuppressPlayAutomation(bundle, null, null, source));
    }

    [Fact]
    public void ShouldSuppressPlayAutomation_false_on_bound_play_thread_conversation()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-play",
                LinkedConversationId = "conv-play",
            },
        };

        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-play");

        Assert.False(ProjectChatDraftService.ShouldSuppressPlayAutomation(bundle, null, null, source));
    }

    [Fact]
    public void ShouldSuppressPlayAutomation_false_on_project_page_during_play_rotation()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-rotate",
            },
        };

        ProjectChatDraftService.BeginPlayDraft(bundle);

        try
        {
            var source = ChatGptUrls.BuildProjectUrl("g-p-rotate");

            Assert.False(ProjectChatDraftService.ShouldSuppressPlayAutomation(bundle, null, null, source));
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }

    [Fact]
    public void ShouldSuppressPlayAutomation_on_project_page_during_utility_draft()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-util",
                LinkedConversationId = "conv-play",
            },
        };

        ProjectChatDraftService.BeginUtilityDraft(bundle);

        try
        {
            var source = ChatGptUrls.BuildProjectUrl("g-p-util");

            Assert.True(ProjectChatDraftService.ShouldSuppressPlayAutomation(bundle, null, null, source));
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }

    [Fact]
    public void IsOnValidAdventureWebTarget_true_on_project_page_during_utility_draft()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-valid",
                LinkedConversationId = "conv-play",
            },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-valid");

        Assert.True(
            AdventureNavigationService.IsOnValidAdventureWebTarget(
                source,
                bundle,
                AdventureNavigationIntent.Play));
    }

    [Fact]
    public void TryAutoBeginOnProjectPage_skips_when_play_mode_active()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-auto",
                LinkedConversationId = "conv-play",
            },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-auto");

        Assert.False(ProjectChatDraftService.TryAutoBeginOnProjectPage(bundle, source, playModeActive: true));
        Assert.False(ProjectChatDraftService.IsActive(bundle));
    }

    [Fact]
    public void TryAutoBeginOnProjectPage_enters_draft_when_stored_play_thread_on_project_page()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-auto",
                LinkedConversationId = "conv-play",
            },
        };

        try
        {
            var source = ChatGptUrls.BuildProjectUrl("g-p-auto");

            Assert.True(ProjectChatDraftService.TryAutoBeginOnProjectPage(bundle, source));
            Assert.True(ProjectChatDraftService.IsActive(bundle));
            Assert.Equal(ProjectChatDraftKind.Utility, ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id));
            Assert.False(
                AdventureNavigationService.ShouldNavigateToPlayTarget(
                    source,
                    bundle,
                    AdventureNavigationService.ResolvePlayBrowseUrl(bundle)!));
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }

    [Fact]
    public void TryAutoBeginOnProjectPage_no_op_without_stored_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-empty",
            },
        };

        var source = ChatGptUrls.BuildProjectUrl("g-p-empty");

        Assert.False(ProjectChatDraftService.TryAutoBeginOnProjectPage(bundle, source));
        Assert.False(ProjectChatDraftService.IsActive(bundle));
    }
}
