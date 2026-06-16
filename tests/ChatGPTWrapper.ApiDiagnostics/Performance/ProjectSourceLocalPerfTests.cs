using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ApiDiagnostics.Reporting;
using ChatGPTWrapper.ApiDiagnostics.Unit;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Performance;

[Collection("SourceSyncLocalPerf")]
[Trait("Category", "Performance")]
[Trait("Category", "Unit")]
public sealed class ProjectSourceLocalPerfTests : IDisposable
{
    private readonly string _tempRoot;

    public ProjectSourceLocalPerfTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-SourcePerf-" + Guid.NewGuid().ToString("N"));
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
    [InlineData(10_240)]
    [InlineData(102_400)]
    public async Task Local_source_pipeline_records_timings(int paddingBytes)
    {
        var report = new SourceSyncPerfReport
        {
            Tier = "Unit",
            FileCount = AdventureTestData.StandardSourcePaths.Length,
        };

        var bundle = AdventureTestData.CreateLinkedBundle();
        try
        {
            await SourceSyncPerfRunnerBase.RunStep(report, "read", "export_force_6_files", () =>
            {
                AdventureTestData.WriteLocalSources(bundle);
                return Task.FromResult($"sourcesDir={ProjectSourceExportService.SourcesDirectory(bundle)}");
            });

            if (paddingBytes > 0)
            {
                AdventureTestData.AppendPaddingToSource(bundle, "scenario.md", paddingBytes);
                report.FileCount = AdventureTestData.StandardSourcePaths.Length;
            }

            await SourceSyncPerfRunnerBase.RunStep(report, "read", "export_if_stale_unchanged", () =>
            {
                ProjectSourceExportService.ExportIfStale(bundle);
                return Task.FromResult("unchanged");
            });

            var dir = ProjectSourceExportService.SourcesDirectory(bundle);
            await SourceSyncPerfRunnerBase.RunStep(report, "read", "hash_6_local_files", () =>
            {
                var hashes = new List<string>();
                foreach (var path in AdventureTestData.StandardSourcePaths)
                {
                    var localPath = Path.Combine(dir, path);
                    if (File.Exists(localPath))
                        hashes.Add(ProjectSourceExportService.ComputeSha256(localPath));
                }

                return Task.FromResult($"count={hashes.Count} padding={paddingBytes}");
            });

            await SourceSyncPerfRunnerBase.RunStep(report, "read", "read_all_bytes_6_files", async () =>
            {
                long totalBytes = 0;
                foreach (var path in AdventureTestData.StandardSourcePaths)
                {
                    var localPath = Path.Combine(dir, path);
                    if (!File.Exists(localPath))
                        continue;

                    var bytes = await File.ReadAllBytesAsync(localPath);
                    totalBytes += bytes.Length;
                }

                return $"totalBytes={totalBytes}";
            });

            await SourceSyncPerfRunnerBase.RunStep(report, "modify", "modify_scenario_reexport", () =>
            {
                bundle.Scenario.Setting = "An abandoned lighthouse during a storm";
                ProjectSourceExportService.ExportForce(bundle);
                var scenarioPath = Path.Combine(dir, "scenario.md");
                var hash = File.Exists(scenarioPath)
                    ? ProjectSourceExportService.ComputeSha256(scenarioPath)
                    : "";
                return Task.FromResult($"scenarioHash={hash[..Math.Min(12, hash.Length)]}");
            });

            await SourceSyncPerfRunnerBase.RunStep(report, "find", "planner_match_100_remotes", () =>
            {
                var entry = bundle.SourceManifest.Entries[0];
                var remotes = new List<GizmoFileRef>
                {
                    new() { FileId = "file-a", Name = entry.RelativePath },
                    new() { FileId = "file-b", Name = entry.RelativePath },
                };

                GizmoFileRef? last = null;
                for (var i = 0; i < 100; i++)
                    last = ProjectFileSyncPlanner.TryMatchRemoteFile(entry, remotes);

                return Task.FromResult($"lastMatch={last?.FileId}");
            });

            await SourceSyncPerfRunnerBase.RunStep(report, "modify", "planner_reconcile_duplicates", () =>
            {
                var plan = BuildDuplicatePlan(dir);
                ProjectFileSyncPlanner.ReconcileDuplicateRows(plan, dir);
                var orphans = ProjectFileSyncPlanner.GetOrphanDuplicates(plan);
                return Task.FromResult($"orphans={orphans.Count} items={plan.Items.Count}");
            });

            await SourceSyncPerfRunnerBase.RunStep(report, "find", "remote_list_cache_hit", () =>
            {
                var gizmoId = AdventureTestData.DefaultMockGizmoId;
                var files = AdventureTestData.StandardSourcePaths
                    .Select((path, index) => new GizmoFileRef
                    {
                        FileId = $"mock-{index}",
                        Name = path,
                    })
                    .ToList();

                ProjectRemoteListCache.Set(gizmoId, files);
                var hit = ProjectRemoteListCache.TryGet(gizmoId, out var cached);
                return Task.FromResult($"hit={hit} count={cached?.Count ?? 0}");
            });
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }

        report.WriteToDisk();
        Assert.True(report.Steps.Count >= 8);
        Assert.True(File.Exists(SourceSyncPerfReport.ReportJsonPath));
        Assert.True(File.Exists(SourceSyncPerfReport.ReportTextPath));
    }

    private static SourceSyncPlan BuildDuplicatePlan(string sourcesDir)
    {
        var plan = new SourceSyncPlan();
        plan.Items.Add(new SourceSyncPlanItem
        {
            Entry = new SourceManifestEntry
            {
                RelativePath = "scenario.md",
                SyncState = SourceSyncState.LocalOnly,
                RemoteFileId = "local-scenario",
            },
        });
        plan.Items.Add(new SourceSyncPlanItem
        {
            Entry = new SourceManifestEntry
            {
                RelativePath = "scenario.md",
                SyncState = SourceSyncState.RemoteOnly,
                RemoteFileId = "remote-scenario",
                RemoteFileName = "scenario.md",
            },
        });

        var localPath = Path.Combine(sourcesDir, "scenario.md");
        if (!Directory.Exists(sourcesDir))
            Directory.CreateDirectory(sourcesDir);
        if (!File.Exists(localPath))
            File.WriteAllText(localPath, "# scenario");

        return plan;
    }
}
