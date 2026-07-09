using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Utility transport: production worker jobs use API-only; setup/probe may use DOM with visible tab.
/// </summary>
internal static class UtilityWorkerTransportService
{
    /// <summary>
    /// Production worker lane — API POST first; DOM composer fallback when POST is blocked (http_403/404).
    /// </summary>
    public static async Task<ConversationSendResult> SendProductionPacketAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string messageText,
        string jobId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        CancellationToken cancellationToken = default)
    {
        var apiResult = await UtilityWorkerApiTransport.PushAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            conversationSend,
            cancellationToken);

        if (apiResult.Success)
            return apiResult;

        if (!UtilityConversationReadinessService.IsUnregisteredFetchError(apiResult.Error))
            return apiResult;

        TracePhase(
            "send_api_dom_fallback",
            jobId,
            conversationId,
            new { error = apiResult.Error });

        return await SendPacketAsync(
            core,
            bundle,
            conversationId,
            gizmoId,
            messageText,
            jobId,
            conversationSend,
            turnService,
            cancellationToken,
            allowDomCapture: true);
    }

    public static async Task<ConversationSendResult> SendPacketAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string messageText,
        string jobId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken = default,
        bool seedOnly = false,
        bool allowDomCapture = true,
        IUtilityWorkerHost? workerHost = null,
        bool setupMode = false,
        UtilityConversationReadinessResult? existingReadiness = null)
    {
        var readiness = existingReadiness ?? await UtilityConversationReadinessService.ProbeAsync(
            core,
            conversationId,
            gizmoId,
            conversationSend,
            turnService,
            bundle,
            cancellationToken,
            skipNavigation: existingReadiness is not null);

        TracePhase(
            "readiness",
            jobId,
            conversationId,
            new
            {
                level = readiness.Level.ToString(),
                readiness.Error,
                readiness.ApiVisible,
                pageHref = readiness.PageHref,
                readiness.DomOnlyReason,
                readiness.ComposerFound,
            });

        if (readiness.Level == UtilityConversationReadinessLevel.Unready)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = readiness.Error ?? "utility_page_not_ready",
                ConversationId = conversationId,
            };
        }

        if (PlaySendDeliveryPolicy.ShouldUseApiWorkerLaneSend(readiness.Level))
        {
            var apiResult = await SendApiWithActivationRetryAsync(
                core,
                bundle,
                conversationId,
                gizmoId,
                messageText,
                jobId,
                conversationSend,
                turnService,
                allowDomCapture,
                readiness,
                cancellationToken);
            if (apiResult.Handled)
                return apiResult.Result!;
        }

        if (!allowDomCapture)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = readiness.Level == UtilityConversationReadinessLevel.DomOnly
                    ? "worker_api_not_ready"
                    : readiness.Error ?? "utility_page_not_ready",
                ConversationId = conversationId,
            };
        }

        TracePhase("send_dom", jobId, conversationId, new
        {
            packetLength = messageText.Length,
            domOnlyReason = readiness.DomOnlyReason,
        });

        if (turnService is null)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "utility_turn_service_required",
                ConversationId = conversationId,
            };
        }

        if (setupMode)
        {
            if (turnService is null)
            {
                return new ConversationSendResult
                {
                    Success = false,
                    Error = "utility_turn_service_required",
                    ConversationId = conversationId,
                };
            }

            TracePhase("send_dom_setup", jobId, conversationId, new { packetLength = messageText.Length });
            return await SubmitDomUtilityPacketAsync(
                turnService,
                core,
                conversationId,
                gizmoId,
                messageText,
                jobId,
                seedOnly,
                cancellationToken);
        }

        var timeoutMs = AdventureTurnService.ComputeUtilityJobTimeoutMs(messageText.Length);
        var captureJobId = seedOnly ? null : jobId;
        var result = await turnService.SubmitUtilityJobAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            timeoutMs: timeoutMs,
            jobId: captureJobId,
            skipPageEnsure: true,
            cancellationToken: cancellationToken);

        if (!result.Success
            && !string.IsNullOrWhiteSpace(readiness.Hint)
            && GenerationJobHandlers.IsCaptureFailureError(result.Error))
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = $"{result.Error} ({readiness.Hint})",
                ConversationId = conversationId,
            };
        }

        if (!result.Success
            && !seedOnly
            && GenerationJobService.IsUtilitySendError(result.Error, "capture_premature")
            && !string.IsNullOrWhiteSpace(result.AssistantText)
            && GenerationJobHandlers.IsSettledJobResponse(jobId, result.AssistantText, result.StreamComplete))
        {
            return new ConversationSendResult
            {
                Success = true,
                ConversationId = result.ConversationId ?? conversationId,
                ParentMessageId = result.ParentMessageId,
                AssistantMessageId = result.AssistantMessageId,
                AssistantText = result.AssistantText,
                StreamComplete = result.StreamComplete,
            };
        }

        return result;
    }

    private readonly record struct ApiWorkerLaneAttempt(bool Handled, ConversationSendResult? Result);

    private static async Task<ApiWorkerLaneAttempt> SendApiWithActivationRetryAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string messageText,
        string jobId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        bool allowDomCapture,
        UtilityConversationReadinessResult readiness,
        CancellationToken cancellationToken)
    {
        TracePhase("send_api", jobId, conversationId, new { packetLength = messageText.Length });

        var apiResult = await TrySendApiWorkerLaneAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            conversationSend,
            cancellationToken);
        if (apiResult.Success)
            return new(true, apiResult);

        if (!UtilityConversationReadinessService.IsUnregisteredFetchError(apiResult.Error))
            return new(true, apiResult);

        return new(false, apiResult);
    }

    private static async Task<ConversationSendResult> TrySendApiWorkerLaneAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        string messageText,
        ChatGptConversationSendService conversationSend,
        CancellationToken cancellationToken)
    {
        await EnsureParentReadyAsync(
            core,
            conversationId,
            gizmoId,
            invalidateCached: false,
            conversationSend,
            cancellationToken);

        return await conversationSend.SendUserMessageAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            cancellationToken);
    }

    private static async Task<ConversationSendResult> SubmitDomUtilityPacketAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        string? jobId,
        bool seedOnly,
        CancellationToken cancellationToken)
    {
        var timeoutMs = AdventureTurnService.ComputeUtilityJobTimeoutMs(messageText.Length);
        var captureJobId = seedOnly ? null : jobId;
        var result = await turnService.SubmitUtilityJobAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            timeoutMs: timeoutMs,
            jobId: captureJobId,
            skipPageEnsure: true,
            cancellationToken: cancellationToken);

        if (!result.Success
            && !seedOnly
            && jobId is not null
            && GenerationJobService.IsUtilitySendError(result.Error, "capture_premature")
            && !string.IsNullOrWhiteSpace(result.AssistantText)
            && GenerationJobHandlers.IsSettledJobResponse(jobId, result.AssistantText, result.StreamComplete))
        {
            return new ConversationSendResult
            {
                Success = true,
                ConversationId = result.ConversationId ?? conversationId,
                ParentMessageId = result.ParentMessageId,
                AssistantMessageId = result.AssistantMessageId,
                AssistantText = result.AssistantText,
                StreamComplete = result.StreamComplete,
            };
        }

        return result;
    }

    /// <summary>
    /// DOM composer attach on the utility worker lane using shadow-compositor hosting (no tab switch).
    /// </summary>
    public static async Task<ConversationSendResult> SendProductionPacketWithAttachmentsAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string messageText,
        string jobId,
        IReadOnlyList<DomAttachmentPayload> domAttachments,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost = null,
        CancellationToken cancellationToken = default)
    {
        if (domAttachments is not { Count: > 0 })
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "missing_attachments",
                ConversationId = conversationId,
            };
        }

        TracePhase("send_dom_attach", jobId, conversationId, new
        {
            packetLength = messageText.Length,
            attachmentCount = domAttachments.Count,
        });

        async Task<ConversationSendResult> SendAsync() =>
            await turnService.SubmitUtilityJobWithAttachmentsAsync(
                core,
                conversationId,
                gizmoId,
                messageText,
                domAttachments,
                jobId,
                skipPageEnsure: true,
                cancellationToken: cancellationToken);

        if (workerHost is not null)
        {
            using var domSend = workerHost.BeginDomAttachmentSend();
            return await workerHost.WithUtilityWebViewActivatedAsync(core, SendAsync, cancellationToken);
        }

        return await SendAsync();
    }

    /// <summary>
    /// DOM composer attach for ephemeral per-job chats (project-home or /c/ URL).
    /// </summary>
    public static async Task<ConversationSendResult> SendEphemeralPacketWithAttachmentsAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        string messageText,
        string jobId,
        IReadOnlyList<DomAttachmentPayload> domAttachments,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost = null,
        bool skipPageEnsure = false,
        bool allowKeyboardSubmitOnProjectHome = false,
        CancellationToken cancellationToken = default)
    {
        if (domAttachments is not { Count: > 0 })
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "missing_attachments",
                ConversationId = conversationId,
            };
        }

        ProjectLinkDiagnostics.Log("ephemeral_attach_send");
        TracePhase("send_ephemeral_dom_attach", jobId, conversationId, new
        {
            packetLength = messageText.Length,
            attachmentCount = domAttachments.Count,
            skipPageEnsure,
            allowKeyboardSubmitOnProjectHome,
        });

        async Task<ConversationSendResult> SendAsync() =>
            await turnService.SubmitUtilityJobWithAttachmentsAsync(
                core,
                conversationId,
                gizmoId,
                messageText,
                domAttachments,
                jobId,
                skipPageEnsure,
                allowKeyboardSubmitOnProjectHome,
                cancellationToken);

        if (workerHost is not null)
        {
            using var domSend = workerHost.BeginDomAttachmentSend();
            return await workerHost.WithUtilityWebViewActivatedAsync(core, SendAsync, cancellationToken);
        }

        return await SendAsync();
    }

    public static async Task<ConversationSendResult> SendSeedAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string seed,
        string jobId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken = default)
    {
        await EnsureParentReadyAsync(
            core,
            conversationId,
            gizmoId,
            invalidateCached: false,
            conversationSend,
            cancellationToken);

        var seedResult = await SendPacketAsync(
            core,
            bundle,
            conversationId,
            gizmoId,
            seed,
            jobId,
            conversationSend,
            turnService,
            cancellationToken,
            seedOnly: true);

        if (seedResult.Success)
            return seedResult;

        if (GenerationJobService.IsUtilitySendError(seedResult.Error, "capture_premature")
            && !string.IsNullOrWhiteSpace(seedResult.AssistantText)
            && seedResult.AssistantText.Length >= AdventureTurnService.UtilityMinCapturedTextLength)
        {
            return new ConversationSendResult
            {
                Success = true,
                ConversationId = seedResult.ConversationId ?? conversationId,
                AssistantText = seedResult.AssistantText,
                StreamComplete = true,
            };
        }

        await Task.Delay(400, cancellationToken);
        await EnsureParentReadyAsync(
            core,
            conversationId,
            gizmoId,
            invalidateCached: true,
            conversationSend,
            cancellationToken);

        return await SendPacketAsync(
            core,
            bundle,
            conversationId,
            gizmoId,
            seed,
            jobId,
            conversationSend,
            turnService,
            cancellationToken,
            seedOnly: true);
    }

    /// <summary>
    /// Registers (if needed) and verifies the worker conversation via utility_worker_ping.
    /// </summary>
    public static async Task<UtilityWorkerCapabilities> ProbeCapabilitiesAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        string workerConversationId,
        string gizmoId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken = default,
        IUtilityWorkerHost? workerHost = null,
        ChatGptProjectApiService? projectApi = null)
    {
        var caps = new UtilityWorkerCapabilities
        {
            WorkerConversationId = workerConversationId,
            LastProbedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            if (workerHost is not null)
            {
                return await workerHost.WithUtilityWebViewActivatedAsync(
                    workerCore,
                    () => ProbeCapabilitiesCoreAsync(
                        workerCore,
                        bundle,
                        workerConversationId,
                        gizmoId,
                        conversationSend,
                        turnService,
                        cancellationToken,
                        workerHost,
                        projectApi ?? workerHost.ProjectApi),
                    cancellationToken);
            }

            return await ProbeCapabilitiesCoreAsync(
                workerCore,
                bundle,
                workerConversationId,
                gizmoId,
                conversationSend,
                turnService,
                cancellationToken,
                workerHost,
                projectApi);
        }
        catch (Exception ex)
        {
            caps.LastProbeError = ex.Message;
            UtilityWorkerSession.SyncCapabilitiesConversationId(bundle);
            bundle.Metadata.UtilityWorkerCapabilities = caps;
            return caps;
        }
    }

    private static async Task<UtilityWorkerCapabilities> ProbeCapabilitiesCoreAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        string workerConversationId,
        string gizmoId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken,
        IUtilityWorkerHost? workerHost,
        ChatGptProjectApiService? projectApi)
    {
        var caps = new UtilityWorkerCapabilities
        {
            WorkerConversationId = workerConversationId,
            LastProbedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            var effectiveConversationId = workerConversationId;
            var session = UtilityWorkerSession.For(bundle.Metadata.Id);
            var page = await session.EnsurePageReadyAsync(workerCore, bundle, cancellationToken);
            if (!page.Success)
            {
                caps.LastProbeError = page.Error ?? "utility_page_not_ready";
                bundle.Metadata.UtilityWorkerCapabilities = caps;
                return caps;
            }

            var readiness = await UtilityConversationReadinessService.ProbeAsync(
                workerCore,
                effectiveConversationId,
                gizmoId,
                conversationSend,
                turnService,
                bundle,
                cancellationToken,
                skipNavigation: true);

            var canRegister = UtilityConversationReadinessService.CanRegisterViaDomPing(readiness);
            caps.HostReady = readiness.Level != UtilityConversationReadinessLevel.Unready
                             && (readiness.ComposerFound
                                 || readiness.Level == UtilityConversationReadinessLevel.Registered);
            caps.ApiFetchOk = readiness.Level == UtilityConversationReadinessLevel.Registered;

            if (readiness.Level == UtilityConversationReadinessLevel.Unready && !canRegister)
            {
                caps.LastProbeError = readiness.Error ?? readiness.DomOnlyReason ?? "worker_not_registered";
                bundle.Metadata.UtilityWorkerCapabilities = caps;
                return caps;
            }

            if (!readiness.ComposerFound
                && readiness.Level != UtilityConversationReadinessLevel.Registered)
            {
                caps.LastProbeError = readiness.Error ?? readiness.DomOnlyReason ?? "utility_page_not_ready";
                bundle.Metadata.UtilityWorkerCapabilities = caps;
                return caps;
            }

            ProjectLinkDiagnostics.Log(
                $"Utility worker probe on {workerCore.Source} (conv {effectiveConversationId}) level={readiness.Level}");

            var usedApiFirst = PlaySendDeliveryPolicy.ShouldUseApiWorkerLaneSend(readiness.Level);
            var probeId = Guid.NewGuid().ToString("N")[..8];
            var send = await SendWorkerPingAsync(
                workerCore,
                bundle,
                effectiveConversationId,
                gizmoId,
                probeId,
                conversationSend,
                turnService,
                cancellationToken,
                workerHost,
                readiness);

            if (!send.Success)
            {
                caps.LastProbeError = send.Error ?? "worker_push_failed";
                bundle.Metadata.UtilityWorkerCapabilities = caps;
                return caps;
            }

            if (TryReconcileFromSend(bundle, effectiveConversationId, send.ConversationId, ref effectiveConversationId))
            {
                readiness = await UtilityConversationReadinessService.ProbeAsync(
                    workerCore,
                    effectiveConversationId,
                    gizmoId,
                    conversationSend,
                    turnService,
                    bundle,
                    cancellationToken,
                    skipNavigation: true);
                caps.HostReady = readiness.Level != UtilityConversationReadinessLevel.Unready
                                 && (readiness.ComposerFound
                                     || readiness.Level == UtilityConversationReadinessLevel.Registered);
                caps.ApiFetchOk = readiness.Level == UtilityConversationReadinessLevel.Registered;
            }

            caps.ApiPullOk = await ValidatePingResponseAsync(
                send,
                probeId,
                workerCore,
                effectiveConversationId,
                conversationSend,
                cancellationToken);
            var domVerified = !usedApiFirst && caps.ApiPullOk;
            if (send.Success)
            {
                caps.ApiPushOk = usedApiFirst
                    ? !string.IsNullOrWhiteSpace(send.ParentMessageId)
                    : caps.ApiPullOk;
            }

            caps.SseReliable = send.StreamComplete;
            if (!domVerified)
            {
                caps.ApiFetchOk = caps.ApiFetchOk
                                  || await ConfirmApiRegisteredAsync(
                                      conversationSend,
                                      workerCore,
                                      effectiveConversationId,
                                      cancellationToken);
            }

            if (!usedApiFirst)
            {
                if (domVerified && caps.ApiPullOk)
                {
                    caps.DomRegistrationVerified = true;
                }
                else
                {
                    readiness = await UtilityConversationReadinessService.ProbeAsync(
                        workerCore,
                        effectiveConversationId,
                        gizmoId,
                        conversationSend,
                        turnService,
                        bundle,
                        cancellationToken,
                        skipNavigation: true);
                    caps.ApiFetchOk = readiness.Level == UtilityConversationReadinessLevel.Registered
                                      || caps.ApiFetchOk;

                    if (PlaySendDeliveryPolicy.ShouldUseApiWorkerLaneSend(readiness.Level))
                    {
                        ProjectLinkDiagnostics.Log("Utility worker probe: API verification ping after DOM registration");
                        var apiProbeId = Guid.NewGuid().ToString("N")[..8];
                        var apiSend = await SendWorkerPingAsync(
                            workerCore,
                            bundle,
                            effectiveConversationId,
                            gizmoId,
                            apiProbeId,
                            conversationSend,
                            turnService,
                            cancellationToken,
                            workerHost,
                            readiness);

                        if (apiSend.Success)
                        {
                            TryReconcileFromSend(
                                bundle,
                                effectiveConversationId,
                                apiSend.ConversationId,
                                ref effectiveConversationId);

                            caps.ApiPushOk = !string.IsNullOrWhiteSpace(apiSend.ParentMessageId);
                            caps.SseReliable = apiSend.StreamComplete;
                            caps.ApiPullOk = await ValidatePingResponseAsync(
                                apiSend,
                                apiProbeId,
                                workerCore,
                                effectiveConversationId,
                                conversationSend,
                                cancellationToken);
                        }
                        else if (domVerified && caps.ApiPullOk)
                        {
                            ProjectLinkDiagnostics.Log(
                                $"Utility worker probe: API verification failed ({apiSend.Error}); DOM registered only");
                            caps.DomRegistrationVerified = true;
                        }
                        else
                        {
                            caps.LastProbeError = apiSend.Error ?? "worker_push_failed";
                            bundle.Metadata.UtilityWorkerCapabilities = caps;
                            return caps;
                        }
                    }
                    else if (!caps.ApiFetchOk)
                    {
                        caps.LastProbeError = "api_not_registered";
                    }
                }
            }
            else if (caps.ApiPullOk && caps.ApiPushOk)
            {
                caps.DomRegistrationVerified = false;
            }

            if (domVerified && caps.ApiPullOk)
                caps.DomRegistrationVerified = true;

            if (UtilityWorkerCapabilities.IsProductionReady(caps))
            {
                caps.LastProbeError = null;
                var verifiedConversationId = ResolveVerifiedWorkerConversationId(
                    workerCore,
                    effectiveConversationId,
                    send.ConversationId);
                UtilityWorkerPinService.TryReconcileVerifiedWorkerConversation(
                    bundle,
                    verifiedConversationId,
                    persist: false);
                caps.WorkerConversationId = verifiedConversationId;

                if (projectApi is not null)
                {
                    caps.LastApiAttachProbeResult = await UtilityWorkerApiAttachProbe.ProbeApiAttachAsync(
                        workerCore,
                        projectApi,
                        conversationSend,
                        verifiedConversationId,
                        gizmoId,
                        cancellationToken);
                }
            }
            else if (!caps.ApiFetchOk || !caps.ApiPushOk)
                caps.LastProbeError ??= "worker_api_not_ready";
            else if (!caps.ApiPullOk)
                caps.LastProbeError ??= "worker_pull_failed";
        }
        catch (Exception ex)
        {
            caps.LastProbeError = ex.Message;
        }

        UtilityWorkerSession.SyncCapabilitiesConversationId(bundle);
        bundle.Metadata.UtilityWorkerCapabilities = caps;
        return caps;
    }

    public static Task<bool> ConfirmApiRegisteredAsync(
        ChatGptConversationSendService conversationSend,
        CoreWebView2 workerCore,
        string workerConversationId,
        CancellationToken cancellationToken) =>
        UtilityWorkerApiTransport.ConfirmRegisteredAsync(
            conversationSend,
            workerCore,
            workerConversationId,
            cancellationToken);

    internal static string BuildPingPacket(string probeId)
    {
        var pingBody = GenerationJobHandlers.BuildWorkerPingPrompt(probeId);
        return ContextTagFormat.WrapUtilityJob(
            GenerationJobId.UtilityWorkerPing,
            pingBody,
            "worker",
            Guid.NewGuid());
    }

    private static async Task<ConversationSendResult> SendWorkerPingAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        string workerConversationId,
        string gizmoId,
        string probeId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken,
        IUtilityWorkerHost? workerHost = null,
        UtilityConversationReadinessResult? readiness = null)
    {
        var wrapped = BuildPingPacket(probeId);
        var send = await SendPacketAsync(
            workerCore,
            bundle,
            workerConversationId,
            gizmoId,
            wrapped,
            GenerationJobId.UtilityWorkerPing,
            conversationSend,
            turnService,
            cancellationToken,
            setupMode: true,
            workerHost: workerHost,
            existingReadiness: readiness);

        return NormalizeWorkerPingSend(send, probeId);
    }

    internal static ConversationSendResult NormalizeWorkerPingSend(
        ConversationSendResult send,
        string probeId)
    {
        if (send.Success)
            return send;

        if (GenerationJobHandlers.IsSettledWorkerPingResponse(send.AssistantText))
        {
            return new ConversationSendResult
            {
                Success = true,
                ConversationId = send.ConversationId,
                ParentMessageId = send.ParentMessageId,
                AssistantText = send.AssistantText,
                AssistantMessageId = send.AssistantMessageId,
                StreamComplete = send.StreamComplete,
            };
        }

        if (GenerationJobService.IsUtilitySendError(send.Error, "capture_premature")
            && GenerationJobHandlers.IsWorkerPingResponseValid(send.AssistantText, probeId))
        {
            return new ConversationSendResult
            {
                Success = true,
                ConversationId = send.ConversationId,
                ParentMessageId = send.ParentMessageId,
                AssistantText = send.AssistantText,
                StreamComplete = true,
            };
        }

        return send;
    }

    private static bool TryReconcileFromSend(
        AdventureBundle bundle,
        string currentConversationId,
        string? sendConversationId,
        ref string effectiveConversationId)
    {
        if (string.IsNullOrWhiteSpace(sendConversationId)
            || string.Equals(sendConversationId, currentConversationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!UtilityWorkerPinService.TryReconcileVerifiedWorkerConversation(
                bundle,
                sendConversationId,
                persist: false))
        {
            return false;
        }

        effectiveConversationId = sendConversationId;
        return true;
    }

    private static string ResolveVerifiedWorkerConversationId(
        CoreWebView2 workerCore,
        string effectiveConversationId,
        string? sendConversationId)
    {
        if (!string.IsNullOrWhiteSpace(sendConversationId))
            return sendConversationId;

        if (Uri.TryCreate(workerCore.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.TryParseConversationId(uri, out var fromSource)
            && !string.IsNullOrWhiteSpace(fromSource))
        {
            return fromSource;
        }

        return effectiveConversationId;
    }

    private static async Task<bool> ValidatePingResponseAsync(
        ConversationSendResult send,
        string probeId,
        CoreWebView2 workerCore,
        string workerConversationId,
        ChatGptConversationSendService conversationSend,
        CancellationToken cancellationToken)
    {
        var text = send.AssistantText;
        if (GenerationJobHandlers.IsWorkerPingResponseValid(text, probeId))
            return true;

        if (string.IsNullOrWhiteSpace(send.ParentMessageId))
            return false;

        var pull = await conversationSend.CaptureAssistantViaApiAsync(
            workerCore,
            workerConversationId,
            send.ParentMessageId,
            cancellationToken);

        return pull.Success
               && GenerationJobHandlers.IsWorkerPingResponseValid(pull.Text, probeId);
    }

    private static async Task EnsureParentReadyAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        bool invalidateCached,
        ChatGptConversationSendService conversationSend,
        CancellationToken cancellationToken)
    {
        if (invalidateCached)
        {
            ConversationParentCache.Invalidate(conversationId);
            ConversationConduitCache.Invalidate(conversationId);
        }

        await conversationSend.PrefetchParentAsync(core, conversationId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(gizmoId))
            await conversationSend.PrefetchConduitAsync(core, conversationId, gizmoId, cancellationToken);

        if (!ConversationParentCache.IsCached(conversationId) && string.IsNullOrWhiteSpace(gizmoId))
            ChatGptConversationSendService.BootstrapNewConversationParent(conversationId);
    }

    private static void TracePhase(string phase, string jobId, string conversationId, object data) =>
        ProjectLinkDiagnostics.Log($"Utility transport {phase} job={jobId} conv={conversationId} {data}");
}
