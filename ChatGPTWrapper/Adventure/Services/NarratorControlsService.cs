using System.Windows.Controls;

using ChatGPTWrapper.Adventure.Models;

using ChatGPTWrapper.Adventure.Services.NarratorScales;



namespace ChatGPTWrapper.Adventure.Services;



public static class NarratorControlsService

{

    public static string GetBaselineHint(AdventureBundle bundle, NarratorParameter parameter)

    {

        var raw = parameter switch

        {

            NarratorParameter.ResponseLength => "normal",

            NarratorParameter.DetailLevel => bundle.Metadata.Settings.DetailLevel,

            NarratorParameter.Tone => NarratorOverrideResolver.ResolveBaselineTone(bundle),

            NarratorParameter.Difficulty => bundle.Metadata.Settings.Difficulty,

            NarratorParameter.ViolenceLevel => bundle.Metadata.Settings.ViolenceLevel?.Trim() ?? "moderate",

            NarratorParameter.NarrativePacing => bundle.Metadata.Settings.NarrativePacing,

            NarratorParameter.ConsequenceWeight => bundle.Metadata.Settings.ConsequenceWeight,

            _ => "",

        };



        if (!string.IsNullOrWhiteSpace(raw))

            return raw.Trim();



        return parameter switch

        {

            NarratorParameter.Tone => "neutral",

            NarratorParameter.Difficulty => "easy",

            NarratorParameter.DetailLevel => "medium",

            NarratorParameter.ViolenceLevel => "moderate",

            NarratorParameter.NarrativePacing => "balanced",

            NarratorParameter.ConsequenceWeight => "balanced",

            _ => raw,

        };

    }



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

        }

        else if (combo.ItemsSource is IEnumerable<NarratorPresetComboItem> items)

        {

            var match = items.FirstOrDefault(i =>

                string.Equals(i.Id, current, StringComparison.OrdinalIgnoreCase)

                || string.Equals(i.DisplayName, current, StringComparison.OrdinalIgnoreCase));

            combo.SelectedItem = match ?? new NarratorPresetComboItem(current, current);

        }



        if (isEditable && combo.SelectedItem is NarratorPresetComboItem selected)

            combo.Text = selected.DisplayName;



        combo.ToolTip = BuildParameterTooltip(parameter, displayValue);

    }



    public static void BindViolenceContractDisplay(TextBlock block, AdventureBundle bundle)

    {

        UtilityStoryContextSettingsService.EnsureDefaults(bundle.Metadata);

        var value = bundle.Metadata.Settings.ViolenceLevel?.Trim() ?? "moderate";

        var summary = NarratorScalesResolver.TryGetViolenceSummary(value);

        block.Text = value;

        block.ToolTip = string.IsNullOrWhiteSpace(summary)

            ? "Violence level — contract only. Edit in Play settings → Instructions."

            : $"{summary} Contract only — edit in Play settings → Instructions. Definitions in narrator-scales.md § violence-level.";

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

        if (scope == NarratorOverrideScope.Adventure)

        {

            NarratorOverrideResolver.SetAdventureBaseline(

                bundle,

                parameter,

                ReadAdventureBaselineComboValue(combo, parameter));

            return;

        }



        NarratorOverrideResolver.SetScopedOverride(bundle, parameter, scope, ReadComboValue(combo, parameter));

    }



    private static string? ReadAdventureBaselineComboValue(ComboBox combo, NarratorParameter parameter)

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



        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    }



    private static string? BuildParameterTooltip(NarratorParameter parameter, string? value)

    {

        var summary = NarratorPresetLibrary.GetPresetDescription(parameter, value);

        if (string.IsNullOrWhiteSpace(summary))

            return null;



        var dimensionId = parameter switch

        {

            NarratorParameter.ResponseLength => "response-length",

            NarratorParameter.DetailLevel => "detail-level",

            NarratorParameter.Tone => "tone",

            NarratorParameter.Difficulty => "combat-difficulty",

            NarratorParameter.ViolenceLevel => "violence-level",

            NarratorParameter.NarrativePacing => "narrative-pacing",

            NarratorParameter.ConsequenceWeight => "consequence-weight",

            _ => null,

        };



        return string.IsNullOrWhiteSpace(dimensionId)

            ? summary

            : $"{summary} (see narrator-scales.md § {dimensionId})";

    }

}


