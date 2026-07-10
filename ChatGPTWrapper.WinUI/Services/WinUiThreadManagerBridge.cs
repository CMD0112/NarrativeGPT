using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Thread-manager actions backed by WinUI WebView hosts and WPF project API bridge.</summary>
internal static class WinUiThreadManagerBridge
{
    public static AdventureThreadManagerActions CreateActions(Guid adventureId) =>
        new()
        {
            StartNarrativeFromSourcesAsync = () => StartNarrativeFromSourcesAsync(adventureId),
            OpenPlayHandoffWizardAsync = () => OpenPlayHandoffWizardAsync(adventureId),
            StartNewDesignThreadAsync = () => StartNewDesignThreadAsync(adventureId),
            CreateThreadSlotAsync = kind => CreateThreadSlotAsync(adventureId, kind),
            ActivateEntryAsync = (kind, entryId) => ActivateEntryAsync(adventureId, kind, entryId),
            OpenEntryAsync = (kind, entryId) => OpenEntryAsync(adventureId, kind, entryId),
            OpenProjectWorkspaceAsync = () => OpenProjectWorkspaceAsync(adventureId),
            PinTabToEntryAsync = (kind, entryId, usePicker) =>
                PinTabToEntryAsync(adventureId, kind, entryId, usePicker),
            ClearEntryPinAsync = (kind, entryId) => ClearEntryPinAsync(adventureId, kind, entryId),
            RemoveEntryAsync = entryId => RemoveEntryAsync(adventureId, entryId),
            ProbeUtilityWorkerAsync = () => ProbeUtilityWorkerAsync(adventureId),
            SetupUtilityWorkerAsync = () => SetupUtilityWorkerAsync(adventureId),
            SetupUtilityWorkerReplaceAsync = replace => SetupUtilityWorkerAsync(adventureId, replace),
            PinUtilityWorkerFromCurrentTabAsync = () => PinUtilityWorkerFromCurrentTabAsync(adventureId),
            OpenUtilityWorkerAsync = () => OpenUtilityWorkerAsync(adventureId),
        };

    private static Task OpenPlayHandoffWizardAsync(Guid adventureId) =>
        WinUiDialogHostService.ShowPlayHandoffAsync(App.CurrentMainWindow, adventureId);

    public static Task OpenProjectWorkspaceAsync(Guid adventureId) =>
        OpenProjectWorkspaceCoreAsync(adventureId);

    private static Task OpenProjectWorkspaceCoreAsync(Guid adventureId) =>
        WinUiDialogHostService.ShowProjectWorkspaceAsync(App.CurrentMainWindow, adventureId);

    private static Task StartNarrativeFromSourcesAsync(Guid adventureId) =>
        RotatePlayThreadAsync(adventureId, new PlayThreadStartRequest { Kind = PlayThreadStartKind.FreshStart });

