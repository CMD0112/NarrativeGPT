using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class SyncTraceTests : IDisposable
{
    private readonly string _tempRoot;

    public SyncTraceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-SyncTraceTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        AppDirectories.ResetStoresForTests();
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

    [Fact]
    public void BeginRun_CompleteRun_writes_valid_jsonl_with_required_fields()
    {
        var adventureId = Guid.NewGuid();
        using var scope = ProjectSyncTrace.BeginRun(adventureId, "g-p-test", autoSafeOnly: true);
        scope.Complete("ok", data: new { uploaded = 1 });

        Assert.True(File.Exists(ProjectSyncTrace.TracePath));
        var lines = File.ReadAllLines(ProjectSyncTrace.TracePath);
        Assert.NotEmpty(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("at", out _));
        Assert.True(root.TryGetProperty("runId", out _));
        Assert.True(root.TryGetProperty("runIdShort", out var runIdShort));
        Assert.Equal(8, runIdShort.GetString()?.Length);
        Assert.Equal(adventureId, root.GetProperty("adventureId").GetGuid());
        Assert.Equal("g-p-test", root.GetProperty("gizmoId").GetString());
        Assert.Equal("sync_run_start", root.GetProperty("event").GetString());

        Assert.True(File.Exists(ProjectSyncTrace.GetRunSummaryPath(scope.Run.RunIdShort)));
    }

    [Fact]
    public void AsyncLocal_scopes_nest_without_leaking_inner_run()
    {
        var outerAdventure = Guid.NewGuid();
        var innerAdventure = Guid.NewGuid();

        using (var outer = ProjectSyncTrace.BeginRun(outerAdventure, "g-p-outer", autoSafeOnly: false))
        {
            Assert.Equal(outer.Run.RunId, ProjectSyncTrace.ActiveRunId);

            using (var inner = ProjectSyncTrace.BeginRun(innerAdventure, "g-p-inner", autoSafeOnly: true))
            {
                Assert.Equal(inner.Run.RunId, ProjectSyncTrace.ActiveRunId);
                inner.Complete("ok");
            }

            Assert.Equal(outer.Run.RunId, ProjectSyncTrace.ActiveRunId);
            outer.Complete("ok");
        }

        Assert.Null(ProjectSyncTrace.ActiveRun);
    }

    [Fact]
    public void Phase_timing_records_durationMs()
    {
        var adventureId = Guid.NewGuid();
        using var scope = ProjectSyncTrace.BeginRun(adventureId, "g-p-phase", autoSafeOnly: true);

        using (ProjectSyncTrace.BeginPhase(SyncTracePhase.Upload))
            Thread.Sleep(25);

        scope.Complete("ok");

        var phaseEnd = File.ReadAllLines(ProjectSyncTrace.TracePath)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .LastOrDefault(el => el.GetProperty("event").GetString() == ProjectSyncTraceEvents.PhaseEnd);

        Assert.NotEqual(default, phaseEnd);
        Assert.True(phaseEnd.GetProperty("durationMs").GetInt64() >= 0);
        Assert.Equal("upload", phaseEnd.GetProperty("phase").GetString());
    }

    [Fact]
    public void LogMirror_includes_run_prefix_when_run_active()
    {
        var adventureId = Guid.NewGuid();
        using var scope = ProjectSyncTrace.BeginRun(adventureId, "g-p-mirror", autoSafeOnly: true);
        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.UploadStart,
            SyncTraceCategory.Upload,
            SyncTraceLevel.Info,
            "Upload starting scenario.md",
            phase: SyncTracePhase.Upload);

        var logText = File.ReadAllText(ProjectLinkDiagnostics.LogPath);
        Assert.Contains($"[run={scope.Run.RunIdShort}]", logText);
        Assert.Contains("[upload/upload]", logText);
        Assert.Contains("Upload starting scenario.md", logText);

        scope.Complete("ok");
    }

    [Fact]
    public void Run_summary_contains_timeline_entries_in_order()
    {
        var adventureId = Guid.NewGuid();
        using var scope = ProjectSyncTrace.BeginRun(adventureId, "g-p-summary", autoSafeOnly: false, operation: "apply");

        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.UploadStart,
            SyncTraceCategory.Upload,
            SyncTraceLevel.Info,
            "Upload starting a.md",
            phase: SyncTracePhase.Upload);
        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.UploadOk,
            SyncTraceCategory.Upload,
            SyncTraceLevel.Info,
            "Upload ok a.md",
            phase: SyncTracePhase.Upload);

        scope.Complete("ok", data: new { uploaded = 1 });

        var summaryPath = ProjectSyncTrace.GetRunSummaryPath(scope.Run.RunIdShort);
        using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryPath));
        var timeline = summaryDoc.RootElement.GetProperty("timeline");
        Assert.True(timeline.GetArrayLength() >= 4);

        var events = timeline.EnumerateArray()
            .Select(el => el.GetProperty("event").GetString())
            .ToList();

        var uploadStartIndex = events.IndexOf(ProjectSyncTraceEvents.UploadStart);
        var uploadOkIndex = events.IndexOf(ProjectSyncTraceEvents.UploadOk);
        var runEndIndex = events.LastIndexOf(ProjectSyncTraceEvents.SyncRunEnd);

        Assert.True(uploadStartIndex >= 0);
        Assert.True(uploadOkIndex > uploadStartIndex);
        Assert.True(runEndIndex > uploadOkIndex);
    }
}
