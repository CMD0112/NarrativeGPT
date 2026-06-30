using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Views;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private void OpenThreadManagerDialog(Guid adventureId, AdventureThreadKind initialKind = AdventureThreadKind.Play)
    {
        var actions = new AdventureThreadManagerActions
        {
            StartNarrativeFromSourcesAsync = () => StartNarrativeFromSourcesAsync(adventureId),
            OpenPlayHandoffWizardAsync = () => OpenPlayHandoffWizardForAdventureAsync(adventureId),
            StartNewDesignThreadAsync = () => StartNewDesignThreadAsync(adventureId),
            CreateThreadSlotAsync = kind => CreateThreadSlotAsync(adventureId, kind),
            ActivateEntryAsync = (kind, entryId) => ActivateRegistryThreadEntryAsync(adventureId, kind, entryId),
            OpenEntryAsync = (kind, entryId) => OpenRegistryThreadEntryAsync(adventureId, kind, entryId),
            OpenProjectWorkspaceAsync = () => OpenProjectWorkspaceAsync(adventureId),
            PinTabToEntryAsync = (kind, entryId, usePicker) => PinTabToRegistryEntryAsync(adventureId, kind, entryId, usePicker),
            ClearEntryPinAsync = (kind, entryId) => ClearRegistryEntryPinAsync(adventureId, kind, entryId),
            RemoveEntryAsync = entryId => RemoveRegistryThreadEntryAsync(adventureId, entryId),
            ProbeUtilityWorkerAsync = () => ProbeUtilityWorkerCapabilitiesAsync(adventureId),
            SetupUtilityWorkerAsync = () => SetupUtilityWorkerAsync(adventureId),
            SetupUtilityWorkerReplaceAsync = replace => SetupUtilityWorkerAsync(adventureId, replace),
            PinUtilityWorkerFromCurrentTabAsync = () => PinCurrentTabAsUtilityWorkerAsync(adventureId),
            OpenUtilityWorkerAsync = () => OpenUtilityWorkerChatAsync(adventureId),
        };

        var dlg = new AdventureThreadManagerDialog(adventureId, actions, initialKind) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        RefreshThreadManagerHostUi(adventureId);
    }

    private Task OpenPlayHandoffWizardForAdventureAsync(Guid adventureId)
    {
        if (_activeAdventureId == adventureId && _playView is not null)
            _playView.OpenPlayHandoffWizard();
        return Task.CompletedTask;
    }

    private void RefreshThreadManagerHostUi(Guid adventureId)
    {
        if (_activeAdventureId != adventureId)
            return;

        if (_appMode == AppMode.Play)
        {
            ReloadPlayAdventure(adventureId);
            _playView?.UpdateJobButtonStates();
            UpdatePlayLinkStatus();
        }
        else if (_appMode == AppMode.Design)
        {
            UpdateDesignLinkStatus();
        }
    }

    private async Task ActivateRegistryThreadEntryAsync(Guid adventureId, AdventureThreadKind kind, Guid entryId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId);
        if (entry is null || entry.Status == AdventureThreadStatus.Archived)
            return;

        if (!AdventureThreadRegistryService.IsActiveEntry(bundle, entryId))
        {
            AdventureThreadRegistryService.SetActivePin(bundle, entryId);
            AdventureThreadRegistryService.Persist(bundle);
        }

        PlayContextSessionCache.Invalidate(adventureId);
        await NavigateRegistryThreadEntryAsync(adventureId, kind, entry);
    }

    private async Task OpenRegistryThreadEntryAsync(Guid adventureId, AdventureThreadKind kind, Guid entryId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId);
        if (entry is null)
            return;

        if (entry.Status == AdventureThreadStatus.Archived)
        {
            await NavigateRegistryThreadEntryAsync(adventureId, kind, entry, activate: false);
            return;
        }

        await ActivateRegistryThreadEntryAsync(adventureId, kind, entryId);
    }

    private async Task NavigateRegistryThreadEntryAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        AdventureThreadEntry entry,
        bool activate = true)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var url = AdventureThreadRegistryService.GetEntryTargetUrl(bundle, entry);
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                this,
                "No navigation target for this thread. Pin a tab or bind a conversation first.",
                "Manage threads",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await EnsureChatWebViewEnvironmentReadyAsync();

        if (kind == AdventureThreadKind.Play)
        {
            var wv = ResolvePlayWebView(bundle) ?? _playWebView ?? GetActiveWebView();
            if (wv is null)
            {
                MessageBox.Show(this, "No browser tab is available.", "Manage threads", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
                await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

            if (wv.CoreWebView2 is not { } core)
                return;

            GetOrRegisterAdventureBridge(wv);
            WireProjectServices(wv);
            SelectTabForWebView(wv);
            _playWebView = wv;

            if (activate
                && AdventureNavigationService.ShouldNavigateToPlayTarget(core.Source, bundle, url))
            {
                core.Navigate(url);
                await WaitForChatGptNavigationAsync(core, expectedDestination: url);
            }
            else if (!activate && !PlayTabPinService.IsOnPlayTarget(core.Source, bundle))
            {
                core.Navigate(url);
                await WaitForChatGptNavigationAsync(core, expectedDestination: url);
            }

            return;
        }

        if (kind == AdventureThreadKind.Design)
        {
            var wv = _designWebView ?? ResolveDesignWebView(bundle) ?? GetActiveWebView();
            if (wv is null)
            {
                MessageBox.Show(this, "No browser tab is available.", "Manage threads", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
                await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

            if (wv.CoreWebView2 is not { } core)
                return;

            GetOrRegisterAdventureBridge(wv);
            WireProjectServices(wv);
            SelectTabForWebView(wv);
            _designWebView = wv;

            if (AdventureNavigationService.ShouldNavigateToDesignTarget(core.Source, bundle, url))
            {
                core.Navigate(url);
                await WaitForChatGptNavigationAsync(core, expectedDestination: url);
            }

            return;
        }

        if (kind == AdventureThreadKind.UtilityWorker)
        {
            var wv = _utilityWorkerWebView
                     ?? UtilityWorkerPinService.TryFindWebViewForWorkerSession(ChatTabs, bundle)
                     ?? ThreadWebViewResolver.TryFindExisting(ChatTabs, bundle, AdventureThreadKind.UtilityWorker)
                     ?? GetActiveWebView();
            if (wv is null)
            {
                MessageBox.Show(this, "No browser tab is available.", "Manage threads", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
                await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

            if (wv.CoreWebView2 is not { } workerCore)
                return;

            GetOrRegisterAdventureBridge(wv);
            WireProjectServices(wv);
            SelectTabForWebView(wv);
            _utilityWorkerWebView = wv;

            workerCore.Navigate(url);
            await WaitForChatGptNavigationAsync(workerCore, expectedDestination: url);
        }
    }

    private async Task OpenUtilityWorkerChatAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.UtilityWorker);
        if (entry is null)
        {
            MessageBox.Show(
                this,
                "Set up the utility worker first.",
                UtilityWorkerSetupCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await NavigateRegistryThreadEntryAsync(adventureId, AdventureThreadKind.UtilityWorker, entry);
    }

    private async Task<Guid?> CreateThreadSlotAsync(Guid adventureId, AdventureThreadKind kind)
    {
        if (kind == AdventureThreadKind.UtilityWorker)
            return null;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
        {
            MessageBox.Show(
                this,
                "Link a ChatGPT Project before creating thread slots.",
                ThreadManagerCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        if (!TextPromptDialog.TryPrompt(
                this,
                ThreadManagerCopy.NewThreadSlotButton.TrimEnd('…'),
                ThreadManagerCopy.NewThreadSlotPrompt(kind),
                ThreadManagerCopy.NewThreadSlotDefaultLabel,
                out var label))
        {
            return null;
        }

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, kind, label);
        AdventureThreadRegistryService.SetActivePin(
            bundle,
            entry.Id,
            notifyPlayThreadChanged: kind == AdventureThreadKind.Play);
        AdventureThreadRegistryService.Persist(bundle);
        PlayContextSessionCache.Invalidate(adventureId);

        await NavigateToLinkedProjectForNewThreadAsync(adventureId, kind, entry);
        RefreshThreadManagerHostUi(adventureId);
        return entry.Id;
    }

    private async Task NavigateToLinkedProjectForNewThreadAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        AdventureThreadEntry entry)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        await EnsureChatWebViewEnvironmentReadyAsync();

        var wv = kind switch
        {
            AdventureThreadKind.Play => ResolvePlayWebView(bundle) ?? _playWebView ?? GetActiveWebView(),
            AdventureThreadKind.Design => _designWebView ?? ResolveDesignWebView(bundle) ?? GetActiveWebView(),
            _ => GetActiveWebView(),
        };

        if (wv is null)
        {
            MessageBox.Show(
                this,
                "Thread slot created. Open a ChatGPT browser tab, create a New chat in your Project, "
                + "then pin it to the new row.",
                ThreadManagerCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        if (wv.CoreWebView2 is not { } core)
            return;

        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);
        SelectTabForWebView(wv);

        if (kind == AdventureThreadKind.Play)
            _playWebView = wv;
        else if (kind == AdventureThreadKind.Design)
            _designWebView = wv;

        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            core.Navigate(projectUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: projectUrl);
        }

        MessageBox.Show(
            this,
            $"Thread slot \"{entry.Label}\" is active.\n\n"
            + "Click New chat in ChatGPT, open that chat in a browser tab, then use "
            + "\"Pin current tab to selected\" or \"Pick browser tab…\" on this row.",
            ThreadManagerCopy.DialogTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task PinTabToRegistryEntryAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        Guid entryId,
        bool usePicker)
    {
        await EnsureChatWebViewEnvironmentReadyAsync();

        WebView2? webView = null;
        if (usePicker)
        {
            var tabs = ThreadTabBindingService.ListWebViewTabs(ChatTabs);
            if (tabs.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No ChatGPT browser tabs are open.",
                    ThreadManagerCopy.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var picker = new BrowserTabPickerDialog(tabs) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedWebView is null)
                return;

            webView = picker.SelectedWebView;
        }
        else
        {
            webView = GetActiveWebView();
            if (webView is null)
            {
                MessageBox.Show(
                    this,
                    "Select a ChatGPT browser tab first, or use Pick browser tab…",
                    ThreadManagerCopy.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        try
        {
            if (kind == AdventureThreadKind.Play)
            {
                PlayTabPinService.PinTabToEntry(bundle, entryId, webView, ChatTabs, setActive: true);
                _playWebView = webView;
            }
            else if (kind == AdventureThreadKind.Design)
            {
                DesignTabPinService.PinDesignTabToEntry(bundle, entryId, webView, ChatTabs, setActive: true);
                _designWebView = webView;
            }

            GetOrRegisterAdventureBridge(webView);
            WireProjectServices(webView);
            SelectTabForWebView(webView);
            RefreshThreadManagerHostUi(adventureId);
        }
        catch (Exception ex)
        {
            var message = ex.Message.Contains("play thread", StringComparison.OrdinalIgnoreCase)
                ? "This conversation is the play thread. Create a New chat in the Project for design."
                : ex.Message;
            MessageBox.Show(this, message, "Pin tab", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Task ClearRegistryEntryPinAsync(Guid adventureId, AdventureThreadKind kind, Guid entryId)
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
        RefreshThreadManagerHostUi(adventureId);
        return Task.CompletedTask;
    }

    private Task RemoveRegistryThreadEntryAsync(Guid adventureId, Guid entryId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.RemoveEntry(bundle, entryId);
        AdventureThreadRegistryService.Persist(bundle);
        PlayContextSessionCache.Invalidate(adventureId);
        RefreshThreadManagerHostUi(adventureId);
        return Task.CompletedTask;
    }

    private async Task PinCurrentTabForKindAsync(Guid adventureId, AdventureThreadKind kind)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || GetActiveWebView() is not { } active)
        {
            MessageBox.Show(
                this,
                "Select a ChatGPT browser tab first, then pin it here.",
                "Threads",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (kind == AdventureThreadKind.Play)
            {
                PlayTabPinService.PinTab(bundle, active, ChatTabs);
                _playWebView = active;
            }
            else if (kind == AdventureThreadKind.Design)
            {
                DesignTabPinService.PinDesignTab(bundle, active, ChatTabs);
                _designWebView = active;
            }
            else if (kind == AdventureThreadKind.UtilityWorker)
            {
                await PinCurrentTabAsUtilityWorkerAsync(adventureId);
            }

            SelectTabForWebView(active);
            RefreshThreadManagerHostUi(adventureId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Pin tab", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await Task.CompletedTask;
    }
}
