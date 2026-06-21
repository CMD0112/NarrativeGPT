using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private GenerationJobService? _generationJobService;
    private readonly SemaphoreSlim _generationJobGate = new(1, 1);

    private GenerationJobService GetOrCreateGenerationJobService(WebView2? wireFrom = null)
    {
        if (_generationJobService is not null)
            return _generationJobService;

        var wv = wireFrom ?? GetPlayWebView();
        if (wv is not null)
            WireProjectServices(wv);

        _generationJobService = new GenerationJobService(
            _projectApiService ?? throw new InvalidOperationException("Project API service not ready."),
            _conversationSendService ?? throw new InvalidOperationException("Conversation send service not ready."),
            TryCreateProjectConversationViaUiAsync);

        return _generationJobService;
    }

    private async Task<string?> TryCreateProjectConversationViaUiAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var wv = GetPlayWebView();
        if (wv is null)
            return null;

        if (_activeAdventureId is not { } adventureId)
            return null;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        ProjectChatDraftService.BeginUtilityDraft(bundle);

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        gizmoId = string.IsNullOrWhiteSpace(gizmoId) ? null : ChatGptUrls.NormalizeGizmoId(gizmoId);
        if (gizmoId is not null && _projectApiService is not null)
            await _projectApiService.EnsureProjectPageAsync(core, gizmoId, cancellationToken);

        var turnService = GetOrCreateTurnService(wv);
        var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
        var conversationId = !string.IsNullOrWhiteSpace(ui.ConversationId)
            ? ui.ConversationId
            : await turnService.GetConversationIdAsync(core);

        if (!string.IsNullOrWhiteSpace(conversationId) && gizmoId is not null
            && !AdventurePlayContextService.IsOnPlayConversationPage(core.Source, conversationId, gizmoId))
        {
            var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(conversationId, gizmoId, core.Source);
            core.Navigate(targetUrl);
            await WaitForChatGptNavigationAsync(core);
        }

        if (!IsAcceptableUtilityUiConversation(bundle, core.Source, conversationId))
            return null;

        return conversationId;
    }

    private static bool IsAcceptableUtilityUiConversation(
        AdventureBundle bundle,
        string? source,
        string? conversationId)
    {
        if (!PlayTabPinService.IsAcceptableUtilityConversationId(bundle, conversationId))
            return false;

        if (string.IsNullOrWhiteSpace(source)
            || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var urlConv)
            || string.IsNullOrWhiteSpace(urlConv))
        {
            return false;
        }

        return string.Equals(urlConv, conversationId, StringComparison.OrdinalIgnoreCase);
    }

    private static TurnRecord? GetLastAcceptedTurn(AdventureBundle bundle) =>
        bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderByDescending(t => t.Index)
            .FirstOrDefault();

    private static GenerationJobContext? EnrichJobContextWithScope(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        bool forceRotate)
    {
        var needsScope = jobId is GenerationJobId.ExtractEntities
            or GenerationJobId.ProposeMemories
            or GenerationJobId.ProcessTurn;

        if (!needsScope)
            return context;

        if (context.Scope is not null)
            return context;

        var scope = UtilityTranscriptScopeService.ResolveFromLocalLog(bundle)
                    ?? UtilityTranscriptScopeService.ResolveFallbackTurn(bundle);

        if (scope is null)
            return null;

        if (jobId == GenerationJobId.ProcessTurn
            && string.Equals(bundle.Metadata.Settings.LastUtilityScopeHash, scope.ScopeHash, StringComparison.Ordinal))
        {
            // Allow re-run; caller may want fresh proposals. No hard block.
        }

        return new GenerationJobContext
        {
            Turn = context.Turn ?? ScopeToTurn(scope),
            Scope = scope,
            CardId = context.CardId,
            EntityId = context.EntityId,
            EntityKind = context.EntityKind,
            ForceRotate = forceRotate,
            UserPrompt = context.UserPrompt,
            ProcessTurnIncludeMemories = context.ProcessTurnIncludeMemories,
            ProcessTurnIncludeEntities = context.ProcessTurnIncludeEntities,
            ProcessTurnIncludeSummary = context.ProcessTurnIncludeSummary,
            SuppressInlineGuide = context.SuppressInlineGuide,
            DesignStep = context.DesignStep,
        };
    }

    private static TurnRecord? ScopeToTurn(UtilityTranscriptScope scope)
    {
        if (scope.TargetPair is not { } pair)
            return null;

        return new TurnRecord
        {
            Index = pair.TurnIndex ?? 0,
            PlayerText = pair.PlayerText,
            NarratorText = pair.NarratorText,
            Status = TurnStatus.Accepted,
        };
    }

    private static bool IsDesignGenerationJob(string jobId) =>
        jobId is GenerationJobId.DesignAdventure
            or GenerationJobId.DesignExtractStep
            or GenerationJobId.DraftFramework
            or GenerationJobId.ProposeJsonImport
            or GenerationJobId.ProposeSourceEdits;

    private async Task<GenerationJobResult?> RunGenerationJobForActiveAdventureAsync(
        string jobId,
        GenerationJobContext? context = null,
        bool forceRotate = false)
    {
        if (_activeAdventureId is not { } adventureId)
        {
            await Dispatcher.InvokeAsync(() => SetPlayComposeStatus($"{jobId}: no active adventure."));
            return null;
        }

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            await Dispatcher.InvokeAsync(() => SetPlayComposeStatus($"{jobId}: link a ChatGPT Project first."));
            return null;
        }

        context ??= new GenerationJobContext();
        context = EnrichJobContextWithScope(bundle, jobId, context, forceRotate);
        if (context is null)
        {
            await Dispatcher.InvokeAsync(() =>
                SetPlayComposeStatus($"{jobId}: no play exchange available — send a turn first."));
            return null;
        }

        var isDesignJob = IsDesignGenerationJob(jobId);
        var isDesignSourceJob = jobId is GenerationJobId.ProposeJsonImport or GenerationJobId.ProposeSourceEdits;

        WebView2 wv;
        CoreWebView2 core;
        if (isDesignJob)
        {
            try
            {
                // Design-source jobs carry a self-contained packet; skip pre-job design seeding.
                wv = await ResolveDesignWebViewAsync(
                    adventureId,
                    selectTab: true,
                    ensureThread: !isDesignSourceJob)
                     ?? throw new InvalidOperationException("Design WebView unavailable.");
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_appMode == AppMode.Design)
                        _designView?.SetStatus($"{jobId}: design tab not ready — {ex.Message}");
                    else
                        SetPlayComposeStatus($"{jobId}: design tab not ready — {ex.Message}");
                });
                return null;
            }

            if (wv.CoreWebView2 is not { } designCore)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_appMode == AppMode.Design)
                        _designView?.SetStatus($"{jobId}: design tab still initializing.");
                    else
                        SetPlayComposeStatus($"{jobId}: design tab still initializing.");
                });
                return null;
            }

            core = designCore;
        }
        else
        {
            var playTab = GetPlayWebView();
            if (playTab is null)
            {
                await Dispatcher.InvokeAsync(() =>
                    SetPlayComposeStatus($"{jobId}: play tab not ready — pin a play tab first."));
                return null;
            }

            wv = playTab;
            if (wv.CoreWebView2 is not { } playCoreResolved)
            {
                await Dispatcher.InvokeAsync(() =>
                    SetPlayComposeStatus($"{jobId}: play tab not ready — pin a play tab first."));
                return null;
            }

            core = playCoreResolved;
        }

        if (!await _generationJobGate.WaitAsync(0))
        {
            await Dispatcher.InvokeAsync(() => SetPlayComposeStatus($"{jobId}: another job is running."));
            return null;
        }

        await Dispatcher.InvokeAsync(() => SetShellJobActive(true));
        GenerationJobResult? jobResult = null;
        try
        {
            await _playSendGate.WaitAsync();
            try
            {
                GetOrRegisterAdventureBridge(wv);
                WireProjectServices(wv);
                var turnService = GetOrCreateTurnService(wv);
                var playWv = GetPlayWebView();
                var playCore = playWv?.CoreWebView2;
                var playTurnService = playWv is not null ? GetOrCreateTurnService(playWv) : null;
                if (playWv is not null)
                    GetOrRegisterAdventureBridge(playWv);
                var service = GetOrCreateGenerationJobService(wv);
                var runContext = new GenerationJobContext
                {
                    Turn = context.Turn,
                    Scope = context.Scope,
                    CardId = context.CardId,
                    EntityId = context.EntityId,
                    EntityKind = context.EntityKind,
                    ForceRotate = forceRotate,
                    UserPrompt = context.UserPrompt,
                    DesignStep = context.DesignStep,
                    ProcessTurnIncludeMemories = context.ProcessTurnIncludeMemories,
                    ProcessTurnIncludeEntities = context.ProcessTurnIncludeEntities,
                    ProcessTurnIncludeSummary = context.ProcessTurnIncludeSummary,
                    SuppressInlineGuide = context.SuppressInlineGuide,
                };
                jobResult = await service.RunJobAsync(
                    core, bundle, jobId, runContext, turnService, playCore, playTurnService);
                var result = jobResult;

                bundle.Metadata.UtilityJobLastErrors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var utilityJobId = GenerationJobHandlers.GetUtilityJobId(jobId);
                if (!result.Success || (result.ProposalCount == 0 && result.Error is not null))
                    bundle.Metadata.UtilityJobLastErrors[utilityJobId] = result.Error ?? result.SkippedReason ?? "failed";
                else
                    bundle.Metadata.UtilityJobLastErrors.Remove(utilityJobId);

                if (jobId == GenerationJobId.ProcessTurn
                    && result.Success
                    && runContext.Scope is { } processedScope)
                {
                    bundle.Metadata.Settings.LastUtilityScopeHash = processedScope.ScopeHash;
                }

                AdventureStore.Save(bundle);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (_appMode == AppMode.Design || isDesignJob)
                    {
                        _designView?.RefreshAfterGenerationJob();
                        UpdateDesignLinkStatus();
                        HandleDesignJobUiResult(jobId, result);
                    }
                    else
                    {
                        ReloadPlayAdventure(adventureId);
                        _playView?.RefreshAfterGenerationJob();
                        UpdatePlayLinkStatus();
                        HandleGenerationJobUiResult(jobId, result);
                    }
                });
            }
            finally
            {
                _playSendGate.Release();
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                SetPlayComposeStatus($"{jobId} error: {ex.Message}"));
        }
        finally
        {
            _generationJobGate.Release();
            await Dispatcher.InvokeAsync(() =>
            {
                SetShellJobActive(false);
                UpdateShellStatusBar();
            });
            await Dispatcher.InvokeAsync(async () =>
            {
                await RestorePlayComposerAsync(GetActivePlayComposeInjection());
            });
        }

        return jobResult;
    }

    internal async Task<string?> SynthesizeSourceContentAsync(
        Guid adventureId,
        string targetRelativePath,
        string parsedContent)
    {
        var previousActive = _activeAdventureId;
        _activeAdventureId = adventureId;
        try
        {
            var result = await RunSynthesizeSourceJobAsync(targetRelativePath, GenerationJobId.ProposeSourceEdits, parsedContent);
            return result;
        }
        finally
        {
            _activeAdventureId = previousActive;
        }
    }

    private void HandleDesignJobUiResult(string jobId, GenerationJobResult result)
    {
        if (_designView is null)
            return;

        string? status = null;
        if (result.Success && result.ProposalCount > 0)
        {
            status = jobId == GenerationJobId.ProposeJsonImport
                ? $"Queued {result.ProposalCount} JSON import proposal(s)."
                : $"Extracted {result.ProposalCount} proposal(s).";

            if (jobId == GenerationJobId.ProposeJsonImport)
                _designView.TryOpenJsonImportReviewDialog();
        }
        else if (!result.Success && result.SkippedReason is null)
            status = FormatDesignJobStatusError(jobId, result.Error);
        else if (!string.IsNullOrWhiteSpace(result.DisplayText))
            status = $"{jobId}: reply ready — check the design thread.";
        else if (result.Success && result.ProposalCount == 0 && result.Error is not null)
            status = $"{jobId}: no proposals ({result.Error}).";
        else if (jobId == GenerationJobId.DraftFramework && result.DraftSourcePath is { Length: > 0 } draftPath)
            status = $"Draft framework saved to sources/{draftPath}";

        if (!string.IsNullOrWhiteSpace(status))
            _designView.SetStatus(status);
    }

    private static string FormatDesignJobStatusError(string jobId, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)
            && error.Contains("design_pin_required", StringComparison.OrdinalIgnoreCase))
        {
            return $"{jobId}: pin a design thread first — Open Project → New chat → Use this tab as design thread";
        }

        return $"{jobId} failed: {error ?? "unknown"}";
    }

    private void HandleGenerationJobUiResult(string jobId, GenerationJobResult result)
    {
        if (_appMode != AppMode.Play)
            return;

        string? status = null;
        if (result.Success && result.ProposalCount > 0)
            status = PendingReviewService.FormatReviewHint(jobId, result.ProposalCount);
        else if (!result.Success && result.SkippedReason is null)
        {
            status = string.Equals(result.Error, "rate_limited", StringComparison.OrdinalIgnoreCase)
                ? $"{jobId} failed: rate limited — wait ~30s and retry."
                : $"{jobId} failed: {result.Error ?? "unknown"}";
        }
        else if (result.Success && result.ProposalCount == 0 && result.Error is not null)
            status = $"{jobId}: no proposals ({result.Error}).";
        else if (jobId == GenerationJobId.DraftFramework && result.DraftSourcePath is { Length: > 0 } draftPath)
            status = $"Draft framework saved to sources/{draftPath}";
        else if (jobId == GenerationJobId.SynthesizeSource && !string.IsNullOrWhiteSpace(result.DisplayText))
            status = $"{jobId}: synthesis ready — review in Source Manager.";

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (_activeAdventureId is { } adventureId)
            {
                var bundle = AdventureStore.Load(adventureId);
                if (bundle is not null && UtilityDeliveryModeService.UsesInlineDelivery(bundle))
                    status += " · inline play thread";
            }

            if (!string.IsNullOrWhiteSpace(result.StoryContextStatusHint))
                status += $" · {result.StoryContextStatusHint}";
            SetPlayComposeStatus(status);
        }
        else if (!string.IsNullOrWhiteSpace(result.StoryContextStatusHint))
        {
            SetPlayComposeStatus(result.StoryContextStatusHint);
        }
    }

    private async Task<UtilityStoryContextBuildResult> BuildLiveStoryContextPreviewAsync(Guid adventureId, string jobId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new UtilityStoryContextBuildResult { CaptureError = "adventure_not_found" };

        var wv = GetPlayWebView();
        if (wv is not null)
            WireProjectServices(wv);

        var playWv = GetPlayWebView();
        var playCore = playWv?.CoreWebView2;
        var playTurnService = playWv is not null ? GetOrCreateTurnService(playWv) : null;
        if (playWv is not null)
            GetOrRegisterAdventureBridge(playWv);

        var sendService = _conversationSendService
                          ?? throw new InvalidOperationException("Conversation send service not ready.");
        var transcriptService = new PlayThreadTranscriptService(sendService, playTurnService);
        var builder = new UtilityStoryContextBuilder(transcriptService);
        var domOnlyCapture = UtilityDeliveryModeService.UsesInlineDelivery(bundle);
        return await builder.BuildAsync(bundle, jobId, playCore, domOnlyCapture: domOnlyCapture);
    }

    private async Task RunScheduledJobsAfterTurnAsync(AdventureBundle bundle, TurnRecord turn)
    {
        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);
        foreach (var jobId in jobs)
            await RunGenerationJobForActiveAdventureAsync(jobId, new GenerationJobContext { Turn = turn });
    }

    private Task RunEntityExtractionForActiveAdventureAsync(TurnRecord? turn = null, bool forceRotate = false) =>
        RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.ExtractEntities,
            turn is null ? null : new GenerationJobContext { Turn = turn },
            forceRotate);

    private Task RunProposeMemoriesAsync() =>
        RunGenerationJobForActiveAdventureAsync(GenerationJobId.ProposeMemories);

    private Task RunProcessLastExchangeAsync(bool includeSummary = false) =>
        RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.ProcessTurn,
            new GenerationJobContext
            {
                ProcessTurnIncludeMemories = true,
                ProcessTurnIncludeEntities = true,
                ProcessTurnIncludeSummary = includeSummary,
                SuppressInlineGuide = true,
            });

    private Task RunExpandEntityAsync(string entityKind, Guid entityId) =>
        RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.ExpandEntity,
            new GenerationJobContext { EntityKind = entityKind, EntityId = entityId, SuppressInlineGuide = true });

    private Task RunUpdateSummaryAsync() =>
        RunGenerationJobForActiveAdventureAsync(GenerationJobId.UpdateSummary);

    private Task RunBootstrapLoreAsync() =>
        RunGenerationJobForActiveAdventureAsync(GenerationJobId.BootstrapLore);

    private Task RunBootstrapSectionsAsync() =>
        RunGenerationJobForActiveAdventureAsync(GenerationJobId.BootstrapSections);

    private Task RunExpandStoryCardAsync(Guid cardId) =>
        RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.ExpandStoryCard,
            new GenerationJobContext { CardId = cardId });

    private Task RunExpandSectionAsync(Guid entityId) =>
        RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.ExpandSection,
            new GenerationJobContext { EntityId = entityId });

    private Task RunContinuityCheckAsync() =>
        RunGenerationJobForActiveAdventureAsync(GenerationJobId.ContinuityCheck);

    private Task RunSourceEditJobAsync(string userPrompt) =>
        RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.ProposeSourceEdits,
            new GenerationJobContext { UserPrompt = userPrompt });

    private async Task<DesignExtractResult?> RunProposeJsonImportAsync(Guid adventureId)
    {
        _activeAdventureId = adventureId;
        var result = await RunGenerationJobForActiveAdventureAsync(GenerationJobId.ProposeJsonImport);
        if (result is null)
            return null;

        return new DesignExtractResult
        {
            Success = result.Success,
            ProposalCount = result.ProposalCount,
            Error = result.Error,
        };
    }

    private Task RunDraftFrameworkAsync() =>
        RunGenerationJobForActiveAdventureAsync(GenerationJobId.DraftFramework);

    private async Task<string?> RunSynthesizeSourceJobAsync(string targetPath, string utilityJobId, string parsedContent)
    {
        if (_activeAdventureId is not { } adventureId)
            return null;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        var prompt = SourceSynthesisService.BuildSynthesizeToFilePrompt(
            bundle,
            targetPath,
            utilityJobId,
            parsedContent);

        var result = await RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.SynthesizeSource,
            new GenerationJobContext { UserPrompt = prompt });

        return result?.DisplayText;
    }

    private async Task OpenProjectSettingsAsync()
    {
        if (_activeAdventureId is not { } adventureId)
            return;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return;

        var url = ChatGptUrls.BuildProjectUrl(bundle.Metadata.LinkedProjectId);
        await AddChatTabAsync("Project settings", new Uri(url));
    }

    private async Task SyncProjectInstructionsIfEnabledAsync(AdventureBundle bundle)
    {
        if (!bundle.Metadata.Settings.AutoSyncProjectInstructions
            || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            || _projectApiService is null
            || !InstructionSourcesPolicy.InstructionDomainChanged(bundle))
            return;

        var wv = FindProjectApiWebView();
        if (wv?.CoreWebView2 is not { } core)
            return;

        var instructions = AdventureProjectBindingService.BuildProjectInstructions(bundle);
        await _projectApiService.UpsertProjectAsync(
            core,
            bundle.Metadata.LinkedProjectId,
            bundle.Metadata.Title,
            instructions);
        InstructionSourcesPolicy.RecordInstructionsSynced(bundle);
        AdventureStore.Save(bundle);
    }
}
