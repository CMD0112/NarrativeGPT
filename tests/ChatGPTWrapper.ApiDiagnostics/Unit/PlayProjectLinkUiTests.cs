using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayProjectLinkUiTests
{
    [Fact]
    public void ShouldShowLinkProjectBanner_true_when_no_project_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };

        Assert.True(AdventureProjectBindingService.ShouldShowLinkProjectBanner(bundle));
        Assert.False(AdventureProjectBindingService.HasLinkedProject(bundle));
    }

    [Fact]
    public void ShouldShowLinkProjectBanner_false_after_linked_project_id_persisted()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
            },
        };
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);

        Assert.False(AdventureProjectBindingService.ShouldShowLinkProjectBanner(bundle));
        Assert.True(AdventureProjectBindingService.HasLinkedProject(bundle));
    }

    [Fact]
    public void ShouldShowLinkProjectBanner_false_when_only_project_link_record_exists()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                ProjectLink = new ProjectLink
                {
                    GizmoId = "g-p-from-record",
                    CanonicalUrl = "https://chatgpt.com/g/g-p-from-record/project",
                },
            },
        };
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);

        Assert.False(AdventureProjectBindingService.ShouldShowLinkProjectBanner(bundle));
        Assert.Equal("g-p-from-record", AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata));
    }

    [Fact]
    public void ShouldDeferLinkedPlayContext_when_project_linked_without_pin_or_turns()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
            },
        };

        Assert.True(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
    }

    [Fact]
    public void ShouldDeferLinkedPlayContext_false_when_play_tab_pinned()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
                PinnedPlayTabKey = "tab-1",
            },
        };

        Assert.False(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
    }

    [Fact]
    public void ShouldProvisionPlayThreadOnLink_only_when_requested_and_not_designing()
    {
        Assert.False(AdventureProjectBindingService.ShouldProvisionPlayThreadOnLink(false, AdventureStatus.Active));
        Assert.True(AdventureProjectBindingService.ShouldProvisionPlayThreadOnLink(true, AdventureStatus.Active));
        Assert.False(AdventureProjectBindingService.ShouldProvisionPlayThreadOnLink(true, AdventureStatus.Designing));
    }

    [Fact]
    public void PostLink_without_pin_hides_banner_and_defers_play_context()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
            },
        };

        Assert.True(AdventureProjectBindingService.HasLinkedProject(bundle));
        Assert.False(AdventureProjectBindingService.ShouldShowLinkProjectBanner(bundle));
        Assert.True(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
    }

    [Fact]
    public void PostLink_with_pin_does_not_defer_play_context_prep()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
                PinnedPlayTabKey = "tab-1",
                LinkedConversationId = "conv-1",
            },
        };

        Assert.True(AdventureProjectBindingService.HasLinkedProject(bundle));
        Assert.False(AdventureProjectBindingService.ShouldShowLinkProjectBanner(bundle));
        Assert.False(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
    }

    [Fact]
    public void ShouldDeferLinkedPlayContext_false_when_adventure_has_accepted_play_turns()
    {
        var bundle = AdventureStore.CreateNew("Played");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        var session = AdventureSessionService.EnsureSession(bundle);
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            SessionId = session.Id,
            Status = TurnStatus.Accepted,
            PlayerText = "look around",
            NarratorText = "You see a hallway.",
        });

        Assert.False(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
    }

    [Fact]
    public void ClearProjectRemoteState_does_not_throw_when_source_manifest_or_entries_null()
    {
        var bundle = AdventureStore.CreateNew("Switch test");
        bundle.Metadata.LinkedProjectId = "g-p-old";
        bundle.SourceManifest = null!;
        AdventureProjectBindingService.ClearProjectRemoteState(bundle, "g-p-old");

        Assert.NotNull(bundle.SourceManifest);
        Assert.Empty(bundle.SourceManifest.Entries);
    }

    [Fact]
    public void ClearProjectRemoteState_clears_null_manifest_entries()
    {
        var bundle = AdventureStore.CreateNew("Switch test");
        bundle.Metadata.LinkedProjectId = "g-p-old";
        bundle.SourceManifest.Entries = null!;
        AdventureProjectBindingService.ClearProjectRemoteState(bundle, "g-p-old");

        Assert.NotNull(bundle.SourceManifest.Entries);
        Assert.Empty(bundle.SourceManifest.Entries);
    }

    [Fact]
    public void BuildProjectInstructions_does_not_throw_when_settings_collections_null()
    {
        var bundle = AdventureStore.CreateNew("Instructions");
        bundle.Metadata.Settings.ContentBoundaries = null!;
        bundle.Metadata.Settings.CharacterPortrayalRules = null!;

        var instructions = AdventureProjectBindingService.BuildProjectInstructions(bundle);

        Assert.Contains("narrator", instructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndSession_does_not_throw_when_session_narrator_overrides_null()
    {
        var bundle = AdventureStore.CreateNew("Session");
        bundle.Metadata.Settings.SessionNarratorOverrides = null!;
        AdventureSessionService.EnsureSession(bundle);

        var ex = Record.Exception(() => AdventureSessionService.EndSession(bundle));

        Assert.Null(ex);
    }

    [Fact]
    public void Evaluate_does_not_throw_when_settings_and_manifest_collections_null()
    {
        var bundle = AdventureStore.CreateNew("Evaluate nulls");
        bundle.Metadata.Settings = null!;
        bundle.SourceManifest.Entries = null!;

        var ex = Record.Exception(() => ProjectSourceInjectionService.Evaluate(bundle));

        Assert.Null(ex);
    }

    [Fact]
    public void BuildInstructionDomainCanonical_does_not_throw_when_settings_collections_null()
    {
        var bundle = AdventureStore.CreateNew("Canonical");
        bundle.Metadata.Settings.ContentBoundaries = null!;
        bundle.Metadata.Settings.CharacterPortrayalRules = null!;

        var ex = Record.Exception(() => InstructionContractService.BuildInstructionDomainCanonical(bundle));

        Assert.Null(ex);
    }

    [Fact]
    public void GetLinkedProjectId_used_for_switch_detects_project_link_record()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                ProjectLink = new ProjectLink { GizmoId = "g-p-from-record" },
            },
        };

        Assert.Equal("g-p-from-record", AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata));
    }
}
