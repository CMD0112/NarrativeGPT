using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsPlaySurfaceTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;
    private bool _suppressPreset;

    public PlaySettingsPlaySurfaceTab()
    {
        InitializeComponent();
        ApplyCardGridLayout();
    }

    public event EventHandler? SettingsChanged;

    private void OnCardsGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyCardGridLayout();

    private void ApplyCardGridLayout() =>
        PlaySettingsCardGridLayout.Apply(
            CardsGrid,
            [AttachmentsCard, CompanionLayoutCard],
            [false, false],
            ActualWidth);

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        var s = context.Bundle.Metadata.Settings;
        AttachmentContextModeCombo.ItemsSource = Enum.GetValues<AttachmentContextMode>();
        AttachmentContextModeCombo.SelectedItem = s.AttachmentContextMode;
        AttachmentOnlyPlaceholderBox.Text = s.AttachmentOnlyPlaceholder;
        InjectAttachmentGuidanceCheck.IsChecked = s.InjectAttachmentGuidance;

        _suppressPreset = true;
        var presetItems = new List<PlayLayoutPresetComboItem> { new(null, "Custom") };
        presetItems.AddRange(PlayLayoutPresetLibrary.All.Select(p => new PlayLayoutPresetComboItem(p.Id, p.DisplayName)));
        PlayLayoutPresetCombo.ItemsSource = presetItems;
        PlayLayoutPresetCombo.DisplayMemberPath = nameof(PlayLayoutPresetComboItem.DisplayName);
        PlayLayoutPresetCombo.SelectedItem = presetItems.FirstOrDefault(i =>
            string.Equals(i.Id, s.PlayLayoutPresetId, StringComparison.OrdinalIgnoreCase))
            ?? presetItems[0];
        _suppressPreset = false;

        BindPlayChrome(context.ChromeSettings.PlaySurface, context.Bundle);
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        var s = context.Bundle.Metadata.Settings;
        if (AttachmentContextModeCombo.SelectedItem is AttachmentContextMode mode)
            s.AttachmentContextMode = mode;
        s.AttachmentOnlyPlaceholder = string.IsNullOrWhiteSpace(AttachmentOnlyPlaceholderBox.Text)
            ? "[Attached file]"
            : AttachmentOnlyPlaceholderBox.Text.Trim();
        s.InjectAttachmentGuidance = InjectAttachmentGuidanceCheck.IsChecked == true;

        var chrome = context.ChromeSettings.PlaySurface;
        if (PlayCompanionOnEnterCombo.SelectedItem is PlayChromeComboItem onEnter)
            chrome.PlayCompanionOnEnter = onEnter.Id;
        if (PlayCompanionDefaultTabCombo.SelectedItem is string defaultTab)
            chrome.PlayCompanionDefaultTab = defaultTab;
        chrome.PlayCompanionRememberExpanders = PlayCompanionRememberExpandersCheck.IsChecked == true;
        if (NarratorPanelDensityCombo.SelectedItem is string narratorDensity)
        {
            chrome.NarratorPanelDensity = narratorDensity;
            if (string.Equals(narratorDensity, "Minimal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(narratorDensity, "Full", StringComparison.OrdinalIgnoreCase))
            {
                context.Bundle.Metadata.Settings.PlayCompanionLastNarratorDensity = narratorDensity;
            }
        }
    }

    public void SaveChrome(PlaySettingsWorkbenchContext context)
    {
        Flush(context);
        UiChromeStore.Save(context.ChromeSettings);
    }

    private void BindPlayChrome(PlaySurfaceChromeDefaults chrome, AdventureBundle bundle)
    {
        PlayCompanionOnEnterCombo.ItemsSource = new[]
        {
            new PlayChromeComboItem(PlayCompanionOnEnterModes.RememberLast, "Remember last"),
            new PlayChromeComboItem(PlayCompanionOnEnterModes.AlwaysCollapsed, "Always collapsed"),
            new PlayChromeComboItem(PlayCompanionOnEnterModes.AlwaysOpen, "Always open"),
        };
        PlayCompanionOnEnterCombo.DisplayMemberPath = nameof(PlayChromeComboItem.Label);
        PlayCompanionOnEnterCombo.SelectedItem = PlayCompanionOnEnterCombo.Items
            .Cast<PlayChromeComboItem>()
            .FirstOrDefault(i => string.Equals(i.Id, chrome.PlayCompanionOnEnter, StringComparison.OrdinalIgnoreCase))
            ?? PlayCompanionOnEnterCombo.Items[0];

        PlayCompanionDefaultTabCombo.ItemsSource = new[] { "Reference", "Warnings", "State" };
        PlayCompanionDefaultTabCombo.SelectedItem = string.IsNullOrWhiteSpace(chrome.PlayCompanionDefaultTab)
            ? "Reference"
            : chrome.PlayCompanionDefaultTab;

        PlayCompanionRememberExpandersCheck.IsChecked = chrome.PlayCompanionRememberExpanders;
        NarratorPanelDensityCombo.ItemsSource = new[] { "RememberLast", "Minimal", "Full" };
        NarratorPanelDensityCombo.SelectedItem = string.IsNullOrWhiteSpace(chrome.NarratorPanelDensity)
            ? "Minimal"
            : chrome.NarratorPanelDensity;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        if (!_suppressPreset
            && ReferenceEquals(sender, PlayLayoutPresetCombo)
            && PlayLayoutPresetCombo.SelectedItem is PlayLayoutPresetComboItem { Id: { } id })
        {
            PlayPanelLayoutService.ApplyPreset(_ctx.Bundle.Metadata.Settings, id);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.NotifySettingsChanged();
    }

    private sealed class PlayLayoutPresetComboItem(string? id, string displayName)
    {
        public string? Id { get; } = id;

        public string DisplayName { get; } = displayName;
    }

    private sealed class PlayChromeComboItem(string id, string label)
    {
        public string Id { get; } = id;

        public string Label { get; } = label;
    }
}
