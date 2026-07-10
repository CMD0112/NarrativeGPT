using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityJobLoggingHooksTests : IDisposable
{
    private readonly string _tempRoot;

    public UtilityJobLoggingHooksTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-utility-logging-" + Guid.NewGuid().ToString("N"));
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void BeforeDispatch_records_worker_dispatch_ingest_and_context_projection()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        var entry = RegisterPlayThread(bundle);
        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            SampleBranch(),
            ThreadConversationLogCaptureSource.Api);

        var runId = Guid.NewGuid();
        var context = new GenerationJobContext { UtilityRunId = runId };

        UtilityJobLoggingHooks.BeforeDispatch(bundle, GenerationJobId.ProposeMemories, context);

        Assert.NotNull(context.PlayThreadIngestEventId);
        Assert.Equal(entry.Id, context.PlayThreadEntryId);
        Assert.NotNull(context.ContextProjectionPath);

        var events = ThreadConversationLogStore.LoadAllIngestEvents(bundle.Metadata.Id, entry.Id);
        Assert.Contains(
            events,
            e => e.EventId == context.PlayThreadIngestEventId
                 && e.CaptureTrigger == ThreadConversationLogSnapshotTrigger.WorkerDispatch);

        var projectionPath = Path.Combine(
            AppDirectories.AdventureDirectory(bundle.Metadata.Id),
            "utility-results",
            context.ContextProjectionPath!);
        Assert.True(File.Exists(projectionPath));
    }

    [Fact]
    public void ApplyLoggingMetadata_copies_context_fields_to_run_record()
    {
        var record = new UtilityJobRunRecord();
        var context = new GenerationJobContext
        {
            PlayThreadIngestEventId = Guid.NewGuid(),
            PlayThreadEntryId = Guid.NewGuid(),
            PlayThreadRawPath = "raw/test.json",
            PlayThreadProjectionPath = "projections/test.json",
            ContextProjectionPath = $"{Guid.NewGuid()}/context-projection.json",
            SourceIoInputPath = "sources/cgw-utility-io/test/in/world.md",
            EphemeralCapturePath = $"{Guid.NewGuid()}/ephemeral-capture.json",
        };

        UtilityJobLoggingHooks.ApplyLoggingMetadata(record, context);

        Assert.Equal(context.PlayThreadIngestEventId, record.PlayThreadIngestEventId);
        Assert.Equal(context.SourceIoInputPath, record.SourceIoInputPath);
        Assert.Equal(context.EphemeralCapturePath, record.EphemeralCapturePath);
    }

    private static AdventureThreadEntry RegisterPlayThread(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play);
        if (entry is null)
        {
            entry = AdventureThreadRegistryService.RegisterEntry(
                bundle,
                AdventureThreadKind.Play,
                conversationId: "test-conversation-id",
                label: "Play");
            AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);
        }

        if (string.IsNullOrWhiteSpace(entry.ConversationId))
            entry.ConversationId = "test-conversation-id";

        return entry;
    }

    private static List<ConversationBranchMessage> SampleBranch() =>
    [
        new()
        {
            NodeId = "u1",
            Role = "user",
            RawText = "look around",
            DisplayText = "look around",
            BranchIndex = 0,
        },
        new()
        {
            NodeId = "a1",
            ParentNodeId = "u1",
            Role = "assistant",
            RawText = "You see a forest.",
            DisplayText = "You see a forest.",
            BranchIndex = 0,
        },
    ];
}

[Trait("Category", "Unit")]
public sealed class UtilityWorkerTransitionCatalogTests
{
    [Theory]
    [InlineData(GenerationJobId.ProcessTurn)]
    [InlineData(GenerationJobId.ProposeMemories)]
    [InlineData(GenerationJobId.ProposeEntitiesFile)]
    [InlineData(GenerationJobId.ProposeSourceEdits)]
    public void Transition_catalog_requires_worker_lane(string jobId)
    {
        Assert.True(UtilityWorkerTransitionCatalog.RequiresWorkerLane(jobId));
        Assert.True(UtilityWorkerTransitionCatalog.ForcesEphemeralLane(jobId));
    }

