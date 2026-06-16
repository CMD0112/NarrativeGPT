using System.Collections;
using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ChatGptProjectApiService
{
    private readonly ChatGptApiBridgeInjection _bridge;

    public ChatGptProjectApiService(ChatGptApiBridgeInjection bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<ChatGptSessionInfo> GetSessionAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        var msg = await _bridge.SendAsync(
            core,
            new { action = "getSession" },
            cancellationToken: cancellationToken);

        EnsureOk(msg, ChatGptApiEndpoints.Session);

        var json = msg.Json;
        if (json is null || json.Value.ValueKind != JsonValueKind.Object)
            return new ChatGptSessionInfo { IsAuthenticated = msg.Ok };

        var root = json.Value;
        return new ChatGptSessionInfo
        {
            IsAuthenticated = root.TryGetProperty("authenticated", out var a) && a.ValueKind == JsonValueKind.True,
            UserId = root.TryGetProperty("userId", out var u) ? u.GetString() : null,
            Email = root.TryGetProperty("email", out var e) ? e.GetString() : null,
        };
    }

    public async Task PrepareForApiAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await _bridge.WaitForInjectablePageAsync(core, cancellationToken: cancellationToken);
        var session = await GetSessionAsync(core, cancellationToken);
        if (!session.IsAuthenticated)
        {
            throw new ChatGptApiException(
                "Not logged in to ChatGPT. Sign in on the Adventure tab, then click Refresh.",
                ChatGptApiEndpoints.Session,
                401);
        }
    }

    public async Task<GizmoSummary?> GetGizmoDetailAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectPageAsync(core, gizmoId, cancellationToken);

        var path = ChatGptApiEndpoints.GizmoDetail(gizmoId);
        var msg = await _bridge.SendAsync(
            core,
            ApiCommand("GET", path),
            cancellationToken: cancellationToken);

        if (msg.Status == 404)
            return null;

        EnsureOk(msg, path);

        if (msg.Json is not { } json)
            return null;

        if (json.TryGetProperty("gizmo", out var wrap))
        {
            if (wrap.TryGetProperty("gizmo", out var inner))
                return GizmoResponseParser.ParseGizmoNode(inner, wrap);
            return GizmoResponseParser.ParseGizmoNode(wrap, wrap);
        }

        return GizmoResponseParser.ParseGizmoNode(json, json);
    }

    public async Task EnsureProjectPageAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        await PrepareForApiAsync(core, cancellationToken);

        if (ChatGptPageGate.TestAllowAnyInjectablePage)
        {
            await WaitForDocumentReadyAsync(core, cancellationToken);
            await _bridge.InjectAsync(core);
            return;
        }

        var targetUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        if (!IsOnProjectPage(core, gizmoId))
        {
            ProjectLinkDiagnostics.Log($"Navigating to project page {targetUrl}");
            core.Navigate(targetUrl);
            await WaitForProjectNavigationAsync(core, gizmoId, cancellationToken);
        }

        await WaitForDocumentReadyAsync(core, cancellationToken);
        await _bridge.InjectAsync(core);
        await TryCaptureAccountIdAsync(core, cancellationToken);
        await SeedProjectClientHeadersAsync(core, targetUrl);
    }

    private async Task TryCaptureAccountIdAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var msg = await _bridge.SendAsync(
            core,
            new { action = "getSession" },
            timeoutMs: 15000,
            cancellationToken: cancellationToken,
            skipReadyWait: true);

        if (!msg.Ok)
            return;

        string? accountId = null;
        if (msg.Root.TryGetProperty("accountId", out var aid) && aid.ValueKind == JsonValueKind.String)
            accountId = aid.GetString();
        else if (msg.Json is { } json && json.TryGetProperty("accountId", out var aid2))
            accountId = aid2.GetString();

        if (!string.IsNullOrWhiteSpace(accountId))
            ChatGptApiClientProfile.SaveHeader("ChatGPT-Account-Id", accountId);
    }

    private static bool IsOnProjectPage(CoreWebView2 core, string gizmoId)
    {
        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri))
            return false;

        if (!ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return false;

        if (ChatGptUrls.TryParseGizmoId(uri, out var parsed)
            && ChatGptUrls.GizmoIdsEqual(parsed, gizmoId))
        {
            return true;
        }

        return SourceMentionsGizmo(core.Source, gizmoId);
    }

    private static bool SourceMentionsGizmo(string? source, string gizmoId)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            return false;
        }

        return source.Contains(gizmoId, StringComparison.OrdinalIgnoreCase)
               && (ChatGptUrls.IsProjectWorkspace(uri)
                   || source.Contains("/g/", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WaitForProjectNavigationAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        if (IsOnProjectPage(core, gizmoId))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && (IsOnProjectPage(core, gizmoId) || SourceMentionsGizmo(core.Source, gizmoId)))
                tcs.TrySetResult();
        }

        core.NavigationCompleted += Handler;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsOnProjectPage(core, gizmoId))
                    return;

                if (SourceMentionsGizmo(core.Source, gizmoId))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Proceeding with project API context despite non-canonical URL: {core.Source}");
                    return;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                var waitSlice = remaining < TimeSpan.FromMilliseconds(500)
                    ? remaining
                    : TimeSpan.FromMilliseconds(500);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(waitSlice, cancellationToken));
                if (completed == tcs.Task)
                {
                    await tcs.Task;
                    return;
                }
            }

            ProjectLinkDiagnostics.Log(
                $"Project page navigation slow; source={core.Source} target={ChatGptUrls.BuildProjectUrl(gizmoId)}");

            if (IsOnProjectPage(core, gizmoId) || SourceMentionsGizmo(core.Source, gizmoId))
                return;

            throw new ChatGptApiException(
                $"Timed out opening project page for {gizmoId}. Last URL: {core.Source}",
                ChatGptApiEndpoints.GizmoDetail(gizmoId));
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static async Task WaitForDocumentReadyAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var raw = await core.ExecuteScriptAsync(
                    "(() => document.readyState === 'complete' || document.readyState === 'interactive')");
                if (raw.Contains("true", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch
            {
                /* page may still be loading */
            }

            await Task.Delay(300, cancellationToken);
        }
    }

    private static async Task SeedProjectClientHeadersAsync(CoreWebView2 core, string projectUrl)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ChatGptApiClientProfile.LoadHeaders())
            headers[kv.Key] = kv.Value;

        headers["Referer"] = projectUrl;
        headers["Origin"] = "https://chatgpt.com";

        var json = JsonSerializer.Serialize(headers);
        await core.ExecuteScriptAsync($"globalThis.__CHATGPT_CLIENT_HEADERS__ = {json};");
    }

    public async Task<IReadOnlyList<GizmoSummary>> ListProjectsAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await PrepareForApiAsync(core, cancellationToken);

        var (fromBridge, probeRoot) = await TryListProjectsViaBridgeAsync(core, cancellationToken);
        if (fromBridge.Count > 0)
            return fromBridge;

        var byId = new Dictionary<string, GizmoSummary>(StringComparer.Ordinal);

        foreach (var ownedOnly in new[] { true, false })
        {
            var page = await FetchSidebarPagesAsync(core, ownedOnly, cancellationToken);
            foreach (var p in page)
                byId[p.Id] = p;

            if (byId.Count > 0)
                break;
        }

        var result = byId.Values.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        if (result.Count == 0 && probeRoot is { } probe)
            WriteSidebarProbe(probe, 0);

        return result;
    }

    private async Task<(IReadOnlyList<GizmoSummary> Projects, JsonElement? ProbeRoot)> TryListProjectsViaBridgeAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var msg = await _bridge.SendAsync(
            core,
            new { action = "listProjects" },
            cancellationToken: cancellationToken);

        if (!msg.Ok)
        {
            EnsureOk(msg, ChatGptApiEndpoints.ProjectsSidebar);
            return ([], null);
        }

        ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.ProjectsSidebar, "GET");

        if (msg.Json is not { } root)
            return ([], null);

        var list = new List<GizmoSummary>();
        if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in projects.EnumerateArray())
            {
                var id = p.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                list.Add(new GizmoSummary
                {
                    Id = id,
                    Title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "Project" : "Project",
                    Instructions = p.TryGetProperty("instructions", out var ins) ? ins.GetString() : null,
                });
            }
        }

        if (list.Count == 0)
            WriteSidebarProbe(root, 0);

        return (list, root);
    }

    private static void WriteSidebarProbe(JsonElement probeRoot, int parsedCount)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var path = Path.Combine(AppDirectories.Root, "last-sidebar-probe.json");
            var doc = new Dictionary<string, object?>
            {
                ["at"] = DateTimeOffset.UtcNow,
                ["parsedCount"] = parsedCount,
                ["itemCount"] = probeRoot.TryGetProperty("itemCount", out var ic) && ic.TryGetInt32(out var n)
                    ? n
                    : null,
                ["hasDeviceId"] = probeRoot.TryGetProperty("hasDeviceId", out var hd) && hd.ValueKind == JsonValueKind.True,
                ["hasAccountId"] = probeRoot.TryGetProperty("hasAccountId", out var ha) && ha.ValueKind == JsonValueKind.True,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            /* ignore */
        }
    }

    private async Task<IReadOnlyList<GizmoSummary>> FetchSidebarPagesAsync(
        CoreWebView2 core,
        bool ownedOnly,
        CancellationToken cancellationToken)
    {
        var all = new List<GizmoSummary>();
        string? cursor = null;

        for (var page = 0; page < 50; page++)
        {
            var query = new Dictionary<string, string>
            {
                ["owned_only"] = ownedOnly ? "true" : "false",
                ["conversations_per_gizmo"] = "0",
            };
            if (!string.IsNullOrEmpty(cursor))
                query["cursor"] = cursor;

            var msg = await _bridge.SendAsync(
                core,
                ApiCommand("GET", ChatGptApiEndpoints.ProjectsSidebar, query),
                cancellationToken: cancellationToken);

            EnsureOk(msg, ChatGptApiEndpoints.ProjectsSidebar);
            ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.ProjectsSidebar, "GET");

            if (msg.Json is not { } root)
                break;

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                all.AddRange(GizmoResponseParser.ParseSidebarItems(items));

            if (!TryGetSidebarNextCursor(root, out var next))
                break;

            cursor = next;
        }

        return all;
    }

    private static object ApiCommand(
        string method,
        string path,
        Dictionary<string, string>? query = null,
        object? body = null)
    {
        var headers = new Dictionary<string, string>();
        foreach (var kv in ChatGptApiClientProfile.LoadHeaders())
            headers[kv.Key] = kv.Value;

        if (headers.Count == 0)
        {
            return new
            {
                action = "apiRequest",
                method,
                path,
                query,
                body,
            };
        }

        return new
        {
            action = "apiRequest",
            method,
            path,
            query,
            body,
            headers,
        };
    }

    private static bool TryGetSidebarNextCursor(JsonElement root, out string? cursor)
    {
        cursor = JsonElementParsing.GetCursorOrNull(root);
        return cursor is not null;
    }

    public async Task<IReadOnlyList<GizmoConversationRef>> ListProjectConversationsAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        var path = ChatGptApiEndpoints.ProjectConversations(gizmoId);
        var all = new List<GizmoConversationRef>();
        var offset = 0;

        for (var page = 0; page < 20; page++)
        {
            var msg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "GET",
                    path,
                    query = new Dictionary<string, string> { ["offset"] = offset.ToString() },
                },
                cancellationToken: cancellationToken);

            EnsureOk(msg, path);
            ChatGptApiDiscovery.RecordSuccess(path, "GET");

            if (msg.Json is not { } json)
                break;

            var batch = ParseConversations(json);
            all.AddRange(batch);

            if (!TryGetNextOffset(json, batch.Count, offset, out offset))
                break;
        }

        return all;
    }

    public async Task<GizmoSummary> UpsertProjectAsync(
        CoreWebView2 core,
        string? gizmoId,
        string? title,
        string? instructions,
        IReadOnlyList<GizmoFileRef>? existingFiles = null,
        string caller = "UpsertProject",
        Guid? adventureId = null,
        CancellationToken cancellationToken = default)
    {
        var intent = string.IsNullOrWhiteSpace(gizmoId)
            ? ProjectUpsertIntent.Create
            : ProjectUpsertIntent.Update;

        var result = await UpsertProjectSafeAsync(
            core,
            intent,
            caller,
            adventureId,
            gizmoId,
            title,
            instructions,
            existingFiles,
            cancellationToken);

        return result.Summary
               ?? throw new ChatGptApiException(
                   "Upsert succeeded but project id could not be resolved.",
                   ChatGptApiEndpoints.ProjectUpsert);
    }

    internal async Task<ProjectUpsertResult> UpsertProjectSafeAsync(
        CoreWebView2 core,
        ProjectUpsertIntent intent,
        string caller,
        Guid? adventureId,
        string? gizmoId,
        string? title,
        string? instructions,
        IReadOnlyList<GizmoFileRef>? files,
        CancellationToken cancellationToken = default,
        object? attachBodyOverride = null,
        AttachUpsertAuditContext? attachAudit = null)
    {
        ValidateUpsertIntent(intent, gizmoId);

        var fileCount = files?.Count ?? 0;
        object body;
        if (attachBodyOverride is not null)
        {
            body = attachBodyOverride;
        }
        else if (intent == ProjectUpsertIntent.AttachFiles)
        {
            var attachTitle = title;
            var attachInstructions = instructions;
            if (string.IsNullOrWhiteSpace(attachTitle) || attachInstructions is null)
            {
                var projectSummary = await GetProjectSummaryAsync(core, gizmoId!, cancellationToken);
                attachTitle ??= projectSummary?.Title ?? "Project";
                attachInstructions ??= projectSummary?.Instructions ?? "";
            }

            body = BuildUpsertAttachBody(gizmoId!, attachTitle, attachInstructions, files);
        }
        else
        {
            body = BuildUpsertBody(gizmoId, title, instructions, files);
        }

        string? requestBodyJson = null;
        try
        {
            requestBodyJson = JsonSerializer.Serialize(body);
        }
        catch
        {
            /* ignore non-serializable body */
        }

        var auditEntry = attachAudit is null && requestBodyJson is null
            ? null
            : new AttachUpsertAuditContext
            {
                Location = attachAudit?.Location,
                DetailBody = attachAudit?.DetailBody ?? false,
                MergedCount = attachAudit?.MergedCount,
                AttachFileName = attachAudit?.AttachFileName,
                RequestBody = requestBodyJson ?? attachAudit?.RequestBody,
            };

        var msg = await _bridge.SendAsync(
            core,
            new
            {
                action = "apiRequest",
                method = "POST",
                path = ChatGptApiEndpoints.ProjectUpsert,
                body,
            },
            timeoutMs: 90000,
            cancellationToken: cancellationToken);

        if (!msg.Ok)
        {
            ProjectUpsertAudit.Record(
                intent,
                caller,
                adventureId,
                gizmoId,
                title,
                fileCount,
                msg.Status,
                null,
                null,
                ProjectUpsertOutcome.Failed,
                msg.BodyText ?? msg.Message,
                auditEntry);

        EnsureOk(msg, ChatGptApiEndpoints.ProjectUpsert);
        }

        ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.ProjectUpsert, "POST");

        var summary = await TryResolveUpsertSummaryAsync(
            core,
            intent,
            gizmoId,
            title,
            msg,
            cancellationToken);
        var responseId = summary?.Id;

        if (intent == ProjectUpsertIntent.AttachFiles
            && string.IsNullOrWhiteSpace(responseId)
            && !string.IsNullOrWhiteSpace(gizmoId))
        {
            responseId = TryExtractUpsertResponseGizmoId(msg);
            if (!string.IsNullOrWhiteSpace(responseId))
            {
                summary ??= new GizmoSummary
                {
                    Id = responseId,
                    Title = title ?? "Project",
                    Instructions = instructions ?? "",
                };
            }
            else
            {
                ProjectLinkDiagnostics.Log(
                    $"Attach upsert returned HTTP {msg.Status} without project id for {gizmoId}; "
                    + "caller must verify on sidebar or fail");
            }
        }

        var outcome = ProjectUpsertAudit.ClassifyOutcome(intent, gizmoId, responseId);

        ProjectUpsertAudit.Record(
            intent,
            caller,
            adventureId,
            gizmoId,
            title,
            fileCount,
            msg.Status,
            responseId,
            summary?.Title,
            outcome,
            msg.BodyText,
            auditEntry);

        if (outcome == ProjectUpsertOutcome.IdMismatch)
        {
            ProjectLinkDiagnostics.Log(
                $"Upsert id mismatch intent={intent.ToString().ToLowerInvariant()} "
                + $"expected={gizmoId} got={responseId} "
                + $"requestBody={ProjectUpsertAudit.Truncate(requestBodyJson, 300)} "
                + $"responseBody={ProjectUpsertAudit.Truncate(msg.BodyText, 300)}");
            var mismatchMessage = intent == ProjectUpsertIntent.AttachFiles
                ? $"upsert_forked_duplicate: upsert returned project {responseId} instead of {gizmoId}. "
                  + "Delete the duplicate project in the ChatGPT sidebar and retry."
                : $"upsert_id_mismatch: expected {gizmoId}, got {responseId}";
            throw new ChatGptApiException(
                mismatchMessage,
                ChatGptApiEndpoints.ProjectUpsert,
                msg.Status,
                msg.BodyText);
        }

        if (intent == ProjectUpsertIntent.AttachFiles
            && !string.IsNullOrWhiteSpace(gizmoId)
            && outcome == ProjectUpsertOutcome.Created)
        {
            throw new ChatGptApiException(
                "upsert_created_instead_of_attach: snorlax upsert created a new project instead of updating the linked id",
                ChatGptApiEndpoints.ProjectUpsert,
                msg.Status,
                msg.BodyText);
        }

        if (intent == ProjectUpsertIntent.Create && string.IsNullOrWhiteSpace(responseId))
        {
            throw new ChatGptApiException(
                "Upsert create succeeded but project id could not be resolved.",
                ChatGptApiEndpoints.ProjectUpsert,
                msg.Status,
                msg.BodyText);
        }

        if (intent is ProjectUpsertIntent.Update
            && string.IsNullOrWhiteSpace(responseId))
        {
            throw new ChatGptApiException(
                $"upsert_id_mismatch: expected {gizmoId}, response had no project id",
                ChatGptApiEndpoints.ProjectUpsert,
                msg.Status,
                msg.BodyText);
        }

        return new ProjectUpsertResult
        {
            Message = msg,
            Summary = summary,
            Outcome = outcome,
        };
    }

    private static void ValidateUpsertIntent(ProjectUpsertIntent intent, string? gizmoId)
    {
        switch (intent)
        {
            case ProjectUpsertIntent.Create when !string.IsNullOrWhiteSpace(gizmoId):
                throw new ArgumentException("Create upsert must not include a gizmo id.", nameof(gizmoId));
            case ProjectUpsertIntent.Update or ProjectUpsertIntent.AttachFiles
                when string.IsNullOrWhiteSpace(gizmoId):
                throw new ArgumentException(
                    $"{intent} upsert requires a gizmo id.",
                    nameof(gizmoId));
        }
    }

    private async Task<GizmoSummary?> TryResolveUpsertSummaryAsync(
        CoreWebView2 core,
        ProjectUpsertIntent intent,
        string? gizmoId,
        string? title,
        ApiBridgeMessage msg,
        CancellationToken cancellationToken)
    {
        if (msg.Json is { } json)
        {
            var parsed = GizmoResponseParser.TryParseGizmoFromUpsert(json);
            if (parsed is not null)
                return parsed;
        }

        if (intent == ProjectUpsertIntent.AttachFiles)
            return null;

        if (!string.IsNullOrWhiteSpace(gizmoId))
        {
            var list = await ListProjectsAsync(core, cancellationToken);
            var hit = list.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, gizmoId));
            if (hit is not null)
                return hit;
        }

        if (intent == ProjectUpsertIntent.Create && !string.IsNullOrWhiteSpace(title))
        {
            var refreshed = await ListProjectsAsync(core, cancellationToken);
            var byTitle = refreshed.FirstOrDefault(p =>
                string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
            if (byTitle is not null)
            {
                ProjectLinkDiagnostics.Log(
                    $"Upsert create resolved project id via title fallback title={title} id={byTitle.Id}");
                return byTitle;
            }
        }

        return null;
    }

    public async Task<ProjectSidebarSnapshotResult> LogSidebarTitleSnapshotAsync(
        CoreWebView2 core,
        string? linkedGizmoId,
        string title,
        Guid adventureId,
        string context,
        CancellationToken cancellationToken = default)
    {
        await PrepareForApiAsync(core, cancellationToken);

        var byId = new Dictionary<string, GizmoSummary>(StringComparer.Ordinal);
        foreach (var ownedOnly in new[] { true, false })
        {
            foreach (var project in await FetchSidebarPagesAsync(core, ownedOnly, cancellationToken))
                byId[project.Id] = project;

            if (byId.Count > 0)
                break;
        }

        var linkedIdFound = !string.IsNullOrWhiteSpace(linkedGizmoId)
                            && byId.Values.Any(p => ChatGptUrls.GizmoIdsEqual(p.Id, linkedGizmoId));

        var linkedTitle = !string.IsNullOrWhiteSpace(linkedGizmoId)
            ? byId.Values.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, linkedGizmoId))?.Title
            : null;

        var titleToMatch = !string.IsNullOrWhiteSpace(linkedTitle) ? linkedTitle : title;
        var sameTitleProjects = byId.Values
            .Where(p => string.Equals(p.Title, titleToMatch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var linkedFileCount = 0;
        if (!string.IsNullOrWhiteSpace(linkedGizmoId))
        {
            var files = await GetProjectFilesDirectAsync(
                core,
                linkedGizmoId,
                cancellationToken,
                ensureProjectPage: false);
            linkedFileCount = files.Count;
        }

        string? warning = null;
        if (sameTitleProjects.Count > 1)
        {
            warning =
                "ChatGPT may have duplicated this project. Check the sidebar and delete extra copies with the same name.";
        }

        ProjectLinkDiagnostics.LogSidebarTitleSnapshot(
            context,
            adventureId,
            titleToMatch,
            linkedGizmoId,
            linkedIdFound,
            linkedFileCount,
            sameTitleProjects);

        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.SidebarBaselineSnapshot,
            SyncTraceCategory.Sidebar,
            SyncTraceLevel.Info,
            $"Sidebar snapshot context={context}",
            phase: SyncTracePhase.Sidebar,
            data: new
            {
                context,
                linkedIdFound,
                linkedFileCount,
                sameTitleProjectCount = sameTitleProjects.Count,
                sameTitleProjectIds = sameTitleProjects.Select(p => p.Id).ToList(),
                warning,
            });

        if (!string.IsNullOrWhiteSpace(warning))
        {
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.SidebarForkDetected,
                SyncTraceCategory.Sidebar,
                SyncTraceLevel.Warn,
                warning,
                phase: SyncTracePhase.Sidebar,
                outcome: "warn",
                data: new { sameTitleProjectCount = sameTitleProjects.Count });
        }

        return new ProjectSidebarSnapshotResult
        {
            LinkedIdFound = linkedIdFound,
            LinkedFileCount = linkedFileCount,
            SameTitleProjectCount = sameTitleProjects.Count,
            SameTitleProjectIds = sameTitleProjects.Select(p => p.Id).ToList(),
            Warning = warning,
        };
    }

    public async Task<ProjectSyncPreflightResult> ValidateSyncPreflightAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        await PrepareForApiAsync(core, cancellationToken);

        var sidebar = await ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
        var linked = sidebar.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, gizmoId));
        if (linked is null)
        {
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.PreflightBlocked,
                SyncTraceCategory.Preflight,
                SyncTraceLevel.Warn,
                $"Linked project {gizmoId} not in sidebar",
                phase: SyncTracePhase.Preflight,
                outcome: "blocked",
                data: new { errorCode = "linked_project_not_in_sidebar", gizmoId });
            return ProjectSyncPreflightResult.Blocked(
                "linked_project_not_in_sidebar",
                $"Linked project {gizmoId} was not found in the ChatGPT sidebar. Open the project in ChatGPT or re-link.");
        }

        var sameTitleProjects = sidebar
            .Where(p => IsSnorlaxProjectId(p.Id)
                        && string.Equals(p.Title, linked.Title, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameTitleProjects.Count > 1)
        {
            var ids = sameTitleProjects.Select(p => p.Id).ToList();
            var orphanIds = ids.Where(id => !ChatGptUrls.GizmoIdsEqual(id, gizmoId)).ToList();
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.SidebarForkDetected,
                SyncTraceCategory.Sidebar,
                SyncTraceLevel.Warn,
                $"Duplicate sidebar projects for title {linked.Title}",
                phase: SyncTracePhase.Preflight,
                outcome: "blocked",
                data: new { linkedGizmoId = gizmoId, orphanIds, allIds = ids });
            return ProjectSyncPreflightResult.Blocked(
                "duplicate_projects_exist",
                FormatOrphanForkRecoveryMessage(gizmoId, linked.Title, orphanIds),
                ids);
        }

        return ProjectSyncPreflightResult.Ok();
    }

    internal static string FormatOrphanForkRecoveryMessage(
        string linkedGizmoId,
        string projectTitle,
        IReadOnlyList<string> orphanIds)
    {
        var deleteList = orphanIds.Count > 0
            ? string.Join(", ", orphanIds)
            : "(check ChatGPT sidebar for same-title projects)";

        return "Multiple ChatGPT projects named \""
               + projectTitle
               + "\" were found (likely orphan forks from a failed sync). "
               + $"Keep the linked project {linkedGizmoId}. "
               + "Delete: "
               + deleteList
               + ". Then delete orphan projects in the ChatGPT sidebar, refresh the ChatGPT tab, restart ChatGPT Wrapper, and Apply Safe again.";
    }

    public async Task<ProjectSyncPreflightResult> ValidateSnorlaxAttachCanUpdateAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSnorlaxProjectId(gizmoId))
            return ProjectSyncPreflightResult.Ok();

        var preflight = await ValidateSyncPreflightAsync(core, gizmoId, cancellationToken);
        if (!preflight.Allowed)
            return preflight;

        var detailJson = await GetGizmoDetailJsonAsync(
            core,
            gizmoId,
            cancellationToken,
            ensureProjectPage: false);
        if (detailJson is null)
        {
            return await BlockWithRefreshedOrphanMessageAsync(
                core,
                gizmoId,
                "detail_unavailable",
                "Could not load ChatGPT project detail for attach validation. Open the linked project in ChatGPT and retry.",
                cancellationToken);
        }

        if (!TryValidateDetailForAttachReadOnly(detailJson.Value, gizmoId, out var detailError))
        {
            return await BlockWithRefreshedOrphanMessageAsync(
                core,
                gizmoId,
                "detail_invalid",
                detailError ?? "Gizmo detail failed read-only attach validation.",
                cancellationToken);
        }

        var detailFiles = GizmoResponseParser.CollectFileRefsDeep(detailJson.Value);
        var location = InferAttachFileLocationFromDetail(detailFiles);
        ProjectLinkDiagnostics.Log(
            $"Snorlax attach canary read-only ok for {gizmoId} merged={detailFiles.Count} location={location}");

        var recentForks = ProjectUpsertAudit.ReadRecentIdMismatchForkIds(gizmoId);
        if (recentForks.Count > 0)
        {
            await PrepareForApiAsync(core, cancellationToken);
            var sidebar = await ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
            var visibleForks = recentForks
                .Where(forkId => sidebar.Any(p => ChatGptUrls.GizmoIdsEqual(p.Id, forkId)))
                .ToList();

            if (visibleForks.Count > 0)
            {
                var linked = sidebar.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, gizmoId));
                return ProjectSyncPreflightResult.Blocked(
                    "recent_upsert_forks",
                    FormatOrphanForkRecoveryMessage(
                        gizmoId,
                        linked?.Title ?? "Project",
                        visibleForks),
                    visibleForks.Concat([gizmoId]).ToList());
            }

            ProjectLinkDiagnostics.Log(
                $"Recent upsert fork ids in audit are not visible in sidebar ({string.Join(", ", recentForks)}); "
                + "allowing sync because sidebar duplicate check passed");
        }

        return ProjectSyncPreflightResult.Ok();
    }

    private async Task<ProjectSyncPreflightResult> BlockWithRefreshedOrphanMessageAsync(
        CoreWebView2 core,
        string gizmoId,
        string errorCode,
        string baseMessage,
        CancellationToken cancellationToken)
    {
        await PrepareForApiAsync(core, cancellationToken);
        var sidebar = await ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
        var linked = sidebar.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, gizmoId));
        if (linked is not null)
        {
            var orphanIds = sidebar
                .Where(p => IsSnorlaxProjectId(p.Id)
                            && string.Equals(p.Title, linked.Title, StringComparison.OrdinalIgnoreCase)
                            && !ChatGptUrls.GizmoIdsEqual(p.Id, gizmoId))
                .Select(p => p.Id)
                .ToList();
            if (orphanIds.Count > 0)
            {
                return ProjectSyncPreflightResult.Blocked(
                    errorCode,
                    FormatOrphanForkRecoveryMessage(gizmoId, linked.Title, orphanIds),
                    orphanIds.Concat([gizmoId]).ToList());
            }
        }

        return ProjectSyncPreflightResult.Blocked(errorCode, baseMessage);
    }

    internal static bool TryValidateDetailForAttachReadOnly(
        JsonElement detailRoot,
        string linkedGizmoId,
        out string? error)
    {
        error = null;
        var node = detailRoot;
        if (detailRoot.TryGetProperty("gizmo", out var wrap))
            node = wrap.TryGetProperty("gizmo", out var inner) ? inner : wrap;

        var detailId = JsonElementParsing.GetStringOrNull(node, "id");
        if (string.IsNullOrWhiteSpace(detailId)
            || !ChatGptUrls.GizmoIdsEqual(detailId, linkedGizmoId))
        {
            error =
                $"Gizmo detail id mismatch: expected {linkedGizmoId}, got {detailId ?? "(none)"}. "
                + "Open the linked project in ChatGPT or re-link.";
            return false;
        }

        var detailFiles = GizmoResponseParser.CollectFileRefsDeep(detailRoot);
        var body = BuildUpsertBodyFromDetail(detailRoot, linkedGizmoId, detailFiles);
        if (body is not Dictionary<string, object?> bodyDict
            || !bodyDict.ContainsKey("display")
            || !bodyDict.ContainsKey("sharing")
            || !bodyDict.ContainsKey("instructions"))
        {
            error = "Gizmo detail is missing required upsert fields (display, sharing, or instructions).";
            return false;
        }

        return true;
    }

    internal enum SnorlaxAttachStrategy
    {
        ProjectFilesApi,
        DetailUpsertFallback,
    }

    internal static SnorlaxAttachStrategy ResolvePrimarySnorlaxAttachStrategy() =>
        SnorlaxAttachStrategy.ProjectFilesApi;

    /// <summary>
    /// Expected link-project.log signals after manual orphan cleanup and successful Apply Safe.
    /// </summary>
    internal static IReadOnlyList<string> SyncSuccessLogSignals { get; } =
    [
        "Snorlax attach canary read-only ok",
        "ProjectFilesAttach ok",
        "Incremental detail upsert file=",
    ];

    internal static bool ClassifySnorlaxCanUpdateCanary(
        ProjectUpsertOutcome outcome,
        string linkedGizmoId,
        string? responseGizmoId) =>
        outcome == ProjectUpsertOutcome.Updated
        && !string.IsNullOrWhiteSpace(responseGizmoId)
        && ChatGptUrls.GizmoIdsEqual(linkedGizmoId, responseGizmoId);

    internal static bool ShouldRetryUpsertLocationAfterAttempt(
        ProjectUpsertOutcome outcome,
        bool filesVisibleOnLinkedProject,
        bool hasMoreLocationCandidates) =>
        hasMoreLocationCandidates
        && outcome == ProjectUpsertOutcome.Updated
        && !filesVisibleOnLinkedProject;

    public async Task<string?> CreateProjectConversationAsync(
        CoreWebView2 core,
        string gizmoId,
        ProjectConversationCreateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await CreateProjectConversationDetailedAsync(core, gizmoId, options, cancellationToken);
        return result.ConversationId;
    }

    public async Task<CreateProjectConversationResult> CreateProjectConversationDetailedAsync(
        CoreWebView2 core,
        string gizmoId,
        ProjectConversationCreateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gizmoId))
            return new CreateProjectConversationResult { Error = "missing_gizmo_id" };

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        await EnsureProjectPageAsync(core, gizmoId, cancellationToken);

        if (options?.UiCreateOnly == true)
            return await TryUiCreateConversationAsync(core, gizmoId, options, cancellationToken);

        string? initError = null;
        if (options?.UiCreateOnly != true)
        {
            var initBody = new Dictionary<string, object?>
            {
                ["gizmo_id"] = gizmoId,
                ["requested_default_model"] = null,
                ["conversation_id"] = null,
                ["timezone_offset_min"] = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
            };

            var initMsg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "POST",
                    path = ChatGptApiEndpoints.ConversationInit,
                    body = initBody,
                },
                cancellationToken: cancellationToken);

            if (initMsg.Ok)
            {
                ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.ConversationInit, "POST");
                var fromInit = TryReadConversationId(initMsg.Json);
                if (!string.IsNullOrWhiteSpace(fromInit))
                {
                    if (initMsg.Json is { } initJson)
                        ChatGptConversationSendService.TrySeedParentCache(fromInit, initJson);
                    EnsureConversationParentBootstrapped(fromInit);
                    return new CreateProjectConversationResult { ConversationId = fromInit };
                }

                ProjectLinkDiagnostics.Log(
                    $"ConversationInit ok (session warmup) for {gizmoId}; no conversation_id in response");
            }
            else
            {
                initError = $"init status={initMsg.Status} error={initMsg.Error}";
                ProjectLinkDiagnostics.Log($"ConversationInit failed for {gizmoId} {initError}");
                ChatGptApiDiscovery.RecordFailure(ChatGptApiEndpoints.ConversationInit, "POST", initMsg.Status);
            }

            var legacyBody = new Dictionary<string, object?>
            {
                ["gizmo_id"] = gizmoId,
                ["model"] = "auto",
            };

            var legacyMsg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "POST",
                    path = ChatGptApiEndpoints.ConversationsCreate,
                    body = legacyBody,
                },
                cancellationToken: cancellationToken);

            if (legacyMsg.Ok)
            {
                ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.ConversationsCreate, "POST");
                var fromLegacy = TryReadConversationId(legacyMsg.Json);
                if (!string.IsNullOrWhiteSpace(fromLegacy))
                {
                    if (legacyMsg.Json is { } legacyJson)
                        ChatGptConversationSendService.TrySeedParentCache(fromLegacy, legacyJson);
                    EnsureConversationParentBootstrapped(fromLegacy);
                    return new CreateProjectConversationResult { ConversationId = fromLegacy };
                }
            }
            else
            {
                ProjectLinkDiagnostics.Log(
                    $"ConversationsCreate failed for {gizmoId} status={legacyMsg.Status} error={legacyMsg.Error}");
                ChatGptApiDiscovery.RecordFailure(ChatGptApiEndpoints.ConversationsCreate, "POST", legacyMsg.Status);
            }
        }

        var uiResult = await TryUiCreateConversationAsync(core, gizmoId, options, cancellationToken);
        if (!string.IsNullOrWhiteSpace(uiResult.ConversationId))
            return uiResult;

        var clientId = Guid.NewGuid().ToString();
        if (await TryRegisterClientConversationAsync(core, gizmoId, clientId, cancellationToken))
        {
            ProjectLinkDiagnostics.Log(
                $"Registered client conversation {clientId} for project {gizmoId} via conversation/init");
            return new CreateProjectConversationResult
            {
                ConversationId = clientId,
                ClientBootstrapped = true,
                Error = initError,
            };
        }

        ChatGptConversationSendService.BootstrapNewConversationParent(clientId);
        ProjectLinkDiagnostics.Log(
            $"Client-bootstrap conversation {clientId} for project {gizmoId}"
            + (initError is null ? "" : $" (init: {initError})"));

        return new CreateProjectConversationResult
        {
            ConversationId = clientId,
            ClientBootstrapped = true,
            Error = initError ?? "conversation_init_register_failed",
        };
    }

    private async Task<CreateProjectConversationResult> TryUiCreateConversationAsync(
        CoreWebView2 core,
        string gizmoId,
        ProjectConversationCreateOptions? options,
        CancellationToken cancellationToken)
    {
        if (options?.TryUiCreate is not { } tryUiCreate)
            return new CreateProjectConversationResult { Error = "ui_create_unavailable" };

        try
        {
            var uiId = await tryUiCreate(core, cancellationToken);
            if (string.IsNullOrWhiteSpace(uiId))
            {
                ProjectLinkDiagnostics.Log($"UI New chat returned no conversation id for project {gizmoId}");
                return new CreateProjectConversationResult { Error = "ui_create_no_conversation_id" };
            }

            EnsureConversationParentBootstrapped(uiId);
            ProjectLinkDiagnostics.Log($"UI started project conversation {uiId} for {gizmoId}");
            return new CreateProjectConversationResult { ConversationId = uiId };
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"UI New chat failed for {gizmoId}: {ex.Message}");
            return new CreateProjectConversationResult { Error = $"ui_create_failed: {ex.Message}" };
        }
    }

    private async Task<bool> TryRegisterClientConversationAsync(
        CoreWebView2 core,
        string gizmoId,
        string clientConversationId,
        CancellationToken cancellationToken)
    {
        var initBody = new Dictionary<string, object?>
        {
            ["gizmo_id"] = gizmoId,
            ["requested_default_model"] = null,
            ["conversation_id"] = clientConversationId,
            ["timezone_offset_min"] = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
        };

        var initMsg = await _bridge.SendAsync(
            core,
            new
            {
                action = "apiRequest",
                method = "POST",
                path = ChatGptApiEndpoints.ConversationInit,
                body = initBody,
            },
            cancellationToken: cancellationToken);

        if (!initMsg.Ok)
            return false;

        ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.ConversationInit, "POST");
        if (initMsg.Json is { } initJson)
            ChatGptConversationSendService.TrySeedParentCache(clientConversationId, initJson);
        EnsureConversationParentBootstrapped(clientConversationId);
        return true;
    }

    private static void EnsureConversationParentBootstrapped(string conversationId)
    {
        if (ConversationParentCache.IsCached(conversationId))
            return;

        ChatGptConversationSendService.BootstrapNewConversationParent(conversationId);
    }

    private static string? TryReadConversationId(JsonElement? json)
    {
        if (json is not { } root)
            return null;

        var direct = JsonElementParsing.GetStringOrNull(root, "conversation_id")
                     ?? JsonElementParsing.GetStringOrNull(root, "id");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (root.TryGetProperty("conversation", out var conversation)
            && conversation.ValueKind == JsonValueKind.Object)
        {
            return JsonElementParsing.GetStringOrNull(conversation, "conversation_id")
                   ?? JsonElementParsing.GetStringOrNull(conversation, "id");
        }

        return null;
    }

    public async Task<IReadOnlyList<GizmoFileRef>> GetProjectFilesAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken = default) =>
        await GetProjectFilesDirectAsync(core, gizmoId, cancellationToken);

    public async Task<IReadOnlyList<GizmoFileRef>> GetProjectFilesDirectAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken = default,
        bool ensureProjectPage = true)
    {
        if (ensureProjectPage)
        await EnsureProjectPageAsync(core, gizmoId, cancellationToken);
        else
            await PrepareForApiAsync(core, cancellationToken);

        if (IsSnorlaxProjectId(gizmoId))
        {
            var sidebarFiles = await GetProjectFilesFromSidebarPagesAsync(core, gizmoId, cancellationToken);
            var (detailStatus, detailFiles) = await TryGetGizmoDetailFilesAsync(core, gizmoId, cancellationToken);
            var merged = MergeFileRefsById(sidebarFiles, detailFiles);

            if (ShouldUseSnorlaxFileListFastPath(sidebarFiles.Count, detailFiles.Count, merged.Count))
            {
                var fastProbe = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sidebar"] = $"200:{sidebarFiles.Count}",
                    ["detail"] = $"{detailStatus}:{detailFiles.Count}",
                    ["projects_files"] = "skipped:0",
                    ["gizmo_files"] = "skipped:0",
                    ["bootstrap"] = "0:0",
                    ["merged"] = merged.Count.ToString(),
                };
                WriteFileListProbe(gizmoId, fastProbe, merged);
                ProjectLinkDiagnostics.Log(
                    $"Merged {merged.Count} project file(s) for {gizmoId} "
                    + $"sidebar={fastProbe["sidebar"]} detail={fastProbe["detail"]} "
                    + $"projects_files={fastProbe["projects_files"]} gizmo_files={fastProbe["gizmo_files"]} "
                    + $"bootstrap={fastProbe["bootstrap"]}");
                return merged;
            }

            var (projectsStatus, projectsFiles) = await TryGetFilesFromApiPathWithStatusAsync(
                core,
                ChatGptApiEndpoints.ProjectFilesList(gizmoId),
                cancellationToken);
            var (gizmoStatus, gizmoFiles) = await TryGetFilesFromApiPathWithStatusAsync(
                core,
                ChatGptApiEndpoints.ProjectFiles(gizmoId),
                cancellationToken);

            merged = MergeFileRefsById(sidebarFiles, detailFiles, projectsFiles, gizmoFiles);
            var bootstrapCount = 0;
            if (merged.Count == 0)
            {
                foreach (var project in await ListProjectsFromBootstrapAsync(core, cancellationToken))
                {
                    if (!ChatGptUrls.GizmoIdsEqual(project.Id, gizmoId) || project.Files.Count == 0)
                        continue;

                    bootstrapCount = project.Files.Count;
                    merged = MergeFileRefsById(merged, project.Files);
                    break;
                }
            }

            var probe = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sidebar"] = $"200:{sidebarFiles.Count}",
                ["detail"] = $"{detailStatus}:{detailFiles.Count}",
                ["projects_files"] = $"{projectsStatus}:{projectsFiles.Count}",
                ["gizmo_files"] = $"{gizmoStatus}:{gizmoFiles.Count}",
                ["bootstrap"] = $"{(bootstrapCount > 0 ? 200 : 0)}:{bootstrapCount}",
                ["merged"] = merged.Count.ToString(),
            };
            WriteFileListProbe(gizmoId, probe, merged);

            if (merged.Count > 0)
            {
                ProjectLinkDiagnostics.Log(
                    $"Merged {merged.Count} project file(s) for {gizmoId} "
                    + $"sidebar={probe["sidebar"]} detail={probe["detail"]} "
                    + $"projects_files={probe["projects_files"]} gizmo_files={probe["gizmo_files"]} "
                    + $"bootstrap={probe["bootstrap"]}");
                return merged;
            }
        }
        else
        {
        var strategies = new (string Path, string Label)[]
        {
            (ChatGptApiEndpoints.ProjectFilesList(gizmoId), "projects_files"),
            (ChatGptApiEndpoints.GizmoDetail(gizmoId), "gizmo_detail"),
            (ChatGptApiEndpoints.ProjectFiles(gizmoId), "gizmo_files"),
        };

            var probe = new Dictionary<string, string>(StringComparer.Ordinal);
            var collected = new List<GizmoFileRef>();
            foreach (var (path, label) in strategies)
            {
                var (status, files) = await TryGetFilesFromApiPathWithStatusAsync(core, path, cancellationToken);
                probe[label] = $"{status}:{files.Count}";
                if (files.Count > 0)
                    collected.AddRange(files);
            }

            var sidebarFiles = await GetProjectFilesFromSidebarPagesAsync(core, gizmoId, cancellationToken);
            probe["sidebar"] = $"200:{sidebarFiles.Count}";
            if (sidebarFiles.Count > 0)
                collected.AddRange(sidebarFiles);

            var merged = MergeFileRefsById(collected);
            probe["merged"] = merged.Count.ToString();
            WriteFileListProbe(gizmoId, probe, merged);

            if (merged.Count > 0)
                return merged;
        }

        ProjectLinkDiagnostics.Log($"No project files found for {gizmoId} after all list strategies.");
        WriteFileListProbe(gizmoId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["merged"] = "0",
        }, []);
        return [];
    }

    private async Task<(int Status, IReadOnlyList<GizmoFileRef> Files)> TryGetGizmoDetailFilesAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        var path = ChatGptApiEndpoints.GizmoDetail(gizmoId);
            var msg = await _bridge.SendAsync(
                core,
                ApiCommand("GET", path),
                cancellationToken: cancellationToken);

            if (msg.Status == 404)
            {
                ChatGptApiDiscovery.RecordFailure(path, "GET", 404);
            return (404, []);
            }

            if (!msg.Ok)
            return (msg.Status ?? 0, []);

            ChatGptApiDiscovery.RecordSuccess(path, "GET");
            if (msg.Json is not { } json)
            return (msg.Status ?? 200, []);

        List<GizmoFileRef> files;
        if (json.TryGetProperty("gizmo", out var wrap))
        {
            files = wrap.TryGetProperty("gizmo", out var inner)
                ? GizmoResponseParser.ParseFilesFromGizmoContext(inner, wrap)
                : GizmoResponseParser.ParseFilesFromGizmoContext(wrap, wrap);
        }
        else
        {
            files = GizmoResponseParser.ParseFilesFromJson(json);
        }

        if (files.Count == 0)
            files = GizmoResponseParser.CollectFileRefsDeep(json);

        return (200, files);
    }

    private async Task<IReadOnlyList<GizmoFileRef>> TryGetFilesFromApiPathAsync(
        CoreWebView2 core,
        string path,
        CancellationToken cancellationToken)
    {
        var (_, files) = await TryGetFilesFromApiPathWithStatusAsync(core, path, cancellationToken);
        return files;
    }

    private async Task<(int Status, IReadOnlyList<GizmoFileRef> Files)> TryGetFilesFromApiPathWithStatusAsync(
        CoreWebView2 core,
        string path,
        CancellationToken cancellationToken)
    {
        var msg = await _bridge.SendAsync(
            core,
            ApiCommand("GET", path),
            cancellationToken: cancellationToken);

        if (msg.Status == 404)
        {
            ChatGptApiDiscovery.RecordFailure(path, "GET", 404);
            return (404, []);
        }

        if (!msg.Ok)
            return (msg.Status ?? 0, []);

        ChatGptApiDiscovery.RecordSuccess(path, "GET");

        if (msg.Json is not { } json)
            return (msg.Status ?? 200, []);

        return (msg.Status ?? 200, GizmoResponseParser.ParseFilesFromJson(json));
    }

    private static void WriteFileListProbe(
        string gizmoId,
        IReadOnlyDictionary<string, string> strategies,
        IReadOnlyList<GizmoFileRef>? mergedFiles = null)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var path = Path.Combine(AppDirectories.Root, "last-file-list-probe.json");
            var json = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                gizmoId,
                strategies,
                mergedFiles = mergedFiles?
                    .Select(f => new { id = f.FileId, name = f.Name })
                    .ToList(),
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            /* diagnostic only */
        }
    }

    internal static List<GizmoFileRef> MergeFileRefsById(params IReadOnlyList<GizmoFileRef>[] sources)
    {
        var byId = new Dictionary<string, GizmoFileRef>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var file in source)
            {
                if (string.IsNullOrWhiteSpace(file.FileId))
                    continue;

                byId[file.FileId] = file;
            }
        }

        return byId.Values.ToList();
    }

    internal static List<GizmoFileRef> MergeFileRefsById(IEnumerable<GizmoFileRef> sources)
    {
        var byId = new Dictionary<string, GizmoFileRef>(StringComparer.Ordinal);
        foreach (var file in sources)
        {
            if (string.IsNullOrWhiteSpace(file.FileId))
                continue;

            byId[file.FileId] = file;
        }

        return byId.Values.ToList();
    }

    private async Task<IReadOnlyList<GizmoFileRef>> GetProjectFilesFromSidebarPagesAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        var collected = new List<GizmoFileRef>();
        foreach (var ownedOnly in new[] { true, false })
        {
            foreach (var project in await FetchSidebarPagesAsync(core, ownedOnly, cancellationToken))
            {
                if (ChatGptUrls.GizmoIdsEqual(project.Id, gizmoId) && project.Files.Count > 0)
                    collected.AddRange(project.Files);
            }

            if (collected.Count > 0)
                break;
        }

        return MergeFileRefsById(collected);
    }

    public async Task<ApiProbeResult> ProbeSidebarAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await PrepareForApiAsync(core, cancellationToken);

        var msg = await _bridge.SendAsync(
            core,
            new { action = "probeApi", path = ChatGptApiEndpoints.ProjectsSidebar },
            cancellationToken: cancellationToken);

        return ParseProbeResult(msg);
    }

    public async Task<IReadOnlyList<GizmoSummary>> ListProjectsViaSidebarOnlyAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        var (fromBridge, _) = await TryListProjectsViaBridgeAsync(core, cancellationToken);
        if (fromBridge.Count > 0)
            return fromBridge;

        var byId = new Dictionary<string, GizmoSummary>(StringComparer.Ordinal);
        foreach (var ownedOnly in new[] { true, false })
        {
            foreach (var p in await FetchSidebarPagesAsync(core, ownedOnly, cancellationToken))
                byId[p.Id] = p;
            if (byId.Count > 0)
                break;
        }

        return byId.Values.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<GizmoSummary>> ListProjectsFromBootstrapAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        var msg = await _bridge.SendAsync(
            core,
            ApiCommand("GET", ChatGptApiEndpoints.GizmosBootstrap),
            cancellationToken: cancellationToken);

        if (!msg.Ok || msg.Json is not { } root)
            return [];

        ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.GizmosBootstrap, "GET");
        return GizmoResponseParser.ParseBootstrapGizmos(root);
    }

    public async Task<IReadOnlyList<GizmoSummary>> ListProjectsFromDomAsync(
        ChatGptApiBridgeInjection bridge,
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await bridge.WaitForInjectablePageAsync(core, cancellationToken: cancellationToken);

        var msg = await bridge.SendAsync(
            core,
            new { action = "discoverProjectsDom" },
            cancellationToken: cancellationToken);

        if (!msg.Ok || msg.Json is not { } json)
            return [];

        if (!json.TryGetProperty("projects", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<GizmoSummary>();
        foreach (var p in arr.EnumerateArray())
        {
            var id = p.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            list.Add(new GizmoSummary
            {
                Id = id,
                Title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "Project" : "Project",
            });
        }

        return list;
    }

    public async Task<byte[]> DownloadFileAsync(
        CoreWebView2 core,
        string fileId,
        CancellationToken cancellationToken = default,
        string? gizmoId = null,
        string? location = null,
        bool failFast = false)
    {
        if (!string.IsNullOrWhiteSpace(gizmoId))
        {
            var inline = await TryDownloadInlineFileFromGizmoDetailAsync(
                core,
                gizmoId,
                fileId,
                cancellationToken);
            if (inline is { Length: > 0 })
                return inline;
        }

        var paths = ChatGptApiEndpoints.BuildFileDownloadPathCandidates(fileId, gizmoId, location);
        var msg = await _bridge.SendAsync(
            core,
            new { action = "downloadFile", fileId, gizmoId, location, paths, failFast },
            timeoutMs: 120000,
            cancellationToken: cancellationToken);

        if (!msg.Ok)
        {
            if (!string.IsNullOrWhiteSpace(gizmoId))
            {
                var inline = await TryDownloadInlineFileFromGizmoDetailAsync(
                    core,
                    gizmoId,
                    fileId,
                    cancellationToken);
                if (inline is { Length: > 0 })
                    return inline;
            }

            var detailJson = ResolveDownloadFailureDetail(msg);
            var message = FormatDownloadFailureMessage(
                msg.Message ?? msg.Error ?? "download_failed",
                msg,
                fileId,
                paths,
                allAttemptsNotFound: AreAllDownloadAttemptsNotFound(msg));
            throw new ChatGptApiException(
                message,
                ResolveDownloadFailureEndpoint(message, msg, fileId, paths),
                msg.Status,
                detailJson);
        }

        if (msg.Root.TryGetProperty("base64", out var b64) && b64.ValueKind == JsonValueKind.String)
        {
            var s = b64.GetString();
            if (!string.IsNullOrEmpty(s))
                return Convert.FromBase64String(s);
        }

        if (msg.Root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            var s = text.GetString();
            if (s is not null)
                return System.Text.Encoding.UTF8.GetBytes(s);
        }

        throw new ChatGptApiException("File download returned no payload.", ChatGptApiEndpoints.FileDownload(fileId));
    }

    public async Task DownloadFileToPathAsync(
        CoreWebView2 core,
        string fileId,
        string destPath,
        CancellationToken cancellationToken = default,
        string? gizmoId = null,
        string? location = null,
        bool failFast = false)
    {
        var bytes = await DownloadFileAsync(core, fileId, cancellationToken, gizmoId, location, failFast);
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(destPath, bytes, cancellationToken);
    }

    private async Task<byte[]?> TryDownloadInlineFileFromGizmoDetailAsync(
        CoreWebView2 core,
        string gizmoId,
        string fileId,
        CancellationToken cancellationToken)
    {
        var detailJson = await GetGizmoDetailJsonAsync(core, gizmoId, cancellationToken);
        if (detailJson is not { } json)
            return null;

        var inline = GizmoResponseParser.TryExtractInlineFileContent(json, fileId);
        if (inline is { Length: > 0 })
            return inline;

        var downloadPath = GizmoResponseParser.TryExtractInlineDownloadPath(json, fileId);
        if (string.IsNullOrWhiteSpace(downloadPath))
            return null;

        return await TryDownloadBytesViaBridgePathsAsync(
            core,
            fileId,
            gizmoId,
            [downloadPath],
            cancellationToken);
    }

    private async Task<byte[]?> TryDownloadBytesViaBridgePathsAsync(
        CoreWebView2 core,
        string fileId,
        string? gizmoId,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var msg = await _bridge.SendAsync(
            core,
            new { action = "downloadFile", fileId, gizmoId, paths },
            timeoutMs: 120000,
            cancellationToken: cancellationToken);

        if (!msg.Ok)
            return null;

        return TryReadDownloadPayload(msg, fileId);
    }

    private static byte[]? TryReadDownloadPayload(ApiBridgeMessage msg, string fileId)
    {
        if (msg.Root.TryGetProperty("base64", out var b64) && b64.ValueKind == JsonValueKind.String)
        {
            var s = b64.GetString();
            if (!string.IsNullOrEmpty(s))
                return Convert.FromBase64String(s);
        }

        if (msg.Root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            var s = text.GetString();
            if (s is not null)
                return System.Text.Encoding.UTF8.GetBytes(s);
        }

        return null;
    }

    internal static bool IsRemoteFileDownloadUnavailable(ChatGptApiException ex)
    {
        if (ex.Message.StartsWith("download_not_available", StringComparison.Ordinal))
            return true;

        if (ex.Message.StartsWith("download_failed", StringComparison.Ordinal)
            && ex.Message.Contains("404", StringComparison.Ordinal))
        {
            return true;
        }

        return ex.StatusCode == 404;
    }

    internal static bool AreAllDownloadAttemptsNotFound(ApiBridgeMessage msg)
    {
        if (!msg.Root.TryGetProperty("detail", out var detail) || detail.ValueKind != JsonValueKind.Object)
            return msg.Status == 404;

        if (!detail.TryGetProperty("attempts", out var attempts) || attempts.ValueKind != JsonValueKind.Array)
            return msg.Status == 404;

        var any = false;
        foreach (var attempt in attempts.EnumerateArray())
        {
            any = true;
            if (!attempt.TryGetProperty("status", out var status)
                || !status.TryGetInt32(out var code)
                || code != 404)
            {
                return false;
            }
        }

        return any || msg.Status == 404;
    }

    internal static string FormatDownloadFailureMessage(
        string baseMessage,
        ApiBridgeMessage msg,
        string fileId,
        IReadOnlyList<string> paths,
        bool allAttemptsNotFound)
    {
        var attempted = FormatAttemptedPathsForMessage(msg, paths);
        var prefix = allAttemptsNotFound ? "download_not_available" : "download_failed";
        var status = msg.Status?.ToString() ?? "0";
        var lastPath = ResolveDownloadFailureEndpoint(baseMessage, msg, fileId, paths);
        return $"{prefix} {status} paths={paths.Count} attempted={attempted};last={lastPath}";
    }

    internal static string FormatAttemptedPathsForMessage(ApiBridgeMessage msg, IReadOnlyList<string> paths)
    {
        if (msg.Root.TryGetProperty("detail", out var detail)
            && detail.ValueKind == JsonValueKind.Object
            && detail.TryGetProperty("attempts", out var attempts)
            && attempts.ValueKind == JsonValueKind.Array)
        {
            var fromBridge = attempts.EnumerateArray()
                .Select(a => a.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (fromBridge.Count > 0)
                return string.Join(";", fromBridge!);
        }

        return string.Join(";", paths);
    }

    private static string ResolveDownloadFailureEndpoint(
        string message,
        ApiBridgeMessage msg,
        string fileId,
        IReadOnlyList<string> paths)
    {
        if (msg.Root.TryGetProperty("detail", out var detail)
            && detail.ValueKind == JsonValueKind.Object
            && detail.TryGetProperty("path", out var pathEl)
            && pathEl.ValueKind == JsonValueKind.String)
        {
            var path = pathEl.GetString();
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        if (message.Contains(";last=", StringComparison.Ordinal))
        {
            var lastIndex = message.LastIndexOf(";last=", StringComparison.Ordinal);
            if (lastIndex >= 0)
                return message[(lastIndex + ";last=".Length)..];
        }

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && (parts[0].Equals("download_failed", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("download_not_available", StringComparison.OrdinalIgnoreCase)))
        {
            return parts[^1];
        }

        return paths.Count > 0 ? paths[^1] : ChatGptApiEndpoints.FileDownload(fileId);
    }

    private static string? ResolveDownloadFailureDetail(ApiBridgeMessage msg)
    {
        if (msg.Root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
            return detail.GetRawText();

        if (!string.IsNullOrWhiteSpace(msg.BodyText))
            return msg.BodyText;

        return msg.Message;
    }

    public async Task DeleteProjectFileAsync(
        CoreWebView2 core,
        string gizmoId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        if (await TryDeleteProjectFileViaBridgeAsync(core, gizmoId, fileId, cancellationToken))
            return;

        var preferred = ChatGptApiDiscovery.GetPreferredFileDeleteTemplate();
        var templates = OrderDeleteTemplates(preferred);

        ChatGptApiException? last = null;
        foreach (var template in templates)
        {
            try
            {
                if (await TryDeleteWithTemplateAsync(core, gizmoId, fileId, template, cancellationToken))
                {
                    ChatGptApiDiscovery.SetPreferredFileDeleteTemplate(template);
                    return;
                }
            }
            catch (ChatGptApiException ex)
            {
                last = ex;
            }
        }

        try
        {
            await DetachProjectFilesViaUpsertAsync(core, gizmoId, [fileId], cancellationToken);
            return;
        }
        catch (ChatGptApiException ex)
        {
            last = ex;
        }

        throw last ?? new ChatGptApiException(
            "All delete methods failed (REST and upsert detach). Remove the file manually in ChatGPT, then sync again.",
            ChatGptApiEndpoints.FileDelete(fileId));
    }

    public async Task DetachProjectFilesViaUpsertAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken = default)
    {
        if (fileIds.Count == 0)
            return;

        var idsToRemove = new HashSet<string>(fileIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        if (idsToRemove.Count == 0)
            return;

        var currentFiles = await GetProjectFilesDirectAsync(
            core,
            gizmoId,
            cancellationToken,
            ensureProjectPage: false);

        if (!currentFiles.Any(f => idsToRemove.Contains(f.FileId)))
            return;

        var pendingRemovals = idsToRemove
            .Where(id => currentFiles.Any(f => string.Equals(f.FileId, id, StringComparison.Ordinal)))
            .ToList();
        foreach (var fileId in pendingRemovals.ToList())
        {
            if (await TryDeleteProjectFileViaBridgeAsync(core, gizmoId, fileId, cancellationToken))
                pendingRemovals.Remove(fileId);
        }

        if (pendingRemovals.Count == 0)
        {
            ProjectLinkDiagnostics.Log(
                $"Detach via bridge delete ok for {gizmoId}: removed {idsToRemove.Count} file(s)");
            return;
        }

        idsToRemove = pendingRemovals.ToHashSet(StringComparer.Ordinal);

        var remaining = currentFiles
            .Where(f => !idsToRemove.Contains(f.FileId))
            .ToList();

        var summary = await GetProjectSummaryAsync(core, gizmoId, cancellationToken);
        var title = summary?.Title ?? "Project";
        var instructions = summary?.Instructions ?? "";

        ProjectLinkDiagnostics.Log(
            $"Detach via upsert for {gizmoId}: removing {idsToRemove.Count} file(s), keeping {remaining.Count}");

        if (IsSnorlaxProjectId(gizmoId))
        {
            var preflight = await ValidateSyncPreflightAsync(core, gizmoId, cancellationToken);
            if (!preflight.Allowed)
            {
                throw new ChatGptApiException(
                    preflight.Message ?? preflight.ErrorCode ?? "sync_blocked",
                    ChatGptApiEndpoints.ProjectUpsert);
            }

            var detailJson = await GetGizmoDetailJsonAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: false);
            if (detailJson is null)
            {
                throw new ChatGptApiException(
                    "Could not load project detail to detach files.",
                    ChatGptApiEndpoints.GizmoDetail(gizmoId));
            }

            await DetachSnorlaxFilesViaDetailUpsertAsync(
                core,
                gizmoId,
                idsToRemove,
                detailJson.Value,
                cancellationToken);
        }
        else
        {
            await AttachViaUpsertAsync(
                core,
                gizmoId,
                remaining,
                title,
                instructions,
                "DetachFiles",
                adventureId: null,
                cancellationToken);
        }

        var after = await GetProjectFilesDirectAsync(
            core,
            gizmoId,
            cancellationToken,
            ensureProjectPage: false);
        var stillPresent = after
            .Where(f => idsToRemove.Contains(f.FileId))
            .Select(f => f.FileId)
            .ToList();
        if (stillPresent.Count > 0)
        {
            throw new ChatGptApiException(
                "upsert_detach_incomplete: file(s) still on project: "
                + string.Join(", ", stillPresent),
                ChatGptApiEndpoints.ProjectUpsert);
        }
    }

    private async Task<bool> TryDeleteProjectFileViaBridgeAsync(
        CoreWebView2 core,
        string gizmoId,
        string fileId,
        CancellationToken cancellationToken)
    {
        var msg = await _bridge.SendAsync(
            core,
            new { action = "deleteProjectFile", gizmoId, fileId },
            timeoutMs: 60000,
            cancellationToken: cancellationToken);

        if (msg.Ok)
        {
            ProjectLinkDiagnostics.Log($"Bridge delete ok file_id={fileId} for {gizmoId}");
            return true;
        }

        ProjectLinkDiagnostics.Log(
            $"Bridge delete failed file_id={fileId} for {gizmoId}: {msg.Error ?? msg.Message}");
        return false;
    }

    public async Task<string?> ReplaceProjectFileAsync(
        CoreWebView2 core,
        string gizmoId,
        string? existingFileId,
        string fileName,
        byte[] content,
        string mimeType = "text/markdown",
        string? projectTitle = null,
        string? projectInstructions = null,
        IReadOnlyList<GizmoFileRef>? existingFiles = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(existingFileId))
        {
            try
            {
                await DeleteProjectFileAsync(core, gizmoId, existingFileId, cancellationToken);
            }
            catch (ChatGptApiException)
            {
                throw new ChatGptApiException(
                    "Could not delete existing project file for replace. Remove it manually in ChatGPT, then sync.",
                    ChatGptApiEndpoints.ProjectFiles(gizmoId));
            }
        }

        return await UploadProjectFileAsync(
            core,
            gizmoId,
            fileName,
            content,
            mimeType,
            projectTitle,
            projectInstructions,
            existingFiles,
            cancellationToken);
    }

    public async Task<GizmoFileRef?> UploadProjectFileBytesAsync(
        CoreWebView2 core,
        string gizmoId,
        string fileName,
        byte[] content,
        string mimeType = "text/markdown",
        CancellationToken cancellationToken = default)
    {
        var useCase = ResolveProjectSourceUploadUseCase(mimeType);
        var msg = await _bridge.SendAsync(
            core,
            new
            {
                action = "uploadFile",
                gizmoId,
                fileName,
                mimeType,
                base64 = Convert.ToBase64String(content),
                useCase,
                useProjectLibrary = IsSnorlaxProjectId(gizmoId),
                skipProjectAttach = true,
            },
            timeoutMs: 180000,
            cancellationToken: cancellationToken);

        if (!msg.Ok)
        {
            ChatGptApiDiscovery.RecordFailure(ChatGptApiEndpoints.FilesUpload, "POST", msg.Status);
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.UploadFailed,
                SyncTraceCategory.Upload,
                SyncTraceLevel.Error,
                $"Upload bridge failed file={fileName}",
                phase: SyncTracePhase.Upload,
                outcome: "failed",
                data: new { fileName, gizmoId, httpStatus = msg.Status, error = msg.Message });
            throw new ChatGptApiException(
                FormatBridgeError(msg, "upload_failed"),
                ChatGptApiEndpoints.FilesUpload,
                msg.Status,
                msg.BodyText);
        }

        ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.FilesUpload, "POST");

        var fileId = ExtractUploadedFileId(msg);
        if (string.IsNullOrWhiteSpace(fileId))
            return null;

        var location = ResolveUploadedProjectFileLocation(msg, useCase);
        var fromLibraryUpload = msg.Root.TryGetProperty("libraryUpload", out var libraryUpload)
                                && libraryUpload.ValueKind == JsonValueKind.True;
        return new GizmoFileRef
        {
            FileId = fileId,
            Name = fileName,
            Location = location,
            FromLibraryUpload = fromLibraryUpload,
        };
    }

    public async Task<GizmoFileRef?> UploadChatAttachmentBytesAsync(
        CoreWebView2 core,
        string fileName,
        byte[] content,
        string mimeType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        var useCase = ResolveUploadUseCase(mimeType);
        var msg = await _bridge.SendAsync(
            core,
            new
            {
                action = "uploadFile",
                fileName,
                mimeType,
                base64 = Convert.ToBase64String(content),
                useCase,
                useProjectLibrary = false,
                skipProjectAttach = true,
            },
            timeoutMs: 180000,
            cancellationToken: cancellationToken);

        if (!msg.Ok)
        {
            ChatGptApiDiscovery.RecordFailure(ChatGptApiEndpoints.FilesUpload, "POST", msg.Status);
            throw new ChatGptApiException(
                FormatBridgeError(msg, "chat_attachment_upload_failed"),
                ChatGptApiEndpoints.FilesUpload,
                msg.Status,
                msg.BodyText);
        }

        ChatGptApiDiscovery.RecordSuccess(ChatGptApiEndpoints.FilesUpload, "POST");

        var fileId = ExtractUploadedFileId(msg);
        if (string.IsNullOrWhiteSpace(fileId))
            return null;

        var location = ResolveUploadedProjectFileLocation(msg, useCase);
        return new GizmoFileRef
        {
            FileId = fileId,
            Name = fileName,
            Location = location,
        };
    }

    public async Task<bool> AttachProjectFilesViaUpsertAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> files,
        string? projectTitle = null,
        string? projectInstructions = null,
        Guid? adventureId = null,
        string caller = "AttachFiles",
        IReadOnlyList<GizmoFileRef>? knownExistingFiles = null,
        bool skipPreflight = false,
        CancellationToken cancellationToken = default,
        SnorlaxAttachOptions? attachOptions = null)
    {
        if (files is not { Count: > 0 })
            return false;

        attachOptions ??= SnorlaxAttachOptions.Default;

        if (IsSnorlaxProjectId(gizmoId))
        {
            return await AttachIncrementalViaDetailUpsertAsync(
                core,
                gizmoId,
                files,
                adventureId,
                caller,
                knownExistingFiles,
                skipPreflight,
                attachOptions,
                cancellationToken);
        }

        var mergedFiles = await MergeWithExistingProjectFilesAsync(core, gizmoId, files, cancellationToken);
        ProjectLinkDiagnostics.Log(
            $"Upsert attach preparing {mergedFiles.Count} file(s) for {gizmoId} (requested={files.Count})");

        await AttachViaUpsertAsync(
            core,
            gizmoId,
            mergedFiles,
            projectTitle,
            projectInstructions,
            caller,
            adventureId,
            cancellationToken);

        await ConfirmAttachedFilesOnProjectAsync(core, gizmoId, mergedFiles, cancellationToken);
        return true;
    }

    private async Task<bool> AttachIncrementalViaDetailUpsertAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> newFiles,
        Guid? adventureId,
        string caller,
        IReadOnlyList<GizmoFileRef>? knownExistingFiles,
        bool skipPreflight,
        SnorlaxAttachOptions attachOptions,
        CancellationToken cancellationToken)
    {
        _ = knownExistingFiles;

        var strictSyncAttach = string.Equals(caller, "SyncAttach", StringComparison.Ordinal);
        if (strictSyncAttach && !skipPreflight)
        {
            var preflight = await ValidateSyncPreflightAsync(core, gizmoId, cancellationToken);
            if (!preflight.Allowed)
            {
            throw new ChatGptApiException(
                    preflight.Message ?? preflight.ErrorCode ?? "sync_blocked",
                    ChatGptApiEndpoints.ProjectUpsert);
            }
        }

        var filesToAttach = newFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
            .ToList();
        if (filesToAttach.Count == 0)
            return false;

        var summary = await GetProjectSummaryAsync(core, gizmoId, cancellationToken);
        var title = summary?.Title ?? "Project";
        var instructions = summary?.Instructions ?? "";

        var attachBaseline = await SnapshotSnorlaxProjectIdsAsync(core, cancellationToken);
        var usedUpsertFallback = false;

        ProjectLinkDiagnostics.Log(
            $"Snorlax incremental attach preparing {filesToAttach.Count} new file(s) for {gizmoId} "
            + $"primary={ResolvePrimarySnorlaxAttachStrategy().ToString().ToLowerInvariant()}");

        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.AttachStrategy,
            SyncTraceCategory.Attach,
            SyncTraceLevel.Info,
            $"Snorlax attach strategy={ResolvePrimarySnorlaxAttachStrategy().ToString().ToLowerInvariant()}",
            phase: SyncTracePhase.Attach,
            data: new
            {
                strategy = ResolvePrimarySnorlaxAttachStrategy().ToString().ToLowerInvariant(),
                fileCount = filesToAttach.Count,
                batch = true,
            });

        var pending = filesToAttach.ToList();
        if (ResolvePrimarySnorlaxAttachStrategy() == SnorlaxAttachStrategy.ProjectFilesApi)
        {
            await EnsureNoNewSnorlaxProjectsBeforeUpsertAsync(
                core,
                attachBaseline,
                gizmoId,
                cancellationToken);

            var batchAttached = await TryAttachSnorlaxFilesViaProjectFilesApiAsync(
                core,
                gizmoId,
                pending,
                attachBaseline,
                strictSyncAttach,
                attachOptions,
                cancellationToken);
            if (batchAttached.Count > 0)
            {
                pending.RemoveAll(f => batchAttached.Contains(f.FileId!));
            }
        }

        foreach (var newFile in pending)
        {
            await EnsureNoNewSnorlaxProjectsBeforeUpsertAsync(
                core,
                attachBaseline,
                gizmoId,
                cancellationToken);

            var perFileBaseline = attachBaseline;

            if (ResolvePrimarySnorlaxAttachStrategy() == SnorlaxAttachStrategy.ProjectFilesApi
                && await TryAttachSnorlaxFileViaProjectFilesApiAsync(
                    core,
                    gizmoId,
                    newFile,
                    perFileBaseline,
                    strictSyncAttach,
                    skipSecondSidebarPoll: true,
                    attachOptions,
                    cancellationToken))
            {
                continue;
            }

            usedUpsertFallback = true;
            var detailJson = await GetGizmoDetailJsonAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: false);
            if (detailJson is null)
            {
                throw new ChatGptApiException(
                    "Could not load ChatGPT project detail for file attach. Open the linked project in ChatGPT and retry.",
                    ChatGptApiEndpoints.GizmoDetail(gizmoId));
            }

            await AttachSnorlaxFileViaUpsertFallbackAsync(
                core,
                gizmoId,
                newFile,
                detailJson.Value,
                title,
                instructions,
                adventureId,
                caller,
                perFileBaseline,
                strictSyncAttach,
                attachOptions,
                cancellationToken);
        }

        return usedUpsertFallback;
    }

    private async Task<HashSet<string>> TryAttachSnorlaxFilesViaProjectFilesApiAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> files,
        HashSet<string> attachBaseline,
        bool strictSyncAttach,
        SnorlaxAttachOptions attachOptions,
        CancellationToken cancellationToken)
    {
        var attached = new HashSet<string>(StringComparer.Ordinal);
        if (files.Count == 0)
            return attached;

        var path = ChatGptApiEndpoints.ProjectFilesAttach(gizmoId);
        var bodies = BuildProjectFilesAttachBodyCandidates(files);

        foreach (var body in bodies)
        {
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.ProjectFilesAttachAttempt,
                SyncTraceCategory.ProjectFiles,
                SyncTraceLevel.Info,
                $"ProjectFiles batch attach attempt count={files.Count}",
                phase: SyncTracePhase.Attach,
                data: new
                {
                    fileCount = files.Count,
                    fileNames = files.Select(f => f.Name).ToList(),
                    gizmoId,
                });

            var msg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "POST",
                    path,
                    body,
                },
                cancellationToken: cancellationToken);

            if (!msg.Ok)
            {
                foreach (var file in files)
                {
                    ProjectAttachAudit.RecordProjectFilesAttachAttempt(
                        gizmoId,
                        file.Name,
                        file.FileId,
                        body,
                        msg.Status ?? 0,
                        msg.BodyText ?? msg.Message);
                }

                ProjectLinkDiagnostics.Log(
                    $"ProjectFilesAttach batch failed count={files.Count} status={msg.Status} for {gizmoId} "
                    + $"body={ProjectUpsertAudit.Truncate(msg.BodyText ?? msg.Message, 300)}");
                continue;
            }

            foreach (var file in files)
            {
                ProjectAttachAudit.RecordProjectFilesAttachAttempt(
                    gizmoId,
                    file.Name,
                    file.FileId,
                    body,
                    msg.Status ?? 200,
                    msg.BodyText);
            }

            ChatGptApiDiscovery.RecordSuccess(path, "POST");
            ProjectLinkDiagnostics.Log(
                $"ProjectFilesAttach batch ok {files.Count} file(s) for {gizmoId}");
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.ProjectFilesAttachOk,
                SyncTraceCategory.ProjectFiles,
                SyncTraceLevel.Info,
                $"ProjectFiles batch attach ok count={files.Count}",
                phase: SyncTracePhase.Attach,
                outcome: "ok",
                data: new { fileCount = files.Count, gizmoId });

            if (!attachOptions.SkipPostAttachSidebar)
            {
                await EvaluatePostAttachSidebarAsync(
                    core,
                    attachBaseline,
                    gizmoId,
                    files,
                    strictSyncAttach,
                    skipSecondSidebarPoll: true,
                    cancellationToken);
            }

            if (!attachOptions.SkipOwnershipVerify)
            {
                await VerifyFilesOwnershipAfterAttachAsync(core, gizmoId, files, cancellationToken);
                await VerifyFilesDownloadableAfterAttachAsync(core, gizmoId, files, cancellationToken);
            }

            InvalidateAttachCaches(gizmoId);

            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file.FileId))
                    attached.Add(file.FileId);
            }

            return attached;
        }

        ProjectLinkDiagnostics.Log(
            $"ProjectFilesAttach batch exhausted attempts for {files.Count} file(s) on {gizmoId}; falling back to per-file");
        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.AttachStrategy,
            SyncTraceCategory.Attach,
            SyncTraceLevel.Info,
            "Batch project-files attach failed; falling back to per-file attach",
            phase: SyncTracePhase.Attach,
            data: new { strategy = "per_file", fileCount = files.Count, gizmoId });
        return attached;
    }

    private async Task<bool> TryAttachSnorlaxFileViaProjectFilesApiAsync(
        CoreWebView2 core,
        string gizmoId,
        GizmoFileRef file,
        HashSet<string> perFileBaseline,
        bool strictSyncAttach,
        bool skipSecondSidebarPoll,
        SnorlaxAttachOptions attachOptions,
        CancellationToken cancellationToken)
    {
        var path = ChatGptApiEndpoints.ProjectFilesAttach(gizmoId);
        var bodies = BuildProjectFilesAttachBodyCandidates([file]);

        foreach (var body in bodies)
        {
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.ProjectFilesAttachAttempt,
                SyncTraceCategory.ProjectFiles,
                SyncTraceLevel.Info,
                $"ProjectFiles attach attempt file={file.Name}",
                phase: SyncTracePhase.Attach,
                data: new { fileName = file.Name, fileId = file.FileId, gizmoId });

            var msg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "POST",
                    path,
                    body,
                },
                cancellationToken: cancellationToken);

            if (!msg.Ok)
            {
                ProjectAttachAudit.RecordProjectFilesAttachAttempt(
                    gizmoId,
                    file.Name,
                    file.FileId,
                    body,
                    msg.Status ?? 0,
                    msg.BodyText ?? msg.Message);
                ProjectLinkDiagnostics.Log(
                    $"ProjectFilesAttach failed file={file.Name} status={msg.Status} for {gizmoId} "
                    + $"body={ProjectUpsertAudit.Truncate(msg.BodyText ?? msg.Message, 300)}");
                continue;
            }

            ProjectAttachAudit.RecordProjectFilesAttachAttempt(
                gizmoId,
                file.Name,
                file.FileId,
                body,
                msg.Status ?? 200,
                msg.BodyText);

            ChatGptApiDiscovery.RecordSuccess(path, "POST");
            ProjectLinkDiagnostics.Log(
                $"ProjectFilesAttach ok file={file.Name} file_id={file.FileId} for {gizmoId}");
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.ProjectFilesAttachOk,
                SyncTraceCategory.ProjectFiles,
                SyncTraceLevel.Info,
                $"ProjectFiles attach ok file={file.Name}",
                phase: SyncTracePhase.Attach,
                outcome: "ok",
                data: new { fileName = file.Name, fileId = file.FileId, gizmoId });

            if (!attachOptions.SkipPostAttachSidebar)
            {
                await EvaluatePostAttachSidebarAsync(
                    core,
                    perFileBaseline,
                    gizmoId,
                    [file],
                    strictSyncAttach,
                    skipSecondSidebarPoll,
                    cancellationToken);
            }

            if (!attachOptions.SkipOwnershipVerify)
            {
                await VerifyFilesOwnershipAfterAttachAsync(core, gizmoId, [file], cancellationToken);
                await VerifyFilesDownloadableAfterAttachAsync(core, gizmoId, [file], cancellationToken);
            }

            InvalidateAttachCaches(gizmoId);
            return true;
        }

        ProjectLinkDiagnostics.Log(
            $"ProjectFilesAttach exhausted attempts for file={file.Name} on {gizmoId}; falling back to upsert");
        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.AttachStrategy,
            SyncTraceCategory.Attach,
            SyncTraceLevel.Info,
            "Falling back to detail upsert attach",
            phase: SyncTracePhase.Attach,
            data: new { strategy = "detail_upsert", fileName = file.Name, gizmoId });
        return false;
    }

    private async Task AttachSnorlaxFileViaUpsertFallbackAsync(
        CoreWebView2 core,
        string gizmoId,
        GizmoFileRef newFile,
        JsonElement detailJson,
        string title,
        string instructions,
        Guid? adventureId,
        string caller,
        HashSet<string> perFileBaseline,
        bool strictSyncAttach,
        SnorlaxAttachOptions attachOptions,
        CancellationToken cancellationToken)
    {
        var detailFiles = GizmoResponseParser.CollectFileRefsDeep(detailJson);
        var mergedFiles = MergeDetailFilesWithUploads(detailFiles, [newFile]);
        var locationCandidates = UpsertFileLocationCandidatesForSnorlaxFallback(detailFiles, mergedFiles);
        var usedPerFileLocations = true;
        IReadOnlyList<GizmoFileRef> bodyFiles = mergedFiles;
        var location = mergedFiles
            .FirstOrDefault(f => string.Equals(f.FileId, newFile.FileId, StringComparison.Ordinal))
            ?.Location
            ?? InferAttachFileLocationFromDetail(detailFiles);

        var maxAttempts = 1 + locationCandidates.Count;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                usedPerFileLocations = false;
                location = locationCandidates[attempt - 1];
                bodyFiles = ApplyUpsertFileLocation(mergedFiles, location);
            }

            var body = BuildUpsertBodyFromDetail(detailJson, gizmoId, bodyFiles);

            ProjectLinkDiagnostics.Log(
                $"Incremental detail upsert file={newFile.Name} merged={bodyFiles.Count} "
                + $"location={location} perFileLocations={usedPerFileLocations}");

            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.UpsertAttachAttempt,
                SyncTraceCategory.Upsert,
                SyncTraceLevel.Info,
                $"Incremental detail upsert file={newFile.Name}",
                phase: SyncTracePhase.Attach,
                data: new
                {
                    fileName = newFile.Name,
                    fileId = newFile.FileId,
                    location,
                    mergedCount = bodyFiles.Count,
                    detailBody = true,
                    strategy = "detail_upsert",
                });

            var attachAudit = new AttachUpsertAuditContext
            {
                Location = location,
                DetailBody = true,
                MergedCount = bodyFiles.Count,
                AttachFileName = newFile.Name,
            };

            ProjectUpsertResult result;
            try
            {
                result = await UpsertProjectSafeAsync(
                    core,
                    ProjectUpsertIntent.AttachFiles,
                    caller,
                    adventureId,
                    gizmoId,
                    title,
                    instructions,
                    bodyFiles,
                    cancellationToken,
                    attachBodyOverride: body,
                    attachAudit: attachAudit);
            }
            catch (ChatGptApiException ex) when (ex.Message.StartsWith("upsert_forked_duplicate", StringComparison.Ordinal)
                                                 || ex.Message.StartsWith("upsert_id_mismatch", StringComparison.Ordinal))
            {
                ProjectLinkDiagnostics.Log(
                    $"Incremental attach id mismatch for {gizmoId} file={newFile.Name}: {ex.Message}");
                throw;
            }

            if (result.Outcome == ProjectUpsertOutcome.Updated)
            {
                if (!attachOptions.SkipPostAttachSidebar)
                {
                    await EvaluatePostAttachSidebarAsync(
                        core,
                        perFileBaseline,
                        gizmoId,
                        [newFile],
                        strictSyncAttach,
                        skipSecondSidebarPoll: true,
                        cancellationToken);
                }

                var linkedFiles = await GetProjectFilesDirectAsync(
                    core,
                    gizmoId,
                    cancellationToken,
                    ensureProjectPage: false);
                if (linkedFiles.Any(f => string.Equals(f.FileId, newFile.FileId, StringComparison.Ordinal)))
                {
                    if (!attachOptions.SkipOwnershipVerify)
                        await VerifyFileOwnershipAfterAttachAsync(core, gizmoId, newFile.FileId!, cancellationToken);

                    InvalidateAttachCaches(gizmoId);
                    return;
                }

                if (ShouldRetryUpsertLocationAfterAttempt(
                        result.Outcome,
                        filesVisibleOnLinkedProject: false,
                        hasMoreLocationCandidates: attempt < maxAttempts - 1))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Upsert attach location={location} returned updated but file not visible on {gizmoId}; "
                        + "trying next location");
                    continue;
                }

                if (!attachOptions.SkipOwnershipVerify)
                    await VerifyFileOwnershipAfterAttachAsync(core, gizmoId, newFile.FileId!, cancellationToken);

                InvalidateAttachCaches(gizmoId);
                return;
            }

            if (result.Outcome == ProjectUpsertOutcome.Unresolved && result.Message.Status == 200)
            {
                var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
                if (!CanProceedAfterSnorlaxAttachUpsert(
                        result,
                        gizmoId,
                        bodyFiles,
                        sidebar,
                        perFileBaseline))
                {
                    throw new ChatGptApiException(
                        $"attach_failed: unresolved upsert without sidebar confirmation for {gizmoId} file={newFile.Name}",
                        ChatGptApiEndpoints.ProjectUpsert);
                }

                if (!attachOptions.SkipPostAttachSidebar)
                {
                    await EvaluatePostAttachSidebarAsync(
                        core,
                        perFileBaseline,
                        gizmoId,
                        [newFile],
                        strictSyncAttach,
                        skipSecondSidebarPoll: true,
                        cancellationToken);
                }

                if (!attachOptions.SkipOwnershipVerify)
                    await VerifyFileOwnershipAfterAttachAsync(core, gizmoId, newFile.FileId!, cancellationToken);

                InvalidateAttachCaches(gizmoId);
                return;
            }

            throw new ChatGptApiException(
                $"attach_failed: snorlax upsert outcome={result.Outcome.ToString().ToLowerInvariant()} "
                + $"for {gizmoId} file={newFile.Name}",
                ChatGptApiEndpoints.ProjectUpsert);
        }

        throw new ChatGptApiException(
            $"attach_failed: files were not confirmed on the linked project after upsert for {gizmoId} file={newFile.Name}",
            ChatGptApiEndpoints.ProjectUpsert);
    }

    private async Task DetachSnorlaxFilesViaDetailUpsertAsync(
        CoreWebView2 core,
        string gizmoId,
        ISet<string> idsToRemove,
        JsonElement detailJson,
        CancellationToken cancellationToken)
    {
        if (!TryValidateDetailForAttachReadOnly(detailJson, gizmoId, out var detailError))
        {
            throw new ChatGptApiException(
                detailError ?? "Gizmo detail validation failed.",
                ChatGptApiEndpoints.GizmoDetail(gizmoId));
        }

        var detailFiles = GizmoResponseParser.CollectFileRefsDeep(detailJson);
        var remainingFromDetail = FilterDetailFilesExcluding(detailFiles, idsToRemove);
        var detachBaseline = await SnapshotSnorlaxProjectIdsAsync(core, cancellationToken);
        await EnsureNoNewSnorlaxProjectsBeforeUpsertAsync(
            core,
            detachBaseline,
            gizmoId,
            cancellationToken);

        var locationCandidates = UpsertFileLocationCandidatesForSnorlaxFallback(
            detailFiles,
            remainingFromDetail);
        IReadOnlyList<GizmoFileRef> bodyFiles = remainingFromDetail;
        var location = InferAttachFileLocationFromDetail(detailFiles);

        ProjectLinkDiagnostics.Log(
            $"Snorlax detail upsert detach for {gizmoId}: removing {idsToRemove.Count} file(s), "
            + $"keeping {remainingFromDetail.Count}");

        var maxAttempts = 1 + locationCandidates.Count;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                location = locationCandidates[attempt - 1];
                bodyFiles = ApplyUpsertFileLocation(remainingFromDetail, location);
            }

            var body = BuildUpsertBodyFromDetail(detailJson, gizmoId, bodyFiles);

            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.UpsertAttachAttempt,
                SyncTraceCategory.Upsert,
                SyncTraceLevel.Info,
                $"Detail upsert detach removing {idsToRemove.Count} file(s)",
                phase: SyncTracePhase.Attach,
                data: new
                {
                    removeCount = idsToRemove.Count,
                    keepCount = bodyFiles.Count,
                    location,
                    detailBody = true,
                    strategy = "detail_upsert_detach",
                    gizmoId,
                });

            ProjectUpsertResult result;
            try
            {
                result = await UpsertProjectSafeAsync(
                    core,
                    ProjectUpsertIntent.AttachFiles,
                    "DetachFiles",
                    adventureId: null,
                    gizmoId,
                    title: null,
                    instructions: null,
                    bodyFiles,
                    cancellationToken,
                    attachBodyOverride: body,
                    attachAudit: new AttachUpsertAuditContext
                    {
                        Location = location,
                        DetailBody = true,
                        MergedCount = bodyFiles.Count,
                    });
            }
            catch (ChatGptApiException ex) when (ex.Message.StartsWith("upsert_forked_duplicate", StringComparison.Ordinal)
                                                 || ex.Message.StartsWith("upsert_id_mismatch", StringComparison.Ordinal))
            {
                ProjectLinkDiagnostics.Log(
                    $"Snorlax detail detach id mismatch for {gizmoId}: {ex.Message}");
                throw;
            }

            if (result.Outcome == ProjectUpsertOutcome.Updated)
            {
                var linkedAfter = await GetProjectFilesDirectAsync(
                    core,
                    gizmoId,
                    cancellationToken,
                    ensureProjectPage: false);
                if (linkedAfter.Any(f => idsToRemove.Contains(f.FileId))
                    && attempt < maxAttempts - 1)
                {
                    ProjectLinkDiagnostics.Log(
                        $"Detail detach upsert updated {gizmoId} but orphan file(s) remain; "
                        + $"trying location={locationCandidates[attempt]}");
                    continue;
                }

                return;
            }

            if (result.Outcome == ProjectUpsertOutcome.Unresolved && result.Message.Status == 200)
            {
                var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
                if (CanProceedAfterSnorlaxDetachUpsert(
                        result,
                        gizmoId,
                        idsToRemove,
                        sidebar,
                        detachBaseline))
                {
                    return;
                }

                if (attempt < maxAttempts - 1)
                {
                    ProjectLinkDiagnostics.Log(
                        $"Detail detach unresolved for {gizmoId}; trying next location");
                    continue;
                }

                throw new ChatGptApiException(
                    $"upsert_detach_failed: unresolved upsert without sidebar confirmation for {gizmoId}",
                    ChatGptApiEndpoints.ProjectUpsert,
                    result.Message.Status,
                    result.Message.BodyText);
            }

            if (attempt < maxAttempts - 1)
            {
                ProjectLinkDiagnostics.Log(
                    $"Detail detach outcome={result.Outcome.ToString().ToLowerInvariant()} for {gizmoId}; "
                    + "trying next location");
                continue;
            }

            throw new ChatGptApiException(
                $"upsert_detach_failed: snorlax upsert outcome={result.Outcome.ToString().ToLowerInvariant()} "
                + $"for {gizmoId}",
                ChatGptApiEndpoints.ProjectUpsert,
                result.Message.Status,
                result.Message.BodyText);
        }
    }

    private async Task AttachSnorlaxProjectFilesAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> newFiles,
        Guid? adventureId,
        string caller,
        CancellationToken cancellationToken) =>
        await AttachIncrementalViaDetailUpsertAsync(
            core,
            gizmoId,
            newFiles,
            adventureId,
            caller,
            knownExistingFiles: null,
            skipPreflight: false,
            SnorlaxAttachOptions.Default,
            cancellationToken);

    private async Task EnsureNoNewSnorlaxProjectsBeforeUpsertAsync(
        CoreWebView2 core,
        ISet<string> sessionBaseline,
        string linkedGizmoId,
        CancellationToken cancellationToken)
    {
        var current = await SnapshotSnorlaxProjectIdsAsync(core, cancellationToken);
        var newForkIds = current.Where(id => !sessionBaseline.Contains(id)).ToList();
        if (newForkIds.Count == 0)
            return;

        ProjectLinkDiagnostics.Log(
            $"New sidebar project(s) before upsert: {string.Join(", ", newForkIds)}");
        throw new ChatGptApiException(
            $"upsert_forked_duplicate: new project {newForkIds[0]} appeared before attach upsert. "
            + $"Keep the linked project {linkedGizmoId}. Delete {newForkIds[0]} in the ChatGPT sidebar and retry.",
            ChatGptApiEndpoints.ProjectUpsert);
    }

    internal static List<GizmoFileRef> ApplyUpsertFileLocation(
        IReadOnlyList<GizmoFileRef> files,
        string location) =>
        files
            .Select(f => new GizmoFileRef
            {
                FileId = f.FileId,
                Name = f.Name,
                Location = location,
            })
            .ToList();

    internal static bool CanProceedAfterSnorlaxAttachUpsert(
        ProjectUpsertResult result,
        string linkedGizmoId,
        IReadOnlyList<GizmoFileRef> mergedFiles,
        IReadOnlyList<GizmoSummary> sidebarProjects,
        ISet<string> baselineSnorlaxIds)
    {
        if (result.Outcome == ProjectUpsertOutcome.Updated)
            return true;

        if (result.Outcome != ProjectUpsertOutcome.Unresolved || result.Message.Status != 200)
            return false;

        if (FindNewSnorlaxProjectIds(baselineSnorlaxIds, sidebarProjects).Count > 0)
            return false;

        return SidebarProjectContainsAllFiles(sidebarProjects, linkedGizmoId, mergedFiles);
    }

    internal static bool CanProceedAfterSnorlaxDetachUpsert(
        ProjectUpsertResult result,
        string linkedGizmoId,
        ISet<string> removedFileIds,
        IReadOnlyList<GizmoSummary> sidebarProjects,
        ISet<string> baselineSnorlaxIds)
    {
        if (result.Outcome == ProjectUpsertOutcome.Updated)
            return true;

        if (result.Outcome != ProjectUpsertOutcome.Unresolved || result.Message.Status != 200)
            return false;

        if (FindNewSnorlaxProjectIds(baselineSnorlaxIds, sidebarProjects).Count > 0)
            return false;

        var linked = sidebarProjects.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, linkedGizmoId));
        if (linked is null)
            return false;

        return !linked.Files.Any(f => removedFileIds.Contains(f.FileId));
    }

    internal static List<GizmoFileRef> FilterDetailFilesExcluding(
        IReadOnlyList<GizmoFileRef> detailFiles,
        ISet<string> fileIdsToRemove)
    {
        var remaining = new List<GizmoFileRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var existing in detailFiles)
        {
            if (string.IsNullOrWhiteSpace(existing.FileId)
                || fileIdsToRemove.Contains(existing.FileId)
                || !seen.Add(existing.FileId))
            {
                continue;
            }

            remaining.Add(new GizmoFileRef
            {
                FileId = existing.FileId,
                Name = string.IsNullOrWhiteSpace(existing.Name) ? existing.FileId : existing.Name,
                Location = ResolveUpsertFileLocation(existing),
            });
        }

        return remaining;
    }

    internal static List<GizmoFileRef> MergeDetailFilesWithUploads(
        IReadOnlyList<GizmoFileRef> detailFiles,
        IReadOnlyList<GizmoFileRef> newUploads)
    {
        var merged = new List<GizmoFileRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var existing in detailFiles)
        {
            if (string.IsNullOrWhiteSpace(existing.FileId) || !seen.Add(existing.FileId))
                continue;

            merged.Add(new GizmoFileRef
            {
                FileId = existing.FileId,
                Name = string.IsNullOrWhiteSpace(existing.Name) ? existing.FileId : existing.Name,
                Location = ResolveUpsertFileLocation(existing),
            });
        }

        var defaultLocation = InferAttachFileLocationFromDetail(detailFiles);

        foreach (var upload in newUploads)
        {
            if (string.IsNullOrWhiteSpace(upload.FileId))
                continue;

            var location = string.IsNullOrWhiteSpace(upload.Location)
                ? defaultLocation
                : ResolveUpsertFileLocation(upload);

            var entry = new GizmoFileRef
            {
                FileId = upload.FileId,
                Name = string.IsNullOrWhiteSpace(upload.Name) ? upload.FileId : upload.Name,
                Location = location,
            };

            var duplicateIndex = merged.FindIndex(f =>
                string.Equals(f.FileId, upload.FileId, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(upload.Name)
                    && string.Equals(f.Name, upload.Name, StringComparison.OrdinalIgnoreCase)));

            if (duplicateIndex >= 0)
                merged[duplicateIndex] = entry;
            else
                merged.Add(entry);

            seen.Add(upload.FileId);
        }

        return merged;
    }

    internal static string InferAttachFileLocationFromDetail(IReadOnlyList<GizmoFileRef> detailFiles)
    {
        if (detailFiles is not { Count: > 0 })
            return DefaultUpsertFileLocation;

        var grouped = detailFiles
            .Select(f => ResolveUpsertFileLocation(f))
            .GroupBy(location => location, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .ToList();

        return grouped.Count > 0 ? grouped[0] : DefaultUpsertFileLocation;
    }

    private static bool IsUpsertAttach422(ChatGptApiException ex) =>
        ex.StatusCode == 422
        || (ex.Message?.Contains("http_422", StringComparison.OrdinalIgnoreCase) ?? false);

    private async Task<HashSet<string>> SnapshotSnorlaxProjectIdsAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
        return sidebar
            .Where(p => IsSnorlaxProjectId(p.Id))
            .Select(p => p.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task EvaluatePostAttachSidebarAsync(
        CoreWebView2 core,
        HashSet<string> baselineIds,
        string linkedGizmoId,
        IReadOnlyList<GizmoFileRef> newFiles,
        bool strictNoNewProjects,
        CancellationToken cancellationToken) =>
        await EvaluatePostAttachSidebarAsync(
            core,
            baselineIds,
            linkedGizmoId,
            newFiles,
            strictNoNewProjects,
            skipSecondSidebarPoll: false,
            cancellationToken);

    private async Task EvaluatePostAttachSidebarAsync(
        CoreWebView2 core,
        HashSet<string> baselineIds,
        string linkedGizmoId,
        IReadOnlyList<GizmoFileRef> newFiles,
        bool strictNoNewProjects,
        bool skipSecondSidebarPoll,
        CancellationToken cancellationToken)
    {
        var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
        EvaluatePostAttachSidebarSnapshot(
            baselineIds,
            sidebar,
            linkedGizmoId,
            newFiles,
            strictNoNewProjects);

        if (!strictNoNewProjects || skipSecondSidebarPoll)
            return;

        await Task.Delay(1500, cancellationToken);
        sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
        EvaluatePostAttachSidebarSnapshot(
            baselineIds,
            sidebar,
            linkedGizmoId,
            newFiles,
            strictNoNewProjects);
    }

    internal static List<string> FindNewSnorlaxProjectIds(
        ISet<string> baselineIds,
        IEnumerable<GizmoSummary> sidebarProjects) =>
        sidebarProjects
            .Where(p => IsSnorlaxProjectId(p.Id) && !baselineIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToList();

    internal static void EvaluatePostAttachSidebarSnapshot(
        ISet<string> baselineIds,
        IReadOnlyList<GizmoSummary> sidebarProjects,
        string linkedGizmoId,
        IReadOnlyList<GizmoFileRef> newFiles,
        bool strictNoNewProjects)
    {
        var newForkIds = FindNewSnorlaxProjectIds(baselineIds, sidebarProjects);
        if (newForkIds.Count == 0)
            return;

        if (strictNoNewProjects)
        {
            ProjectLinkDiagnostics.Log($"Upsert fork detected: new sidebar project {newForkIds[0]}");
            throw new ChatGptApiException(
                $"upsert_forked_duplicate: new project {newForkIds[0]} appeared during attach. "
                + "Delete it in the ChatGPT sidebar and retry.",
                ChatGptApiEndpoints.ProjectUpsert);
        }

        var newFileIds = newFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
            .Select(f => f.FileId!)
            .ToList();

        if (newFileIds.Count > 0
            && newFileIds.All(fileId =>
                FindProjectIdsContainingFile(sidebarProjects, fileId)
                    .Any(id => ChatGptUrls.GizmoIdsEqual(id, linkedGizmoId))))
        {
            ProjectLinkDiagnostics.Log(
                $"Sidebar fork orphan(s) {string.Join(", ", newForkIds)}; "
                + $"files confirmed on linked project {linkedGizmoId}");
            return;
        }

        foreach (var forkId in newForkIds)
        {
            var forkOwnsNewFile = newFileIds.Any(fileId =>
                FindProjectIdsContainingFile(sidebarProjects, fileId)
                    .Any(id => ChatGptUrls.GizmoIdsEqual(id, forkId)));

            if (!forkOwnsNewFile)
                continue;

            throw new ChatGptApiException(
                $"attach_landed_on_wrong_project: files landed on fork {forkId} instead of {linkedGizmoId}. "
                + $"Delete {forkId} in the ChatGPT sidebar and retry.",
                ChatGptApiEndpoints.ProjectUpsert);
        }

        ProjectLinkDiagnostics.Log($"Upsert fork detected: new sidebar project {newForkIds[0]}");
        throw new ChatGptApiException(
            $"upsert_forked_duplicate: new project {newForkIds[0]} appeared during attach. "
            + "Delete it in the ChatGPT sidebar and retry.",
            ChatGptApiEndpoints.ProjectUpsert);
    }

    public async Task<ProjectSyncPreflightResult> ValidateAttachFileOwnershipPreflightAsync(
        CoreWebView2 core,
        string linkedGizmoId,
        IEnumerable<string> fileIds,
        CancellationToken cancellationToken = default)
    {
        await PrepareForApiAsync(core, cancellationToken);

        var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
        return ValidateAttachFileOwnershipPreflight(sidebar, linkedGizmoId, fileIds);
    }

    internal static ProjectSyncPreflightResult ValidateAttachFileOwnershipPreflight(
        IReadOnlyList<GizmoSummary> sidebarProjects,
        string linkedGizmoId,
        IEnumerable<string> fileIds)
    {
        var conflictingProjectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fileId in fileIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            foreach (var ownerId in FindProjectIdsContainingFile(sidebarProjects, fileId!))
            {
                if (!ChatGptUrls.GizmoIdsEqual(ownerId, linkedGizmoId))
                    conflictingProjectIds.Add(ownerId);
            }
        }

        if (conflictingProjectIds.Count == 0)
            return ProjectSyncPreflightResult.Ok();

        var ids = conflictingProjectIds.ToList();
        return ProjectSyncPreflightResult.Blocked(
            "file_owned_by_other_project",
            "One or more uploaded files are already attached to another ChatGPT project. "
            + "Remove them from the other project or delete the duplicate project, then sync again. Ids: "
            + string.Join(", ", ids),
            ids);
    }

    private async Task<JsonElement?> GetGizmoDetailJsonAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken,
        bool ensureProjectPage = true)
    {
        if (ensureProjectPage)
            await EnsureProjectPageAsync(core, gizmoId, cancellationToken);
        else
            await PrepareForApiAsync(core, cancellationToken);

        var path = ChatGptApiEndpoints.GizmoDetail(gizmoId);
        var msg = await _bridge.SendAsync(
            core,
            ApiCommand("GET", path),
            cancellationToken: cancellationToken);

        if (msg.Status == 404 || !msg.Ok || msg.Json is not { } json)
            return null;

        return json;
    }

    public async Task<IReadOnlyList<string>> FindProjectsContainingFileAsync(
        CoreWebView2 core,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        await PrepareForApiAsync(core, cancellationToken);

        var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
        return FindProjectIdsContainingFile(sidebar, fileId);
    }

    public IReadOnlyList<string> FindProjectsContainingFile(
        IReadOnlyList<GizmoSummary> sidebar,
        string fileId) =>
        FindProjectIdsContainingFile(sidebar, fileId);

    private async Task<IReadOnlyList<GizmoSummary>> ListAllSidebarProjectsAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken,
        bool includeUnownedProjects = true)
    {
        if (includeUnownedProjects && ProjectSidebarCache.TryGet(out var cached))
            return cached;

        var byId = new Dictionary<string, GizmoSummary>(StringComparer.Ordinal);
        foreach (var project in await FetchSidebarPagesAsync(core, ownedOnly: true, cancellationToken))
            byId[project.Id] = project;

        if (includeUnownedProjects)
        {
            foreach (var project in await FetchSidebarPagesAsync(core, ownedOnly: false, cancellationToken))
                byId[project.Id] = project;

            var result = byId.Values.ToList();
            ProjectSidebarCache.Set(result);
            return result;
        }

        return byId.Values.ToList();
    }

    private static void InvalidateAttachCaches(string gizmoId)
    {
        ProjectRemoteListCache.Invalidate(gizmoId);
        ProjectSidebarCache.Invalidate();
    }

    internal static IReadOnlyList<string> FindProjectIdsContainingFile(
        IEnumerable<GizmoSummary> sidebarProjects,
        string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return [];

        return sidebarProjects
            .Where(p => p.Files.Any(f => string.Equals(f.FileId, fileId, StringComparison.Ordinal)))
            .Select(p => p.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    internal enum AttachFileOwnershipStatus
    {
        LinkedOwns,
        WrongOwner,
        NotVisible,
    }

    internal static AttachFileOwnershipStatus ResolveAttachFileOwnership(
        IReadOnlyList<string> owners,
        string linkedGizmoId,
        out string? wrongOwnerId)
    {
        wrongOwnerId = null;
        if (owners.Any(id => ChatGptUrls.GizmoIdsEqual(id, linkedGizmoId)))
            return AttachFileOwnershipStatus.LinkedOwns;

        wrongOwnerId = owners.FirstOrDefault(id => !ChatGptUrls.GizmoIdsEqual(id, linkedGizmoId));
        return wrongOwnerId is not null
            ? AttachFileOwnershipStatus.WrongOwner
            : AttachFileOwnershipStatus.NotVisible;
    }

    private async Task VerifyFileOwnershipAfterAttachAsync(
        CoreWebView2 core,
        string linkedGizmoId,
        string fileId,
        CancellationToken cancellationToken) =>
        await VerifyFilesOwnershipAfterAttachAsync(core, linkedGizmoId, [new GizmoFileRef { FileId = fileId, Name = fileId }], cancellationToken);

    private async Task VerifyFilesOwnershipAfterAttachAsync(
        CoreWebView2 core,
        string linkedGizmoId,
        IReadOnlyList<GizmoFileRef> files,
        CancellationToken cancellationToken)
    {
        var fileIds = files
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
            .Select(f => f.FileId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (fileIds.Count == 0)
            return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            IReadOnlyList<GizmoFileRef> onLinked;
            if (!ProjectRemoteListCache.TryGet(linkedGizmoId, out onLinked))
            {
                onLinked = await GetProjectFilesDirectAsync(
                    core,
                    linkedGizmoId,
                    cancellationToken,
                    ensureProjectPage: false);
                ProjectRemoteListCache.Set(linkedGizmoId, onLinked);
            }

            if (fileIds.All(id => onLinked.Any(f => string.Equals(f.FileId, id, StringComparison.Ordinal))))
            {
                ProjectLinkDiagnostics.Log(
                    $"Incremental attach confirmed {fileIds.Count} file_id(s) on linked project {linkedGizmoId}");
                return;
            }

            var missingOnLinked = fileIds
                .Where(id => onLinked.All(f => !string.Equals(f.FileId, id, StringComparison.Ordinal)))
                .ToList();
            if (missingOnLinked.Count == 0)
                return;

            var (allLinkedOwns, firstWrongOwner) = await ResolveBatchAttachOwnershipAsync(
                core,
                linkedGizmoId,
                missingOnLinked,
                includeUnownedProjects: false,
                cancellationToken);

            if (!allLinkedOwns && firstWrongOwner is null)
            {
                (allLinkedOwns, firstWrongOwner) = await ResolveBatchAttachOwnershipAsync(
                    core,
                    linkedGizmoId,
                    missingOnLinked,
                    includeUnownedProjects: true,
                    cancellationToken);
            }

            if (allLinkedOwns)
            {
                ProjectLinkDiagnostics.Log(
                    $"Incremental attach confirmed {fileIds.Count} file_id(s) on linked project {linkedGizmoId}");
                return;
            }

            if (firstWrongOwner is not null)
            {
                throw new ChatGptApiException(
                    $"attach_landed_on_wrong_project: expected {linkedGizmoId}, found files on {firstWrongOwner}",
                    ChatGptApiEndpoints.ProjectUpsert);
            }

            ProjectRemoteListCache.Invalidate(linkedGizmoId);
            if (attempt < 2)
                await Task.Delay(800, cancellationToken);
        }

        throw new ChatGptApiException(
            $"attach_not_visible: {fileIds.Count} file(s) not found on linked project {linkedGizmoId} after attach",
            ChatGptApiEndpoints.ProjectUpsert);
    }

    public Task VerifyUploadedProjectFilesDownloadableAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> files,
        CancellationToken cancellationToken) =>
        VerifyFilesDownloadableAfterAttachAsync(core, gizmoId, files, cancellationToken);

    private async Task VerifyFilesDownloadableAfterAttachAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> files,
        CancellationToken cancellationToken)
    {
        var detailJson = await GetGizmoDetailJsonAsync(core, gizmoId, cancellationToken, ensureProjectPage: false);
        var detailFiles = detailJson is { } json
            ? GizmoResponseParser.CollectFileRefsDeep(json)
            : (IReadOnlyList<GizmoFileRef>)[];
        var probeFiles = files
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
            .Select(f => new GizmoFileRef
            {
                FileId = f.FileId,
                Name = f.Name,
                Location = f.Location,
            })
            .ToList();
        EnrichFileRefsFromDetail(probeFiles, detailFiles);

        foreach (var file in probeFiles)
        {
            try
            {
                await DownloadFileAsync(
                    core,
                    file.FileId!,
                    cancellationToken,
                    gizmoId,
                    file.Location,
                    failFast: true);
            }
            catch (ChatGptApiException ex) when (IsRemoteFileDownloadUnavailable(ex))
            {
                ProjectLinkDiagnostics.Log(
                    $"Post-attach download probe failed file={file.Name} file_id={file.FileId} "
                    + $"location={file.Location ?? "(null)"} for {gizmoId}: {ex.Message}");
                throw new ChatGptApiException(
                    $"upload_not_downloadable: file={file.Name} file_id={file.FileId}",
                    ChatGptApiEndpoints.FileDownload(file.FileId!),
                    ex.StatusCode,
                    ex.RawBody);
            }
        }

        ProjectLinkDiagnostics.Log(
            $"Post-attach download probe ok for {probeFiles.Count} file(s) on {gizmoId}");
    }

    private async Task<(bool AllLinkedOwns, string? FirstWrongOwner)> ResolveBatchAttachOwnershipAsync(
        CoreWebView2 core,
        string linkedGizmoId,
        IReadOnlyList<string> fileIds,
        bool includeUnownedProjects,
        CancellationToken cancellationToken)
    {
        var sidebar = await ListAllSidebarProjectsAsync(
            core,
            cancellationToken,
            includeUnownedProjects);

        var allLinkedOwns = true;
        string? firstWrongOwner = null;
        foreach (var fileId in fileIds)
        {
            var owners = FindProjectIdsContainingFile(sidebar, fileId);
            var status = ResolveAttachFileOwnership(owners, linkedGizmoId, out var wrongOwner);
            if (status == AttachFileOwnershipStatus.LinkedOwns)
                continue;

            allLinkedOwns = false;
            if (status == AttachFileOwnershipStatus.WrongOwner)
            {
                firstWrongOwner = wrongOwner;
                break;
            }
        }

        return (allLinkedOwns, firstWrongOwner);
    }

    private async Task AttachFileViaBridgeAsync(
        CoreWebView2 core,
        string gizmoId,
        GizmoFileRef file,
        IReadOnlyList<GizmoFileRef>? existingFiles,
        string? projectTitle,
        string? projectInstructions,
        CancellationToken cancellationToken)
    {
        var msg = await _bridge.SendAsync(
            core,
            new
            {
                action = "attachProjectFile",
                gizmoId,
                fileId = file.FileId,
                fileName = file.Name,
                projectTitle,
                projectInstructions,
                existingFiles = existingFiles?
                    .Select(f => new { file_id = f.FileId, name = f.Name, location = f.Location })
                    .ToArray(),
                attachViaUpsertOnly = IsSnorlaxProjectId(gizmoId),
                allowUpsertAttachFallback = true,
            },
            timeoutMs: 90000,
            cancellationToken: cancellationToken);

        if (!msg.Ok)
        {
            throw new ChatGptApiException(
                FormatBridgeError(msg, "attach_failed"),
                ChatGptApiEndpoints.ProjectUpsert,
                msg.Status,
                msg.BodyText);
        }

        var responseGizmoId = TryExtractUpsertResponseGizmoId(msg);
        if (!string.IsNullOrWhiteSpace(responseGizmoId)
            && !ChatGptUrls.GizmoIdsEqual(responseGizmoId, gizmoId))
        {
            throw new ChatGptApiException(
                $"attach_landed_on_wrong_project: upsert returned project {responseGizmoId} instead of {gizmoId}",
                ChatGptApiEndpoints.ProjectUpsert);
        }

        ProjectLinkDiagnostics.Log(
            $"Bridge attach ok file={file.FileId} name={file.Name} project={gizmoId}");
    }

    internal static string? TryExtractUpsertResponseGizmoId(ApiBridgeMessage msg)
    {
        if (msg.Json is { } json)
        {
            var id = GizmoResponseParser.TryExtractUpsertGizmoId(json);
            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        if (msg.Root.TryGetProperty("responseGizmoId", out var direct)
            && direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString();
        }

        return null;
    }

    internal static void EnrichFileRefsFromDetail(
        IList<GizmoFileRef> targets,
        IReadOnlyList<GizmoFileRef> detailFiles)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (string.IsNullOrWhiteSpace(target.FileId))
                continue;

            var match = detailFiles.FirstOrDefault(d =>
                string.Equals(d.FileId, target.FileId, StringComparison.Ordinal));
            if (match is null)
                continue;

            var name = string.IsNullOrWhiteSpace(target.Name) ? match.Name : target.Name;
            var location = string.IsNullOrWhiteSpace(target.Location) ? match.Location : target.Location;
            if (string.Equals(name, target.Name, StringComparison.Ordinal)
                && string.Equals(location, target.Location, StringComparison.Ordinal))
            {
                continue;
            }

            targets[i] = new GizmoFileRef
            {
                FileId = target.FileId,
                Name = name,
                Location = location,
                Size = target.Size,
            };
        }
    }

    private async Task EnsureFileOnLinkedProjectAsync(
        CoreWebView2 core,
        string linkedGizmoId,
        string fileId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
            if (FindProjectIdsContainingFile(sidebar, fileId)
                .Any(id => ChatGptUrls.GizmoIdsEqual(id, linkedGizmoId)))
            {
                ProjectLinkDiagnostics.Log(
                    $"Incremental attach confirmed file_id {fileId} on linked project {linkedGizmoId}");
                return;
            }

            var wrongOwner = FindProjectIdsContainingFile(sidebar, fileId)
                .FirstOrDefault(id => !ChatGptUrls.GizmoIdsEqual(id, linkedGizmoId));
            if (wrongOwner is not null)
            {
                throw new ChatGptApiException(
                    $"attach_landed_on_wrong_project: expected {linkedGizmoId}, found {fileId} on {wrongOwner}",
                    ChatGptApiEndpoints.ProjectUpsert);
            }

            if (attempt < 2)
                await Task.Delay(800, cancellationToken);
        }

        throw new ChatGptApiException(
            $"attach_not_visible: file {fileId} not found on linked project {linkedGizmoId} after attach",
            ChatGptApiEndpoints.ProjectUpsert);
    }

    internal static bool SidebarProjectContainsAllFiles(
        IReadOnlyList<GizmoSummary> sidebarProjects,
        string linkedGizmoId,
        IReadOnlyList<GizmoFileRef> expectedFiles)
    {
        var linked = sidebarProjects.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, linkedGizmoId));
        if (linked is null)
            return false;

        return expectedFiles.All(f => linked.Files.Any(r =>
            string.Equals(r.FileId, f.FileId, StringComparison.Ordinal)));
    }

    internal static string? FindWrongOwnerForFiles(
        IReadOnlyList<GizmoSummary> sidebarProjects,
        string linkedGizmoId,
        IReadOnlyList<GizmoFileRef> files)
    {
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.FileId))
                continue;

            var wrongOwner = FindProjectIdsContainingFile(sidebarProjects, file.FileId)
                .FirstOrDefault(id => !ChatGptUrls.GizmoIdsEqual(id, linkedGizmoId));
            if (wrongOwner is not null)
                return wrongOwner;
        }

        return null;
    }

    private async Task ConfirmAttachedFilesOnProjectAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> expectedFiles,
        CancellationToken cancellationToken,
        bool ensureProjectPage = true)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var remoteFiles = await GetProjectFilesDirectAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: ensureProjectPage && attempt == 0);

            if (expectedFiles.All(f => RemoteFilesContainById(remoteFiles, f.FileId)))
            {
                ProjectLinkDiagnostics.Log(
                    $"Attach confirmed {expectedFiles.Count} file(s) on {gizmoId} via merged file list");
                return;
            }

            if (attempt < 2)
                await Task.Delay(1500, cancellationToken);
        }

        ProjectLinkDiagnostics.Log(
            $"Attach did not confirm files for {gizmoId}; requested={expectedFiles.Count}");
        throw new ChatGptApiException(
            "attach_failed: files were not confirmed on the linked project",
            ChatGptApiEndpoints.ProjectFilesAttach(gizmoId));
    }

    private async Task<List<GizmoFileRef>> MergeWithExistingProjectFilesAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> requestedFiles,
        CancellationToken cancellationToken)
    {
        var merged = new List<GizmoFileRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var existing in await GetProjectFilesDirectAsync(
                     core,
                     gizmoId,
                     cancellationToken,
                     ensureProjectPage: false))
        {
            if (string.IsNullOrWhiteSpace(existing.FileId) || !seen.Add(existing.FileId))
                continue;

            merged.Add(existing);
        }

        foreach (var file in requestedFiles)
        {
            if (string.IsNullOrWhiteSpace(file.FileId))
                continue;

            var duplicateIndex = merged.FindIndex(f =>
                string.Equals(f.FileId, file.FileId, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(file.Name)
                    && string.Equals(f.Name, file.Name, StringComparison.OrdinalIgnoreCase)));

            if (duplicateIndex >= 0)
                merged[duplicateIndex] = file;
            else
                merged.Add(file);

            seen.Add(file.FileId);
        }

        return merged;
    }

    public async Task<string?> UploadProjectFileAsync(
        CoreWebView2 core,
        string gizmoId,
        string fileName,
        byte[] content,
        string mimeType = "text/markdown",
        string? projectTitle = null,
        string? projectInstructions = null,
        IReadOnlyList<GizmoFileRef>? existingFiles = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectPageAsync(core, gizmoId, cancellationToken);

        var uploaded = await UploadProjectFileBytesAsync(
            core,
            gizmoId,
            fileName,
            content,
            mimeType,
            cancellationToken);
        if (uploaded is null)
            return null;

        if (IsSnorlaxProjectId(gizmoId))
        {
            await AttachSnorlaxProjectFilesAsync(
                core,
                gizmoId,
                [uploaded],
                adventureId: null,
                caller: "UploadProjectFile",
                cancellationToken);
            return await FinalizeUploadedProjectFileAsync(
                core,
                gizmoId,
                fileName,
                uploaded.FileId,
                new ApiBridgeMessage("""{"type":"apiResult","ok":true}"""),
                cancellationToken);
        }

        var fileRefs = BuildUpsertFileRefs(uploaded.FileId, fileName, existingFiles);
        var upsertMsg = await AttachViaUpsertAsync(
            core,
            gizmoId,
            fileRefs,
            projectTitle,
            projectInstructions,
            "UploadProjectFile",
            adventureId: null,
            cancellationToken);

        return await FinalizeUploadedProjectFileAsync(
            core,
            gizmoId,
            fileName,
            uploaded.FileId,
            upsertMsg,
            cancellationToken);
    }

    private async Task<ApiBridgeMessage> AttachViaUpsertAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> fileRefs,
        string? projectTitle,
        string? projectInstructions,
        string caller,
        Guid? adventureId,
        CancellationToken cancellationToken)
    {
        var isSyncAttach = string.Equals(caller, "SyncAttach", StringComparison.Ordinal);
        var summary = await GetProjectSummaryAsync(core, gizmoId, cancellationToken);
        if (isSyncAttach)
        {
            // Mirror linked project metadata only — never adventure title/instructions.
            projectTitle = summary?.Title ?? "Project";
            projectInstructions = summary?.Instructions ?? "";
        }
        else
        {
            projectTitle ??= summary?.Title ?? "Project";
            projectInstructions ??= summary?.Instructions ?? "";
        }

        ApiBridgeMessage? lastMsg = null;
        ProjectUpsertOutcome? lastOutcome = null;
        IReadOnlyList<string> locationCandidates = isSyncAttach
            ? UpsertFileLocationCandidatesForSyncAttach(fileRefs)
            : UpsertFileLocationCandidates(fileRefs).ToList();

        foreach (var location in locationCandidates)
        {
            var bodyFiles = fileRefs
                .Select(f => new GizmoFileRef
                {
                    FileId = f.FileId,
                    Name = f.Name,
                    Location = location,
                })
                .ToList();

            ProjectUpsertResult result;
            try
            {
                result = await UpsertProjectSafeAsync(
                    core,
                    ProjectUpsertIntent.AttachFiles,
                    caller,
                    adventureId,
                    gizmoId,
                    projectTitle,
                    projectInstructions,
                    bodyFiles,
                    cancellationToken);
            }
            catch (ChatGptApiException ex) when (ex.Message.StartsWith("upsert_forked_duplicate", StringComparison.Ordinal)
                                                 || ex.Message.StartsWith("upsert_id_mismatch", StringComparison.Ordinal))
            {
                ProjectLinkDiagnostics.Log(
                    $"Upsert attach id mismatch for {gizmoId} location={location}: {ex.Message}");
                throw;
            }
            catch (ChatGptApiException ex)
            {
                ProjectLinkDiagnostics.Log(
                    $"Upsert attach failed for {gizmoId} location={location}: {ex.Message}");
                if (isSyncAttach && location != locationCandidates[^1])
                    continue;

                throw;
            }

            lastMsg = result.Message;
            lastOutcome = result.Outcome;
            ProjectLinkDiagnostics.Log(
                $"Upsert attach location={location} requested={bodyFiles.Count} for {gizmoId} "
                + $"outcome={result.Outcome.ToString().ToLowerInvariant()}");

            if (result.Outcome == ProjectUpsertOutcome.Updated)
            {
                if (isSyncAttach)
                {
                    var sidebar = await ListAllSidebarProjectsAsync(core, cancellationToken);
                    var filesVisible = SidebarProjectContainsAllFiles(sidebar, gizmoId, bodyFiles);
                    if (filesVisible)
                        return result.Message;

                    var wrongOwner = FindWrongOwnerForFiles(sidebar, gizmoId, bodyFiles);
                    if (wrongOwner is not null)
                    {
                        throw new ChatGptApiException(
                            $"attach_landed_on_wrong_project: files landed on fork {wrongOwner} instead of {gizmoId}. "
                            + $"Delete {wrongOwner} in the ChatGPT sidebar and retry.",
                            ChatGptApiEndpoints.ProjectUpsert);
                    }

                    if (ShouldRetryUpsertLocationAfterAttempt(
                            result.Outcome,
                            filesVisible,
                            location != locationCandidates[^1]))
                    {
                        ProjectLinkDiagnostics.Log(
                            $"Upsert attach location={location} did not confirm on {gizmoId}; trying next location");
                        continue;
                    }

                    throw new ChatGptApiException(
                        "attach_failed: files were not confirmed on the linked project after upsert",
                        ChatGptApiEndpoints.ProjectFilesAttach(gizmoId));
                }

                return result.Message;
            }

            if (result.Outcome == ProjectUpsertOutcome.Created)
            {
                throw new ChatGptApiException(
                    "upsert_created_instead_of_attach: attach upsert created a new project",
                    ChatGptApiEndpoints.ProjectUpsert,
                    result.Message.Status,
                    result.Message.BodyText);
            }
        }

        if (lastOutcome == ProjectUpsertOutcome.IdMismatch)
        {
            throw new ChatGptApiException(
                $"upsert_id_mismatch: attach did not confirm project id {gizmoId}",
                ChatGptApiEndpoints.ProjectUpsert);
        }

        return lastMsg
               ?? throw new ChatGptApiException(
                   "attach_failed: upsert did not return a response",
                   ChatGptApiEndpoints.ProjectUpsert);
    }

    internal static IReadOnlyList<string> UpsertFileLocationCandidatesForSyncAttach(
        IReadOnlyList<GizmoFileRef> files) =>
        [DefaultUpsertFileLocation, AlternateUpsertFileLocation];

    private async Task<bool> ProjectFilesContainAllAsync(
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<GizmoFileRef> expectedFiles,
        CancellationToken cancellationToken)
    {
        var remoteFiles = await GetProjectFilesDirectAsync(
            core,
            gizmoId,
            cancellationToken,
            ensureProjectPage: false);

        return expectedFiles.All(f => RemoteFilesContainById(remoteFiles, f.FileId));
    }

    internal static IEnumerable<string> UpsertFileLocationCandidates(IReadOnlyList<GizmoFileRef> files)
    {
        var preferred = files
            .Select(f => ResolveUpsertFileLocation(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var location in preferred)
            yield return location;

        foreach (var fallback in new[] { DefaultUpsertFileLocation, AlternateUpsertFileLocation })
        {
            if (!preferred.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                yield return fallback;
        }
    }

    internal static IReadOnlyList<string> UpsertFileLocationCandidatesForSnorlaxFallback(
        IReadOnlyList<GizmoFileRef> detailFiles,
        IReadOnlyList<GizmoFileRef> mergedFiles)
    {
        var candidates = UpsertFileLocationCandidates(mergedFiles).ToList();
        if (!ShouldPreferSedimentFirstForSnorlaxFallback(detailFiles, mergedFiles))
            return candidates;

        return candidates
            .OrderBy(location =>
                location.Equals(AlternateUpsertFileLocation, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(location => location, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool ShouldPreferSedimentFirstForSnorlaxFallback(
        IReadOnlyList<GizmoFileRef> detailFiles,
        IReadOnlyList<GizmoFileRef> mergedFiles)
    {
        if (!InferAttachFileLocationFromDetail(detailFiles)
                .Equals(DefaultUpsertFileLocation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return mergedFiles.All(file =>
            ResolveUpsertFileLocation(file).Equals(DefaultUpsertFileLocation, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<GizmoSummary?> GetProjectSummaryAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        var sidebar = await ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
        var fromSidebar = sidebar.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, gizmoId));

        var detailJson = await GetGizmoDetailJsonAsync(
            core,
            gizmoId,
            cancellationToken,
            ensureProjectPage: false);
        if (detailJson is { } json)
            return ParseGizmoSummaryFromDetailJson(json) ?? fromSidebar;

        return fromSidebar;
    }

    private static GizmoSummary? ParseGizmoSummaryFromDetailJson(JsonElement json)
    {
        if (json.TryGetProperty("gizmo", out var wrap))
        {
            if (wrap.TryGetProperty("gizmo", out var inner))
                return GizmoResponseParser.ParseGizmoNode(inner, wrap);
            return GizmoResponseParser.ParseGizmoNode(wrap, wrap);
        }

        return GizmoResponseParser.ParseGizmoNode(json, json);
    }

    private async Task<string?> FinalizeUploadedProjectFileAsync(
        CoreWebView2 core,
        string gizmoId,
        string fileName,
        string fileId,
        ApiBridgeMessage upsertMsg,
        CancellationToken cancellationToken)
    {
        if (UpsertResponseContainsFile(upsertMsg, fileId, fileName))
        {
            ProjectLinkDiagnostics.Log($"Upsert attach confirmed file {fileId} on {gizmoId}");
        return fileId;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var remoteFiles = await GetProjectFilesDirectAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: false);
            if (RemoteFilesContain(remoteFiles, fileId, fileName))
            {
                ProjectLinkDiagnostics.Log($"Project file list confirmed {fileId} on {gizmoId}");
                return fileId;
            }

            if (attempt < 2)
                await Task.Delay(1500, cancellationToken);
        }

        return upsertMsg.Ok ? fileId : null;
    }

    internal static bool IsSnorlaxProjectId(string? gizmoId) =>
        !string.IsNullOrWhiteSpace(gizmoId)
        && gizmoId.StartsWith("g-p-", StringComparison.OrdinalIgnoreCase);

    private static bool UpsertResponseContainsFile(ApiBridgeMessage msg, string fileId, string fileName)
    {
        if (msg.Json is not { } json)
            return false;

        if (GizmoResponseParser.TryParseGizmoFromUpsert(json) is { } parsed
            && RemoteFilesContain(parsed.Files, fileId, fileName))
        {
            return true;
        }

        return JsonContainsFileReference(json, fileId, fileName);
    }

    private static bool UpsertResponseContainsAllFiles(
        ApiBridgeMessage msg,
        IReadOnlyList<GizmoFileRef> expectedFiles)
    {
        if (expectedFiles.Count == 0)
            return true;

        if (msg.Json is not { } json)
            return false;

        var parsed = GizmoResponseParser.TryParseGizmoFromUpsert(json);
        if (parsed is { Files.Count: > 0 })
        {
            return expectedFiles.All(f =>
                RemoteFilesContain(parsed.Files, f.FileId, f.Name));
        }

        return expectedFiles.All(f => JsonContainsFileReference(json, f.FileId, f.Name));
    }

    private static bool JsonContainsFileReference(JsonElement json, string fileId, string fileName)
    {
        if (ContainsFileReferenceNode(json, fileId, fileName))
            return true;

        foreach (var prop in json.EnumerateObject())
        {
            if (ContainsFileReferenceNode(prop.Value, fileId, fileName))
                return true;
        }

        return false;
    }

    private static bool ContainsFileReferenceNode(JsonElement node, string fileId, string fileName)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var nodeFileId = JsonElementParsing.GetStringOrNull(node, "file_id")
                ?? JsonElementParsing.GetStringOrNull(node, "id");

            if (string.Equals(nodeFileId, fileId, StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrWhiteSpace(fileName)
                && node.TryGetProperty("name", out var nameEl)
                && string.Equals(nameEl.GetString(), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (node.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            foreach (var child in node.ValueKind == JsonValueKind.Array
                         ? node.EnumerateArray()
                         : node.EnumerateObject().Select(p => p.Value))
            {
                if (ContainsFileReferenceNode(child, fileId, fileName))
                    return true;
            }
        }

        return false;
    }

    private static List<GizmoFileRef> BuildUpsertFileRefs(
        string newFileId,
        string fileName,
        IReadOnlyList<GizmoFileRef>? existingFiles)
    {
        var refs = new List<GizmoFileRef>
        {
            new()
            {
                FileId = newFileId,
                Name = fileName,
                Location = DefaultUpsertFileLocation,
            },
        };

        if (existingFiles is null)
            return refs;

        foreach (var existing in existingFiles)
        {
            if (string.IsNullOrWhiteSpace(existing.FileId)
                || string.Equals(existing.FileId, newFileId, StringComparison.Ordinal))
            {
                continue;
            }

            refs.Add(new GizmoFileRef
            {
                FileId = existing.FileId,
                Name = string.IsNullOrWhiteSpace(existing.Name) ? existing.FileId : existing.Name,
                Location = ResolveUpsertFileLocation(existing),
            });
        }

        return refs;
    }

    private static string? ExtractUploadedFileId(ApiBridgeMessage msg)
    {
        if (msg.Root.TryGetProperty("fileId", out var fid) && fid.ValueKind == JsonValueKind.String)
            return fid.GetString();

        if (msg.Json is { } json)
        {
            return JsonElementParsing.GetStringOrNull(json, "file_id")
                   ?? JsonElementParsing.GetStringOrNull(json, "id");
        }

        return null;
    }

    private static bool RemoteFilesContain(
        IReadOnlyList<GizmoFileRef> remoteFiles,
        string fileId,
        string fileName)
    {
        if (RemoteFilesContainById(remoteFiles, fileId))
                return true;

        foreach (var f in remoteFiles)
        {
            if (!string.IsNullOrWhiteSpace(f.Name)
                && string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool RemoteFilesContainById(
        IReadOnlyList<GizmoFileRef> remoteFiles,
        string fileId) =>
        !string.IsNullOrWhiteSpace(fileId)
        && remoteFiles.Any(f => string.Equals(f.FileId, fileId, StringComparison.Ordinal));

    internal static string ResolveUploadUseCase(string mimeType)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "multimodal";

        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mimeType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            || mimeType.Contains("msword", StringComparison.OrdinalIgnoreCase))
        {
            return "my_files";
        }

        return "ace_upload";
    }

    internal static string ResolveProjectSourceUploadUseCase(string mimeType) =>
        ResolveUploadUseCase(mimeType) switch
        {
            "my_files" => "ace_upload",
            var useCase => useCase,
        };

    internal static string ResolveUploadedProjectFileLocation(ApiBridgeMessage msg, string useCase)
    {
        if (msg.Root.TryGetProperty("libraryUpload", out var libraryUpload)
            && libraryUpload.ValueKind == JsonValueKind.True)
        {
            return AlternateUpsertFileLocation;
        }

        if (msg.Root.TryGetProperty("location", out var location)
            && location.ValueKind == JsonValueKind.String)
        {
            var normalized = NormalizeUpsertFileLocation(location.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return ResolveUploadFileLocation(useCase);
    }

    internal static string ResolveUploadFileLocation(string useCase)
    {
        if (useCase.Equals("gizmo", StringComparison.OrdinalIgnoreCase)
            || useCase.Equals("multimodal", StringComparison.OrdinalIgnoreCase)
            || useCase.Equals("ace_upload", StringComparison.OrdinalIgnoreCase))
        {
            return AlternateUpsertFileLocation;
        }

        return DefaultUpsertFileLocation;
    }

    private static string ResolveAttachPath(string gizmoId)
    {
        var preferred = ChatGptApiDiscovery.GetPreferredUploadAttachPath();
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var adapted = AdaptPreferredAttachPath(preferred, gizmoId);
            if (adapted is not null)
                return adapted;
        }

        return ChatGptApiEndpoints.ProjectFilesAttach(gizmoId);
    }

    internal static string? AdaptPreferredAttachPath(string preferred, string gizmoId)
    {
        const string marker = "/projects/";
        var idx = preferred.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var afterProjects = idx + marker.Length;
        var nextSlash = preferred.IndexOf('/', afterProjects);
        if (nextSlash < 0)
            return ChatGptApiEndpoints.ProjectFilesAttach(gizmoId);

        var suffix = preferred[nextSlash..];
        return $"/backend-api/projects/{Uri.EscapeDataString(gizmoId)}{suffix}";
    }

    internal static object BuildUpsertBody(
        string? gizmoId,
        string? title,
        string? instructions,
        IReadOnlyList<GizmoFileRef>? existingFiles = null)
    {
        // ChatGPT snorlax/upsert expects a flat body (not { gizmo: { ... } }).
        var body = new Dictionary<string, object?>
        {
            ["instructions"] = instructions ?? "",
            ["display"] = new Dictionary<string, object?>
            {
                ["name"] = string.IsNullOrWhiteSpace(title) ? "Project" : title,
                ["description"] = "",
                ["prompt_starters"] = Array.Empty<object>(),
            },
            ["tools"] = Array.Empty<object>(),
            ["files"] = BuildUpsertFiles(existingFiles),
            ["training_disabled"] = false,
            ["sharing"] = DefaultPrivateSharing(),
        };

        if (!string.IsNullOrWhiteSpace(gizmoId))
            body["id"] = gizmoId;

        return body;
    }

    internal static object BuildUpsertAttachBody(
        string gizmoId,
        string? title,
        string? instructions,
        IReadOnlyList<GizmoFileRef>? files) =>
        BuildUpsertBody(gizmoId, title, instructions, files);

    internal static object BuildUpsertBodyFromDetail(
        JsonElement detailRoot,
        string gizmoId,
        IReadOnlyList<GizmoFileRef>? files)
    {
        var node = detailRoot;
        if (detailRoot.TryGetProperty("gizmo", out var wrap))
            node = wrap.TryGetProperty("gizmo", out var inner) ? inner : wrap;

        var defaultBody = (Dictionary<string, object?>)BuildUpsertBody(gizmoId, null, null, null);

        var body = new Dictionary<string, object?>
        {
            ["id"] = gizmoId,
            ["instructions"] = node.TryGetProperty("instructions", out var instrEl)
                ? JsonElementParsing.GetStringOrNull(instrEl) ?? ""
                : "",
            ["files"] = BuildUpsertFiles(files),
            ["training_disabled"] = node.TryGetProperty("training_disabled", out var td)
                                    && td.ValueKind == JsonValueKind.True,
        };

        body["display"] = SanitizeUpsertDisplay(
            node.TryGetProperty("display", out var display)
                ? JsonElementToObject(display) ?? defaultBody["display"]
                : defaultBody["display"]);

        body["sharing"] = EnsureUpsertSharingList(
            node.TryGetProperty("sharing", out var sharing)
                ? NormalizeUpsertSharing(sharing, defaultBody["sharing"]!)
                : defaultBody["sharing"]);

        body["tools"] = node.TryGetProperty("tools", out var tools)
            ? NormalizeUpsertTools(tools)
            : Array.Empty<object>();

        return body;
    }

    internal static object SanitizeUpsertDisplay(object? display)
    {
        var fallback = new Dictionary<string, object?>
        {
            ["name"] = "Project",
            ["description"] = "",
            ["prompt_starters"] = Array.Empty<object>(),
        };

        if (display is not Dictionary<string, object?> dict)
            return fallback;

        return new Dictionary<string, object?>
        {
            ["name"] = dict.TryGetValue("name", out var name) && name is string nameText && !string.IsNullOrWhiteSpace(nameText)
                ? nameText
                : "Project",
            ["description"] = dict.TryGetValue("description", out var description) ? description ?? "" : "",
            ["prompt_starters"] = dict.TryGetValue("prompt_starters", out var starters) && starters is IList
                ? starters
                : Array.Empty<object>(),
        };
    }

    internal static object DefaultPrivateSharing() =>
        new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "private",
                ["capabilities"] = DefaultPrivateSharingCapabilities(),
            },
        };

    internal static Dictionary<string, object?> DefaultPrivateSharingCapabilities() =>
        new()
                    {
                        ["can_read"] = true,
                        ["can_view_config"] = false,
                        ["can_write"] = false,
                        ["can_delete"] = false,
                        ["can_export"] = false,
                        ["can_share"] = false,
        };

    internal static bool IsUpsertSharingType(string? type) =>
        string.Equals(type, "private", StringComparison.Ordinal);

    internal static object? TryCoerceUpsertSharingEntry(object? entry, object defaultSharing)
    {
        if (entry is not Dictionary<string, object?> dict)
            return null;

        var type = dict.TryGetValue("type", out var typeObj) ? typeObj as string : null;
        if (!dict.TryGetValue("capabilities", out var capabilitiesObj))
            return null;

        if (IsUpsertSharingType(type))
            return dict;

        if (string.IsNullOrWhiteSpace(type))
        {
            return new Dictionary<string, object?>
            {
                ["type"] = "private",
                ["capabilities"] = capabilitiesObj is Dictionary<string, object?> caps
                    ? caps
                    : GetDefaultCapabilitiesFromSharing(defaultSharing),
            };
        }

        return null;
    }

    internal static object NormalizeUpsertSharingEntryArray(object? converted, object defaultSharing)
    {
        if (converted is not object[] entries || entries.Length == 0)
            return defaultSharing;

        var coerced = new List<object>(entries.Length);
        foreach (var entry in entries)
        {
            var normalized = TryCoerceUpsertSharingEntry(entry, defaultSharing);
            if (normalized is null)
                return defaultSharing;

            coerced.Add(normalized);
        }

        return coerced.ToArray();
    }

    internal static object EnsureUpsertSharingList(object? sharing)
    {
        if (sharing is IList { Count: > 0 })
            return sharing;

        return DefaultPrivateSharing();
    }

    /// <summary>
    /// Gizmo detail returns sharing as a read-model object; snorlax upsert requires a typed array.
    /// </summary>
    internal static object NormalizeUpsertSharing(JsonElement sharingFromDetail, object defaultSharing)
    {
        return sharingFromDetail.ValueKind switch
        {
            JsonValueKind.Array => NormalizeUpsertSharingEntryArray(
                JsonElementToObject(sharingFromDetail),
                defaultSharing),
            JsonValueKind.Object => CoerceSingleUpsertSharingEntry(
                JsonElementToObject(sharingFromDetail),
                defaultSharing),
            _ => defaultSharing,
        };
    }

    private static object CoerceSingleUpsertSharingEntry(object? entry, object defaultSharing)
    {
        var coerced = TryCoerceUpsertSharingEntry(entry, defaultSharing);
        return coerced is not null ? new[] { coerced } : defaultSharing;
    }

    private static Dictionary<string, object?> GetDefaultCapabilitiesFromSharing(object defaultSharing)
    {
        if (defaultSharing is IList list
            && list.Count > 0
            && list[0] is Dictionary<string, object?> entry
            && entry.TryGetValue("capabilities", out var caps)
            && caps is Dictionary<string, object?> capsDict)
        {
            return capsDict;
        }

        return DefaultPrivateSharingCapabilities();
    }

    internal static object NormalizeUpsertTools(JsonElement toolsFromDetail)
    {
        if (toolsFromDetail.ValueKind != JsonValueKind.Array)
            return Array.Empty<object>();

        return JsonElementToObject(toolsFromDetail) ?? Array.Empty<object>();
    }

    internal static object? JsonElementToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = JsonElementToObject(prop.Value);
                return dict;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(JsonElementToObject).ToArray();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out var n) ? n : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static object[] BuildUpsertFiles(IReadOnlyList<GizmoFileRef>? existingFiles)
    {
        if (existingFiles is not { Count: > 0 })
            return [];

        return existingFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
            .Select(f => (object)BuildUpsertFileEntry(f))
            .ToArray();
    }

    internal const string DefaultUpsertFileLocation = "fs";

    internal const string AlternateUpsertFileLocation = "sediment";

    internal static string ResolveUpsertFileLocation(GizmoFileRef file) =>
        NormalizeUpsertFileLocation(file.Location);

    internal static string NormalizeUpsertFileLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return DefaultUpsertFileLocation;

        if (location.Equals("fs", StringComparison.OrdinalIgnoreCase)
            || location.Equals("sediment", StringComparison.OrdinalIgnoreCase))
        {
            return location.ToLowerInvariant();
        }

        if (location.StartsWith("file-service://", StringComparison.OrdinalIgnoreCase)
            || location.StartsWith("fs://", StringComparison.OrdinalIgnoreCase))
        {
            return "fs";
        }

        if (location.StartsWith("sediment://", StringComparison.OrdinalIgnoreCase))
            return AlternateUpsertFileLocation;

        return DefaultUpsertFileLocation;
    }

    internal static Dictionary<string, object?> BuildUpsertFileEntry(GizmoFileRef file)
    {
        var name = string.IsNullOrWhiteSpace(file.Name) ? file.FileId : file.Name;
        return new Dictionary<string, object?>
        {
            ["file_id"] = file.FileId,
            ["name"] = name,
            ["location"] = ResolveUpsertFileLocation(file),
        };
    }

    internal static object BuildProjectFilesAttachBody(GizmoFileRef file) =>
        BuildProjectFilesAttachBody([file]);

    internal static object BuildProjectFilesAttachBody(IReadOnlyList<GizmoFileRef> files) =>
        new Dictionary<string, object?>
        {
            ["files"] = files
                .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
                .Select(f => (object)BuildUpsertFileEntry(f))
                .ToArray(),
        };

    internal static object BuildProjectFilesAttachBodyMinimal(GizmoFileRef file) =>
        BuildProjectFilesAttachBodyMinimal([file]);

    internal static object BuildProjectFilesAttachBodyMinimal(IReadOnlyList<GizmoFileRef> files)
    {
        return new Dictionary<string, object?>
        {
            ["files"] = files
                .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
                .Select(f =>
                {
                    var name = string.IsNullOrWhiteSpace(f.Name) ? f.FileId : f.Name;
                    return (object)new Dictionary<string, object?>
                    {
                        ["file_id"] = f.FileId,
                        ["name"] = name,
                    };
                })
                .ToArray(),
        };
    }

    internal static IReadOnlyList<object> BuildProjectFilesAttachBodyCandidates(GizmoFileRef file) =>
        BuildProjectFilesAttachBodyCandidates([file]);

    internal static IReadOnlyList<object> BuildProjectFilesAttachBodyCandidates(IReadOnlyList<GizmoFileRef> files)
    {
        var candidates = new List<object>();
        foreach (var location in UpsertFileLocationCandidates(files))
        {
            var located = ApplyUpsertFileLocation(files, location);
            candidates.Add(BuildProjectFilesAttachBody(located));
            candidates.Add(BuildProjectFilesAttachBodyMinimal(located));
        }

        return candidates;
    }

    internal static bool ShouldUseSnorlaxFileListFastPath(
        int sidebarCount,
        int detailCount,
        int mergedCount) =>
        mergedCount > 0 && sidebarCount + detailCount > 0;

    internal static bool IsPlanPreflightFresh(
        SourceSyncPlan plan,
        string gizmoId,
        TimeSpan? maxAge = null)
    {
        maxAge ??= TimeSpan.FromMinutes(5);
        return !plan.SyncBlocked
               && plan.PreflightPassedAt.HasValue
               && string.Equals(plan.PreflightGizmoId, gizmoId, StringComparison.Ordinal)
               && DateTimeOffset.UtcNow - plan.PreflightPassedAt.Value < maxAge;
    }

    internal static bool IsPlanCanaryFresh(SourceSyncPlan plan, string gizmoId, TimeSpan? maxAge = null) =>
        plan.CanaryPassed && IsPlanPreflightFresh(plan, gizmoId, maxAge);

    internal static IReadOnlyList<GizmoConversationRef> ParseConversationsForTests(JsonElement json) =>
        ParseConversations(json);

    internal static bool TryGetNextOffsetForTests(
        JsonElement json,
        int batchCount,
        int currentOffset,
        out int nextOffset) =>
        TryGetNextOffset(json, batchCount, currentOffset, out nextOffset);

    internal static string? TryReadConversationIdForTests(JsonElement? json) =>
        TryReadConversationId(json);

    private static IReadOnlyList<GizmoConversationRef> ParseConversations(JsonElement json)
    {
        var list = new List<GizmoConversationRef>();
        JsonElement arr = default;
        if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            arr = items;
        else if (json.ValueKind == JsonValueKind.Array)
            arr = json;

        if (arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            var id = JsonElementParsing.GetStringOrNull(item, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            DateTimeOffset? updated = null;
            var updateSecs = JsonElementParsing.GetInt64OrNull(item, "update_time");
            if (updateSecs is not null)
                updated = DateTimeOffset.FromUnixTimeSeconds(updateSecs.Value);

            list.Add(new GizmoConversationRef
            {
                Id = id,
                Title = JsonElementParsing.GetStringOrNull(item, "title"),
                UpdatedAt = updated,
            });
        }

        return list;
    }

    private static bool TryGetNextOffset(JsonElement json, int batchCount, int currentOffset, out int nextOffset)
    {
        nextOffset = currentOffset + batchCount;
        if (batchCount == 0)
            return false;

        if (json.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.False)
            return false;

        var total = JsonElementParsing.GetInt64OrNull(json, "total");
        if (total is not null && nextOffset >= total.Value)
            return false;

        return batchCount >= 20;
    }

    private static IReadOnlyList<string> OrderDeleteTemplates(string? preferred)
    {
        var all = new[]
        {
            ChatGptApiDiscovery.FileDeleteTemplates.ProjectsFilePath,
            ChatGptApiDiscovery.FileDeleteTemplates.ProjectsFilesBody,
            ChatGptApiDiscovery.FileDeleteTemplates.GizmoFilePath,
            ChatGptApiDiscovery.FileDeleteTemplates.GizmoFilesBody,
            ChatGptApiDiscovery.FileDeleteTemplates.FilePath,
        };

        if (string.IsNullOrWhiteSpace(preferred))
            return all;

        return all.OrderByDescending(t => string.Equals(t, preferred, StringComparison.Ordinal)).ToList();
    }

    private async Task<bool> TryDeleteWithTemplateAsync(
        CoreWebView2 core,
        string gizmoId,
        string fileId,
        string template,
        CancellationToken cancellationToken)
    {
        ApiBridgeMessage msg;
        string endpoint;

        switch (template)
        {
            case ChatGptApiDiscovery.FileDeleteTemplates.ProjectsFilePath:
                endpoint = ChatGptApiEndpoints.ProjectFilesFileDelete(gizmoId, fileId);
                msg = await _bridge.SendAsync(
                    core,
                    new { action = "apiRequest", method = "DELETE", path = endpoint },
                    cancellationToken: cancellationToken);
                break;

            case ChatGptApiDiscovery.FileDeleteTemplates.ProjectsFilesBody:
                endpoint = ChatGptApiEndpoints.ProjectFilesCollectionDelete(gizmoId);
                msg = await _bridge.SendAsync(
                    core,
                    new
                    {
                        action = "apiRequest",
                        method = "DELETE",
                        path = endpoint,
                        body = new Dictionary<string, object?> { ["file_id"] = fileId },
                    },
                    cancellationToken: cancellationToken);
                break;

            case ChatGptApiDiscovery.FileDeleteTemplates.GizmoFilePath:
                endpoint = ChatGptApiEndpoints.ProjectFileDelete(gizmoId, fileId);
                msg = await _bridge.SendAsync(
                    core,
                    new { action = "apiRequest", method = "DELETE", path = endpoint },
                    cancellationToken: cancellationToken);
                break;

            case ChatGptApiDiscovery.FileDeleteTemplates.GizmoFilesBody:
                endpoint = ChatGptApiEndpoints.ProjectFilesDelete(gizmoId);
                msg = await _bridge.SendAsync(
                    core,
                    new
                    {
                        action = "apiRequest",
                        method = "DELETE",
                        path = endpoint,
                        body = new Dictionary<string, object?> { ["file_id"] = fileId },
                    },
                    cancellationToken: cancellationToken);
                break;

            default:
                endpoint = ChatGptApiEndpoints.FileDelete(fileId);
                msg = await _bridge.SendAsync(
                    core,
                    new { action = "apiRequest", method = "DELETE", path = endpoint },
                    cancellationToken: cancellationToken);
                break;
        }

        if (msg.Ok && (msg.Status is null or >= 200 and < 300))
        {
            ChatGptApiDiscovery.RecordSuccess(endpoint, "DELETE");
            return true;
        }

        ChatGptApiDiscovery.RecordFailure(endpoint, "DELETE", msg.Status);
        return false;
    }

    internal static ApiProbeResult ParseProbeResult(ApiBridgeMessage msg)
    {
        var keys = new List<string>();
        int? itemCount = null;

        if (msg.Json is { } json)
        {
            foreach (var prop in json.EnumerateObject())
                keys.Add(prop.Name);

            if (json.TryGetProperty("itemCount", out var ic) && ic.TryGetInt32(out var n))
                itemCount = n;
        }

        return new ApiProbeResult
        {
            Ok = msg.Ok,
            Status = msg.Status,
            ItemCount = itemCount,
            JsonKeys = keys,
            HasDeviceId = msg.Json?.TryGetProperty("hasDeviceId", out var hd) == true
                        && hd.ValueKind == JsonValueKind.True,
            HasAccountId = msg.Json?.TryGetProperty("hasAccountId", out var ha) == true
                         && ha.ValueKind == JsonValueKind.True,
            Authenticated = msg.Json?.TryGetProperty("authenticated", out var au) == true
                            && au.ValueKind == JsonValueKind.True,
            Error = msg.Error ?? msg.Message,
        };
    }

    private static void EnsureOk(ApiBridgeMessage msg, string endpoint)
    {
        if (msg.Ok && (msg.Status is null or >= 200 and < 300))
            return;

        var detail = msg.Message;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = string.Equals(msg.Error, "missing_oai_device_id", StringComparison.Ordinal)
                ? "Sign in to ChatGPT in the Adventure tab and refresh the page, then try again."
                : msg.Error ?? $"API call failed ({msg.Status})";
        }

        if (!string.IsNullOrWhiteSpace(msg.BodyText))
            detail = $"{detail} — {TruncateApiBody(msg.BodyText)}";

        detail = $"{detail} ({endpoint})";

        throw new ChatGptApiException(detail, endpoint, msg.Status, msg.BodyText);
    }

    private static string TruncateApiBody(string body)
    {
        var s = body.Trim();
        return s.Length <= 240 ? s : s[..240] + "…";
    }

    private static string FormatBridgeError(ApiBridgeMessage msg, string fallback)
    {
        var detail = msg.Message ?? msg.Error ?? fallback;
        if (!string.IsNullOrWhiteSpace(msg.BodyText))
            detail = $"{detail} — {TruncateApiBody(msg.BodyText)}";
        if (msg.Status is { } status)
            detail = $"{detail} (HTTP {status})";
        return detail;
    }
}
