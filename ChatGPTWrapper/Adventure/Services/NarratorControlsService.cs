using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class NarratorControlsService
{
    public static string GetBaselineHint(AdventureBundle bundle, NarratorParameter parameter) =>
        parameter switch
        {
            NarratorParameter.ResponseLength => "normal",
            NarratorParameter.DetailLevel => bundle.Metadata.Settings.DetailLevel,
            NarratorParameter.Tone => NarratorOverrideResolver.ResolveBaselineTone(bundle),
            NarratorParameter.Difficulty => bundle.Metadata.Settings.Difficulty,
            _ => "",
        };

    public static void PopulateCombo(
        ComboBox combo,
        AdventureBundle bundle,
        NarratorParameter parameter,
        NarratorOverrideScope scope,
        bool isEditable = false)
    {
        var baseline = GetBaselineHint(bundle, parameter);
        var current = NarratorOverrideResolver.GetScopedOverride(bundle, parameter, scope);
        var displayValue = current ?? baseline;

        combo.IsEditable = isEditable;
        combo.ItemsSource = NarratorPresetLibrary.BuildComboItems(parameter, baseline, displayValue);
        combo.DisplayMemberPath = nameof(NarratorPresetComboItem.DisplayName);

        if (current is null)
        {
            combo.SelectedItem = NarratorPresetComboItem.Inherit(baseline);
            if (isEditable)
                combo.Text = NarratorOverrideResolver.InheritLabel;
        }
        else if (combo.ItemsSource is IEnumerable<NarratorPresetComboItem> items)
        {
            var match = items.FirstOrDefault(i =>
                string.Equals(i.Id, current, StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.DisplayName, current, StringComparison.OrdinalIgnoreCase));
            combo.SelectedItem = match ?? new NarratorPresetComboItem(current, current);
            if (isEditable)
                combo.Text = current;
        }
    }

    public static string? ReadComboValue(ComboBox combo, NarratorParameter parameter)
    {
        if (combo.SelectedItem is NarratorPresetComboItem item)
        {
            if (item.IsInherit)
                return null;

            return NarratorPresetLibrary.PresetsFor(parameter)
                       .FirstOrDefault(p => string.Equals(p.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                       ?.PacketValue
                   ?? item.Id
                   ?? item.DisplayName;
        }

        var text = string.IsNullOrWhiteSpace(combo.Text)
            ? combo.SelectedItem as string
            : combo.Text.Trim();

        return NarratorOverrideResolver.NormalizeOverrideValue(parameter, text);
    }

    public static void SaveComboValue(
        AdventureBundle bundle,
        ComboBox combo,
        NarratorParameter parameter,
        NarratorOverrideScope scope)
    {
        NarratorOverrideResolver.SetScopedOverride(bundle, parameter, scope, ReadComboValue(combo, parameter));
    }
}
