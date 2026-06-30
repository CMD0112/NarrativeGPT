using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
[Trait("Category", "Unit")]
public sealed class DesignThreadPersistenceTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void GetDesignTargetUrl_builds_project_conversation_when_session_exists()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design URL test");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.ConversationId = "design-thread-1";

        var url = DesignTabPinService.GetDesignTargetUrl(bundle);

        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("design-thread-1", "g-p-design"),
            url);
    }

    [Fact]
    public void HasPersistedDesignSession_true_when_design_registry_entry_exists()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Persisted session");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.ConversationId = "conv-design";

        Assert.True(DesignTabPinService.HasPersistedDesignSession(bundle));
        Assert.Equal("conv-design", AdventureDesignContextService.GetDesignConversationId(bundle));
    }

    [Fact]
    public void FormatDesignThreadStatus_shows_conversation_when_ready()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Status test");
        bundle.Metadata.LinkedProjectId = "g-p-abc";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.ConversationId = "thread-xyz";

        var status = DesignTabPinService.FormatDesignThreadStatus(bundle);

        Assert.Contains("Design thread", status, StringComparison.Ordinal);
        Assert.Contains("thread-xyz", status, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDesignBrowseUrl_falls_back_to_project_page_when_no_thread()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Browse URL test");
        bundle.Metadata.LinkedProjectId = "g-p-design";

        var url = DesignTabPinService.GetDesignBrowseUrl(bundle);

        Assert.Equal(ChatGptUrls.BuildProjectUrl("g-p-design"), url);
    }

    [Fact]
    public void TryResolveDesignConversationFromSource_rejects_play_thread_not_browser_tab()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Thread guard");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        bundle.Metadata.LinkedConversationId = "play-thread-id";

        var playUrl = ChatGptUrls.BuildProjectConversationUrl("play-thread-id", "g-p-design");
        Assert.False(
            DesignTabPinService.TryResolveDesignConversationFromSource(bundle, playUrl, out _, out var playError));
        Assert.Equal("design_same_as_play_thread", playError);

        var designUrl = ChatGptUrls.BuildProjectConversationUrl("design-thread-id", "g-p-design");
        Assert.True(
            DesignTabPinService.TryResolveDesignConversationFromSource(bundle, designUrl, out var conversationId, out var designError));
        Assert.Null(designError);
        Assert.Equal("design-thread-id", conversationId);
    }

    [Fact]
    public void TryResolveDesignConversationFromSource_rejects_play_thread_from_registry_only()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Registry play guard");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        bundle.Metadata.LinkedConversationId = null;
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var playEntry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play);
        AdventureThreadRegistryService.UpdateConversationId(bundle, playEntry.Id, "play-from-registry");
        AdventureThreadRegistryService.SetActivePin(bundle, playEntry.Id, notifyPlayThreadChanged: false);

        var playUrl = ChatGptUrls.BuildProjectConversationUrl("play-from-registry", "g-p-design");
        Assert.False(
            DesignTabPinService.TryResolveDesignConversationFromSource(bundle, playUrl, out _, out var error));
        Assert.Equal("design_same_as_play_thread", error);
    }

    [Fact]
    public void FormatDesignThreadStatus_shows_pin_instructions_when_not_ready()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Pin hint");
        bundle.Metadata.LinkedProjectId = "g-p-abc";

        var status = DesignTabPinService.FormatDesignThreadStatus(bundle);

        Assert.Contains("design thread", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Threads", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PruneUnverifiedDesignSession_archives_session_not_in_project_list()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Prune stale");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        bundle.Metadata.UtilitySessions = new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase)
        {
            [GenerationJobId.DesignAdventure] = new GenerationUtilitySession
            {
                ConversationId = "bootstrap-stale-id",
                Sequence = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
            },
        };

        var conversations = new List<GizmoConversationRef>
        {
            new() { Id = "real-conv-1", Title = "[CGW:design] test" },
        };

        Assert.True(DesignTabPinService.PruneUnverifiedDesignSession(bundle, conversations));
        Assert.Null(AdventureDesignContextService.GetDesignConversationId(bundle));
    }

    [Fact]
    public void TryResolveDesignSessionFromPin_reads_conversation_from_pinned_url()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Pin resolve");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.PinnedTabUrl = ChatGptUrls.BuildProjectConversationUrl("pinned-conv", "g-p-test");

        var session = DesignTabPinService.TryResolveDesignSessionFromPin(bundle);

        Assert.NotNull(session);
        Assert.Equal("pinned-conv", session!.ConversationId);
    }

    [Fact]
    public void Save_preserves_design_tab_url_when_stale_bundle_overwrites()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design pin save");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.PinnedTabUrl = ChatGptUrls.BuildProjectConversationUrl("design-1", "g-p-test");
        AdventureStore.Save(bundle);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Metadata.LinkedProjectId = null;
        stale.Metadata.ThreadRegistry = [];
        stale.Metadata.ActiveThreadIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        stale.Metadata.Title = "Updated title";

        AdventureStore.Save(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("g-p-test", reloaded.Metadata.LinkedProjectId);
        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("design-1", "g-p-test"),
            AdventureThreadRegistryService.GetActiveEntry(reloaded, AdventureThreadKind.Design)!.PinnedTabUrl);
        Assert.Equal("Updated title", reloaded.Metadata.Title);
    }

    [Fact]
    public void Continue_design_skips_setup_wizard_when_past_setup_step()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Continue routing");
        AdventureDesignService.GoToStep(bundle, AdventureDesignStep.Concept);
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.DesignWorkspace.CurrentStep > AdventureDesignStep.Setup);
    }

    [Fact]
    public void ReleaseDesignThread_clears_pin_and_session_keeps_project()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Rotate design");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.PinnedTabKey = "design-tab-key";
        entry.PinnedTabUrl = ChatGptUrls.BuildProjectConversationUrl("design-old", "g-p-design");
        entry.ConversationId = "design-old";
        entry.DesignJobState = new DesignThreadJobState
        {
            Sequence = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow,
        };
        AdventureStore.Save(bundle);

        DesignThreadRotationService.ReleaseDesignThread(bundle);
        DesignThreadRotationService.PersistRelease(bundle);

        Assert.Null(AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)?.PinnedTabKey);
        Assert.Null(AdventureDesignContextService.GetDesignConversationId(bundle));
        Assert.Equal("g-p-design", bundle.Metadata.LinkedProjectId);
        Assert.Contains(
            bundle.Metadata.ThreadRegistry,
            e => e.Kind == AdventureThreadKind.Design
                 && e.Status == AdventureThreadStatus.Archived
                 && e.ConversationId == "design-old");

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Null(AdventureDesignContextService.GetDesignConversationId(reloaded));
        Assert.Equal("g-p-design", reloaded.Metadata.LinkedProjectId);
        Assert.Equal(
            ChatGptUrls.BuildProjectUrl("g-p-design"),
            DesignTabPinService.GetDesignBrowseUrl(reloaded));
    }

    [Fact]
    public void PinDesignTab_after_release_binds_new_design_conversation()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Rebind design");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.ConversationId = "design-old";

        DesignThreadRotationService.ReleaseDesignThread(bundle);
        Assert.Null(AdventureDesignContextService.GetDesignConversationId(bundle));

        var url = ChatGptUrls.BuildProjectConversationUrl("design-new", "g-p-design");
        Assert.True(
            DesignTabPinService.TryResolveDesignConversationFromSource(bundle, url, out var conversationId, out _));
        Assert.Equal("design-new", conversationId);
    }

    [Fact]
    public void BuildStartPacket_includes_utility_seed_and_general_design_brief()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Clipboard test");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureDesignService.GoToStep(bundle, AdventureDesignStep.Concept);
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Concept, "setting", "A fogbound harbor");

        var packet = DesignThreadRotationService.BuildStartPacket(bundle);

        Assert.Contains("[CGW:design]", packet, StringComparison.Ordinal);
        Assert.Contains("=== ADVENTURE DESIGN ===", packet, StringComparison.Ordinal);
        Assert.Contains("Clipboard test", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("ADVENTURE DESIGN — CONCEPT", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("fogbound harbor", packet, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseDesignThread_resets_step_seed_flags()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Reset seeds");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureDesignService.GetOrCreateStep(bundle, AdventureDesignStep.Concept).StepSeedSent = true;

        DesignThreadRotationService.ReleaseDesignThread(bundle);

        Assert.False(AdventureDesignService.GetOrCreateStep(bundle, AdventureDesignStep.Concept).StepSeedSent);
    }

    [Fact]
    public void FormatStartThreadReadyMessage_mentions_pin_step()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Ready message");
        bundle.Metadata.LinkedProjectId = "g-p-design";

        var message = DesignThreadRotationService.FormatStartThreadReadyMessage(
            ChatGptUrls.BuildProjectUrl("g-p-design"),
            bundle);

        Assert.Contains("New chat", message, StringComparison.Ordinal);
        Assert.Contains("Ctrl+V", message, StringComparison.Ordinal);
        Assert.Contains("Use this tab as design thread", message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetActivePin_syncs_design_utility_session_to_active_row()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Switch design");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        var first = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design, "Cast");
        first.ConversationId = "design-cast";
        first.DesignJobState = new DesignThreadJobState { Sequence = 2, JobCount = 1 };

        var second = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design, "Framework");
        second.ConversationId = "design-framework";
        second.DesignJobState = new DesignThreadJobState { Sequence = 5, JobCount = 3 };

        AdventureThreadRegistryService.SetActivePin(bundle, first.Id, notifyPlayThreadChanged: false);
        Assert.Equal("design-cast", GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure)!.ConversationId);
        Assert.Equal(2, GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure)!.Sequence);

        AdventureThreadRegistryService.SetActivePin(bundle, second.Id, notifyPlayThreadChanged: false);
        Assert.Equal("design-framework", GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure)!.ConversationId);
        Assert.Equal(5, GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure)!.Sequence);
    }

    [Fact]
    public void RemoveEntry_purges_stale_design_utility_session()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Remove archived");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        var archived = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        archived.Label = "Old";
        archived.ConversationId = "design-old";
        archived.Status = AdventureThreadStatus.Archived;

        var replacement = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design, "Current");
        replacement.ConversationId = "design-active";
        AdventureThreadRegistryService.SetActivePin(bundle, replacement.Id, notifyPlayThreadChanged: false);

        bundle.Metadata.UtilitySessions[GenerationJobId.DesignAdventure] = new GenerationUtilitySession
        {
            ConversationId = "design-old",
            Sequence = 1,
        };

        AdventureThreadRegistryService.RemoveEntry(bundle, archived.Id);

        Assert.DoesNotContain(bundle.Metadata.ThreadRegistry, e => e.Id == archived.Id);
        Assert.Equal("design-active", GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure)!.ConversationId);
    }

    [Fact]
    public void ReleaseDesignThread_clears_utility_session_on_fresh_slot()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Fresh slot");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.ConversationId = "design-old";
        bundle.Metadata.UtilitySessions[GenerationJobId.DesignAdventure] = new GenerationUtilitySession
        {
            ConversationId = "design-old",
            Sequence = 1,
        };

        DesignThreadRotationService.ReleaseDesignThread(bundle);

        Assert.Null(GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure));
        Assert.Null(AdventureDesignContextService.GetDesignConversationId(bundle));
    }

    [Fact]
    public void ShouldEnsureDesignThreadOnOpen_is_false_for_offline_first_entry()
    {
        Assert.False(AdventureDesignContextService.ShouldEnsureDesignThreadOnOpen);
    }

    [Fact]
    public void FormatDesignModeOpenStatus_reports_local_ready_with_stale_design_session()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Stale session open");
        bundle.Metadata.LinkedProjectId = "g-p-stale";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Design);
        entry.ConversationId = "deleted-thread";

        var status = AdventureDesignContextService.FormatDesignModeOpenStatus(bundle);

        Assert.Contains("Local sources ready", status, StringComparison.Ordinal);
        Assert.Contains("verify the design thread", status, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyLocalSourcesResumeStep_jumps_to_sources_when_lore_files_exist()
    {
        var bundle = AdventureStore.CreateNew("Resume sources", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);

            AdventureDesignContextService.ApplyLocalSourcesResumeStep(bundle);
            AdventureStore.Save(bundle);

            var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Equal(AdventureDesignStep.Sources, reloaded.DesignWorkspace.CurrentStep);
            Assert.True(AdventureSourceFileService.HasLocalLoreSourceFiles(reloaded));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void ApplyLocalSourcesEditEntry_jumps_to_sources_from_review()
    {
        var bundle = AdventureStore.CreateNew("Edit entry", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureDesignService.EnsureWorkspace(bundle);
            AdventureDesignService.GoToStep(bundle, AdventureDesignStep.Review);
            bundle.Metadata.Status = AdventureStatus.Active;
            AdventureStore.Save(bundle);

            AdventureDesignContextService.ApplyLocalSourcesEditEntry(bundle);
            AdventureStore.Save(bundle);

            var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Equal(AdventureDesignStep.Sources, reloaded.DesignWorkspace.CurrentStep);
            Assert.Equal(AdventureStatus.Active, reloaded.Metadata.Status);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void ApplyLocalSourcesResumeStep_leaves_review_unchanged()
    {
        var bundle = AdventureStore.CreateNew("Resume review", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureDesignService.EnsureWorkspace(bundle);
            AdventureDesignService.GoToStep(bundle, AdventureDesignStep.Review);
            AdventureStore.Save(bundle);

            AdventureDesignContextService.ApplyLocalSourcesResumeStep(bundle);

            Assert.Equal(AdventureDesignStep.Review, bundle.DesignWorkspace.CurrentStep);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void CanOpenLocalSourcesEdit_false_without_lore_files()
    {
        var bundle = AdventureStore.CreateNew("No sources");
        try
        {
            Assert.False(AdventureDesignContextService.CanOpenLocalSourcesEdit(bundle));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void FormatLocalSourcesEditStatus_mentions_play_available()
    {
        var bundle = AdventureStore.CreateNew("Edit status", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var status = AdventureDesignContextService.FormatLocalSourcesEditStatus(bundle);

            Assert.Contains("Editing local sources", status, StringComparison.Ordinal);
            Assert.Contains("Play mode", status, StringComparison.Ordinal);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void HasLocalLoreSourceFiles_true_when_scenario_md_exists()
    {
        var bundle = AdventureStore.CreateNew("Lore probe");
        try
        {
            Assert.False(AdventureSourceFileService.HasLocalLoreSourceFiles(bundle));

            AdventureTestData.WriteLocalSources(bundle);
            Assert.True(AdventureSourceFileService.HasLocalLoreSourceFiles(bundle));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void GenerationJobContext_preserves_design_step_for_extract_job()
    {
        var context = new GenerationJobContext
        {
            DesignStep = AdventureDesignStep.World,
            UserPrompt = "test",
        };

        var copied = new GenerationJobContext
        {
            UserPrompt = context.UserPrompt,
            DesignStep = context.DesignStep,
        };

        Assert.Equal(AdventureDesignStep.World, copied.DesignStep);
    }

    [Fact]
    public void HasLinkedProject_true_when_only_project_link_record_exists()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Legacy link only");
        bundle.Metadata.LinkedProjectId = null;
        bundle.Metadata.ProjectLink = new ProjectLink
        {
            GizmoId = "g-p-legacy-only",
            CanonicalUrl = ChatGptUrls.BuildProjectUrl("g-p-legacy-only"),
        };
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(AdventureProjectBindingService.HasLinkedProject(reloaded));
        Assert.True(AdventureDesignChatService.CanUseChat(reloaded));
    }

    [Fact]
    public void EnsureWorkspace_handles_null_design_workspace_steps()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Null steps");
        bundle.DesignWorkspace.Steps = null!;

        AdventureDesignService.EnsureWorkspace(bundle);
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Setup, "title", "Recovered");

        Assert.NotNull(bundle.DesignWorkspace.Steps);
        Assert.Equal("Recovered", AdventureDesignService.GetField(bundle, AdventureDesignStep.Setup, "title"));
    }

    [Fact]
    public void GetSession_handles_null_utility_sessions()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Null utility sessions");
        bundle.Metadata.UtilitySessions = null!;

        Assert.Null(GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure));
        Assert.NotNull(bundle.Metadata.UtilitySessions);
    }
}
