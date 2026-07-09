using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class WinUiNarratorBehaviorPanel : UserControl
{
    private NarratorSettingsSession? _session;
    private bool _suppress;

    public WinUiNarratorBehaviorPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsChanged;

    public void Bind(NarratorSettingsSession session)
    {
        _session = session;
        _suppress = true;
        try
        {
            BindSceneProfile();
            BindScope(session.SelectedScope);
            BindParameterCombos(session.SelectedScope);
            BindAdvancedFields(session.SelectedScope);
            OverrideChipsText.Text = session.FormatOverrideChips();
        }
        finally
        {
            _suppress = false;
        }
    }

    public void FlushToSession()
    {
        if (_session is null)
            return;

        var scope = ReadScope();
        SaveParameterCombos(scope);
        NarratorOverrideResolver.PersistScope(_session.Bundle.Metadata.Settings, scope);
        FlushAdvancedFields(scope);
        ApplySceneProfile(scope);
        OverrideChipsText.Text = _session.FormatOverrideChips();
    }

    private void BindSceneProfile()
    {
        if (_session is null)
            return;

        SceneProfileCombo.ItemsSource = new List<NarratorPresetComboItem>
        {
            NarratorPresetComboItem.Inherit(),
        }.Concat(NarratorPresetLibrary.SceneProfiles.Select(p => new NarratorPresetComboItem(p.Id, p.DisplayName)))
        .ToList();
        SceneProfileCombo.DisplayMemberPath = nameof(NarratorPresetComboItem.DisplayName);
        if (SceneProfileCombo.ItemsSource is IEnumerable<NarratorPresetComboItem> items)
            SceneProfileCombo.SelectedItem = items.FirstOrDefault(i => i.IsInherit) ?? items.FirstOrDefault();
        else
            SceneProfileCombo.SelectedIndex = 0;
    }

    private void BindScope(NarratorOverrideScope scope)
    {
        ScopeTurnRadio.IsChecked = scope == NarratorOverrideScope.Turn;
        ScopeSessionRadio.IsChecked = scope == NarratorOverrideScope.Session;
        ScopeAdventureRadio.IsChecked = scope == NarratorOverrideScope.Adventure;
    }

    private NarratorOverrideScope ReadScope()
    {
        if (ScopeSessionRadio.IsChecked == true)
            return NarratorOverrideScope.Session;
        if (ScopeAdventureRadio.IsChecked == true)
            return NarratorOverrideScope.Adventure;
        return NarratorOverrideScope.Turn;
    }

    private void BindParameterCombos(NarratorOverrideScope scope)
    {
        if (_session is null)
            return;

        var bundle = _session.Bundle;
        WinUiNarratorComboHelper.Populate(ResponseLengthCombo, bundle, NarratorParameter.ResponseLength, scope);
        WinUiNarratorComboHelper.Populate(DetailLevelCombo, bundle, NarratorParameter.DetailLevel, scope);
        WinUiNarratorComboHelper.Populate(ToneCombo, bundle, NarratorParameter.Tone, scope, isEditable: true);
        WinUiNarratorComboHelper.Populate(NarrativePacingCombo, bundle, NarratorParameter.NarrativePacing, scope);
        WinUiNarratorComboHelper.Populate(DifficultyCombo, bundle, NarratorParameter.Difficulty, scope, isEditable: true);
        WinUiNarratorComboHelper.Populate(ViolenceCombo, bundle, NarratorParameter.ViolenceLevel, scope);
        WinUiNarratorComboHelper.Populate(ConsequenceWeightCombo, bundle, NarratorParameter.ConsequenceWeight, scope);
    }

    private void SaveParameterCombos(NarratorOverrideScope scope)
    {
        if (_session is null)
            return;

        var bundle = _session.Bundle;
        WinUiNarratorComboHelper.Save(bundle, ResponseLengthCombo, NarratorParameter.ResponseLength, scope);
        WinUiNarratorComboHelper.Save(bundle, DetailLevelCombo, NarratorParameter.DetailLevel, scope);
        WinUiNarratorComboHelper.Save(bundle, ToneCombo, NarratorParameter.Tone, scope);
        WinUiNarratorComboHelper.Save(bundle, NarrativePacingCombo, NarratorParameter.NarrativePacing, scope);
        WinUiNarratorComboHelper.Save(bundle, DifficultyCombo, NarratorParameter.Difficulty, scope);
        WinUiNarratorComboHelper.Save(bundle, ViolenceCombo, NarratorParameter.ViolenceLevel, scope);
        WinUiNarratorComboHelper.Save(bundle, ConsequenceWeightCombo, NarratorParameter.ConsequenceWeight, scope);
    }

    private void BindAdvancedFields(NarratorOverrideScope scope)
    {
        if (_session is null)
            return;

        var settings = _session.Bundle.Metadata.Settings;
        TurnDirectiveBox.Text = settings.PlayTurnOverrides.TurnDirective ?? "";
        var sessionOverrides = NarratorOverrideResolver.GetSessionOverrides(_session.Bundle)
                               ?? new PlaySessionNarratorOverrides();
        SessionAddendumBox.Text = sessionOverrides.TemporaryAddendum ?? "";
        EmphasizeBoundariesCheck.IsChecked = scope switch
        {
            NarratorOverrideScope.Turn => settings.PlayTurnOverrides.EmphasizeBoundaries,
            NarratorOverrideScope.Session => sessionOverrides.EmphasizeBoundaries,
            _ => settings.PlayTurnOverrides.EmphasizeBoundaries || sessionOverrides.EmphasizeBoundaries,
        };
        EmphasizePortrayalRulesCheck.IsChecked = scope switch
        {
            NarratorOverrideScope.Turn => settings.PlayTurnOverrides.EmphasizePortrayalRules,
            NarratorOverrideScope.Session => sessionOverrides.EmphasizePortrayalRules,
            _ => settings.PlayTurnOverrides.EmphasizePortrayalRules || sessionOverrides.EmphasizePortrayalRules,
        };
    }

    private void FlushAdvancedFields(NarratorOverrideScope scope)
    {
        if (_session is null)
            return;

        var bundle = _session.Bundle;
        var settings = bundle.Metadata.Settings;
        settings.PlayTurnOverrides.TurnDirective = string.IsNullOrWhiteSpace(TurnDirectiveBox.Text)
            ? null
            : TurnDirectiveBox.Text.Trim();

        var sessionOverrides = NarratorOverrideResolver.GetOrCreateSessionOverrides(bundle);
        sessionOverrides.TemporaryAddendum = string.IsNullOrWhiteSpace(SessionAddendumBox.Text)
            ? null
            : SessionAddendumBox.Text.Trim();

        switch (scope)
        {
            case NarratorOverrideScope.Turn:
                settings.PlayTurnOverrides.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                settings.PlayTurnOverrides.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                break;
            case NarratorOverrideScope.Session:
                sessionOverrides.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                sessionOverrides.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                break;
            case NarratorOverrideScope.Adventure:
                settings.PlayTurnOverrides.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                settings.PlayTurnOverrides.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                sessionOverrides.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                sessionOverrides.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                break;
        }
    }

    private void ApplySceneProfile(NarratorOverrideScope scope)
    {
        if (_session is null || SceneProfileCombo.SelectedItem is not NarratorPresetComboItem item)
            return;

        if (item.IsInherit)
        {
            NarratorOverrideResolver.ResetScope(_session.Bundle, scope);
            return;
        }

        if (item.Id is null)
            return;

        NarratorPresetLibrary.ApplySceneProfile(_session.Bundle, item.Id, scope);
    }

    private void ResetScope_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;

        var scope = ReadScope();
        NarratorOverrideResolver.ResetScope(_session.Bundle, scope);
        _suppress = true;
        if (SceneProfileCombo.ItemsSource is IEnumerable<NarratorPresetComboItem> profileItems)
            SceneProfileCombo.SelectedItem = profileItems.FirstOrDefault(i => i.IsInherit) ?? profileItems.FirstOrDefault();
        BindParameterCombos(scope);
        _suppress = false;
        OnSettingsChanged(sender, e);
    }

    private void OnSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;

        if (sender is RadioButton && _session is not null)
            BindParameterCombos(ReadScope());

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
