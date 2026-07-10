using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    public async Task OpenAdventureDesignWizardAsync(Guid? adventureId = null)
    {
        var wizard = adventureId is { } id
            ? new AdventureDesignWizard(id) { Owner = this }
            : new AdventureDesignWizard { Owner = this };

        if (!wizard.IsLoaded && wizard.AdventureId == Guid.Empty)
            return;

        WireDesignWizard(wizard);

        if (wizard.ShowDialog() != true)
        {
            _dashboardView?.RefreshList();
            return;
        }

        _dashboardView?.RefreshList();

        if (wizard.ContinueToDesign)
        {
            if (_appMode is AppMode.Play or AppMode.Design && _activeAdventureId == wizard.AdventureId)
                await SwitchToDesignSessionCoreAsync(wizard.AdventureId, DesignModeEntryIntent.Default);
            else
                await StartDesignModeAsync(wizard.AdventureId);
        }
    }

    public async Task OpenContinueDesignWizardAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        if (_appMode is AppMode.Play or AppMode.Design && _activeAdventureId == adventureId)
        {
            await SwitchToDesignSessionAsync();
            return;
        }

        if (bundle.Metadata.Status != AdventureStatus.Designing)
        {
            if (AdventureDesignContextService.CanOpenLocalSourcesEdit(bundle))
            {
                await StartDesignModeAsync(adventureId, DesignModeEntryIntent.LocalSourcesEdit);
                return;
            }

            var accepted = bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted);
            if (accepted > 0)
            {
                MessageBox.Show(this,
                    "This adventure already has play turns and no local source files.\n\n"
                    + "Use Play settings to edit scenario, or export sources first.",
                    "Continue design",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            bundle.Metadata.Status = AdventureStatus.Designing;
            AdventureDesignService.EnsureWorkspace(bundle);
            AdventureDesignService.HydrateFromScenario(bundle);
            AdventureStore.Save(bundle);
        }

        AdventureDesignService.EnsureWorkspace(bundle);
        if (bundle.DesignWorkspace.CurrentStep > AdventureDesignStep.Setup)
        {
            await StartDesignModeAsync(adventureId);
            return;
        }

        await OpenAdventureDesignWizardAsync(adventureId);
    }

    private void WireDesignWizard(AdventureDesignWizard wizard)
    {
        wizard.LinkProjectAsync = async () =>
        {
            _activeAdventureId = wizard.AdventureId;
            await OpenProjectWorkspaceAsync(wizard.AdventureId);
        };
    }

    private async Task<DesignChatSendResult> SendDesignDomChatAsync(
        Guid adventureId,
        string promptText,
        AdventureDesignStep recordStep)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignChatSendResult { Success = false, Error = "adventure_not_found" };

        if (!AdventureDesignChatService.CanUseChat(bundle))
            return new DesignChatSendResult { Success = false, Error = "link_project_first" };

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);

        WebView2 wv;
        try
        {
            wv = await ResolveDesignWebViewAsync(
                     adventureId,
                     selectTab: true,
                     ensureThread: false,
                     preserveCurrentPage: true)
                 ?? throw new InvalidOperationException("Design browser tab unavailable.");
        }
        catch (Exception ex)
        {
            return new DesignChatSendResult { Success = false, Error = ex.Message };
        }

        if (wv.CoreWebView2 is not { } core)
            return new DesignChatSendResult { Success = false, Error = "design_tab_initializing" };

        if (!AdventureDesignDomChatService.TryGetDesignConversationId(bundle, core, out var conversationId, out _))
        {
            var targetUrl = DesignTabPinService.GetDesignTargetUrl(bundle)
                            ?? DesignTabPinService.GetDesignBrowseUrl(bundle);
            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                core.Navigate(targetUrl);
                await WaitForChatGptNavigationAsync(core, expectedDestination: targetUrl);
            }

            if (!AdventureDesignDomChatService.TryGetDesignConversationId(bundle, core, out conversationId, out _))
            {
                return new DesignChatSendResult
                {
                    Success = false,
                    Error = AdventureDesignDomChatService.FormatPinError("design_tab_not_on_conversation"),
                };
            }
        }

        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);
        var turnService = GetOrCreateTurnService(wv);

        var result = await AdventureDesignDomChatService.SendPromptAsync(
            core,
            bundle,
            turnService,
            promptText);

        if (result.Success)
        {
            AdventureDesignChatService.RecordAssistantMessage(
                bundle,
                recordStep,
                result.AssistantText ?? "(sent — check design thread for reply)");
            AdventureStore.Save(bundle);
            UpdateDesignLinkStatus();
        }

        return result;
    }

    private async Task<DesignChatSendResult> RunDesignChatAsync(Guid adventureId, string userText)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignChatSendResult { Success = false, Error = "adventure_not_found" };

        if (!AdventureDesignChatService.CanUseChat(bundle))
            return new DesignChatSendResult { Success = false, Error = "link_project_first" };

        var step = bundle.DesignWorkspace.CurrentStep;
        var prompt = AdventureDesignChatService.ResolveOutgoingMessage(bundle, step, userText);
        AdventureDesignChatService.RecordUserMessage(bundle, step, userText);
        AdventureStore.Save(bundle);

        var result = await SendDesignDomChatAsync(adventureId, prompt, step);
        if (result.Success)
            _designView?.RefreshAfterGenerationJob();

        return result;
    }

    private async Task<DesignChatSendResult> RunDesignSourceFilePromptAsync(
        Guid adventureId,
        string relativePath,
        string? refinementRequest = null)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignChatSendResult { Success = false, Error = "adventure_not_found" };

        if (!AdventureDesignChatService.CanUseChat(bundle))
            return new DesignChatSendResult { Success = false, Error = "link_project_first" };

        string prompt;
        try
        {
            prompt = string.Equals(relativePath, InstructionContractService.InstructionsSnippetFile, StringComparison.OrdinalIgnoreCase)
                ? InstructionRefinementPromptService.BuildRefinementPrompt(bundle, refinementRequest)
                : AdventureDesignSourcePromptService.BuildPrompt(bundle, relativePath);
        }
        catch (Exception ex)
        {
            return new DesignChatSendResult { Success = false, Error = ex.Message };
        }

        prompt = AdventureDesignChatService.ResolveSourceFilePromptMessage(bundle, prompt);
        AdventureDesignChatService.RecordUserMessage(
            bundle,
            AdventureDesignStep.Sources,
            $"[{relativePath}] source file prompt");
        AdventureStore.Save(bundle);

        var result = await SendDesignDomChatAsync(adventureId, prompt, AdventureDesignStep.Sources);
        if (result.Success)
        {
            bundle = AdventureStore.Load(adventureId);
            if (bundle is not null)
            {
                AdventureDesignService.MarkSourceFilePromptSent(bundle, relativePath, result.AssistantText);
                AdventureSourceFileService.TrySaveFromDesignReply(
                    bundle,
                    result.AssistantText ?? "",
                    [relativePath]);
                AdventureStore.Save(bundle);
            }

            _designView?.RefreshAfterGenerationJob();
        }

        return result;
    }

    private Task<bool> RunOpenInstructionDesignerAsync(Guid adventureId)
    {
        _activeAdventureId = adventureId;
        var result = InstructionDesignerDialog.Show(this, adventureId);
        if (result == true)
            _designView?.RefreshAfterGenerationJob();
        return Task.FromResult(result == true);
    }

    private Task RunGenerateInstructionsFileAsync(Guid adventureId)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        InstructionContractService.HydrateDesignInstructionFields(bundle);
        InstructionContractService.GenerateInstructionsSnippetFile(bundle);
        AdventureStore.Save(bundle);
        _designView?.RefreshAfterGenerationJob();
        _designView?.SetStatus("Generated instructions-snippet.md from canonical contract (no AI).");
        return Task.CompletedTask;
    }

    private Task<DesignChatSendResult> RunRefineInstructionsAsync(Guid adventureId, string? refinementRequest) =>
        RunDesignSourceFilePromptAsync(
            adventureId,
            InstructionContractService.InstructionsSnippetFile,
            refinementRequest);

    private async Task<DesignChatSendResult> RunDesignCombinedSourceFilePromptsAsync(
        Guid adventureId,
        IReadOnlyList<string> relativePaths)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignChatSendResult { Success = false, Error = "adventure_not_found" };

        if (!AdventureDesignChatService.CanUseChat(bundle))
            return new DesignChatSendResult { Success = false, Error = "link_project_first" };

        string prompt;
        IReadOnlyList<string> paths;
        try
        {
            paths = AdventureDesignSourcePromptService.NormalizeSelectedPaths(relativePaths);
            if (paths.Count == 0)
                return new DesignChatSendResult { Success = false, Error = "Select at least one source file." };

            prompt = AdventureDesignSourcePromptService.BuildCombinedPrompt(bundle, paths);
        }
        catch (Exception ex)
        {
            return new DesignChatSendResult { Success = false, Error = ex.Message };
        }

        prompt = AdventureDesignChatService.ResolveSourceFilePromptMessage(bundle, prompt);
        AdventureDesignChatService.RecordUserMessage(
            bundle,
            AdventureDesignStep.Sources,
            $"[{string.Join(", ", paths)}] combined source file prompt");
        AdventureStore.Save(bundle);

        var result = await SendDesignDomChatAsync(adventureId, prompt, AdventureDesignStep.Sources);
        if (result.Success)
        {
            bundle = AdventureStore.Load(adventureId);
            if (bundle is not null)
            {
                foreach (var path in paths)
                    AdventureDesignService.MarkSourceFilePromptSent(bundle, path, result.AssistantText);
                AdventureSourceFileService.TrySaveFromDesignReply(
                    bundle,
                    result.AssistantText ?? "",
                    paths);
                AdventureStore.Save(bundle);
            }

            _designView?.RefreshAfterGenerationJob();
        }

        return result;
    }

    private async Task<DesignSourcePullResult> RunPullSourcesFromDesignThreadAsync(Guid adventureId)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignSourcePullResult { Success = false, Error = "adventure_not_found" };

        if (!AdventureDesignChatService.CanUseChat(bundle))
            return new DesignSourcePullResult { Success = false, Error = "link_project_first" };

        WebView2 wv;
        try
        {
            wv = await ResolveDesignWebViewAsync(
                     adventureId,
                     selectTab: true,
                     ensureThread: false,
                     preserveCurrentPage: true)
                 ?? throw new InvalidOperationException("Design browser tab unavailable.");
        }
        catch (Exception ex)
        {
            return new DesignSourcePullResult { Success = false, Error = ex.Message };
        }

        if (wv.CoreWebView2 is not { } core)
            return new DesignSourcePullResult { Success = false, Error = "design_tab_initializing" };

        if (!AdventureDesignDomChatService.TryGetDesignConversationId(bundle, core, out _, out var pinError))
        {
            var targetUrl = DesignTabPinService.GetDesignTargetUrl(bundle)
                            ?? DesignTabPinService.GetDesignBrowseUrl(bundle);
            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                core.Navigate(targetUrl);
                await WaitForChatGptNavigationAsync(core, expectedDestination: targetUrl);
            }

            if (!AdventureDesignDomChatService.TryGetDesignConversationId(bundle, core, out _, out pinError))
            {
                return new DesignSourcePullResult
                {
                    Success = false,
                    Error = AdventureDesignDomChatService.FormatPinError(pinError),
                };
            }
        }

        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);
        var turnService = GetOrCreateTurnService(wv);

        var result = await AdventureDesignDomChatService.PullLatestSourceFilesAsync(
            core,
            bundle,
            turnService);

        if (result.Success)
        {
            AdventureStore.Save(bundle);
            _designView?.RefreshAfterGenerationJob();
        }

        return result;
    }

    private async Task<DesignExtractResult?> RunDesignExtractStepAsync(Guid adventureId, AdventureDesignStep step)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is not null)
        {
            AdventureDesignService.GoToStep(bundle, step);
            AdventureStore.Save(bundle);
        }

        var result = await RunGenerationJobForActiveAdventureAsync(
            GenerationJobId.DesignExtractStep,
            new GenerationJobContext
            {
                DesignStep = step,
                SuppressInlineGuide = true,
            });

        if (result is null)
            return null;

        return new DesignExtractResult
        {
            Success = result.Success,
            ProposalCount = result.ProposalCount,
            Error = result.Error,
        };
    }

    private async Task LaunchDesignedAdventureAsync(Guid adventureId, bool bootstrapLore, bool startPlay)
    {
        _activeAdventureId = adventureId;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            throw new InvalidOperationException("Adventure not found.");

        bundle.DesignWorkspace.LaunchBootstrapLore = bootstrapLore;
        bundle.DesignWorkspace.LaunchStartPlay = startPlay;

        var finalize = AdventureDesignFinalizeService.Finalize(bundle);
        if (!finalize.Success)
            throw new InvalidOperationException(finalize.Error ?? "Finalize failed.");

        bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            await SyncProjectInstructionsIfEnabledAsync(bundle);

        if (bootstrapLore && !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            if (bundle.Metadata.Settings.UseSectionInjection)
                await RunBootstrapSectionsAsync();
            else
                await RunBootstrapLoreAsync();
        }

        if (startPlay)
        {
            if (_appMode == AppMode.Design && _activeAdventureId == adventureId)
                await SwitchToPlaySessionAsync();
            else
                await StartPlayModeAsync(adventureId);
        }
    }
}
