using System.Diagnostics;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ApiDiagnostics.Reporting;
using ChatGPTWrapper.ApiDiagnostics.Unit;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

public sealed class LiveSourceSyncPerfRunner
{
    public const string GizmoIdEnvVar = "CGW_PERF_GIZMO_ID";
    public const string SkipUploadEnvVar = "CGW_PERF_SKIP_UPLOAD";
    public const string EnsureProjectPageEnvVar = "CGW_PERF_ENSURE_PROJECT_PAGE";
    public const string TimeoutMinutesEnvVar = "CGW_PERF_TIMEOUT_MINUTES";
    public const string CleanupProbeEnvVar = "CGW_PERF_CLEANUP_PROBE";
    public const string CleanupAllProbesEnvVar = "CGW_PERF_CLEANUP_ALL_PROBES";
    public const string DownloadMaxEnvVar = "CGW_PERF_DOWNLOAD_MAX";
    public const string RefreshMatchedSourcesEnvVar = "CGW_PERF_REFRESH_MATCHED_SOURCES";
    public const string SkipAttachVerifyEnvVar = "CGW_PERF_SKIP_ATTACH_VERIFY";
    public const string SkipAttachSidebarEnvVar = "CGW_PERF_SKIP_ATTACH_SIDEBAR";
    public const string DownloadFailFastEnvVar = "CGW_PERF_DOWNLOAD_FAIL_FAST";

    private const int DefaultDownloadMax = 6;

    private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromMinutes(20);

    private readonly WebView2DiagnosticHost _host;

    public LiveSourceSyncPerfRunner(WebView2DiagnosticHost host) => _host = host;

    public static bool SkipUpload =>
        string.Equals(Environment.GetEnvironmentVariable(SkipUploadEnvVar), "1", StringComparison.Ordinal);

    public static bool EnsureProjectPage =>
        string.Equals(Environment.GetEnvironmentVariable(EnsureProjectPageEnvVar), "1", StringComparison.Ordinal);

    public static bool CleanupProbe =>
        !string.Equals(Environment.GetEnvironmentVariable(CleanupProbeEnvVar), "0", StringComparison.Ordinal);

    public static bool CleanupAllProbes =>
        string.Equals(Environment.GetEnvironmentVariable(CleanupAllProbesEnvVar), "1", StringComparison.Ordinal);

    public static bool RefreshMatchedSources =>
        string.Equals(Environment.GetEnvironmentVariable(RefreshMatchedSourcesEnvVar), "1", StringComparison.Ordinal);

    public static bool SkipAttachVerify =>
        string.Equals(Environment.GetEnvironmentVariable(SkipAttachVerifyEnvVar), "1", StringComparison.Ordinal);

    public static bool SkipAttachSidebar =>
        string.Equals(Environment.GetEnvironmentVariable(SkipAttachSidebarEnvVar), "1", StringComparison.Ordinal);

    public static bool DownloadFailFast =>
        string.Equals(Environment.GetEnvironmentVariable(DownloadFailFastEnvVar), "1", StringComparison.Ordinal);

    public async Task<SourceSyncPerfReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = new SourceSyncPerfReport
        {
            Tier = "Live",
            MachineName = Environment.MachineName,
        };

        var timeout = ResolveRunTimeout();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await _host.RunOnUiAsync(
                () => RunOnUiAsync(report, timeoutCts.Token),
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            report.AddStep(new SourceSyncPerfStep
            {
                Id = "live_run_timeout",
                Phase = "other",
                DurationMs = (long)timeout.TotalMilliseconds,
                Error = $"Live perf run exceeded {timeout.TotalMinutes:0} minute timeout.",
            });
            throw;
        }
        finally
        {
            report.WriteToDisk();
        }

