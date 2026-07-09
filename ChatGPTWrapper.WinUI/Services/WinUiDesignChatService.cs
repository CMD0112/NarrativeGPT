using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Design-thread DOM send and extract jobs for WinUI (CMD-555).</summary>
internal static class WinUiDesignChatService
{
    private static readonly Dictionary<object, AdventureTurnService> TurnServices = new();
    private static readonly Dictionary<object, ChatGptAdventureBridgeInjection> Bridges = new();
    private static ChatGptConversationSendService? _conversationSend;
    private static ChatGptProjectApiService? _projectApi;
    private static int _apiWarmDepth;

    public static async Task<DesignChatSendResult> SendStepBriefAsync(
        Guid adventureId,
        string userText,
        WinUiPlaySessionService session)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignChatSendResult { Success = false, Error = "adventure_not_found" };

        if (!AdventureProjectBindingService.HasLinkedProject(bundle))
            return new DesignChatSendResult { Success = false, Error = "link_project_first" };

        var step = bundle.DesignWorkspace.CurrentStep;
        var prompt = AdventureDesignChatService.ResolveOutgoingMessage(bundle, step, userText);
        AdventureDesignChatService.RecordUserMessage(bundle, step, userText);
        AdventureStore.Save(bundle);

        var result = await SendDomChatAsync(adventureId, prompt, step, session);
        if (result.Success)
            session.ReloadBundle(adventureId);

