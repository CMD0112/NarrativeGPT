using System.Windows;
using System.Windows.Input;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private IReadOnlyList<ShellShortcutDefinition> _effectiveShellShortcuts = ShellShortcutCatalog.Defaults;

    private void InitializeShellShortcuts()
    {
        RefreshEffectiveShellShortcuts();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        ApplyShellShortcutMenuGestures();
    }

    private void RefreshEffectiveShellShortcuts()
    {
        _effectiveShellShortcuts = ShellShortcutCatalog.ResolveEffectiveShortcuts(_chrome.ShellShortcutOverrides);
    }

    private void ApplyShellShortcutMenuGestures()
    {
        if (FormatMenuItem is not null)
            FormatMenuItem.InputGestureText = GestureFor(ShellShortcutCatalog.Format);

        if (PreferencesMenuItem is not null)
            PreferencesMenuItem.InputGestureText = GestureFor(ShellShortcutCatalog.Preferences);

        if (PlaySettingsMenuItem is not null)
            PlaySettingsMenuItem.InputGestureText = GestureFor(ShellShortcutCatalog.PlaySettings);

        if (FocusChatMenuItem is not null)
            FocusChatMenuItem.InputGestureText = GestureFor(ShellShortcutCatalog.FocusChat);

        if (KeyboardShortcutsMenuItem is not null)
            KeyboardShortcutsMenuItem.InputGestureText = GestureFor(ShellShortcutCatalog.ShowShortcuts);
    }

    private string GestureFor(string shortcutId)
    {
        var shortcut = _effectiveShellShortcuts.FirstOrDefault(candidate => candidate.Id == shortcutId);
        return shortcut?.GestureText ?? string.Empty;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ShellShortcutCatalog.TryMatch(e, _effectiveShellShortcuts, out var shortcut))
            return;

        if (!CanExecuteShellShortcut(shortcut))
            return;

        ExecuteShellShortcut(shortcut.Id);
        e.Handled = true;
    }

    private bool CanExecuteShellShortcut(ShellShortcutDefinition shortcut)
    {
        if (_appMode == AppMode.Adventures && shortcut.Scope is ShellShortcutScope.Play)
            return false;

        if (shortcut.Scope == ShellShortcutScope.Play && _appMode != AppMode.Play)
            return false;

        if (shortcut.Scope == ShellShortcutScope.AdventureSession
            && (_activeAdventureId is null || _appMode is not (AppMode.Play or AppMode.Design)))
        {
            return false;
        }

        if (!shortcut.AllowWhenWebViewFocused && IsActiveWebViewKeyboardFocused())
            return false;

        return true;
    }

    private bool IsActiveWebViewKeyboardFocused()
    {
        if (GetActiveWebView() is not { } webView)
            return false;

        return webView.IsKeyboardFocusWithin;
    }

    private void ExecuteShellShortcut(string shortcutId)
    {
        switch (shortcutId)
        {
            case ShellShortcutCatalog.Preferences:
                OpenPreferencesHub();
                break;
            case ShellShortcutCatalog.Format:
                FormatButton_Click(this, new RoutedEventArgs());
                break;
            case ShellShortcutCatalog.PlaySettings:
                OpenActiveAdventurePlaySettings();
                break;
            case ShellShortcutCatalog.FocusChat:
                TogglePlayPanelFocusMode();
                break;
            case ShellShortcutCatalog.ToggleLeftPanel:
                TogglePlayLeftPanelFromShortcut();
                break;
            case ShellShortcutCatalog.ToggleRightPanel:
                TogglePlayRightPanelFromShortcut();
                break;
            case ShellShortcutCatalog.TabReference:
                NavigatePlayTabFromShortcut("Reference");
                break;
            case ShellShortcutCatalog.TabWarnings:
                NavigatePlayTabFromShortcut("Warnings");
                break;
            case ShellShortcutCatalog.TabState:
                NavigatePlayTabFromShortcut("State");
                break;
            case ShellShortcutCatalog.TabNotes:
                NavigatePlayTabFromShortcut("Notes");
                break;
            case ShellShortcutCatalog.ShowShortcuts:
                ShowKeyboardShortcutsDialog();
                break;
        }
    }

    private void OpenActiveAdventurePlaySettings()
    {
        if (_activeAdventureId is null || _appMode is not (AppMode.Play or AppMode.Design))
            return;

        if (_appMode == AppMode.Play)
        {
            _playView?.OpenPlaySettings(PlaySettingsTab.Settings);
            return;
        }

        var bundle = AdventureStore.Load(_activeAdventureId.Value);
        if (bundle is null)
            return;

        var dialog = new PlayPromptInjectionDialog(bundle, previewPlayerLine: null, PlaySettingsTab.Settings)
        {
            Owner = this,
        };
        WireStandalonePlaySettingsDialog(dialog, _activeAdventureId.Value);
        dialog.ShowDialog();
    }

    private void NavigatePlayTabFromShortcut(string tabName) =>
        _playView?.NavigateToPlayTab(tabName);

    private void TogglePlayLeftPanelFromShortcut()
    {
        if (_activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        ClearPlayPanelFocusMode();
        TogglePlaySidePanelCollapsed(!bundle.Metadata.Settings.PlaySidePanelCollapsed);
    }

    private void TogglePlayRightPanelFromShortcut()
    {
        if (_activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null || !ShouldShowRightCompanionColumn(bundle))
            return;

        ClearPlayPanelFocusMode();
        TogglePlayNotesPanelCollapsed(!bundle.Metadata.Settings.PlayNotesPanelCollapsed);
    }

    private void ShowKeyboardShortcutsDialog()
    {
        var dialog = new KeyboardShortcutsDialog(_chrome, OnShellShortcutsChanged)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private void OnShellShortcutsChanged()
    {
        RefreshEffectiveShellShortcuts();
        ApplyShellShortcutMenuGestures();
    }

    private void KeyboardShortcutsMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowKeyboardShortcutsDialog();

    private void FocusChatMenuItem_Click(object sender, RoutedEventArgs e) =>
        TogglePlayPanelFocusMode();

    private void PlaySettingsMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenActiveAdventurePlaySettings();

    private void ShellPlayFocusButton_Click(object sender, RoutedEventArgs e) =>
        TogglePlayPanelFocusMode();
}