        return report;
    }

    private static TimeSpan ResolveRunTimeout()
    {
        var raw = Environment.GetEnvironmentVariable(TimeoutMinutesEnvVar);
        return int.TryParse(raw, out var minutes) && minutes > 0
            ? TimeSpan.FromMinutes(minutes)
            : DefaultRunTimeout;
    }

    private static int ResolveDownloadMax()
    {
        var raw = Environment.GetEnvironmentVariable(DownloadMaxEnvVar);
        return int.TryParse(raw, out var max) && max >= 0 ? max : DefaultDownloadMax;
    }

    private static SnorlaxAttachOptions ResolvePerfAttachOptions() =>
        new(
            SkipOwnershipVerify: SkipAttachVerify,
            SkipPostAttachSidebar: SkipAttachSidebar);

    private async Task RunOnUiAsync(SourceSyncPerfReport report, CancellationToken cancellationToken)
    {
        var core = RequireCore();
        var bridge = RequireBridge();

        await RunStep(report, "find", "live_webview_ready", () =>
        {
            _ = core;
            return Task.FromResult($"source={core.Source}");
        });

        await RunStep(report, "find", "live_bridge_inject", async () =>
        {
            await NavigateChatGptAsync(core, cancellationToken);
            await bridge.InjectAsync(core);
            await bridge.WaitForBridgeReadyAsync(core, 20000, cancellationToken);
            return $"href={core.Source}";
        });

        var api = new ChatGptProjectApiService(bridge);
        string? gizmoId = Environment.GetEnvironmentVariable(GizmoIdEnvVar);

        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            var projects = await api.ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
            gizmoId = projects.FirstOrDefault(p => ChatGptProjectApiService.IsSnorlaxProjectId(p.Id))?.Id
                      ?? projects.FirstOrDefault()?.Id;
        }

        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            report.AddStep(new SourceSyncPerfStep
            {
                Id = "live_resolve_gizmo",
                Phase = "find",
                DurationMs = 0,
                Error = "No linked project found. Set CGW_PERF_GIZMO_ID or create a ChatGPT project.",
            });
            return;
        }

        report.GizmoId = gizmoId;

        var previousRoot = AppDirectories.TestRootOverride;
        AppDirectories.TestRootOverride = Path.Combine(
            Path.GetTempPath(),
            "ChatGPTWrapper-LiveSourcePerf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(AppDirectories.TestRootOverride);

        var bundle = AdventureTestData.CreateLinkedBundle(projectId: gizmoId);
        AdventureStore.Save(bundle);

        string? probeFileId = null;
        var probeName = $"cgw-perf-probe-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.md";
        var perfAttachOptions = ResolvePerfAttachOptions();

        try
        {
            IReadOnlyList<GizmoFileRef>? firstList = null;

            await RunStep(report, "find", "live_list_remote_direct", async () =>
            {
                ProjectRemoteListCache.Invalidate(gizmoId);
                firstList = await api.GetProjectFilesDirectAsync(
                    core,
                    gizmoId,
                    cancellationToken,
                    ensureProjectPage: false);
                report.FileCount = firstList.Count;
                var probePath = Path.Combine(AppDirectories.Root, "last-file-list-probe.json");
                var probeDetail = File.Exists(probePath) ? "probe_written" : "no_probe";
                return $"count={firstList.Count} {probeDetail}";
            });

            if (CleanupAllProbes && firstList is { Count: > 0 })
            {
                await RunStep(report, "modify", "live_cleanup_all_probes", async () =>
                {
                    var deleted = 0;
                    foreach (var remote in firstList)
                    {
                        var name = remote.Name ?? "";
                        if (!name.StartsWith("cgw-perf-probe-", StringComparison.Ordinal))
                            continue;

                        if (string.IsNullOrWhiteSpace(remote.FileId))
                            continue;

                        try
                        {
                            await api.DeleteProjectFileAsync(core, gizmoId, remote.FileId, cancellationToken);
                            deleted++;
                        }
                        catch
                        {
                            /* best effort */
                        }
                    }

                    ProjectRemoteListCache.Invalidate(gizmoId);
                    firstList = await api.GetProjectFilesDirectAsync(
                        core,
                        gizmoId,
                        cancellationToken,
                        ensureProjectPage: false);
                    report.FileCount = firstList.Count;
                    return $"deleted={deleted} remaining={firstList.Count}";
                });
            }

            await RunStep(report, "find", "live_list_remote_cached", () =>
            {
                if (firstList is { Count: > 0 })
                    ProjectRemoteListCache.Set(gizmoId, firstList);

                var sw = Stopwatch.StartNew();
                var hit = ProjectRemoteListCache.TryGet(gizmoId, out var cached);
                sw.Stop();
                return Task.FromResult($"cacheHit={hit} count={cached?.Count ?? 0} elapsed={sw.ElapsedMilliseconds}ms");
            });

            await RunStep(report, "read", "live_export_local", () =>
            {
                AdventureTestData.WriteLocalSources(bundle);
                return Task.FromResult($"entries={bundle.SourceManifest.Entries.Count}");
            });

            var sync = new ProjectSourceSyncService(api);
            var orchestrator = new ProjectFileSyncOrchestrator(api, sync);

            await RunStep(report, "find", "live_build_plan_status", async () =>
            {
                var plan = await orchestrator.BuildStatusPlanAsync(
                    core,
                    bundle,
                    ensureProjectPage: false,
                    cachedRemoteFiles: firstList);
                return $"items={plan.Items.Count} blocked={plan.SyncBlocked}";
            });

            SourceSyncPlan? fullPlan = null;
            await RunStep(report, "find", "live_build_plan_full", async () =>
            {
                ProjectRemoteListCache.Invalidate(gizmoId);
                fullPlan = await orchestrator.BuildPlanAsync(
                    core,
                    bundle,
                    ensureProjectPage: false,
                    exportSources: true);
                return $"items={fullPlan.Items.Count} unmatched={fullPlan.UnmatchedRemoteFiles.Count}";
            });

            if (EnsureProjectPage)
            {
                await RunStep(report, "find", "live_ensure_project_page", async () =>
                {
                    await api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
                    return $"href={core.Source}";
                });
            }

            if (RefreshMatchedSources && fullPlan is not null)
            {
                await RunStep(report, "modify", "live_refresh_matched_sources", async () =>
                {
                    var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
                    var refreshed = 0;
                    var skipped = 0;
                    foreach (var item in fullPlan.Items)
                    {
                        var localPath = Path.Combine(sourcesDir, item.Entry.RelativePath);
                        if (!File.Exists(localPath))
                        {
                            skipped++;
                            continue;
                        }

                        var bytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
                        var uploaded = await api.UploadProjectFileBytesAsync(
                            core,
                            gizmoId,
                            item.Entry.RelativePath,
                            bytes,
                            "text/markdown",
                            cancellationToken: cancellationToken);
                        if (uploaded?.FileId is null)
                        {
                            skipped++;
                            continue;
                        }

                        await api.AttachProjectFilesViaUpsertAsync(
                            core,
                            gizmoId,
                            [uploaded],
                            caller: "LiveSourceSyncPerf",
                            skipPreflight: true,
                            cancellationToken: cancellationToken,
                            attachOptions: perfAttachOptions);

                        item.Entry.RemoteFileId = uploaded.FileId;
                        item.Entry.RemoteFileName = uploaded.Name;
                        refreshed++;
                    }

                    ProjectRemoteListCache.Invalidate(gizmoId);
                    var remote = await api.GetProjectFilesDirectAsync(
                        core,
                        gizmoId,
                        cancellationToken,
                        ensureProjectPage: false);
                    fullPlan.DetectedRemoteFiles = remote.ToList();
                    return $"refreshed={refreshed} skipped={skipped}";
                });
            }

            var downloadMax = ResolveDownloadMax();
            if (downloadMax > 0 && fullPlan is not null)
            {
                var downloadTargets = fullPlan.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Entry.RemoteFileId))
                    .Select(item =>
                    {
                        var remote = fullPlan.DetectedRemoteFiles.FirstOrDefault(
                            f => string.Equals(f.FileId, item.Entry.RemoteFileId, StringComparison.Ordinal));
                        return (Item: item, Remote: remote);
                    })
                    .Where(x => x.Remote is not null)
                    .Take(downloadMax)
                    .ToList();

                foreach (var target in downloadTargets)
                {
                    var remote = target.Remote!;
                    var fileId = remote.FileId;
                    var fileName = remote.Name ?? fileId;
                    await RunStep(report, "download", $"live_download_{SanitizeStepId(fileName)}", async () =>
                    {
                        var tempPath = Path.Combine(Path.GetTempPath(), $"cgw-perf-{fileId}.bin");
                        var location = remote.Location ?? "(unknown)";
                        await api.DownloadFileToPathAsync(
                            core,
                            fileId,
                            tempPath,
                            cancellationToken,
                            gizmoId,
                            remote.Location,
                            failFast: DownloadFailFast);
                        var hash = ProjectSourceExportService.ComputeSha256(tempPath);
                        try { File.Delete(tempPath); } catch { /* ignore */ }
                        return $"fileId={fileId} location={location} hash={hash[..Math.Min(12, hash.Length)]}";
                    });
                }
            }
            else if (downloadMax == 0)
            {
                report.AddStep(new SourceSyncPerfStep
                {
                    Id = "live_download_skipped",
                    Phase = "download",
                    DurationMs = 0,
                    Detail = $"{DownloadMaxEnvVar}=0",
                });
            }

            await RunStep(report, "read", "live_read_local_sources", async () =>
            {
                var dir = ProjectSourceExportService.SourcesDirectory(bundle);
                long total = 0;
                var count = 0;
                foreach (var entry in bundle.SourceManifest.Entries)
                {
                    var path = Path.Combine(dir, entry.RelativePath);
                    if (!File.Exists(path))
                        continue;

                    total += (await File.ReadAllBytesAsync(path, cancellationToken)).Length;
                    count++;
                }

                return $"files={count} bytes={total}";
            });

            if (!SkipUpload)
            {
                await RunStep(report, "upload", "live_upload_probe_file", async () =>
                {
                    var content = System.Text.Encoding.UTF8.GetBytes("# CGW source sync perf probe\n");
                    var uploaded = await api.UploadProjectFileBytesAsync(
                        core,
                        gizmoId,
                        probeName,
                        content,
                        "text/markdown",
                        cancellationToken: cancellationToken);
                    probeFileId = uploaded?.FileId;
                    return $"fileId={probeFileId ?? "(none)"} name={probeName}";
                });

                if (!string.IsNullOrWhiteSpace(probeFileId))
                {
                    var attachStepId = SkipAttachVerify || SkipAttachSidebar
                        ? "live_attach_probe_fast"
                        : "live_attach_probe";

                    await RunStep(report, "upload", attachStepId, async () =>
                    {
                        var attached = await api.AttachProjectFilesViaUpsertAsync(
                            core,
                            gizmoId,
                            [new GizmoFileRef { FileId = probeFileId!, Name = probeName }],
                            caller: "LiveSourceSyncPerf",
                            skipPreflight: true,
                            cancellationToken: cancellationToken,
                            attachOptions: perfAttachOptions);
                        var remote = await api.GetProjectFilesDirectAsync(
                            core,
                            gizmoId,
                            cancellationToken,
                            ensureProjectPage: false);
                        var visible = remote.Any(f => string.Equals(f.FileId, probeFileId, StringComparison.Ordinal));
                        var flags = $"verify={(SkipAttachVerify ? "skipped" : "on")} sidebar={(SkipAttachSidebar ? "skipped" : "on")}";
                        return $"usedUpsertFallback={attached} visible={visible} {flags}";
                    });
                }

                await RunStep(report, "upload", "live_apply_safe_subset", async () =>
                {
                    var plan = await sync.BuildPlanAsync(
                        core,
                        bundle,
                        ensureProjectPage: false,
                        exportSources: false);
                    var result = await sync.ApplyPlanAsync(
                        core,
                        bundle,
                        plan,
                        autoSafeOnly: true,
                        cancellationToken: cancellationToken,
                        ensureProjectPage: EnsureProjectPage);
                    MergeTracePhases(report, result.RunSummaryPath);
                    return $"success={result.Success} uploaded={result.Uploaded} pulled={result.Pulled} skipped={result.Skipped}"
                           + (string.IsNullOrWhiteSpace(result.Error) ? "" : $" error={result.Error}");
                });
            }
            else
            {
                report.AddStep(new SourceSyncPerfStep
                {
                    Id = "live_upload_skipped",
                    Phase = "upload",
                    DurationMs = 0,
                    Detail = $"{SkipUploadEnvVar}=1",
                });
            }
        }
        finally
        {
            if (CleanupProbe && !string.IsNullOrWhiteSpace(probeFileId))
            {
                try
                {
                    await api.DeleteProjectFileAsync(core, gizmoId, probeFileId, cancellationToken);
                    report.AddStep(new SourceSyncPerfStep
                    {
                        Id = "live_cleanup_probe",
                        Phase = "modify",
                        DurationMs = 0,
                        Detail = $"deleted={probeFileId}",
                    });
                }
                catch (Exception ex)
                {
                    report.AddStep(new SourceSyncPerfStep
                    {
                        Id = "live_cleanup_probe",
                        Phase = "modify",
                        DurationMs = 0,
                        Error = ex.Message,
                    });
                }
            }

            AppDirectories.TestRootOverride = previousRoot;
            AdventureTestData.DeleteBundle(bundle);
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
            report.AddTracePhase(phase.ToString().ToLowerInvariant(), durationMs);
    }

    private static string SanitizeStepId(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Trim('_');
    }

    private async Task RunStep(
        SourceSyncPerfReport report,
        string phase,
        string id,
        Func<Task<string>> work) =>
        await SourceSyncPerfRunnerBase.RunStep(report, phase, id, work);

    private static async Task NavigateChatGptAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var current)
            && ChatGptUrls.IsTrustedChatGptTopLevelUri(current))
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            if (Uri.TryCreate(core.Source, UriKind.Absolute, out var u)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(u))
                tcs.TrySetResult();
        }

        core.NavigationCompleted += Handler;
        try
        {
            core.Navigate("https://chatgpt.com");
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private CoreWebView2 RequireCore() =>
        _host.Core ?? throw new InvalidOperationException("WebView2 core not ready");

    private ChatGptApiBridgeInjection RequireBridge() =>
        _host.Bridge ?? throw new InvalidOperationException("Bridge not registered");
}