        return result;
    }

    public static async Task<DesignChatSendResult> SendSourceFilePromptAsync(
        Guid adventureId,
        string relativePath,
        WinUiPlaySessionService session)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignChatSendResult { Success = false, Error = "adventure_not_found" };

        if (!AdventureProjectBindingService.HasLinkedProject(bundle))
            return new DesignChatSendResult { Success = false, Error = "link_project_first" };

        string prompt;
        try
        {
            prompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, relativePath);
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

        var result = await SendDomChatAsync(adventureId, prompt, AdventureDesignStep.Sources, session);
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

            session.ReloadBundle(adventureId);
        }

        return result;
    }

    public static async Task<DesignExtractResult?> ExtractStepAsync(
        Guid adventureId,
        AdventureDesignStep step,
        WinUiPlaySessionService session)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        AdventureDesignService.GoToStep(bundle, step);
        AdventureStore.Save(bundle);

        if (!DesignTabPinService.PreferPinnedDesignWebView(bundle))
        {
            return new DesignExtractResult
            {
                Success = false,
                Error = DesignTabPinService.DesignPinRequiredError,
            };
        }

        var webView = await WinUiDesignTabRegistry.ResolveAsync(session, bundle, selectTab: true);
        if (webView?.CoreWebView2 is not { } designCore)
        {
            return new DesignExtractResult
            {
                Success = false,
                Error = "Design browser tab unavailable — pin a design thread first.",
            };
        }

        await EnsureApiAsync(adventureId);
        var designTurn = GetTurnService(session, webView);
        var playWebView = session.PlayWebView;
        var playCore = playWebView?.CoreWebView2;
        var playTurn = playCore is not null && playWebView is not null
            ? GetTurnService(session, playWebView)
            : null;

        var result = await WinUiDesignWebViewBridge.ExtractStepForWinUiAsync(
            adventureId,
            step,
            designCore,
            playCore,
            _projectApi!,
            _conversationSend!,
            designTurn,
            playTurn);

        session.ReloadBundle(adventureId);
        return result;
    }

    private static async Task<DesignChatSendResult> SendDomChatAsync(
        Guid adventureId,
        string promptText,
        AdventureDesignStep recordStep,
        WinUiPlaySessionService session)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return new DesignChatSendResult { Success = false, Error = "adventure_not_found" };

        var webView = await WinUiDesignTabRegistry.ResolveAsync(session, bundle, selectTab: true);
        var core = webView?.CoreWebView2;
        if (core is null)
            return new DesignChatSendResult { Success = false, Error = "design_tab_initializing" };

        if (!WinUiDesignWebViewBridge.TryGetDesignConversationId(bundle, core, out _, out _))
        {
            var targetUrl = DesignTabPinService.GetDesignTargetUrl(bundle)
                            ?? DesignTabPinService.GetDesignBrowseUrl(bundle);
            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                WinUiChatNavigation.Navigate(core, targetUrl);
                await WinUiChatNavigation.WaitForNavigationAsync(core, targetUrl);
            }

            bundle = AdventureStore.Load(adventureId)!;
            if (!WinUiDesignWebViewBridge.TryGetDesignConversationId(bundle, core, out _, out var pinError))
            {
                return new DesignChatSendResult
                {
                    Success = false,
                    Error = AdventureDesignDomChatService.FormatPinError(pinError),
                };
            }
        }

        await EnsureApiAsync(adventureId);
        var turnService = GetTurnService(session, webView!);
        var result = await WinUiDesignWebViewBridge.SendDesignPromptAsync(core, bundle, turnService, promptText);

        if (result.Success)
        {
            bundle = AdventureStore.Load(adventureId)!;
            AdventureDesignChatService.RecordAssistantMessage(
                bundle,
                recordStep,
                result.AssistantText ?? "(sent — check design thread for reply)");
            AdventureStore.Save(bundle);
        }

        return result;
    }

    private static async Task EnsureApiAsync(Guid adventureId)
    {
        if (_conversationSend is not null && _projectApi is not null)
            return;

        if (Interlocked.Increment(ref _apiWarmDepth) > 1)
        {
            Interlocked.Decrement(ref _apiWarmDepth);
            while ((_conversationSend is null || _projectApi is null) && Volatile.Read(ref _apiWarmDepth) > 0)
                await Task.Delay(50);
            return;
        }

        try
        {
            await WpfStaProjectHostBridge.InvokeAsync(async host =>
            {
                await host.EnsureReadyAsync(adventureId, cancellationToken: CancellationToken.None);
                _projectApi = host.Api;
                _conversationSend = new ChatGptConversationSendService(host.Api.Bridge);
            });
        }
        finally
        {
            Interlocked.Decrement(ref _apiWarmDepth);
        }
    }

    private static AdventureTurnService GetTurnService(WinUiPlaySessionService session, WebView2 webView)
    {
        if (TurnServices.TryGetValue(webView, out var existing))
            return existing;

        var core = webView.CoreWebView2
                   ?? throw new InvalidOperationException("CoreWebView2 is not ready.");
        var bridge = Bridges.GetValueOrDefault(webView)
                     ?? RegisterBridge(session, webView, core);
        var turnService = new AdventureTurnService(bridge);
        if (_conversationSend is not null)
            turnService.SetConversationSendService(_conversationSend);
        TurnServices[webView] = turnService;
        return turnService;
    }

    private static ChatGptAdventureBridgeInjection RegisterBridge(
        WinUiPlaySessionService session,
        WebView2 webView,
        object core)
    {
        var bridge = ChatGptAdventureBridgeInjection.CreateForCore(core);
        bridge.Register();
        WinUiTurnInvalidationBridge.Wire(bridge, session);
        Bridges[webView] = bridge;
        return bridge;
    }

    public static string FormatSendStatus(DesignChatSendResult result, string? successLabel = null)
    {
        if (result.Success)
        {
            if (string.Equals(result.Error, "sent_no_capture", StringComparison.OrdinalIgnoreCase))
                return "Prompt sent — check the design thread in ChatGPT for the reply.";

            return !string.IsNullOrWhiteSpace(successLabel)
                ? successLabel
                : "Sent — check the design thread in ChatGPT.";
        }

        return result.Error switch
        {
            null or "" => "Send failed.",
            "link_project_first" => "Link a ChatGPT Project first.",
            "design_tab_initializing" => "Design browser tab is still loading — try again.",
            _ => AdventureDesignDomChatService.FormatSendError(result.Error),
        };
    }
}