    private static async Task StartNewDesignThreadAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            ShowInfo(
                "Link a ChatGPT Project before starting a new design thread.",
                "Start new design thread");
            return;
        }

        if (!await ConfirmAsync(
                "Start new design thread",
                "This will release the current design thread binding while keeping your linked Project.\n\n"
                + "The design thread start packet will be copied to your clipboard and your Design tab "
                + "will navigate to your Project.\n\n"
                + "Click New chat in ChatGPT, paste (Ctrl+V), and press Send. Then pin the tab to the design row."))
        {
            return;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        ProjectChatDraftService.BeginDesignDraft(bundle);
        DesignThreadRotationService.ReleaseDesignThread(bundle);
        DesignThreadRotationService.PersistRelease(bundle);

        bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var startPacket = DesignThreadRotationService.BuildStartPacket(bundle);
        if (!ClipboardCopy.TrySetText(startPacket, "StartNewDesignThread"))
        {
            ShowWarning(
                "Could not copy the design thread start packet to the clipboard.",
                "Start new design thread");
            return;
        }

        await WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var chatHost = WinUiShellHost.GetShellChatHost();
            var webView = chatHost?.GetActiveWebView() ?? chatHost?.GetFirstWebView();
            if (webView is null)
            {
                ShowInfo("No browser tab is available.", "Start new design thread");
                return;
            }

            await chatHost!.NavigateAsync(webView, new Uri(ChatGptUrls.BuildProjectUrl(gizmoId)));
            chatHost.SelectWebView(webView);
            ShowInfo(
                "Design thread start packet copied.\n\n"
                + "Click New chat in ChatGPT, paste (Ctrl+V), send, then pin the tab from Threads.",
                "Start new design thread");
        });

        WinUiShellHost.RefreshSessionChrome();
    }

    public static Task StartNewPlayThreadAsync(Guid adventureId, PlayThreadStartRequest? request) =>
        RotatePlayThreadAsync(adventureId, request ?? new PlayThreadStartRequest());

    private static async Task RotatePlayThreadAsync(Guid adventureId, PlayThreadStartRequest request)
    {
        var bundle = PlayThreadPacketService.ReloadFresh(adventureId);
        if (bundle is null)
            return;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            ShowInfo(PlayThreadRotationCopy.LinkProjectFirstMessage, PlayThreadRotationCopy.NarrativeFromSourcesTitle);
            return;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var isHandoff = request.Kind == PlayThreadStartKind.Handoff;
        var hasPlayHistory = PlayTurnScopeService.GetPacketAcceptedTurns(bundle).Count > 0;
        var confirmBody = isHandoff
            ? PlayThreadRotationCopy.HandoffConfirmBody
            : PlayThreadRotationCopy.NarrativeFromSourcesConfirmBody(hasPlayHistory);
        var dialogTitle = isHandoff
            ? PlayThreadRotationCopy.HandoffToNewChatTitle
            : PlayThreadRotationCopy.NarrativeFromSourcesTitle;

        if (!request.SkipConfirmation
            && !await ConfirmAsync(dialogTitle, confirmBody))
        {
            return;
        }

        var clipboardPacket = PlayHandoffService.PrepareClipboardPacket(bundle, request, request.Kind);
        ProjectChatDraftService.BeginPlayDraft(bundle);
        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);
        PlayContextSessionCache.Invalidate(adventureId);

        if (!ClipboardCopy.TrySetText(clipboardPacket, isHandoff ? "PlayHandoff" : "StartNarrativeFromSources"))
        {
            ShowWarning("Could not copy the start packet to the clipboard.", dialogTitle);
            return;
        }

        await WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var chatHost = WinUiShellHost.GetShellChatHost();
            var webView = chatHost?.GetActiveWebView() ?? chatHost?.GetFirstWebView();
            if (webView is null)
            {
                ShowInfo("No browser tab is available.\n\nThe packet is still on your clipboard.", dialogTitle);
                return;
            }

            await chatHost!.NavigateAsync(webView, new Uri(ChatGptUrls.BuildProjectUrl(gizmoId)));
            chatHost.SelectWebView(webView);

            var reloaded = AdventureStore.Load(adventureId);
            if (reloaded is not null)
            {
                var tab = chatHost.FindTabForWebView(webView);
                WinUiPlayTabPinExtensions.PinTabToEntry(reloaded, GetActivePlayEntryId(reloaded), webView, tab);
            }

            ShowInfo(
                "Start packet copied.\n\nClick New chat in ChatGPT, paste (Ctrl+V), send, then pin the tab from Threads.",
                dialogTitle);
        });

        WinUiShellHost.RefreshSessionChrome();
    }

    private static Guid GetActivePlayEntryId(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        return (AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)
                ?? AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play)).Id;
    }

    private static async Task<Guid?> CreateThreadSlotAsync(Guid adventureId, AdventureThreadKind kind)
    {
        if (kind == AdventureThreadKind.UtilityWorker)
            return null;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
        {
            ShowInfo(
                "Link a ChatGPT Project before creating thread slots.",
                ThreadManagerCopy.DialogTitle);
            return null;
        }

        var (promptOk, label) = await PromptForLabelAsync(
            ThreadManagerCopy.NewThreadSlotButton.TrimEnd('…'),
            ThreadManagerCopy.NewThreadSlotPrompt(kind),
            ThreadManagerCopy.NewThreadSlotDefaultLabel);
        if (!promptOk)
            return null;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, kind, label);
        AdventureThreadRegistryService.SetActivePin(
            bundle,
            entry.Id,
            notifyPlayThreadChanged: kind == AdventureThreadKind.Play);
        AdventureThreadRegistryService.Persist(bundle);
        PlayContextSessionCache.Invalidate(adventureId);

        await NavigateToLinkedProjectForNewThreadAsync(adventureId, kind, entry);
        WinUiShellHost.RefreshSessionChrome();
        return entry.Id;
    }

    private static async Task NavigateToLinkedProjectForNewThreadAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        AdventureThreadEntry entry)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);

        await WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var chatHost = kind == AdventureThreadKind.Design
                ? WinUiShellHost.GetShellChatHost()
                : WinUiShellHost.GetShellChatHost();
            var webView = chatHost?.GetActiveWebView() ?? chatHost?.GetFirstWebView();
            if (webView is null)
            {
                ShowInfo(
                    "Thread slot created. Open a ChatGPT browser tab, create a New chat in your Project, "
                    + "then pin it to the new row.",
                    ThreadManagerCopy.DialogTitle);
                return;
            }

            await chatHost!.NavigateAsync(webView, new Uri(projectUrl));
            chatHost.SelectWebView(webView);
            ShowInfo(
                $"Thread slot \"{entry.Label}\" is active.\n\n"
                + "Click New chat in ChatGPT, open that chat in a browser tab, then use "
                + "\"Pin current tab to selected\" or \"Pick browser tab…\" on this row.",
                ThreadManagerCopy.DialogTitle);
        });
    }

    private static Task ActivateEntryAsync(Guid adventureId, AdventureThreadKind kind, Guid entryId) =>
        NavigateRegistryEntryAsync(adventureId, kind, entryId, activate: true);

    private static Task OpenEntryAsync(Guid adventureId, AdventureThreadKind kind, Guid entryId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId);
        if (entry is null)
            return Task.CompletedTask;

        if (entry.Status == AdventureThreadStatus.Archived)
            return NavigateRegistryEntryAsync(adventureId, kind, entryId, activate: false);

        return ActivateEntryAsync(adventureId, kind, entryId);
    }

    private static Task NavigateRegistryEntryAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        Guid entryId,
        bool activate)
    {
        return WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return;

            AdventureThreadRegistryService.EnsureMigrated(bundle);
            var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId);
            if (entry is null || entry.Status == AdventureThreadStatus.Archived && activate)
                return;

            if (activate && !AdventureThreadRegistryService.IsActiveEntry(bundle, entryId))
            {
                AdventureThreadRegistryService.SetActivePin(bundle, entryId);
                AdventureThreadRegistryService.Persist(bundle);
            }

            var url = AdventureThreadRegistryService.GetEntryTargetUrl(bundle, entry);
            if (string.IsNullOrWhiteSpace(url))
            {
                ShowInfo(
                    "No navigation target for this thread. Pin a tab or bind a conversation first.",
                    ThreadManagerCopy.DialogTitle);
                return;
            }

            var chatHost = ResolveChatHost(kind);
            var webView = ResolveWebViewForKind(chatHost, bundle, kind, entry);
            if (webView is null)
            {
                ShowInfo("No browser tab is available.", ThreadManagerCopy.DialogTitle);
                return;
            }

            await WinUiShellHost.Session.EnsurePageHostAsync(webView);
            if (activate || kind == AdventureThreadKind.UtilityWorker)
                webView.Source = new Uri(url);
            else
                webView.Source = new Uri(url);

            chatHost?.SelectWebView(webView);
            PlayContextSessionCache.Invalidate(adventureId);
            WinUiShellHost.RefreshSessionChrome();
        });
    }

    private static ChatTabHost? ResolveChatHost(AdventureThreadKind kind) =>
        WinUiShellHost.GetShellChatHost();

    private static WebView2? ResolveWebViewForKind(
        ChatTabHost? chatHost,
        AdventureBundle bundle,
        AdventureThreadKind kind,
        AdventureThreadEntry entry)
    {
        if (chatHost is null)
            return null;

        if (chatHost.FindWebViewByPinKey(entry.PinnedTabKey) is { } pinned)
            return pinned;

        return chatHost.GetActiveWebView() ?? chatHost.GetFirstWebView();
    }

    private static Task PinTabToEntryAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        Guid entryId,
        bool usePicker)
    {
        return WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var chatHost = ResolveChatHost(kind);
            if (chatHost is null)
            {
                ShowInfo("Open a play or design session with a browser tab first.", ThreadManagerCopy.DialogTitle);
                return;
            }

            WebView2? webView;
            if (usePicker)
            {
                if (WinUiShellHost.XamlRoot is null)
                    return;

                webView = await chatHost.PickTabAsync(WinUiShellHost.XamlRoot);
                if (webView is null)
                    return;
            }
            else
            {
                webView = chatHost.GetActiveWebView();
                if (webView is null)
                {
                    ShowInfo(
                        "Select a ChatGPT browser tab first, or use Pick browser tab…",
                        ThreadManagerCopy.DialogTitle);
                    return;
                }
            }

            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return;

            var tab = chatHost.FindTabForWebView(webView);
            try
            {
                if (kind == AdventureThreadKind.Play)
                    WinUiPlayTabPinExtensions.PinTabToEntry(bundle, entryId, webView, tab);
                else if (kind == AdventureThreadKind.Design)
                    WinUiDesignTabPin.PinTabToEntry(bundle, entryId, webView, tab);

                chatHost.SelectWebView(webView);
                WinUiShellHost.Session.PinActiveTab(webView);
                WinUiShellHost.RefreshSessionChrome();
            }
            catch (Exception ex)
            {
                var message = ex.Message.Contains("play thread", StringComparison.OrdinalIgnoreCase)
                    ? "This conversation is the play thread. Create a New chat in the Project for design."
                    : ex.Message;
                ShowWarning(message, "Pin tab");
            }
        });
    }

    private static Task ClearEntryPinAsync(Guid adventureId, AdventureThreadKind kind, Guid entryId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.ClearEntryPin(bundle, entryId);

        if (kind == AdventureThreadKind.Play
            && AdventureThreadRegistryService.IsActiveEntry(bundle, entryId))
        {
            bundle.Metadata.PinnedPlayTabKey = null;
            bundle.Metadata.PinnedPlayTabTitle = null;
            bundle.Metadata.PinnedPlayTabUrl = null;
        }
        else if (kind == AdventureThreadKind.Design
                 && AdventureThreadRegistryService.IsActiveEntry(bundle, entryId))
        {
            AdventureThreadRegistryService.ClearLegacyDesignBindingFields(bundle.Metadata);
            AdventureThreadRegistryService.SyncActiveDesignUtilitySession(bundle);
        }

        AdventureThreadRegistryService.Persist(bundle);
        PlayContextSessionCache.Invalidate(adventureId);
        WinUiShellHost.RefreshSessionChrome();
        return Task.CompletedTask;
    }

    private static Task RemoveEntryAsync(Guid adventureId, Guid entryId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.RemoveEntry(bundle, entryId);
        AdventureThreadRegistryService.Persist(bundle);
        PlayContextSessionCache.Invalidate(adventureId);
        WinUiShellHost.RefreshSessionChrome();
        return Task.CompletedTask;
    }

    private static Task ProbeUtilityWorkerAsync(Guid adventureId)
    {
        var session = WinUiShellHost.Session;
        if (session.CurrentBundle?.Metadata.Id != adventureId)
            return Task.CompletedTask;

        return WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var ok = await session.UtilityWorker.ProbeAsync();
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return;

            var status = UtilityWorkerSetupService.Evaluate(bundle);
            ShowInfo(
                ok
                    ? $"{status.ConnectionBannerText}\n\n{status.CapabilityDetail}"
                    : UtilityWorkerSetupCopy.VerifyFailedStatus(bundle.Metadata.UtilityWorkerCapabilities?.LastProbeError),
                UtilityWorkerSetupCopy.DialogTitle);
            WinUiShellHost.RefreshSessionChrome();
        });
    }

    private static Task SetupUtilityWorkerAsync(Guid adventureId, bool replaceExisting = false)
    {
        return WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return;

            AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
            var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
            if (string.IsNullOrWhiteSpace(gizmoId))
            {
                ShowInfo(UtilityWorkerSetupCopy.LinkProjectFirstMessage, UtilityWorkerSetupCopy.DialogTitle);
                return;
            }

            if (!replaceExisting)
            {
                var setupStatus = UtilityWorkerSetupService.Evaluate(bundle);
                if (setupStatus.WorkerPinned && setupStatus.ConnectionGreen)
                    return;
            }

            gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
            ProjectChatDraftService.BeginUtilityDraft(bundle);

            var chatHost = WinUiShellHost.GetShellChatHost();
            WebView2? webView = chatHost?.FindWebViewByPinKey(
                AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.UtilityWorker)?.PinnedTabKey);

            if (webView is null)
            {
                await chatHost!.AddTabAsync("Utility worker");
                webView = chatHost.GetActiveWebView();
            }

            if (webView is null)
            {
                ShowWarning(UtilityWorkerSetupCopy.SetupFailedMessage, UtilityWorkerSetupCopy.DialogTitle);
                return;
            }

            await chatHost!.NavigateAsync(webView, new Uri(ChatGptUrls.BuildProjectUrl(gizmoId)));
            chatHost.SelectWebView(webView);

            ShowInfo(
                UtilityWorkerSetupCopy.ManualCreateTimeoutMessage,
                UtilityWorkerSetupCopy.DialogTitle);
        });
    }

    private static Task PinUtilityWorkerFromCurrentTabAsync(Guid adventureId) =>
        WinUiShellHost.RunOnUiThreadAsync(() =>
        {
            var bundle = AdventureStore.Load(adventureId);
            var chatHost = WinUiShellHost.GetShellChatHost();
            var webView = chatHost?.GetActiveWebView();
            if (bundle is null || webView is null)
            {
                ShowInfo(
                    "Select a ChatGPT browser tab first, then pin it here.",
                    UtilityWorkerSetupCopy.DialogTitle);
                return Task.CompletedTask;
            }

            if (!UtilityWorkerSetupService.Evaluate(bundle).ProjectLinked)
            {
                ShowInfo(UtilityWorkerSetupCopy.LinkProjectFirstMessage, UtilityWorkerSetupCopy.DialogTitle);
                return Task.CompletedTask;
            }

            var tab = chatHost!.FindTabForWebView(webView);
            var tabKey = tab?.Tag as string;
            var tabTitle = tab?.Header?.ToString();
            if (!WinUiUtilityWorkerPin.BindFromWebView(bundle, webView, tabKey, tabTitle))
            {
                ShowWarning(UtilityWorkerSetupCopy.PinCurrentTabFailedMessage, UtilityWorkerSetupCopy.DialogTitle);
                return Task.CompletedTask;
            }

            ShowInfo(UtilityWorkerSetupCopy.SetupPartialMessage(null), UtilityWorkerSetupCopy.DialogTitle);
            WinUiShellHost.RefreshSessionChrome();
            return Task.CompletedTask;
        });

    private static Task OpenUtilityWorkerAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.UtilityWorker);
        if (entry is null)
        {
            ShowInfo("Set up the utility worker first.", UtilityWorkerSetupCopy.DialogTitle);
            return Task.CompletedTask;
        }

        return NavigateRegistryEntryAsync(adventureId, AdventureThreadKind.UtilityWorker, entry.Id, activate: true);
    }

    private static void ShowInfo(string message, string title) =>
        _ = WinUiShellHost.RunOnUiThreadAsync(() =>
            WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, title, message));

    private static void ShowWarning(string message, string title) =>
        _ = WinUiShellHost.RunOnUiThreadAsync(() =>
            WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, title, message));

    private static Task<bool> ConfirmAsync(string title, string message) =>
        WinUiShellHost.RunOnUiThreadAsync(() =>
            WinUiDialogHelper.ConfirmAsync(App.CurrentMainWindow, title, message));

    private static async Task<(bool Success, string Label)> PromptForLabelAsync(
        string title,
        string prompt,
        string defaultText)
    {
        var (success, result) = await WinUiDialogHostService.PromptAsync(
            App.CurrentMainWindow,
            title,
            prompt,
            defaultText);
        return (success, result);
    }
}
