using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsInjectionTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;
    private bool _suppress;

    public PlaySettingsInjectionTab()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsChanged;

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        _suppress = true;
        try
        {
            PlayInjectionPolicyService.EnsureDefaults(context.Bundle.Metadata);
            var s = context.Bundle.Metadata.Settings;
            var policy = PlayInjectionPolicyService.Resolve(s);

            InjectionPresetCombo.ItemsSource = InjectionPresetLibrary.All
                .Select(p => new InjectionPresetComboItem(p.Id, p.DisplayName))
                .Concat([new InjectionPresetComboItem(InjectionPresetIds.Custom, "Custom")])
                .ToList();
            InjectionPresetCombo.DisplayMemberPath = nameof(InjectionPresetComboItem.DisplayName);
            var presetId = policy.InjectionPresetId ?? InjectionPresetIds.Standard;
            InjectionPresetCombo.SelectedItem = InjectionPresetCombo.Items
                .Cast<InjectionPresetComboItem>()
                .FirstOrDefault(i => string.Equals(i.Id, presetId, StringComparison.OrdinalIgnoreCase))
                ?? InjectionPresetCombo.Items.Cast<InjectionPresetComboItem>().First();

            UpdateInjectionPresetHint();
            MaxPacketMirrorText.Text = $"{s.MaxPacketChars:N0} characters (adventure-wide)";

            IncludeSummaryCheck.IsChecked = policy.IncludeSummary;
            IncludeStateCheck.IsChecked = policy.IncludeState;
            IncludeMemoryCheck.IsChecked = policy.IncludePinnedMemory;
            IncludeTranscriptCheck.IsChecked = policy.IncludeTranscript;
            IncludeCardsCheck.IsChecked = policy.IncludeTriggeredCards;
            IncludeSourcesCheck.IsChecked = policy.IncludeSourcesPointers;
            InjectAttachmentGuidanceInjectionCheck.IsChecked = s.InjectAttachmentGuidance;
            TranscriptMaxTurnsBox.Text = policy.TranscriptMaxTurns.ToString();
            UseContextTagsCheck.IsChecked = s.UseContextTags;
            UseSectionInjectionCheck.IsChecked = s.UseSectionInjection;

            NarratorPanel.Bind(context.NarratorSession);
        }
        finally
        {
            _suppress = false;
        }
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        NarratorPanel.FlushToSession();
        if (!ReferenceEquals(_ctx?.Bundle, context.Bundle))
            return;

        var settings = context.Bundle.Metadata.Settings;
        settings.InjectionPolicy ??= new PlayInjectionPolicy();
        var policy = settings.InjectionPolicy;

        if (InjectionPresetCombo.SelectedItem is InjectionPresetComboItem presetItem)
            policy.InjectionPresetId = presetItem.Id;

        policy.IncludeSummary = IncludeSummaryCheck.IsChecked == true;
        policy.IncludeState = IncludeStateCheck.IsChecked == true;
        policy.IncludePinnedMemory = IncludeMemoryCheck.IsChecked == true;
        policy.IncludeTranscript = IncludeTranscriptCheck.IsChecked == true;
        policy.IncludeTriggeredCards = IncludeCardsCheck.IsChecked == true;
        policy.IncludeSourcesPointers = IncludeSourcesCheck.IsChecked == true;
        settings.InjectAttachmentGuidance = InjectAttachmentGuidanceInjectionCheck.IsChecked == true;
        if (int.TryParse(TranscriptMaxTurnsBox.Text, out var turns))
            policy.TranscriptMaxTurns = Math.Max(0, turns);
        settings.UseContextTags = UseContextTagsCheck.IsChecked == true;
        settings.UseSectionInjection = UseSectionInjectionCheck.IsChecked == true;

        var readiness = ProjectSourceInjectionService.Evaluate(context.Bundle);
        InjectionPolicyGuard.EnforceMandatorySections(settings, readiness.CanDelegateStaticContent);
    }

    public void FlushNarratorToSession() => NarratorPanel.FlushToSession();

    private void UpdateInjectionPresetHint()
    {
        if (InjectionPresetCombo.SelectedItem is not InjectionPresetComboItem item)
            return;

        var spec = InjectionPresetLibrary.Find(item.Id);
        InjectionPresetHint.Text = spec?.Description ?? "Custom — manual section and budget settings.";
    }

    private void GoToNarratorContract_Click(object sender, RoutedEventArgs e) =>
        _ctx?.NavigateToTab?.Invoke(PlaySettingsTab.Settings);

    private void OnNarratorChanged(object? sender, EventArgs e) =>
        OnChanged(InjectionPresetCombo, new RoutedEventArgs());

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;

        var ctx = _ctx;
        if (ctx is null)
            return;

        if (ReferenceEquals(sender, InjectionPresetCombo) && InjectionPresetCombo.SelectedItem is InjectionPresetComboItem { Id: { } id }
            && !string.Equals(id, InjectionPresetIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            _suppress = true;
            PlayInjectionPolicyService.ApplyPreset(ctx.Bundle.Metadata.Settings, id);
            Bind(ctx);
            _suppress = false;
        }
        else
        {
            UpdateInjectionPresetHint();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        ctx.NotifySettingsChanged();
    }

    private sealed class InjectionPresetComboItem(string? id, string displayName)
    {
        public string? Id { get; } = id;

        public string DisplayName { get; } = displayName;
    }
}