    [Fact]
    public void Design_jobs_are_not_in_transition_catalog()
    {
        Assert.False(UtilityWorkerTransitionCatalog.RequiresWorkerLane(GenerationJobId.DesignAdventure));
        Assert.False(UtilityWorkerTransitionCatalog.RequiresWorkerLane(GenerationJobId.ProposeJsonImport));
    }
}

[Trait("Category", "Unit")]
public sealed class SourceFileRevisionServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public SourceFileRevisionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-source-revision-" + Guid.NewGuid().ToString("N"));
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void BuildRevisionPrompt_uses_source_pointers_not_inline_excerpts()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(sourcesDir);
        File.WriteAllText(Path.Combine(sourcesDir, SectionSchema.WorldFile), "# World\nForest realm.");

        var runId = Guid.NewGuid();
        var prompt = SourceFileRevisionService.BuildRevisionPrompt(
            bundle,
            "Add a mountain range.",
            runId,
            gizmoId: "g-p-test");

        Assert.Contains("Retrieve from `sources/cgw-utility-io/", prompt, StringComparison.Ordinal);
        Assert.Contains("[[cgw:sources", prompt, StringComparison.Ordinal);
        Assert.Contains(SourceFileRevisionService.OutputFileName, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("=== CURRENT SOURCE EXCERPTS ===", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractProposalsJson_reads_delimited_block()
    {
        const string json = """[{"targetFile":"world.md","operation":"append","content":"New lore","rationale":"test"}]""";
        var response = $"""
            --- begin {SourceFileRevisionService.OutputFileName} ---
            {json}
            --- end {SourceFileRevisionService.OutputFileName} ---
            """;

        var extracted = SourceFileRevisionService.TryExtractProposalsJson(response);
        Assert.Equal(json, extracted);
    }
}

[Trait("Category", "Unit")]
public sealed class EntityExtractionSourceIoTests : IDisposable
{
    private readonly string _tempRoot;

    public EntityExtractionSourceIoTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-extract-source-io-" + Guid.NewGuid().ToString("N"));
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void BuildScopedExtractionPrompt_includes_source_pointers_when_run_id_set()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        var adventureDir = AppDirectories.AdventureDirectory(bundle.Metadata.Id);
        Directory.CreateDirectory(adventureDir);
        File.WriteAllText(
            Path.Combine(adventureDir, SourceJsonImportService.EntitiesJsonFileName),
            """{"schemaVersion":1,"characters":[]}""");
        File.WriteAllText(
            Path.Combine(adventureDir, SourceJsonImportService.ScenarioJsonFileName),
            """{"schemaVersion":1,"setting":"Forest"}""");

        var runId = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var prompt = EntityExtractionService.BuildScopedExtractionPrompt(
            bundle,
            new UtilityTranscriptScope
            {
                TargetPair = new TranscriptTurnPair
                {
                    TurnIndex = 1,
                    PlayerText = "look around",
                    NarratorText = "You see trees.",
                },
            },
            runId);

        Assert.Contains("[[cgw:sources", prompt, StringComparison.Ordinal);
        Assert.Contains("extract-entities", prompt, StringComparison.Ordinal);
        Assert.Contains("entities.json", prompt, StringComparison.Ordinal);
        Assert.Contains("scenario.json", prompt, StringComparison.Ordinal);
        Assert.Contains("EXTRACTION JOB", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesSourceFileIo_includes_extract_entities()
    {
        Assert.True(UtilitySourceFileIoCatalog.UsesSourceFileIo(GenerationJobId.ExtractEntities));
        Assert.True(UtilitySourceFileIoCatalog.UsesSourceFileIo(GenerationJobId.ExpandEntity));
    }
}
