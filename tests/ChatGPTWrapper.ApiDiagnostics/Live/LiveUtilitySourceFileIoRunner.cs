using System.Diagnostics;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ApiDiagnostics.Reporting;
using ChatGPTWrapper.ApiDiagnostics.Unit;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

/// <summary>
/// CMD-442: utility source-reference file I/O — publish to Project sources, verify bytes, parse delimited output.
/// </summary>
public sealed class LiveUtilitySourceFileIoRunner
{
    public const string GizmoIdEnvVar = "CGW_UTILITY_SOURCE_IO_GIZMO_ID";
    public const string UploadMethodEnvVar = "CGW_UTILITY_SOURCE_IO_UPLOAD_METHOD";
    public const string E2eEnvVar = "CGW_UTILITY_SOURCE_IO_E2E";
    public const string ConversationIdEnvVar = "CGW_UTILITY_SOURCE_IO_CONVERSATION_ID";

    private readonly WebView2DiagnosticHost _host;

    public LiveUtilitySourceFileIoRunner(WebView2DiagnosticHost host) => _host = host;

    public async Task<UtilitySourceFileIoReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = new UtilitySourceFileIoReport();
        try
        {
            await _host.RunOnUiAsync(
                () => RunOnUiAsync(report, cancellationToken),
                cancellationToken);
        }
        finally
        {
            report.WriteToDisk();
        }

