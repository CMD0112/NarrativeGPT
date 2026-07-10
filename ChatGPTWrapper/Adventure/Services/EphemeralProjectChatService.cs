using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// One-shot linked-project chat: create → send → capture → delete.
/// Does not bind conversation ids to adventure metadata or utility sessions.
/// </summary>
public sealed class EphemeralProjectChatService
{
    private readonly ChatGptProjectApiService _projectApi;
    private readonly ChatGptConversationSendService _conversationSend;

    public EphemeralProjectChatService(
        ChatGptProjectApiService projectApi,
        ChatGptConversationSendService conversationSend)
    {
        _projectApi = projectApi;
        _conversationSend = conversationSend;
    }

    public async Task<EphemeralProvisionResult> ProvisionComposerAsync(
        EphemeralProjectChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Core is null)
            return ProvisionFail(EphemeralProjectChatPhase.Create, "missing_core");

        if (string.IsNullOrWhiteSpace(request.GizmoId))
            return ProvisionFail(EphemeralProjectChatPhase.Create, "missing_gizmo_id");

        var created = await CreateConversationAsync(request, cancellationToken);
        if (!created.Success)
            return created.ProvisionResult!;

        return created.ProvisionResult!;
    }

    public async Task<EphemeralProjectChatResult> RunOnceAsync(
        EphemeralProjectChatRequest request,
        CancellationToken cancellationToken = default) =>
        await RunOnceAsync(request, utilityOptions: null, cancellationToken);

    internal async Task<EphemeralProjectChatResult> RunOnceAsync(
        EphemeralProjectChatRequest request,
        EphemeralUtilityRunOptions? utilityOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Core is null)
            return Fail(EphemeralProjectChatPhase.Create, "missing_core");

        if (string.IsNullOrWhiteSpace(request.GizmoId))
            return Fail(EphemeralProjectChatPhase.Create, "missing_gizmo_id");

        if (string.IsNullOrWhiteSpace(request.MessageText))
            return Fail(EphemeralProjectChatPhase.Send, "missing_message_text");

        var gizmoId = ChatGptUrls.NormalizeGizmoId(request.GizmoId);
        var core = request.Core;

        var provision = await CreateConversationAsync(request, cancellationToken);
        if (!provision.Success)
        {
            var fail = provision.ProvisionResult!;
            return new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = fail.FailedPhase,
                Error = fail.Error,
                ConversationId = fail.ConversationId,
                DomComposerReady = fail.DomComposerReady,
            };
        }

        var created = provision.Created!;
        var conversationId = created.ConversationId?.Trim() ?? "";

        if (!ShouldUseDomSend(created) && !string.IsNullOrWhiteSpace(conversationId))
            await EnsureSendReadyAsync(core, conversationId, gizmoId, cancellationToken);

        var sendResult = await SendEphemeralMessageAsync(
            request,
            utilityOptions,
            core,
            conversationId,
            gizmoId,
            created,
            cancellationToken);

        if (!sendResult.Success)
        {
            var cleanup = await TryDeleteAsync(
                core,
                sendResult.ConversationId ?? conversationId,
                request.DeleteAfterCapture,
                cancellationToken);
            return new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = EphemeralProjectChatPhase.Send,
                Error = sendResult.Error ?? "send_failed",
                ConversationId = sendResult.ConversationId ?? conversationId,
                Deleted = cleanup.Deleted,
                DeleteError = cleanup.DeleteError,
            };
        }

        var effectiveConversationId = sendResult.ConversationId ?? conversationId;
        var parentMessageId = sendResult.ParentMessageId;
        if (string.IsNullOrWhiteSpace(parentMessageId))
        {
            ConversationParentCache.Invalidate(effectiveConversationId);
            parentMessageId = await _conversationSend.PrefetchParentAsync(
                core,
                effectiveConversationId,
                cancellationToken);
        }

        var responseText = sendResult.AssistantText;
        var streamComplete = sendResult.StreamComplete;
        string? captureError = null;

        if (!IsSettledResponse(responseText, streamComplete))
        {
            var captured = await CaptureWithRetriesAsync(
                core,
                effectiveConversationId,
                parentMessageId,
                request,
                cancellationToken);
            responseText = captured.Text;
            captureError = captured.Error;
            streamComplete = captured.StreamComplete;
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            var cleanup = await TryDeleteAsync(core, effectiveConversationId, request.DeleteAfterCapture, cancellationToken);
            return new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = EphemeralProjectChatPhase.Capture,
                Error = captureError ?? sendResult.Error ?? "capture_no_assistant",
                ConversationId = effectiveConversationId,
                Deleted = cleanup.Deleted,
                DeleteError = cleanup.DeleteError,
            };
        }

        var deleteOutcome = await CompleteDeleteAsync(
            core,
            effectiveConversationId,
            request,
            cancellationToken);

        return new EphemeralProjectChatResult
        {
            Success = true,
            ResponseText = responseText,
            ConversationId = effectiveConversationId,
            StreamComplete = streamComplete,
            Deleted = deleteOutcome.Deleted,
            DeleteError = deleteOutcome.DeleteError,
        };
    }

    private sealed record CreateConversationOutcome(
        bool Success,
        CreateProjectConversationResult? Created,
        EphemeralProvisionResult? ProvisionResult);

    private async Task<CreateConversationOutcome> CreateConversationAsync(
        EphemeralProjectChatRequest request,
        CancellationToken cancellationToken)
    {
        var gizmoId = ChatGptUrls.NormalizeGizmoId(request.GizmoId);
        var core = request.Core!;

        if (!request.WarmSession && !request.ComposerAlreadyOpen)
            await _projectApi.PrepareForApiAsync(core, cancellationToken);

        CreateProjectConversationResult created;
        if (request.ComposerAlreadyOpen)
        {
            if (request.TurnService is null)
            {
                return FailOutcome(
                    EphemeralProjectChatPhase.Create,
                    "composer_already_open_requires_turn_service");
            }

            var fromUrl = await request.TurnService.GetConversationIdAsync(core);
            ProjectLinkDiagnostics.Log("Ephemeral chat using caller-opened project composer");
            created = new CreateProjectConversationResult
            {
                ConversationId = fromUrl,
                DomComposerReady = true,
            };
        }
        else
        {
            await _projectApi.EnsureProjectPageAsync(core, gizmoId, cancellationToken);

            var createOptions = BuildCreateOptions(request);
            created = await _projectApi.CreateProjectConversationDetailedAsync(
                core,
                gizmoId,
                createOptions,
                cancellationToken);

            if (!IsAcceptableCreateResult(created))
            {
                if (request.TurnService is not null
                    && await IsComposerReadyOnProjectHomeAsync(request.TurnService, core, gizmoId, cancellationToken))
                {
                    var fromUrl = await request.TurnService.GetConversationIdAsync(core);
                    ProjectLinkDiagnostics.Log(
                        "Ephemeral composer ready on project home; using DOM send");
                    created = new CreateProjectConversationResult
                    {
                        ConversationId = fromUrl,
                        DomComposerReady = true,
                    };
                }
                else if (request.TryUiCreate is not null)
                {
                    var fromUi = await request.TryUiCreate(core, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(fromUi))
                    {
                        created = new CreateProjectConversationResult { ConversationId = fromUi };
                    }
                    else
                    {
                        ProjectLinkDiagnostics.Log(
                            $"Ephemeral UI create unavailable ({created.Error ?? "unknown"}); registering client conversation");
                        created = await _projectApi.RegisterClientProjectConversationAsync(
                            core,
                            gizmoId,
                            cancellationToken: cancellationToken);
                    }
                }
            }
        }

        if (!IsAcceptableCreateResult(created))
        {
            return FailOutcome(
                EphemeralProjectChatPhase.Create,
                created.Error ?? "create_failed",
                created.ConversationId,
                created.DomComposerReady);
        }

        var conversationId = created.ConversationId?.Trim() ?? "";
        var nav = await EnsureEphemeralSendPageAsync(
            core,
            conversationId,
            gizmoId,
            created,
            cancellationToken);
        if (!nav.Success)
        {
            return FailOutcome(
                EphemeralProjectChatPhase.Navigate,
                nav.Error ?? "navigation_failed",
                string.IsNullOrWhiteSpace(conversationId) ? null : conversationId,
                created.DomComposerReady);
        }

        return new CreateConversationOutcome(
            true,
            created,
            new EphemeralProvisionResult
            {
                Success = true,
                ConversationId = string.IsNullOrWhiteSpace(conversationId) ? null : conversationId,
                DomComposerReady = created.DomComposerReady,
            });
    }

    private static CreateConversationOutcome FailOutcome(
        EphemeralProjectChatPhase phase,
        string error,
        string? conversationId = null,
        bool domComposerReady = false) =>
        new(
            false,
            null,
            new EphemeralProvisionResult
            {
                Success = false,
                FailedPhase = phase,
                Error = error,
                ConversationId = conversationId,
                DomComposerReady = domComposerReady,
            });

    private static EphemeralProvisionResult ProvisionFail(EphemeralProjectChatPhase phase, string error) =>
        new()
        {
            Success = false,
            FailedPhase = phase,
            Error = error,
        };

    internal static bool IsAcceptableCreateResult(CreateProjectConversationResult? result) =>
        result is not null
        && (result.DomComposerReady
            || (!string.IsNullOrWhiteSpace(result.ConversationId)
                && (!result.ClientBootstrapped || result.InitRegistered)));

    internal static bool ShouldUseDomSend(CreateProjectConversationResult created) =>
        created.DomComposerReady || created.InitRegistered;

    internal static int ComputeEphemeralDomTimeoutMs(int messageLength, int? overrideMs) =>
        overrideMs ?? (messageLength <= 500 ? 90_000 : AdventureTurnService.ComputeUtilityJobTimeoutMs(messageLength));

    internal static bool IsDomFallbackSendError(string? error) =>
        string.Equals(error, "http_403", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "missing_conduit_token", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "missing_parent_message_id", StringComparison.OrdinalIgnoreCase);

    internal static bool CanSendFromProjectHome(string? href, string gizmoId)
    {
        if (!UtilityConversationPageService.IsProjectHomePage(href))
            return false;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        return !string.IsNullOrWhiteSpace(href)
               && href.Contains(gizmoId, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldSkipConversationNavigation(CreateProjectConversationResult created) =>
        created.InitRegistered || created.DomComposerReady;

    private static async Task<bool> IsComposerReadyOnProjectHomeAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        if (!await turnService.EnsureUtilityBridgeReadyAsync(core, cancellationToken))
            return false;

        var href = await UtilityConversationPageService.GetPageHrefAsync(core);
        if (!CanSendFromProjectHome(href, gizmoId))
            return false;

        var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
        return health.ComposerFound;
    }

    private async Task<ConversationSendResult> SendEphemeralMessageAsync(
        EphemeralProjectChatRequest request,
        EphemeralUtilityRunOptions? utilityOptions,
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CreateProjectConversationResult created,
        CancellationToken cancellationToken)
    {
        if (utilityOptions?.DomAttachments is { Count: > 0 })
        {
            return await SendEphemeralAttachmentsAsync(
                request,
                utilityOptions,
                core,
                conversationId,
                gizmoId,
                created,
                cancellationToken);
        }

        if (request.TurnService is not null && ShouldUseDomSend(created))
        {
            ProjectLinkDiagnostics.Log("Ephemeral chat using DOM send from project home composer");
            var domTimeout = ComputeEphemeralDomTimeoutMs(request.MessageText.Length, request.SendTimeoutMs);
            var composerWait = request.ComposerAlreadyOpen
                ? request.MaxComposerWaitSeconds ?? 8
                : request.MaxComposerWaitSeconds;
            return NormalizeEphemeralDomSendResult(
                await request.TurnService.SubmitUtilityJobAsync(
                    core,
                    conversationId,
                    gizmoId,
                    request.MessageText,
                    timeoutMs: domTimeout,
                    skipPageEnsure: true,
                    maxComposerWaitSeconds: composerWait,
                    cancellationToken: cancellationToken));
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "missing_conversation_id",
            };
        }

        var apiResult = await _conversationSend.SendUserMessageAsync(
            core,
            conversationId,
            gizmoId,
            request.MessageText,
            cancellationToken);

        if (apiResult.Success || request.TurnService is null || !IsDomFallbackSendError(apiResult.Error))
            return apiResult;

        ProjectLinkDiagnostics.Log(
            $"Ephemeral API send failed ({apiResult.Error}); falling back to DOM send");
        var fallbackTimeout = ComputeEphemeralDomTimeoutMs(request.MessageText.Length, request.SendTimeoutMs);
        return NormalizeEphemeralDomSendResult(
            await request.TurnService.SubmitUtilityJobAsync(
                core,
                conversationId,
                gizmoId,
                request.MessageText,
                timeoutMs: fallbackTimeout,
                skipPageEnsure: true,
                maxComposerWaitSeconds: request.MaxComposerWaitSeconds,
                cancellationToken: cancellationToken));
    }

    private async Task<ConversationSendResult> SendEphemeralAttachmentsAsync(
        EphemeralProjectChatRequest request,
        EphemeralUtilityRunOptions utilityOptions,
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CreateProjectConversationResult created,
        CancellationToken cancellationToken)
    {
        if (request.TurnService is null)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "turn_service_required_for_attachments",
                ConversationId = conversationId,
            };
        }

        if (utilityOptions.WorkerHost is null)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "worker_host_required_for_dom_attach",
                ConversationId = conversationId,
            };
        }

        var jobId = utilityOptions.JobId ?? "ephemeral_attach";
        var probe = await EphemeralDomAttachSupport.ProbeAsync(request.TurnService, core, cancellationToken);
        EphemeralDomAttachSupport.LogAttachProbe(
            "attach_start",
            probe,
            utilityOptions.DomAttachments!.Count);

        var attachTarget = await EphemeralDomAttachSupport.ResolveAttachTargetAsync(
            core,
            gizmoId,
            conversationId,
            created,
            request.TurnService,
            cancellationToken);

        if (attachTarget is null)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = probe.OnProjectHome && !probe.SubmitFound
                    ? "project_home_submit_unavailable"
                    : "ephemeral_attach_provision_failed",
                ConversationId = conversationId,
            };
        }

        conversationId = attachTarget.ConversationId;
        created = attachTarget.Created;
        var skipPageEnsure = attachTarget.SkipPageEnsure;
        ProjectLinkDiagnostics.Log(
            skipPageEnsure
                ? "Ephemeral chat using DOM attach from project home composer"
                : "Ephemeral chat using DOM attach on conversation page");

        var push = await UtilityWorkerTransportService.SendEphemeralPacketWithAttachmentsAsync(
            core,
            conversationId,
            gizmoId,
            request.MessageText,
            jobId,
            utilityOptions.DomAttachments!,
            request.TurnService,
            utilityOptions.WorkerHost,
            skipPageEnsure,
            allowKeyboardSubmitOnProjectHome: skipPageEnsure,
            cancellationToken: cancellationToken);

        if (push.Success)
            return push;

        var effectiveConversationId = EphemeralDomAttachSupport.ResolveFallbackConversationId(
            push.ConversationId,
            conversationId,
            utilityOptions.FallbackConversationId);

        if (!string.IsNullOrWhiteSpace(effectiveConversationId))
        {
            ProjectLinkDiagnostics.Log("ephemeral_attach_fallback_attach_worker");
            var workerPush = await UtilityAttachWorkerService.TryDomAttachAsync(
                core,
                effectiveConversationId,
                gizmoId,
                request.MessageText,
                utilityOptions.DomAttachments!,
                cancellationToken);

            if (workerPush.Success)
                return workerPush;

            if (utilityOptions.WorkerHost is not null
                && utilityOptions.Bundle is not null
                && !string.IsNullOrWhiteSpace(utilityOptions.FallbackConversationId)
                && !string.Equals(
                    effectiveConversationId,
                    utilityOptions.FallbackConversationId,
                    StringComparison.OrdinalIgnoreCase))
            {
                ProjectLinkDiagnostics.Log("ephemeral_attach_fallback_pinned_dom");
                var pinnedDom = await UtilityWorkerTransportService.SendProductionPacketWithAttachmentsAsync(
                    core,
                    utilityOptions.Bundle,
                    utilityOptions.FallbackConversationId,
                    gizmoId,
                    request.MessageText,
                    jobId,
                    utilityOptions.DomAttachments!,
                    request.TurnService,
                    utilityOptions.WorkerHost,
                    cancellationToken);
                if (pinnedDom.Success)
                    return pinnedDom;
            }

            return workerPush;
        }

        return push;
    }

    internal static ConversationSendResult NormalizeEphemeralDomSendResult(ConversationSendResult result)
    {
        if (result.Success || string.IsNullOrWhiteSpace(result.AssistantText))
            return result;

        if (!IsEphemeralDomRecoverableError(result.Error))
            return result;

        return new ConversationSendResult
        {
            Success = true,
            ConversationId = result.ConversationId,
            ParentMessageId = result.ParentMessageId,
            AssistantMessageId = result.AssistantMessageId,
            AssistantText = result.AssistantText,
            StreamComplete = result.StreamComplete,
        };
    }

    internal static bool IsEphemeralDomRecoverableError(string? error) =>
        string.Equals(error, "conversation_mismatch", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "capture_premature", StringComparison.OrdinalIgnoreCase);

    private async Task<UtilityConversationPageResult> EnsureEphemeralSendPageAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CreateProjectConversationResult created,
        CancellationToken cancellationToken)
    {
        if (ShouldSkipConversationNavigation(created))
        {
            var pageHref = await UtilityConversationPageService.GetPageHrefAsync(core);
            if (!CanSendFromProjectHome(pageHref, gizmoId))
                await _projectApi.EnsureProjectPageAsync(core, gizmoId, cancellationToken);

            ProjectLinkDiagnostics.Log(
                created.DomComposerReady
                    ? "Ephemeral chat sending from project home (DOM composer ready)"
                    : $"Ephemeral chat sending from project home (init-registered {conversationId})");
            return new UtilityConversationPageResult { Success = true };
        }

        var nav = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
            core,
            conversationId,
            gizmoId,
            cancellationToken);
        if (nav.Success)
            return nav;

        var href = await UtilityConversationPageService.GetPageHrefAsync(core);
        if (CanSendFromProjectHome(href, gizmoId))
        {
            ProjectLinkDiagnostics.Log(
                $"Ephemeral chat continuing from project home after navigate miss ({nav.Error})");
            return new UtilityConversationPageResult { Success = true };
        }

        return nav;
    }

    internal static bool IsSettledResponse(string? text, bool streamComplete) =>
        !string.IsNullOrWhiteSpace(text) && streamComplete;

    private static ProjectConversationCreateOptions BuildCreateOptions(EphemeralProjectChatRequest request)
    {
        Func<CoreWebView2, CancellationToken, Task<string?>>? tryUi = null;
        if (request.TryUiCreate is { } uiCreate)
        {
            tryUi = async (core, ct) => await uiCreate(core, ct);
        }

        return new ProjectConversationCreateOptions
        {
            SkipClientBootstrap = true,
            SkipLegacyApiCreate = request.UiCreateOnly,
            TryUiCreate = tryUi,
            UiCreateOnly = request.UiCreateOnly,
        };
    }

    private async Task<(bool Deleted, string? DeleteError)> CompleteDeleteAsync(
        CoreWebView2 core,
        string conversationId,
        EphemeralProjectChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.DeleteAfterCapture)
            return (false, null);

        if (request.DeleteInBackground)
        {
            _ = TryDeleteAsync(core, conversationId, deleteAfterCapture: true, CancellationToken.None);
            return (false, null);
        }

        return await TryDeleteAsync(core, conversationId, request.DeleteAfterCapture, cancellationToken);
    }

    private async Task EnsureSendReadyAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        await _conversationSend.PrefetchParentAsync(core, conversationId, cancellationToken);
        await _conversationSend.PrefetchConduitAsync(core, conversationId, gizmoId, cancellationToken);

        if (!ConversationParentCache.IsCached(conversationId))
            ChatGptConversationSendService.BootstrapNewConversationParent(conversationId);
    }

    private async Task<(string? Text, string? Error, bool StreamComplete)> CaptureWithRetriesAsync(
        CoreWebView2 core,
        string conversationId,
        string? parentMessageId,
        EphemeralProjectChatRequest request,
        CancellationToken cancellationToken)
    {
        string? responseText = null;
        string? lastError = null;

        for (var attempt = 0; attempt < request.CaptureMaxAttempts; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(request.CapturePollDelay, cancellationToken);

            var capture = await _conversationSend.CaptureAssistantViaApiAsync(
                core,
                conversationId,
                parentMessageId,
                cancellationToken);

            if (capture.Success && !string.IsNullOrWhiteSpace(capture.Text))
            {
                responseText = capture.Text;
                lastError = null;
                if (attempt >= 1)
                    return (responseText, null, StreamComplete: true);
            }
            else
            {
                lastError = capture.Error;
            }
        }

        return (responseText, lastError ?? "capture_timeout", StreamComplete: responseText is not null);
    }

    private async Task<(bool Deleted, string? DeleteError)> TryDeleteAsync(
        CoreWebView2 core,
        string conversationId,
        bool deleteAfterCapture,
        CancellationToken cancellationToken)
    {
        if (!deleteAfterCapture || string.IsNullOrWhiteSpace(conversationId))
            return (false, null);

        var hide = await _conversationSend.HideConversationAsync(core, conversationId, cancellationToken);
        if (hide.Success)
            return (true, null);

        ProjectLinkDiagnostics.Log(
            $"Ephemeral chat hide failed for {conversationId}: {hide.Error ?? "unknown"}");
        return (false, hide.Error ?? "hide_failed");
    }

    private static EphemeralProjectChatResult Fail(EphemeralProjectChatPhase phase, string error) =>
        new()
        {
            Success = false,
            FailedPhase = phase,
            Error = error,
        };
}
