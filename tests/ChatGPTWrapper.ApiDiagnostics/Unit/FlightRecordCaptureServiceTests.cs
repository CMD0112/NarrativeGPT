using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class FlightRecordCaptureServiceTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void CapturePlaySend_persists_v2_entry_with_injection_manifest()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-flight-cap", inSync: true);
        const string playerLine = "listen at the door";

        var prepared = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                PriorThreadUserMessageCount = 1,
            },
            (_, _, _) => playerLine).Prepared;

        var artifact = PreparedSendArtifactBuilder.FromPrepareResult(
            playerLine,
            prepared,
            priorThreadUserMessageCount: 1,
            bundle);

        var turn = new TurnRecord { PlayerText = playerLine };
        var traceRunId = Guid.NewGuid();

        var entry = FlightRecordCaptureService.CapturePlaySend(
            bundle,
            turn,
            artifact,
            new FlightDeliverySnapshot
            {
                Channel = "Api",
                Outcome = "ok",
                Verified = true,
                ConversationId = "conv-1",
            },
            traceRunId);

        Assert.Equal(artifact.Hash, entry.PacketHash);
        Assert.Equal(playerLine, entry.PlayerLine);
        Assert.Equal(FlightRecordKind.PlaySend, entry.Kind);
        Assert.NotNull(entry.Injection);
        Assert.NotEmpty(entry.Injection!.Sections);
        Assert.Equal(prepared.Sections.Count, entry.Injection.Sections.Count);
        Assert.Equal(prepared.Profile.ToString(), entry.Injection.Profile);
        Assert.True(entry.Injection.MergedCharCount > 0);
        Assert.NotNull(entry.Delivery);
        Assert.True(entry.Delivery!.Verified);
        Assert.Equal(traceRunId.ToString("D"), entry.PlaySendTraceRunId);
        Assert.Equal(PromptHistoryMigration.CurrentSchemaVersion, bundle.PromptHistory.SchemaVersion);
        Assert.Equal(artifact.Hash, turn.PromptPacketHash);
    }

    [Fact]
    public void CapturePlaySend_persists_bundled_utility_runs()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-flight-util", inSync: true);
        const string playerLine = "search the room";
        var runId = Guid.NewGuid();
        var manifest = new UtilityContextManifestRecord
        {
            Lane = nameof(UtilityExecutionChannel.AutoBackground),
            JobId = GenerationJobId.UpdateSummary,
            SectionsIncluded = ["summary"],
            TotalCharCount = 420,
        };

        var prepared = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                PriorThreadUserMessageCount = 0,
            },
            (_, _, _) => playerLine).Prepared;

        var artifact = PreparedSendArtifactBuilder.FromPrepareResult(
            playerLine,
            prepared,
            priorThreadUserMessageCount: 0,
            bundle);

        var turn = new TurnRecord { PlayerText = playerLine };
        var dispatches = new List<PendingUtilityInjection>
        {
            new()
            {
                RunId = runId,
                JobId = GenerationJobId.UpdateSummary,
                Channel = UtilityExecutionChannel.AutoBackground,
                ContextManifest = manifest,
            },
        };

        var entry = FlightRecordCaptureService.CapturePlaySend(
            bundle,
            turn,
            artifact,
            new FlightDeliverySnapshot { Channel = "Api", Outcome = "ok", Verified = true },
            utilityDispatches: dispatches);

        Assert.Single(entry.UtilityJobIds);
        Assert.Equal(runId, entry.UtilityJobIds[0]);
        Assert.Single(entry.UtilityRuns);
        Assert.Equal(GenerationJobId.UpdateSummary, entry.UtilityRuns[0].JobId);
        Assert.NotNull(entry.UtilityRuns[0].ContextManifest);
        Assert.Contains("summary", entry.UtilityRuns[0].ContextManifest!.SectionsIncluded);

        var rows = FlightRecordCorrelationService.BuildUtilityRows(bundle, entry);
        Assert.Single(rows);
        Assert.Contains("summary", rows[0].ManifestSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindFlightRecordIdForUtilityRun_returns_latest_matching_entry()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-flight-link", inSync: true);
        var runId = Guid.NewGuid();
        bundle.PromptHistory.Entries.Add(new PromptHistoryEntry
        {
            UtilityJobIds = [runId],
        });

        var found = FlightRecordCorrelationService.FindFlightRecordIdForUtilityRun(bundle, runId);

        Assert.NotNull(found);
        Assert.Equal(bundle.PromptHistory.Entries[0].Id, found);
    }

    [Fact]
    public void BuildInjectionSnapshot_matches_InjectionSectionManifestBuilder()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-flight-manifest", inSync: true);
        const string playerLine = "open the gate";

        var prepared = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                PriorThreadUserMessageCount = 0,
            },
            (_, _, _) => playerLine).Prepared;

        var artifact = PreparedSendArtifactBuilder.FromPrepareResult(
            playerLine,
            prepared,
            priorThreadUserMessageCount: 0,
            bundle);

        var snapshot = FlightRecordCaptureService.BuildInjectionSnapshot(artifact);

        Assert.Equal(prepared.Sections.Count, snapshot.Sections.Count);
        for (var i = 0; i < prepared.Sections.Count; i++)
        {
            Assert.Equal(prepared.Sections[i].Id, snapshot.Sections[i].Id);
            Assert.Equal(prepared.Sections[i].Kind.ToString(), snapshot.Sections[i].Kind);
            Assert.Equal(prepared.Sections[i].Included, snapshot.Sections[i].Included);
        }
    }

    [Fact]
    public void PreparedSendArtifactMapper_round_trips_manifest_fields()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-flight-mapper", inSync: true);
        const string playerLine = "probe the room";

        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                PriorThreadUserMessageCount = 2,
            },
            (_, _, _) => playerLine);

        var artifact = PreparedSendArtifactBuilder.FromPrepareResult(
            playerLine,
            session.Prepared,
            priorThreadUserMessageCount: 2,
            bundle);

        var mapped = PreparedSendArtifactMapper.ToPrepareResult(artifact);

        Assert.Equal(session.Prepared.Hash, mapped.Hash);
        Assert.Equal(session.Prepared.Profile, mapped.Profile);
        Assert.Equal(session.Prepared.Sections.Count, mapped.Sections.Count);
        Assert.Equal(session.Prepared.HasUtilityInjection, mapped.HasUtilityInjection);
        Assert.Equal(session.Prepared.BaselinePointers.Count, mapped.BaselinePointers.Count);
    }

    [Fact]
    public void PromptPacketBuilder_BuildContext_includes_resolved_pointers()
    {
        var bundle = CreateBundleWithSourceSections();
        const string playerLine = "i speak to mara";

        var ctx = PromptPacketBuilder.BuildContext(bundle, playerLine);

        Assert.NotEmpty(ctx.ThisTurnPointers);
    }

    [Fact]
    public void PromptPacketBuilder_Build_propagates_resolved_pointers()
    {
        var bundle = CreateBundleWithSourceSections();
        const string playerLine = "i speak to mara";

        var packet = PromptPacketBuilder.Build(bundle, playerLine);

        Assert.NotEmpty(packet.ThisTurnPointers);
        Assert.Contains(packet.ThisTurnPointers, p => p.MachineId.Contains("mara", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CapturePlaySend_persists_pointer_snapshots_when_resolved()
    {
        var bundle = CreateBundleWithSourceSections();
        const string playerLine = "i speak to mara";

        var prepared = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                PriorThreadUserMessageCount = 5,
            },
            (_, _, _) => playerLine).Prepared;

        Assert.Contains(prepared.BaselinePointers, p => p.SectionId == "opening");
        Assert.Contains(prepared.ThisTurnPointers, p => p.MachineId.Contains("mara", StringComparison.OrdinalIgnoreCase));

        var artifact = PreparedSendArtifactBuilder.FromPrepareResult(
            playerLine,
            prepared,
            priorThreadUserMessageCount: 5,
            bundle);

        var turn = new TurnRecord { PlayerText = playerLine };
        var entry = FlightRecordCaptureService.CapturePlaySend(
            bundle,
            turn,
            artifact,
            new FlightDeliverySnapshot { Channel = "Api", Outcome = "ok", Verified = true });

        Assert.NotEmpty(entry.Injection!.ThisTurnPointers);
        Assert.Contains(entry.Injection.ThisTurnPointers, p => p.MachineId.Contains("mara", StringComparison.OrdinalIgnoreCase));
    }

    private static AdventureBundle CreateBundleWithSourceSections()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-flight-ptr", inSync: true, entryCount: 4);
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.SourceManifest.Entries =
        [
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.ScenarioFile,
                SyncState = SourceSyncState.InSync,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "opening",
                        Kind = "scenario",
                        Title = "Opening",
                        BodyCache = "Opening situation",
                        KeyPhrase = "Rain",
                    },
                ],
            },
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.WorldFile,
                SyncState = SourceSyncState.InSync,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "rules",
                        Kind = "rule",
                        Title = "Rules",
                        BodyCache = "Magic is rare",
                        KeyPhrase = "Magic",
                    },
                ],
            },
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.CastFile,
                SyncState = SourceSyncState.InSync,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "npcs/mara-voss",
                        ParentId = "npcs",
                        Kind = "person",
                        Title = "Mara Voss",
                        Aliases = ["Mara", "Mara Voss"],
                        BodyCache = "Apothecary",
                    },
                ],
            },
        ];

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        foreach (var entry in bundle.SourceManifest.Entries.Where(e => e.Sections.Count > 0))
        {
            var path = Path.Combine(dir, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"# {entry.RelativePath}\n");
        }

        return bundle;
    }
}