        return report;
    }

    private async Task RunOnUiAsync(UtilitySourceFileIoReport report, CancellationToken cancellationToken)
    {
        var core = _host.Core
                   ?? throw new InvalidOperationException("WebView2 core is not initialized.");
        var bridge = _host.Bridge
                     ?? throw new InvalidOperationException("API bridge is not initialized.");

        await RunStep(report, "navigate_chatgpt", async () =>
        {
            await NavigateChatGptAsync(core, cancellationToken);
            return core.Source;
        });

        await bridge.InjectAsync(core);
        await bridge.WaitForBridgeReadyAsync(core, 60_000, cancellationToken);
        var api = new ChatGptProjectApiService(bridge);

        var gizmoId = ResolveGizmoId();
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            var projects = await api.ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
            gizmoId = projects.FirstOrDefault(p => ChatGptProjectApiService.IsSnorlaxProjectId(p.Id))?.Id
                      ?? projects.FirstOrDefault()?.Id;
        }

        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            report.AddStep(new UtilitySourceFileIoStep
            {
                Id = "resolve_gizmo",
                DurationMs = 0,
                Pass = false,
                Error = $"No project id. Set {GizmoIdEnvVar} or sign in and create a ChatGPT project.",
            });
            return;
        }

        report.GizmoId = gizmoId;

        await RunStep(report, "ensure_project_page", async () =>
        {
            await api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
            return core.Source;
        });

        var runToken = Guid.NewGuid().ToString("N")[..12];
        report.RunToken = runToken;
        var remotePath = UtilitySourceFileIoService.BuildDiagnosticRemotePath(runToken);
        report.RemoteSourcesPath = remotePath;
        var payload = UtilitySourceFileIoService.BuildDiagnosticPayload(runToken);
        var uploadMethod = ResolveUploadMethod();

        UtilitySourcePublishResult? publishResult = null;
        await RunStep(report, "publish_source_file", async () =>
        {
            publishResult = await UtilitySourceFileIoService.PublishBytesToProjectAsync(
                api,
                core,
                gizmoId,
                remotePath,
                payload,
                mimeType: "text/markdown",
                bundle: null,
                uploadMethodOverride: uploadMethod,
                cancellationToken: cancellationToken);

            if (!publishResult.Success)
                throw new InvalidOperationException(publishResult.Error ?? "publish_failed");

            report.VerifiedByteCount = publishResult.VerifiedByteCount;
            return $"file_id={publishResult.File?.FileId} bytes={publishResult.VerifiedByteCount} method={uploadMethod?.ToString() ?? "default"}";
        });

        await RunStep(report, "build_source_pointer", () =>
        {
            var pointer = UtilitySourceFileIoService.BuildSourceRetrieveLine(remotePath);
            var taskPointer = UtilitySourceFileIoService.BuildTaskScopedPointerLine(
                remotePath,
                "CGW utility source I/O diagnostic input");
            if (string.IsNullOrWhiteSpace(pointer) || !pointer.Contains(remotePath, StringComparison.Ordinal))
                throw new InvalidOperationException("pointer_build_failed");

            return $"retrieve_len={pointer.Length} task_len={taskPointer.Length}";
        });

        await RunStep(report, "parse_delimited_output_fixture", () =>
        {
            const string fixture = """
                Applied edits.

                --- begin cgw-utility-source-io-out.md ---
                # Revised diagnostic
                token: fixture
                --- end cgw-utility-source-io-out.md ---
                """;

            var extracted = UtilitySourceFileIoService.TryExtractDelimitedBlock(
                fixture,
                "cgw-utility-source-io-out.md");
            if (string.IsNullOrWhiteSpace(extracted) || !extracted.Contains("fixture", StringComparison.Ordinal))
                throw new InvalidOperationException("fixture_parse_failed");

            return $"extracted_len={extracted.Length}";
        });

        await RunStep(report, "delivery_block_shape", () =>
        {
            var block = UtilitySourceFileIoService.BuildDelimitedOutputDeliveryBlock(
                "cgw-utility-source-io-out.md",
                remotePath);
            if (!block.Contains("--- begin cgw-utility-source-io-out.md ---", StringComparison.Ordinal))
                throw new InvalidOperationException("delivery_block_missing_delimiters");

            return $"block_len={block.Length}";
        });

        if (IsE2eEnabled())
            await RunE2eLoopAsync(report, core, bridge, api, gizmoId, runToken, remotePath, cancellationToken);
    }

    private async Task RunE2eLoopAsync(
        UtilitySourceFileIoReport report,
        CoreWebView2 core,
        ChatGptApiBridgeInjection bridge,
        ChatGptProjectApiService api,
        string gizmoId,
        string runToken,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var webView = _host.WebView
                      ?? throw new InvalidOperationException("WebView not initialized for E2E.");

        AdventureTurnService? turnService = null;
        await RunStep(report, "e2e_setup_adventure_bridge", async () =>
        {
            var adventureBridge = new ChatGptAdventureBridgeInjection(webView);
            adventureBridge.Register();
            await adventureBridge.InjectAsync(core);
            turnService = new AdventureTurnService(adventureBridge);
            var ready = await turnService.EnsureUtilityBridgeReadyAsync(core, cancellationToken);
            if (!ready)
                throw new InvalidOperationException("adventure_bridge_not_ready");

            return "bridge_ready=true";
        });

        var existingConversationId = Environment.GetEnvironmentVariable(ConversationIdEnvVar)?.Trim();
        var composerReady = false;
        if (!string.IsNullOrWhiteSpace(existingConversationId))
        {
            await RunStep(report, "e2e_conversation_ready", async () =>
            {
                await api.EnsureProjectConversationPageAsync(
                    core,
                    existingConversationId,
                    gizmoId,
                    cancellationToken);
                return $"conversation_id={existingConversationId}";
            });
        }
        else
        {
            await RunStep(report, "e2e_project_home", async () =>
            {
                await api.EnsureCanonicalProjectHomeAsync(core, gizmoId, cancellationToken);
                var href = await UtilityConversationPageService.GetPageHrefAsync(core);
                if (!EphemeralProjectChatService.CanSendFromProjectHome(href, gizmoId))
                    throw new InvalidOperationException($"not_on_project_home: {href}");

                return href ?? core.Source;
            });

            composerReady = await EnsureProjectComposerReadyAsync(turnService!, core, gizmoId, cancellationToken);
            await RunStep(report, "e2e_composer_ready", () =>
                composerReady
                    ? "composer_ready=true"
                    : throw new InvalidOperationException("project_composer_not_ready"));
        }

        var runId = Guid.NewGuid();
        var packet = UtilitySourceFileIoService.BuildE2eJobPacket(gizmoId, remotePath, runToken, runId);
        var conversationSend = new ChatGptConversationSendService(bridge);
        var ephemeral = new EphemeralProjectChatService(api, conversationSend);

        EphemeralProjectChatResult? chatResult = null;
        var ephemeralCreate = string.IsNullOrWhiteSpace(existingConversationId);
        await RunStep(report, "e2e_send_pointer_job", async () =>
        {
            if (!string.IsNullOrWhiteSpace(existingConversationId))
            {
                var send = await conversationSend.SendUserMessageAsync(
                    core,
                    existingConversationId,
                    gizmoId,
                    packet,
                    cancellationToken);
                if (!send.Success)
                    throw new InvalidOperationException(send.Error ?? "send_failed");

                chatResult = new EphemeralProjectChatResult
                {
                    Success = true,
                    ConversationId = send.ConversationId ?? existingConversationId,
                    ResponseText = send.AssistantText,
                    StreamComplete = send.StreamComplete,
                };
                return $"mode=existing_conv id={chatResult.ConversationId} assistant_len={chatResult.ResponseText?.Length ?? 0}";
            }

            chatResult = await ephemeral.RunOnceAsync(
                new EphemeralProjectChatRequest
                {
                    Core = core,
                    GizmoId = gizmoId,
                    MessageText = packet,
                    TurnService = turnService,
                    ComposerAlreadyOpen = composerReady,
                    TryUiCreate = composerReady
                        ? null
                        : (c, ct) => TryUiOpenProjectChatAsync(turnService!, c, ct),
                    WarmSession = true,
                    DeleteAfterCapture = true,
                    DeleteInBackground = false,
                    CaptureMaxAttempts = 8,
                    CapturePollDelay = TimeSpan.FromSeconds(2),
                    MaxComposerWaitSeconds = 12,
                },
                cancellationToken);

            if (!chatResult.Success)
                throw new InvalidOperationException(
                    $"{chatResult.FailedPhase}:{chatResult.Error ?? "ephemeral_chat_failed"}");

            return $"mode=ephemeral_create id={chatResult.ConversationId} assistant_len={chatResult.ResponseText?.Length ?? 0} deleted={chatResult.Deleted}";
        });

        report.ConversationId = chatResult?.ConversationId;

        string? extracted = null;
        await RunStep(report, "e2e_extract_delimited_output", () =>
        {
            var responseText = chatResult?.ResponseText;
            if (string.IsNullOrWhiteSpace(responseText))
                throw new InvalidOperationException("empty_assistant_response");

            extracted = UtilitySourceFileIoService.TryExtractE2eOutput(responseText, runToken);
            if (string.IsNullOrWhiteSpace(extracted))
            {
                report.E2eClassification = "extract-miss";
                throw new InvalidOperationException("delimited_output_missing");
            }

            report.ExtractedOutputLength = extracted.Length;
            if (!UtilitySourceFileIoService.E2eOutputContainsToken(extracted, runToken))
            {
                report.E2eClassification = "hash-mismatch";
                throw new InvalidOperationException("e2e_confirm_line_missing");
            }

            report.E2eClassification = "pass";
            return $"extracted_len={extracted.Length} token_ok=true";
        });

        if (ephemeralCreate)
        {
            await RunStep(report, "e2e_delete_ephemeral_thread", () =>
            {
                if (chatResult is not { Deleted: true })
                {
                    report.EphemeralThreadDeleted = false;
                    throw new InvalidOperationException(chatResult?.DeleteError ?? "ephemeral_thread_not_deleted");
                }

                report.EphemeralThreadDeleted = true;
                return $"conversation_id={chatResult.ConversationId}";
            });
        }
    }

    private static async Task<bool> EnsureProjectComposerReadyAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        for (var warmup = 0; warmup < 40; warmup++)
        {
            var href = await UtilityConversationPageService.GetPageHrefAsync(core);
            var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
            if (health.ComposerFound
                && EphemeralProjectChatService.CanSendFromProjectHome(href, gizmoId))
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
        if (ui.ComposerReady || ui.Success)
        {
            var href = await UtilityConversationPageService.GetPageHrefAsync(core);
            return EphemeralProjectChatService.CanSendFromProjectHome(href, gizmoId);
        }

        var finalHealth = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
        var finalHref = await UtilityConversationPageService.GetPageHrefAsync(core);
        return finalHealth.ComposerFound
               && EphemeralProjectChatService.CanSendFromProjectHome(finalHref, gizmoId);
    }

    private static async Task<string?> TryUiOpenProjectChatAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
        var conversationId = ui.ConversationId ?? await turnService.GetConversationIdAsync(core);
        if (!string.IsNullOrWhiteSpace(conversationId))
            return conversationId;

        var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
        return health.ComposerFound ? string.Empty : null;
    }

    private static bool IsE2eEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable(E2eEnvVar), "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable(E2eEnvVar), "true", StringComparison.OrdinalIgnoreCase);

    private static async Task RunStep(
        UtilitySourceFileIoReport report,
        string stepId,
        Func<Task<string>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = await action();
            report.AddStep(new UtilitySourceFileIoStep
            {
                Id = stepId,
                DurationMs = sw.ElapsedMilliseconds,
                Pass = true,
                Detail = detail,
            });
            report.WriteToDisk();
        }
        catch (Exception ex)
        {
            report.AddStep(new UtilitySourceFileIoStep
            {
                Id = stepId,
                DurationMs = sw.ElapsedMilliseconds,
                Pass = false,
                Error = ex.Message,
            });
            report.WriteToDisk();
            throw;
        }
    }

    private static Task RunStep(
        UtilitySourceFileIoReport report,
        string stepId,
        Func<string> action) =>
        RunStep(report, stepId, () => Task.FromResult(action()));

    private static async Task NavigateChatGptAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        if (core.Source.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase))
            return;

        core.Navigate("https://chatgpt.com/");
        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (core.Source.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException("chatgpt_navigation_timeout");
    }

    private static string? ResolveGizmoId() =>
        Environment.GetEnvironmentVariable(GizmoIdEnvVar)
        ?? Environment.GetEnvironmentVariable(LiveProjectSourceDownloadRunner.GizmoIdEnvVar);

    private static ProjectSourceUploadMethod? ResolveUploadMethod()
    {
        var raw = Environment.GetEnvironmentVariable(UploadMethodEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return ProjectSourceUploadMethod.PureApi;

        return Enum.TryParse<ProjectSourceUploadMethod>(raw, ignoreCase: true, out var method)
            ? method
            : ProjectSourceUploadMethod.PureApi;
    }
}
