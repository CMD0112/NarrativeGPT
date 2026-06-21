using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

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
            ActivateEntryAsync = (kind, entryId) => ActivateRegistryThreadEntryAsync(adventureId, kind, entryId),
            OpenEntryAsync = (kind, entryId) => OpenRegistryThreadEntryAsync(adventureId, kind, entryId),
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
                await WaitForChatGptNavigationAsync(core);
            }
            else if (!activate && !PlayTabPinService.IsOnPlayTarget(core.Source, bundle))
            {
                core.Navigate(url);
                await WaitForChatGptNavigationAsync(core);
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
                await WaitForChatGptNavigationAsync(core);
            }
        }
    }
}
