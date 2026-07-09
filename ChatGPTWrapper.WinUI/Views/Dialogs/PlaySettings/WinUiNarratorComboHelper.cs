using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal static class WinUiNarratorComboHelper
{
    public static void Populate(
        ComboBox combo,
        AdventureBundle bundle,
        NarratorParameter parameter,
        NarratorOverrideScope scope,
        bool isEditable = false)
    {
        var baseline = NarratorControlsService.GetBaselineHint(bundle, parameter);
        var current = NarratorOverrideResolver.GetScopedOverride(bundle, parameter, scope);
        var displayValue = current ?? baseline;
        var items = NarratorPresetLibrary.BuildComboItems(parameter, baseline, displayValue).ToList();

        combo.IsEditable = isEditable;
        combo.ItemsSource = items;
        combo.DisplayMemberPath = nameof(NarratorPresetComboItem.DisplayName);

        if (current is null)
        {
            combo.SelectedItem = NarratorPresetComboItem.Inherit(baseline);
        }
        else
        {
            var match = items.FirstOrDefault(i =>
                string.Equals(i.Id, current, StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.DisplayName, current, StringComparison.OrdinalIgnoreCase));
            combo.SelectedItem = match ?? new NarratorPresetComboItem(current, current);
        }

        if (isEditable && combo.SelectedItem is NarratorPresetComboItem selected)
            combo.Text = selected.DisplayName;
    }

    public static void Save(
        AdventureBundle bundle,
        ComboBox combo,
        NarratorParameter parameter,
        NarratorOverrideScope scope)
    {
        var value = ReadValue(combo, parameter);
        NarratorOverrideResolver.SetScopedOverride(bundle, parameter, scope, value);
    }

    public static string? ReadValue(ComboBox combo, NarratorParameter parameter)
    {
        if (combo.SelectedItem is NarratorPresetComboItem item)
        {
            if (item.IsInherit)
                return null;

            return string.IsNullOrWhiteSpace(item.Id) ? item.DisplayName : item.Id;
        }

        if (combo.IsEditable)
        {
            var text = combo.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }
}
