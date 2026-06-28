using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class NarratorBehaviorPanelBinder
{
    public static readonly NarratorParameter[] NarrationParameters =
    [
        NarratorParameter.ResponseLength,
        NarratorParameter.DetailLevel,
        NarratorParameter.Tone,
        NarratorParameter.NarrativePacing,
    ];

    public static readonly NarratorParameter[] CombatParameters =
    [
        NarratorParameter.Difficulty,
        NarratorParameter.ViolenceLevel,
        NarratorParameter.ConsequenceWeight,
    ];

    public static void BindSceneProfile(ComboBox combo, AdventureBundle bundle, bool selectInherit = true)
    {
        combo.ItemsSource = new List<NarratorPresetComboItem>
        {
            NarratorPresetComboItem.Inherit(),
        }.Concat(NarratorPresetLibrary.SceneProfiles.Select(p => new NarratorPresetComboItem(p.Id, p.DisplayName)))
        .ToList();
        combo.DisplayMemberPath = nameof(NarratorPresetComboItem.DisplayName);
        if (selectInherit)
            combo.SelectedIndex = 0;
    }

    public static void SelectSceneProfileInherit(ComboBox combo)
    {
        if (combo.ItemsSource is IEnumerable<NarratorPresetComboItem> items)
            combo.SelectedItem = items.FirstOrDefault(i => i.IsInherit) ?? items.FirstOrDefault();
        else
            combo.SelectedIndex = 0;
    }

    public static void BindScope(
        RadioButton turnRadio,
        RadioButton sessionRadio,
        RadioButton adventureRadio,
        NarratorOverrideScope scope)
    {
        turnRadio.IsChecked = scope == NarratorOverrideScope.Turn;
        sessionRadio.IsChecked = scope == NarratorOverrideScope.Session;
        adventureRadio.IsChecked = scope == NarratorOverrideScope.Adventure;
    }

    public static NarratorOverrideScope ReadScope(
        RadioButton turnRadio,
        RadioButton sessionRadio,
        RadioButton adventureRadio)
    {
        if (sessionRadio.IsChecked == true)
            return NarratorOverrideScope.Session;
        if (adventureRadio.IsChecked == true)
            return NarratorOverrideScope.Adventure;
        return NarratorOverrideScope.Turn;
    }

    public static void BindParameterCombos(
        AdventureBundle bundle,
        NarratorOverrideScope scope,
        IReadOnlyDictionary<NarratorParameter, ComboBox> combos)
    {
        foreach (var (parameter, combo) in combos)
        {
            var isEditable = parameter is NarratorParameter.Tone or NarratorParameter.Difficulty;
            NarratorControlsService.PopulateCombo(combo, bundle, parameter, scope, isEditable);
        }
    }

    public static void SaveParameterCombos(
        AdventureBundle bundle,
        NarratorOverrideScope scope,
        IReadOnlyDictionary<NarratorParameter, ComboBox> combos)
    {
        foreach (var (parameter, combo) in combos)
            NarratorControlsService.SaveComboValue(bundle, combo, parameter, scope);
    }

    public static string FormatOverrideChips(AdventureBundle bundle) =>
        NarratorOverrideResolver.GetActiveOverrideChips(bundle) is { Count: > 0 } chips
            ? $"Active: {string.Join(" · ", chips)}"
            : "No active overrides.";
}
