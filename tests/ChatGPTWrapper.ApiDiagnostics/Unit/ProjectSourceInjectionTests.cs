using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(nameof(IsolatedAppRootCollection))]
public sealed class ProjectSourceInjectionTests
{
    private static AdventureBundle CreateLinkedBundle(
        bool inSync = true,
        bool forceFat = false,
        string? projectId = "g-p-test")
    {
        var entries = new List<SourceManifestEntry>
        {
            new() { RelativePath = "scenario.md", SyncState = SourceSyncState.InSync },
            new() { RelativePath = "world.md", SyncState = SourceSyncState.InSync },
            new() { RelativePath = "plot.md", SyncState = SourceSyncState.InSync },
            new() { RelativePath = "cast.md", SyncState = SourceSyncState.InSync },
        };

        if (!inSync)
            entries[0].SyncState = SourceSyncState.LocalNewer;

        return new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = projectId,
                Settings = new AdventureSettings
                {
                    ForceFatPackets = forceFat,
                    SourcePublishMode = SourcePublishMode.ApiSync,
                },
            },
            Scenario = new ScenarioDocument
            {
                Setting = "A haunted castle on the moor",
                PlayerRole = "Investigator",
                Genre = "Gothic horror",
                OpeningSituation = "Rain lashes the drawbridge.",
                PlotEssentials = "The lord vanished three nights ago.",
                WorldRules = "Magic is subtle and rare.",
            },
            SourceManifest = new SourceManifest { Entries = entries },
        };
    }

    [Fact]
    public void Evaluate_linked_and_in_sync_can_delegate()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.SourceManifest.RefreshSyncedFlag();

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        Assert.True(readiness.CanDelegateStaticContent);
        Assert.True(readiness.HasLinkedProject);
        Assert.True(readiness.AllSourcesInSync);
        Assert.Equal(4, readiness.SyncedFiles.Count);
        Assert.Null(readiness.BlockingReason);
    }

    [Fact]
    public void Evaluate_local_newer_blocks_delegation()
    {
        var bundle = CreateLinkedBundle(inSync: false);

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        Assert.False(readiness.CanDelegateStaticContent);
        Assert.Contains("out of sync", readiness.BlockingReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, readiness.OutOfSyncCount);
    }

    [Fact]
    public void Evaluate_no_project_blocks_delegation()
    {
        var bundle = CreateLinkedBundle(projectId: null);

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        Assert.False(readiness.CanDelegateStaticContent);
        Assert.False(readiness.HasLinkedProject);
        Assert.Contains("No ChatGPT Project linked", readiness.BlockingReason);
    }

    [Fact]
    public void Evaluate_force_fat_blocks_even_when_synced()
    {
        var bundle = CreateLinkedBundle(inSync: true, forceFat: true);

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        Assert.False(readiness.CanDelegateStaticContent);
        Assert.Contains("Force fat packets", readiness.BlockingReason);
    }

    [Fact]
    public void Evaluate_never_exported_when_manifest_empty()
    {
        var bundle = CreateLinkedBundle();
        bundle.SourceManifest.Entries.Clear();

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        Assert.False(readiness.CanDelegateStaticContent);
        Assert.Contains("never exported", readiness.BlockingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProjectSourcesSection_lists_synced_files_with_pointers()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseSectionInjection = false;
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        var section = ProjectSourceInjectionService.BuildProjectSourcesSection(bundle, readiness);

        Assert.Contains("=== PROJECT SOURCES", section);
        Assert.Contains("scenario.md — Setting, player role, genre, opening", section);
        Assert.Contains("world.md — World rules", section);
        Assert.Contains("Expected source file shapes", section);
        Assert.Contains("scenario.md: # Title", section);
        Assert.Contains(bundle.Metadata.LinkedProjectId!, section);
    }

    [Fact]
    public void Delegated_packet_contains_source_pointers_not_scenario_body()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        PopulateSectionManifest(bundle);
        bundle.SourceManifest.RefreshSyncedFlag();

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");

        Assert.Equal(PacketMode.Thin, ctx.Mode);
        Assert.Contains("scenario.md", ctx.ContextText);
        Assert.DoesNotContain("=== SCENARIO ===", ctx.ContextText);
        Assert.DoesNotContain("A haunted castle on the moor", ctx.ContextText);
        Assert.Contains("[[cgw:sources", ctx.ContextText);
        Assert.Contains("v=\"2\"", ctx.ContextText);
        Assert.DoesNotContain("Perspective:", ctx.ContextText);
    }

    [Fact]
    public void Delegated_packet_includes_transcript_tag_when_turns_exist()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.SourceManifest.RefreshSyncedFlag();
        bundle.Metadata.LinkedConversationId = "thread-1";
        AdventureSessionService.EnsureSession(bundle);
        var turn = TurnTimelineService.CreateTurn(bundle, "open the door");
        TurnTimelineService.AcceptTurn(turn, "The hinges groan.");
        PlayTurnScopeService.AssignConversation(turn, "thread-1");

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");

        Assert.Contains("[[cgw:transcript", ctx.ContextText);
        Assert.Contains("open the door", ctx.ContextText);
        Assert.Contains("hinges groan", ctx.ContextText);
    }

    [Fact]
    public void Fat_packet_contains_full_scenario_when_not_delegated()
    {
        var bundle = CreateLinkedBundle(inSync: false);
        bundle.Metadata.Settings.UseContextTags = false;
        bundle.Metadata.Settings.UseSectionInjection = false;

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");

        Assert.Equal(PacketMode.Fat, ctx.Mode);
        Assert.Contains("=== SCENARIO ===", ctx.ContextText);
        Assert.Contains("A haunted castle on the moor", ctx.ContextText);
    }

    [Fact]
    public void FormatLinkStatusSources_reflects_delegation_and_fallback()
    {
        var synced = ProjectSourceInjectionService.Evaluate(CreateLinkedBundle(inSync: true));
        Assert.Contains("source-delegated", ProjectSourceInjectionService.FormatLinkStatusSources(synced));

        var unsynced = ProjectSourceInjectionService.Evaluate(CreateLinkedBundle(inSync: false));
        Assert.Contains("fat fallback", ProjectSourceInjectionService.FormatLinkStatusSources(unsynced));
    }

    [Fact]
    public void FormatStructuredPreview_orders_sources_before_instructions()
    {
        var packet = ContextTagFormat.WrapBlock("instructions", "Narrator rules.")
                     + ContextTagFormat.WrapBlock("sources", "scenario.md — Setting");

        var preview = ContextTagFormat.FormatStructuredPreview(packet);

        Assert.True(preview.IndexOf("[sources]", StringComparison.Ordinal) <
                    preview.IndexOf("[instructions]", StringComparison.Ordinal));
    }

    // CMD-72 AC4 — api sync blocked path
    [Fact]
    public void Fat_packet_surfaces_blocking_reason_in_sources_when_baseline_empty()
    {
        var bundle = CreateLinkedBundle(inSync: false);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");

        Assert.Equal(PacketMode.Fat, ctx.Mode);
        var sources = ContextTagFormat.ExtractBlock(ctx.ContextText, "sources");
        Assert.NotNull(sources);
        Assert.Contains("ALWAYS RETRIEVE", sources);
        Assert.Contains("Sources not ready:", sources);
        Assert.Contains("out of sync", sources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open source sync", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("- (none)", sources);
    }

    // CMD-72 AC3 — synced-file fallback when baseline index empty
    [Fact]
    public void Delegated_packet_surfaces_synced_file_fallback_when_baseline_empty()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.SourceManifest.RefreshSyncedFlag();

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");

        Assert.Equal(PacketMode.Thin, ctx.Mode);
        var sources = ContextTagFormat.ExtractBlock(ctx.ContextText, "sources");
        Assert.NotNull(sources);
        Assert.Contains("section index empty", sources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scenario.md", sources);
        Assert.DoesNotContain("- (none)", sources);
        Assert.DoesNotContain("Sources not ready:", sources);
    }

    [Fact]
    public void Ready_adventure_turn1_always_retrieve_lists_baseline_pointers_not_none_AC3()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        PopulateSectionManifest(bundle);
        bundle.SourceManifest.RefreshSyncedFlag();

        var ctx = PromptPacketBuilder.BuildContext(bundle, "Begin");

        Assert.Equal(PacketMode.Thin, ctx.Mode);
        var sources = ContextTagFormat.ExtractBlock(ctx.ContextText, "sources");
        Assert.NotNull(sources);
        Assert.Contains("ALWAYS RETRIEVE", sources);
        Assert.Contains("id: opening", sources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: rules", sources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: player", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("- (none)", sources);
        Assert.DoesNotContain("section index empty", sources, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ready_manual_publish_turn1_always_retrieve_lists_baseline_pointers_not_none_AC3()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.LocalSha256 = "abc123";
            SourceManifestHelper.MarkManuallyPublished(entry);
        }

        PopulateSectionManifest(bundle);

        var ctx = PromptPacketBuilder.BuildContext(bundle, "Begin");

        Assert.Equal(PacketMode.Thin, ctx.Mode);
        var sources = ContextTagFormat.ExtractBlock(ctx.ContextText, "sources");
        Assert.NotNull(sources);
        Assert.Contains("id: opening", sources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: rules", sources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: player", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("- (none)", sources);
    }

    [Fact]
    public void Blocked_manual_publish_turn1_surfaces_readiness_warning_not_none_AC4()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;

        var ctx = PromptPacketBuilder.BuildContext(bundle, "Begin");

        Assert.Equal(PacketMode.Fat, ctx.Mode);
        var sources = ContextTagFormat.ExtractBlock(ctx.ContextText, "sources");
        Assert.NotNull(sources);
        Assert.Contains("Sources not ready:", sources);
        Assert.Contains("manual publish", sources, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drag files to ChatGPT Project", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("- (none)", sources);
    }

    [Fact]
    public void Blocked_adventure_sources_warning_matches_evaluate_blocking_reason_AC4()
    {
        var bundle = CreateLinkedBundle(inSync: false);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        var ctx = PromptPacketBuilder.BuildContext(bundle, "Begin");
        var sources = ContextTagFormat.ExtractBlock(ctx.ContextText, "sources");

        Assert.NotNull(sources);
        Assert.NotNull(readiness.BlockingReason);
        Assert.Contains(readiness.BlockingReason, sources);
        var status = ProjectSourceInjectionService.FormatLinkStatusSources(readiness);
        Assert.Contains("out of sync", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("out of sync", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("- (none)", sources);
    }

    [Fact]
    public void Delegated_tagged_packet_formats_sections_with_line_breaks()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Summary.RollingSummary = "poato";
        bundle.State.CurrentLocation = "room";
        bundle.State.OpenObjectives = "find the key";
        PopulateSectionManifest(bundle);
        bundle.SourceManifest.RefreshSyncedFlag();

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");

        Assert.Contains("[[cgw:sources", ctx.ContextText);
        Assert.Contains("[[cgw:instructions", ctx.ContextText);
        Assert.Contains("[[cgw:summary", ctx.ContextText);
        Assert.DoesNotContain("=== STORY SO FAR", ctx.ContextText);
        Assert.DoesNotContain("=== PROJECT SOURCES", ctx.ContextText);

        var sources = ContextTagFormat.ExtractBlock(ctx.ContextText, "sources");
        Assert.NotNull(sources);
        Assert.Contains("ALWAYS RETRIEVE", sources);
        Assert.Contains("scenario.md", sources);

        var summary = ContextTagFormat.ExtractBlock(ctx.ContextText, "summary");
        Assert.Equal("poato", summary);

        var state = ContextTagFormat.ExtractBlock(ctx.ContextText, "state");
        Assert.NotNull(state);
        Assert.Contains("Location: room", state);
        Assert.Contains("Objectives: find the key", state);
        Assert.DoesNotContain("roomObjectives:", state);

        var instructions = ContextTagFormat.ExtractBlock(ctx.ContextText, "instructions");
        Assert.NotNull(instructions);
        Assert.DoesNotContain("poato", instructions);

        var preview = ContextTagFormat.FormatStructuredPreview(
            PromptPacketBuilder.AssembleWithUser(ctx.ContextText, "look around", useContextTags: true));
        Assert.True(preview.IndexOf("[sources]", StringComparison.Ordinal) <
                    preview.IndexOf("[instructions]", StringComparison.Ordinal));
        Assert.Contains("[summary]", preview);
        Assert.Contains("poato", preview);
        Assert.Contains("scenario.md", preview);
    }

    private static void PopulateSectionManifest(AdventureBundle bundle)
    {
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.Sections = entry.RelativePath switch
            {
                "scenario.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "opening",
                        Kind = "scenario",
                        Title = "Opening",
                        BodyCache = bundle.Scenario.OpeningSituation,
                        KeyPhrase = "Rain",
                    },
                ],
                "world.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "rules",
                        Kind = "rule",
                        Title = "Rules",
                        BodyCache = bundle.Scenario.WorldRules,
                        KeyPhrase = "Magic",
                    },
                ],
                "cast.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "player",
                        Kind = "person",
                        Title = "Player",
                        BodyCache = bundle.Scenario.PlayerRole,
                        KeyPhrase = "Investigator",
                    },
                ],
                _ => [],
            };
        }

        WriteCanonicalSourceFiles(bundle);
    }

    private static void WriteCanonicalSourceFiles(AdventureBundle bundle)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        foreach (var entry in bundle.SourceManifest.Entries.Where(e => e.Sections.Count > 0))
        {
            File.WriteAllText(
                Path.Combine(dir, entry.RelativePath),
                $"# {entry.RelativePath}\n");
        }
    }

    [Fact]
    public void Evaluate_manual_mode_requires_published_lore_files()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.LocalSha256 = "abc123";
            SourceManifestHelper.MarkManuallyPublished(entry);
        }

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        Assert.True(readiness.CanDelegateStaticContent);
        Assert.Equal(SourcePublishMode.Manual, readiness.PublishMode);
    }

    [Fact]
    public void Evaluate_manual_mode_blocks_when_not_published()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        Assert.False(readiness.CanDelegateStaticContent);
        Assert.Contains("manual publish", readiness.BlockingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatLinkStatusSources_api_sync_out_of_sync_includes_manual_hint()
    {
        var bundle = CreateLinkedBundle(inSync: false);
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var status = ProjectSourceInjectionService.FormatLinkStatusSources(readiness);

        Assert.Contains("out of sync", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Manual publish", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Source Manager", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_manual_mode_probe_differ_does_not_block_delegation()
    {
        var bundle = CreateLinkedBundle(inSync: true);
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var entry in bundle.SourceManifest.Entries.Where(e => SourceManifestHelper.IsLoreSourceFile(e.RelativePath)))
        {
            entry.LocalSha256 = "abc123";
            SourceManifestHelper.MarkManuallyPublished(entry);
        }

        bundle.SourceManifest.Entries[0].RemoteProbeMatch = RemoteProbeMatch.Differ;

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        Assert.True(readiness.CanDelegateStaticContent);
        Assert.Equal(1, readiness.ProbeDifferCount);
        Assert.Contains("differs from canonical", readiness.ProbeWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatStructuredPreview_preserves_line_breaks_from_windows_packet()
    {
        var packet = ContextTagFormat.WrapBlock(
            "sources",
            "Project: g-p-test\r\n\r\n- scenario.md — Setting\r\n- plot.md — Plot");

        var preview = ContextTagFormat.FormatStructuredPreview(packet);

        Assert.Contains("- scenario.md", preview);
        Assert.Contains("- plot.md", preview);
        Assert.DoesNotContain("Setting\r\n- plot", preview);
        Assert.DoesNotContain("Setting - plot", preview);
    }
}
