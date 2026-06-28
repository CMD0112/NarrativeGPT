using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class NarratorBehaviorPanel : UserControl
{
    private NarratorSettingsSession? _session;
    private bool _suppressEvents;
    private string _scopeGroupName = "NarratorScopePanel";

    public NarratorBehaviorPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyScopeGroupName();
    }

    public string ScopeGroupName
    {
        get => _scopeGroupName;
        set
        {
            _scopeGroupName = string.IsNullOrWhiteSpace(value) ? "NarratorScopePanel" : value;
            if (IsLoaded)
                ApplyScopeGroupName();
        }
    }

    public event EventHandler? SettingsChanged;

    public event EventHandler? AdvancedRequested;

    public NarratorSettingsSession? Session => _session;

    private IReadOnlyDictionary<NarratorParameter, ComboBox> ParameterCombos =>
        new Dictionary<NarratorParameter, ComboBox>
        {
            [NarratorParameter.ResponseLength] = ResponseLengthCombo,
            [NarratorParameter.DetailLevel] = DetailLevelCombo,
            [NarratorParameter.Tone] = ToneCombo,
            [NarratorParameter.NarrativePacing] = NarrativePacingCombo,
            [NarratorParameter.Difficulty] = DifficultyCombo,
            [NarratorParameter.ViolenceLevel] = ViolenceCombo,
            [NarratorParameter.ConsequenceWeight] = ConsequenceWeightCombo,
        };

    public void Bind(NarratorSettingsSession session)
    {
        _session = session;
        _suppressEvents = true;

        NarratorBehaviorPanelBinder.BindSceneProfile(SceneProfileCombo, session.Bundle);
        session.BindScopeToUi(ScopeTurnRadio, ScopeSessionRadio, ScopeAdventureRadio);
        session.BindParameterCombos(session.SelectedScope, ParameterCombos);
        UpdateChips();
        UpdateScopeHint();

        _suppressEvents = false;
    }

    public NarratorOverrideScope ReadScope() =>
        _session?.ReadScopeFromUi(ScopeTurnRadio, ScopeSessionRadio, ScopeAdventureRadio)
        ?? NarratorOverrideScope.Turn;

    public void FlushToSession()
    {
        if (_session is null)
            return;

        _session.FlushFromPanel(ParameterCombos);
        UpdateChips();
    }

    public void SaveTo(AdventureBundle bundle)
    {
        FlushToSession();
        if (_session is null)
            return;

        NarratorSettingsSession.CopyNarratorSettings(_session.Bundle.Metadata.Settings, bundle.Metadata.Settings);
        NarratorOverrideResolver.PersistScope(bundle.Metadata.Settings, _session.SelectedScope);
    }

    private void ApplyScopeGroupName()
    {
        ScopeTurnRadio.GroupName = _scopeGroupName;
        ScopeSessionRadio.GroupName = _scopeGroupName;
        ScopeAdventureRadio.GroupName = _scopeGroupName;
    }

    private void UpdateChips()
    {
        if (_session is null)
            return;

        OverrideChipsText.Text = _session.FormatOverrideChips();
    }

    private void UpdateScopeHint()
    {
        ScopeHintText.Visibility = ReadScope() == NarratorOverrideScope.Adventure
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RaiseSettingsChanged()
    {
        if (_suppressEvents || _session is null)
            return;

        _session.FlushFromPanel(ParameterCombos);
        UpdateChips();
        UpdateScopeHint();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _session is null)
            return;

        var newScope = ReadScope();
        _suppressEvents = true;
        _session.HandleScopeChange(newScope, ParameterCombos);
        NarratorBehaviorPanelBinder.SelectSceneProfileInherit(SceneProfileCombo);
        _session.BindParameterCombos(_session.SelectedScope, ParameterCombos);
        UpdateChips();
        UpdateScopeHint();
        _suppressEvents = false;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Parameter_Changed(object sender, RoutedEventArgs e) => RaiseSettingsChanged();

    private void SceneProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || _session is null)
            return;

        if (SceneProfileCombo.SelectedItem is not NarratorPresetComboItem item)
            return;

        var scope = ReadScope();
        if (item.IsInherit)
        {
            NarratorOverrideResolver.ResetScope(_session.Bundle, scope);
            RefreshParameterCombos();
            RaiseSettingsChanged();
            return;
        }

        if (item.Id is null)
            return;

        NarratorPresetLibrary.ApplySceneProfile(_session.Bundle, item.Id, scope);
        RefreshParameterCombos();
        RaiseSettingsChanged();
    }

    private void ResetScope_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;

        NarratorOverrideResolver.ResetScope(_session.Bundle, ReadScope());
        _suppressEvents = true;
        NarratorBehaviorPanelBinder.SelectSceneProfileInherit(SceneProfileCombo);
        RefreshParameterCombos();
        _suppressEvents = false;
        RaiseSettingsChanged();
    }

    private void RefreshParameterCombos()
    {
        if (_session is null)
            return;

        _suppressEvents = true;
        _session.BindParameterCombos(_session.SelectedScope, ParameterCombos);
        UpdateChips();
        UpdateScopeHint();
        _suppressEvents = false;
    }

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;

        if (AdvancedRequested is not null)
        {
            AdvancedRequested(this, EventArgs.Empty);
            return;
        }

        var dialog = new NarratorAdvancedDialog(_session.Bundle, ReadScope())
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true)
        {
            Bind(_session);
            RaiseSettingsChanged();
        }
    }
}