[Trait("Category", "Unit")]
public sealed class PromptHistoryMigrationTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void Migrate_upgrades_v1_document_to_schema_v2()
    {
        var document = new PromptHistoryDocument
        {
            SchemaVersion = 1,
            Entries =
            [
                new PromptHistoryEntry
                {
                    PacketText = "legacy packet",
                    PacketHash = "abc",
                },
            ],
        };

        var migrated = PromptHistoryMigration.Migrate(document);

        Assert.True(migrated);
        Assert.Equal(PromptHistoryMigration.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal(FlightRecordKind.PlaySend, document.Entries[0].Kind);
        Assert.Null(document.Entries[0].Injection);
    }

    [Fact]
    public void V2_entry_round_trips_through_json()
    {
        var entry = new PromptHistoryEntry
        {
            PlayerLine = "test line",
            PacketText = "packet body",
            PacketHash = "hash",
            Kind = FlightRecordKind.PlaySend,
            Injection = new FlightInjectionSnapshot
            {
                Profile = "SourceDelegated",
                Sections =
                [
                    new FlightInjectionSectionRecord
                    {
                        Id = "sources",
                        Kind = "Reference",
                        Included = true,
                    },
                ],
            },
            Delivery = new FlightDeliverySnapshot
            {
                Channel = "Api",
                Outcome = "ok",
                Verified = true,
            },
        };

        var json = JsonSerializer.Serialize(entry, AdventureJson.Options);
        var roundTrip = JsonSerializer.Deserialize<PromptHistoryEntry>(json, AdventureJson.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal("test line", roundTrip!.PlayerLine);
        Assert.NotNull(roundTrip.Injection);
        Assert.Single(roundTrip.Injection!.Sections);
        Assert.True(roundTrip.Delivery!.Verified);
    }
}

[Trait("Category", "Unit")]
public sealed class FlightRecordDetailFormatterTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void ToSectionRows_maps_included_sections()
    {
        var injection = new FlightInjectionSnapshot
        {
            Sections =
            [
                new FlightInjectionSectionRecord
                {
                    Id = "sources",
                    Kind = "Reference",
                    Included = true,
                    CharEstimate = 120,
                },
            ],
        };

        var rows = FlightRecordDetailFormatter.ToSectionRows(injection);

        Assert.Single(rows);
        Assert.Equal("Project sources", rows[0].DisplayName);
        Assert.True(rows[0].Included);
    }

    [Fact]
    public void ToPointerRows_labels_baseline_bucket()
    {
        var injection = new FlightInjectionSnapshot
        {
            BaselinePointers =
            [
                new FlightContextPointerRecord
                {
                    MachineId = "world/lore",
                    Title = "Cosmology",
                    Source = "Baseline",
                    Score = 100,
                    Mode = "PointerOnly",
                },
            ],
        };

        var rows = FlightRecordDetailFormatter.ToPointerRows(injection, baseline: true);

        Assert.Single(rows);
        Assert.Equal("Always retrieve", rows[0].Bucket);
        Assert.Equal("Cosmology", rows[0].Title);
    }

    [Fact]
    public void FormatLogTurnLink_includes_display_turn_number()
    {
        var link = new LogTurnLink
        {
            TurnId = Guid.NewGuid(),
            TurnIndex = 3,
            DisplayTurnNumber = 4,
            PlayerSnippet = "hello",
        };

        var text = FlightRecordDetailFormatter.FormatLogTurnLink(link);

        Assert.Contains("Play pair 4", text, StringComparison.Ordinal);
        Assert.Contains("log index 3", text, StringComparison.Ordinal);
    }
}
