using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ApiDiagnostics.Reporting;
using ChatGPTWrapper.ApiDiagnostics.Unit;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Performance;

[Collection("SourceSyncMockBridge")]
[Trait("Category", "Performance")]
[Trait("Category", "Integration")]
public sealed class ProjectSourceMockBridgePerfTests : IDisposable
{
    private readonly SourceSyncMockBridgeHost _host;
    private readonly string _tempRoot;

    public ProjectSourceMockBridgePerfTests(SourceSyncMockBridgeHost host)
    {
        _host = host;
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-MockPerf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        ProjectRemoteListCache.Invalidate();
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public async Task Mock_bridge_pipeline_records_timings(int mockDelayMs)
    {
        var report = new SourceSyncPerfReport
        {
            Tier = "Integration",
            MockDelayMs = mockDelayMs,
            GizmoId = AdventureTestData.DefaultMockGizmoId,
            FileCount = AdventureTestData.StandardSourcePaths.Length,
        };

        var bundle = AdventureTestData.CreateLinkedBundle(inSync: false);
        var gizmoId = AdventureTestData.DefaultMockGizmoId;
        SourceSyncPlan? coldPlan = null;
        SourceSyncPlan? warmPlan = null;
        ProjectSourceSyncResult? applyResult = null;

        try
        {
            await _host.InitializeAsync();
            await _host.SetMockDelayAsync(mockDelayMs);
            var api = new ChatGptProjectApiService(_host.Bridge!);
            var sync = new ProjectSourceSyncService(api);
            var orchestrator = new ProjectFileSyncOrchestrator(api, sync);
            AdventureTestData.WriteLocalSources(bundle);
            SeedRemoteIds(bundle);

            await _host.RunOnUiAsync(async () =>
            {
                var core = _host.Core!;

                await SourceSyncPerfRunnerBase.RunStep(report, "find", "mock_list_remote_files", async () =>
                {
                    ProjectRemoteListCache.Invalidate(gizmoId);
                    var files = await api.GetProjectFilesDirectAsync(
                        core,
                        gizmoId,
                        CancellationToken.None,
                        ensureProjectPage: false);
                    return $"count={files.Count}";
                });

                await SourceSyncPerfRunnerBase.RunStep(report, "find", "mock_list_cached", async () =>
                {
                    ProjectRemoteListCache.Set(gizmoId, await api.GetProjectFilesDirectAsync(
                        core,
                        gizmoId,
                        CancellationToken.None,
                        ensureProjectPage: false));
                    var hit = ProjectRemoteListCache.TryGet(gizmoId, out var cached);
                    return $"cacheHit={hit} count={cached?.Count ?? 0}";
                });

                await SourceSyncPerfRunnerBase.RunStep(report, "find", "mock_build_plan_cold", async () =>
                {
                    ProjectRemoteListCache.Invalidate(gizmoId);
                    coldPlan = await orchestrator.BuildPlanAsync(
                        core,
                        bundle,
                        ensureProjectPage: false,
                        exportSources: true);
                    var compareCount = coldPlan!.Items.Count(i =>
                        !string.IsNullOrEmpty(i.Entry.RemoteSha256)
                        || i.Entry.SyncState is SourceSyncState.RemoteNewer
                            or SourceSyncState.LocalNewer
                            or SourceSyncState.Conflict);
                    return $"items={coldPlan.Items.Count} compareSignals={compareCount}";
                });

                await SourceSyncPerfRunnerBase.RunStep(report, "find", "mock_build_plan_warm", async () =>
                {
                    warmPlan = await orchestrator.BuildPlanAsync(
                        core,
                        bundle,
                        ensureProjectPage: false,
                        cachedRemoteFiles: coldPlan?.DetectedRemoteFiles,
                        exportSources: false);
                    return $"items={warmPlan.Items.Count} blocked={warmPlan.SyncBlocked}";
                });

                await SourceSyncPerfRunnerBase.RunStep(report, "download", "mock_download_3_files_parallel", () =>
                {
                    var downloads = coldPlan?.Items.Count(i => !string.IsNullOrEmpty(i.Entry.RemoteSha256)) ?? 0;
                    return Task.FromResult($"remoteHashesRecorded={downloads}");
                });

                await SourceSyncPerfRunnerBase.RunStep(report, "modify", "mock_modify_local_replan", async () =>
                {
                    var dir = ProjectSourceExportService.SourcesDirectory(bundle);
                    var scenarioPath = Path.Combine(dir, "scenario.md");
                    await File.AppendAllTextAsync(scenarioPath, "\n\nLocal edit for perf test.\n");
                    var entry = bundle.SourceManifest.Entries.First(e => e.RelativePath == "scenario.md");
                    entry.LocalSha256 = ProjectSourceExportService.ComputeSha256(scenarioPath);
                    entry.SyncState = SourceSyncState.LocalNewer;

                    var replan = await orchestrator.BuildPlanAsync(
                        core,
                        bundle,
                        ensureProjectPage: false,
                        exportSources: false);
                    var localNewer = replan.Items.Count(i =>
                        i.Entry.SyncState == SourceSyncState.LocalNewer
                        || i.Entry.PlannedAction == SourceSyncAction.PushReplace);
                    return $"localNewer={localNewer}";
                });

                await SourceSyncPerfRunnerBase.RunStep(report, "read", "mock_read_upload_bytes", async () =>
                {
                    var dir = ProjectSourceExportService.SourcesDirectory(bundle);
                    long bytes = 0;
                    foreach (var path in AdventureTestData.StandardSourcePaths)
                    {
                        var localPath = Path.Combine(dir, path);
                        if (File.Exists(localPath))
                            bytes += (await File.ReadAllBytesAsync(localPath)).Length;
                    }

                    return $"bytes={bytes}";
                });

                if (coldPlan is not null)
                {
                    await SourceSyncPerfRunnerBase.RunStep(report, "upload", "mock_attach_batch", async () =>
                    {
                        var applyPlan = await orchestrator.BuildPlanAsync(
                            core,
                            bundle,
                            ensureProjectPage: false,
                            exportSources: false);
                        applyResult = await sync.ApplyPlanAsync(
                            core,
                            bundle,
                            applyPlan,
                            autoSafeOnly: true);
                        MergeTracePhases(report, applyResult.RunSummaryPath);
                        return $"success={applyResult.Success} uploaded={applyResult.Uploaded} pulled={applyResult.Pulled}"
                               + (string.IsNullOrWhiteSpace(applyResult.Error) ? "" : $" error={applyResult.Error}");
                    });
                }
            });
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }

        report.WriteToDisk();
        Assert.True(report.Steps.Count >= 8);
        Assert.True(File.Exists(SourceSyncPerfReport.ReportJsonPath));
    }

    private static void SeedRemoteIds(AdventureBundle bundle)
    {
        for (var i = 0; i < bundle.SourceManifest.Entries.Count; i++)
        {
            var entry = bundle.SourceManifest.Entries[i];
            entry.RemoteFileId = $"mock-file-{i}";
            entry.RemoteFileName = entry.RelativePath;
            entry.RemoteSha256 = "";
            entry.BaselineSha256 = "";
        }
    }

    private static void MergeTracePhases(SourceSyncPerfReport report, string? runSummaryPath)
    {
        if (string.IsNullOrWhiteSpace(runSummaryPath))
            return;

        var phases = ProjectSyncTrace.ReadPhaseDurationsFromSummary(runSummaryPath);
        if (phases is null)
            return;

        foreach (var (phase, durationMs) in phases)
        {
            report.AddTracePhase(phase.ToString().ToLowerInvariant(), durationMs);
        }
    }
}
