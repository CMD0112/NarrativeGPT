using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ProjectDiscoveryService
{
    public static string TracePath => Path.Combine(AppDirectories.Root, "project-discovery-trace.jsonl");

    public async Task<ProjectDiscoveryResult> DiscoverAsync(
        ChatGptProjectApiService api,
        ChatGptApiBridgeInjection bridge,
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await api.PrepareForApiAsync(core, cancellationToken);

        var byId = new Dictionary<string, GizmoSummary>(StringComparer.Ordinal);
        var strategies = new List<string>();
        var diagnostics = new List<string>();
        var rawCount = 0;

        async Task MergeAsync(string strategy, IReadOnlyList<GizmoSummary> batch)
        {
            if (batch.Count == 0)
                return;

            if (!strategies.Contains(strategy))
                strategies.Add(strategy);

            rawCount += batch.Count;
            foreach (var p in batch)
                byId[p.Id] = p;

            await TraceAsync(strategy, batch.Count, byId.Count);
        }

        try
        {
            var sidebar = await api.ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
            await MergeAsync("ApiSidebar", sidebar);
            diagnostics.Add($"sidebar:{sidebar.Count}");
        }
        catch (Exception ex)
        {
            diagnostics.Add($"sidebar_error:{ex.Message}");
            await TraceAsync("ApiSidebar", 0, 0, ex.Message);
        }

        if (byId.Count == 0)
        {
            try
            {
                var bootstrap = await api.ListProjectsFromBootstrapAsync(core, cancellationToken);
                await MergeAsync("ApiBootstrap", bootstrap);
                diagnostics.Add($"bootstrap:{bootstrap.Count}");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"bootstrap_error:{ex.Message}");
                await TraceAsync("ApiBootstrap", 0, 0, ex.Message);
            }
        }

        if (byId.Count == 0)
        {
            try
            {
                var dom = await api.ListProjectsFromDomAsync(bridge, core, cancellationToken);
                await MergeAsync("DomProjects", dom);
                diagnostics.Add($"dom:{dom.Count}");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"dom_error:{ex.Message}");
                await TraceAsync("DomProjects", 0, 0, ex.Message);
            }
        }

        var projects = byId.Values.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        return new ProjectDiscoveryResult
        {
            Projects = projects,
            StrategiesUsed = strategies,
            Diagnostics = string.Join("; ", diagnostics),
            RawItemCount = rawCount,
        };
    }

    private static async Task TraceAsync(string strategy, int batchCount, int mergedCount, string? error = null)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var line = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                strategy,
                batchCount,
                mergedCount,
                error,
            });
            await File.AppendAllTextAsync(TracePath, line + Environment.NewLine);

            DiagnosticsMirror.WriteText(
                DiagnosticsChannel.Api,
                DiagnosticsLevel.Debug,
                "project_discovery",
                $"strategy={strategy} batches={batchCount} merged={mergedCount}",
                source: "project-discovery-trace",
                data: new { strategy, batchCount, mergedCount, error });
        }
        catch
        {
            /* ignore */
        }
    }
}
