using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Unified utility send/probe transport for the worker lane and generation jobs.
/// Routes API vs DOM via <see cref="PlaySendDeliveryPolicy"/>. Background outbox uses an off-screen
/// host; API 403 falls back to DOM there without selecting the utility tab.
/// </summary>
internal static class UtilityWorkerTransportService
{
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
        IUtilityWorkerHost? workerHost = null)
    {
        var readiness = await UtilityConversationReadinessService.ProbeAsync(
            core,
            conversationId,
            gizmoId,
            conversationSend,
            turnService,
            bundle,
            cancellationToken);

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
                    ? "worker_background_requires_api"
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

        var timeoutMs = AdventureTurnService.ComputeUtilityJobTimeoutMs(messageText.Length);
        var captureJobId = seedOnly ? null : jobId;
        var result = await turnService.SubmitUtilityJobAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            timeoutMs,
            captureJobId,
            cancellationToken);

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

        if (allowDomCapture && turnService is not null)
        {
            TracePhase(
                "send_dom_offscreen",
                jobId,
                conversationId,
                new { error = apiResult.Error });

            var domResult = await SubmitDomUtilityPacketAsync(
                turnService,
                core,
                conversationId,
                gizmoId,
                messageText,
                jobId,
                seedOnly: false,
                cancellationToken);

            if (domResult.Success
                || (!string.IsNullOrWhiteSpace(domResult.AssistantText)
                    && GenerationJobHandlers.IsSettledJobResponse(
                        jobId,
                        domResult.AssistantText,
                        domResult.StreamComplete)))
            {
                return new(true, domResult);
            }

            return new(true, domResult);
        }

        if (!allowDomCapture)
            return new(true, apiResult);

        TracePhase(
            "send_api_dom_fallback",
            jobId,
            conversationId,
            new { error = apiResult.Error, domOnlyReason = readiness.DomOnlyReason });

        var reprobe = await UtilityConversationReadinessService.ProbeAsync(
            core,
            conversationId,
            gizmoId,
            conversationSend,
            turnService,
            bundle,
            cancellationToken);
        if (reprobe.Level == UtilityConversationReadinessLevel.Unready)
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
            timeoutMs,
            captureJobId,
            cancellationToken);

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
        CancellationToken cancellationToken = default)
    {
        var caps = new UtilityWorkerCapabilities
        {
            WorkerConversationId = workerConversationId,
            LastProbedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            var nav = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                workerCore,
                workerConversationId,
                gizmoId,
                cancellationToken);
            if (!nav.Success)
            {
                caps.LastProbeError = nav.Error ?? "utility_page_not_ready";
                bundle.Metadata.UtilityWorkerCapabilities = caps;
                return caps;
            }

            var readiness = await UtilityConversationReadinessService.ProbeAsync(
                workerCore,
                workerConversationId,
                gizmoId,
                conversationSend,
                turnService,
                bundle,
                cancellationToken);

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
                $"Utility worker probe on {workerCore.Source} (conv {workerConversationId}) level={readiness.Level}");

            var usedApiFirst = PlaySendDeliveryPolicy.ShouldUseApiWorkerLaneSend(readiness.Level);
            var probeId = Guid.NewGuid().ToString("N")[..8];
            var send = await SendWorkerPingAsync(
                workerCore,
                bundle,
                workerConversationId,
                gizmoId,
                probeId,
                conversationSend,
                turnService,
                cancellationToken);

            if (!send.Success)
            {
                caps.LastProbeError = send.Error ?? "worker_push_failed";
                bundle.Metadata.UtilityWorkerCapabilities = caps;
                return caps;
            }

            caps.ApiPullOk = await ValidatePingResponseAsync(
                send,
                probeId,
                workerCore,
                workerConversationId,
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
                                      workerConversationId,
                                      cancellationToken);
            }

            if (!usedApiFirst)
            {
                if (domVerified && caps.ApiPullOk)
                {
                    caps.DomRegistrationVerified = true;
                    caps.ApiPushOk = true;
                }
                else
                {
                    readiness = await UtilityConversationReadinessService.ProbeAsync(
                        workerCore,
                        workerConversationId,
                        gizmoId,
                        conversationSend,
                        turnService,
                        bundle,
                        cancellationToken);
                    caps.ApiFetchOk = readiness.Level == UtilityConversationReadinessLevel.Registered
                                      || caps.ApiFetchOk;

                    if (PlaySendDeliveryPolicy.ShouldUseApiWorkerLaneSend(readiness.Level))
                    {
                        ProjectLinkDiagnostics.Log("Utility worker probe: API verification ping after DOM registration");
                        var apiProbeId = Guid.NewGuid().ToString("N")[..8];
                        var apiSend = await SendWorkerPingAsync(
                            workerCore,
                            bundle,
                            workerConversationId,
                            gizmoId,
                            apiProbeId,
                            conversationSend,
                            turnService,
                            cancellationToken);

                        if (apiSend.Success)
                        {
                            caps.ApiPushOk = !string.IsNullOrWhiteSpace(apiSend.ParentMessageId);
                            caps.SseReliable = apiSend.StreamComplete;
                            caps.ApiPullOk = await ValidatePingResponseAsync(
                                apiSend,
                                apiProbeId,
                                workerCore,
                                workerConversationId,
                                conversationSend,
                                cancellationToken);
                        }
                        else if (domVerified && caps.ApiPullOk)
                        {
                            ProjectLinkDiagnostics.Log(
                                $"Utility worker probe: API verification failed ({apiSend.Error}); keeping DOM registration");
                            caps.DomRegistrationVerified = true;
                            caps.ApiPushOk = true;
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

            if (!caps.ApiPullOk)
                caps.LastProbeError ??= "worker_pull_failed";
            else if (caps.IsGreen)
                caps.LastProbeError = null;
        }
        catch (Exception ex)
        {
            caps.LastProbeError = ex.Message;
        }

        bundle.Metadata.UtilityWorkerCapabilities = caps;
        return caps;
    }

    public static async Task<bool> ConfirmApiRegisteredAsync(
        ChatGptConversationSendService conversationSend,
        CoreWebView2 workerCore,
        string workerConversationId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var fetch = await conversationSend.FetchConversationAsync(
                workerCore,
                workerConversationId,
                cancellationToken);
            if (fetch.Success)
                return true;

            if (attempt < 2)
                await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        }

        return false;
    }

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
        CancellationToken cancellationToken)
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
            cancellationToken);

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
