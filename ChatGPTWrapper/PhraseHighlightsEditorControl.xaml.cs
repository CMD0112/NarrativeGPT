using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;
using Microsoft.Win32;

namespace ChatGPTWrapper;

public partial class PhraseHighlightsEditorControl : UserControl
{
    private const string DefaultColor = "#FFD166";
    private const double SwatchSize = 28;
    private const double SwatchRadius = 5;
    private const int AutoColorPreviewCount = 18;

    private static readonly string[] PresetColors = PhraseHighlightPresetColors.ManualPicker;
    private static readonly JsonSerializerOptions HighlightsJsonOptions = new() { WriteIndented = true };

    private readonly ObservableCollection<RuleRow> _rows = [];
    private readonly CollectionViewSource _rulesViewSource = new();
    private PhraseHighlightRuleSortMode _ruleSortMode = PhraseHighlightRuleSortMode.Manual;
    private PhraseHighlightRuleGroupMode _ruleGroupMode = PhraseHighlightRuleGroupMode.None;
    private string _filterText = "";
    private bool _suppressEditorEvents;
    private bool _suppressRulesNotify;
    private RuleRow? _activeEditorRow;

    public event EventHandler? RulesChanged;

    public event EventHandler? ColorAssignmentChanged;

    public Func<Guid?>? ResolveActiveAdventureId { get; set; }

    private UiChromeSettings? _workingChrome;
    private bool _suppressProfileEvents;
    private HighlightColorAssignmentOptions _workingColorOptions = new();
    private int _palettePreviewSalt;
    private string _selectedColorProfileId = HighlightColorProfileIds.ThemeHarmony;
    private string _selectedGroupingProfileId = HighlightColorGroupingProfileIds.None;
    private HighlightColorGroupingProfile? _workingGroupingProfile;

    public PhraseHighlightsEditorControl()
    {
        InitializeComponent();

        BuildSwatches(TextColorSwatchesPanel, "text");
        BuildSwatches(BackgroundColorSwatchesPanel, "background");
        EnsureFontWeightChoiceComboItems();

        _rulesViewSource.Source = _rows;
        _rulesViewSource.Filter += RulesView_Filter;
        RulesListView.ItemsSource = _rulesViewSource.View;

        Loaded += (_, _) =>
        {
            RefreshImportAvailability();
            UpdateRuleCountText();
            UpdateFilterStatusText();
        };
    }

    public void AttachChromeSettings(UiChromeSettings working)
    {
        _workingChrome = working;
        HighlightColorProfileService.Normalize(working);
        HighlightColorGroupingProfileService.Normalize(working);
        _selectedColorProfileId = HighlightColorProfileService.ResolveInitialProfileId(working);
        _selectedGroupingProfileId = HighlightColorGroupingProfileService.ResolveInitialProfileId(working);
        _workingColorOptions = HighlightColorProfileService.ResolveEffectiveOptions(working);
        _workingGroupingProfile = HighlightColorGroupingProfileService.ResolveEffectiveProfile(working);
        RefreshAutoColorProfileCombo();
        RefreshAutoColorGroupingProfileCombo();
    }

    public void ApplyColorAssignmentTo(UiChromeSettings settings)
    {
        if (_workingChrome is null)
            return;

        settings.ActiveHighlightColorProfileId = _workingChrome.ActiveHighlightColorProfileId;
        settings.HighlightColorCustomOptions = _workingChrome.HighlightColorCustomOptions.Clone();
        settings.HighlightColorProfiles = _workingChrome.HighlightColorProfiles.Select(p => p.Clone()).ToList();
        settings.ActiveHighlightColorGroupingProfileId = _workingChrome.ActiveHighlightColorGroupingProfileId;
        settings.HighlightColorGroupingProfiles = _workingChrome.HighlightColorGroupingProfiles.Select(p => p.Clone()).ToList();
        settings.HighlightColorGroupingCustomProfile = _workingChrome.HighlightColorGroupingCustomProfile.Clone();
    }

    private HighlightColorGroupingProfile? ResolveEffectiveGroupingProfile()
    {
        if (_workingChrome is null
            || _selectedGroupingProfileId.Equals(HighlightColorGroupingProfileIds.None, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_selectedGroupingProfileId.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return _workingGroupingProfile?.Clone() ?? _workingChrome.HighlightColorGroupingCustomProfile.Clone();

        return HighlightColorGroupingProfileLibrary.Find(
            _workingChrome.HighlightColorGroupingProfiles,
            _selectedGroupingProfileId)?.Clone();
    }

    private void SyncWorkingGroupingProfileToChrome()
    {
        if (_workingChrome is null)
            return;

        _workingChrome.ActiveHighlightColorGroupingProfileId = _selectedGroupingProfileId;
        if (_selectedGroupingProfileId.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
            && _workingGroupingProfile is not null)
        {
            _workingChrome.HighlightColorGroupingCustomProfile = _workingGroupingProfile.Clone();
        }
    }

    private void RefreshAutoColorGroupingProfileCombo()
    {
        if (_workingChrome is null || AutoColorGroupingProfileCombo is null)
            return;

        _suppressProfileEvents = true;
        try
        {
            var profiles = new List<HighlightColorGroupingProfile>
            {
                new()
                {
                    Id = HighlightColorGroupingProfileIds.None,
                    Name = "None",
                    Description = "All names share one distinct-color pool.",
                    IsBuiltIn = true,
                },
            };
            profiles.AddRange(HighlightColorGroupingProfileService.ListSelectableProfiles(_workingChrome));
            if (_selectedGroupingProfileId.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            {
                profiles.Add(new HighlightColorGroupingProfile
                {
                    Id = HighlightColorGroupingProfileIds.Custom,
                    Name = "Custom",
                    IsBuiltIn = false,
                });
            }

            AutoColorGroupingProfileCombo.ItemsSource = profiles;
            AutoColorGroupingProfileCombo.SelectedItem = profiles.FirstOrDefault(p =>
                p.Id.Equals(_selectedGroupingProfileId, StringComparison.OrdinalIgnoreCase))
                ?? profiles[0];

            if (AutoColorGroupingStatusText is not null)
            {
                AutoColorGroupingStatusText.Text = HighlightColorGroupingProfileService.DescribeProfileStatus(
                    _workingChrome,
                    _selectedGroupingProfileId);
            }

            UpdateGroupingProfileButtons();
        }
        finally
        {
            _suppressProfileEvents = false;
        }
    }

    private void AutoColorGroupingProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileEvents || _workingChrome is null)
            return;

        if (AutoColorGroupingProfileCombo.SelectedItem is not HighlightColorGroupingProfile profile)
            return;

        _selectedGroupingProfileId = profile.Id;
        _workingChrome.ActiveHighlightColorGroupingProfileId = profile.Id;
        _workingGroupingProfile = profile.Id.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
            ? _workingChrome.HighlightColorGroupingCustomProfile.Clone()
            : profile.Id.Equals(HighlightColorGroupingProfileIds.None, StringComparison.OrdinalIgnoreCase)
                ? null
                : profile.Clone();

        SyncWorkingGroupingProfileToChrome();
        RefreshAutoColorGroupingProfileCombo();
        RefreshAllGroupingDisplays();
        ReconcileSharedGroupColors();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CustomizeGroupingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null)
            return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow as Window;
        var source = ResolveEffectiveGroupingProfile()
            ?? HighlightColorGroupingProfileLibrary.BuiltInProfiles
                .First(p => p.Id == HighlightColorGroupingProfileIds.ByEntityCategory)
                .Clone();
        if (HighlightColorGroupingDialog.Show(
                owner,
                source,
                out var profile,
                ResolveActiveAdventureId?.Invoke(),
                GetRules(),
                readOnly: false) != true)
            return;

        _selectedGroupingProfileId = HighlightColorGroupingProfileIds.Custom;
        _workingGroupingProfile = profile;
        _workingChrome.HighlightColorGroupingCustomProfile = profile.Clone();
        _workingChrome.ActiveHighlightColorGroupingProfileId = HighlightColorGroupingProfileIds.Custom;
        SyncWorkingGroupingProfileToChrome();
        RefreshAutoColorGroupingProfileCombo();
        RefreshAllGroupingDisplays();
        ReconcileSharedGroupColors();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateGroupingProfileButtons()
    {
        if (_workingChrome is null)
            return;

        var selected = AutoColorGroupingProfileCombo?.SelectedItem as HighlightColorGroupingProfile;
        var isBuiltIn = selected?.IsBuiltIn == true;
        var isNone = _selectedGroupingProfileId.Equals(HighlightColorGroupingProfileIds.None, StringComparison.OrdinalIgnoreCase);
        var isCustomSentinel = _selectedGroupingProfileId.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase);

        if (RenameGroupingProfileButton is not null)
            RenameGroupingProfileButton.IsEnabled = selected is not null && !isBuiltIn && !isNone && !isCustomSentinel;
        if (DeleteGroupingProfileButton is not null)
            DeleteGroupingProfileButton.IsEnabled = selected is not null && !isBuiltIn && !isNone && !isCustomSentinel;
        if (SaveGroupingProfileButton is not null)
            SaveGroupingProfileButton.IsEnabled = selected is not null && !isNone && (!isBuiltIn || isCustomSentinel);
    }

    private void NewGroupingProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null)
            return;

        var owner = Window.GetWindow(this);
        var name = PromptForText(owner, "New grouping profile", "Profile name", "My groupings");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var template = ResolveEffectiveGroupingProfile()
            ?? HighlightColorGroupingProfileLibrary.BuiltInProfiles.First(p =>
                p.Id == HighlightColorGroupingProfileIds.CastDistinctWorldShared).Clone();
        var profile = HighlightColorGroupingProfileService.CreateUserProfile(_workingChrome, name, template);
        _selectedGroupingProfileId = profile.Id;
        _workingGroupingProfile = profile.Clone();
        _workingChrome.ActiveHighlightColorGroupingProfileId = profile.Id;
        SyncWorkingGroupingProfileToChrome();
        RefreshAutoColorGroupingProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DuplicateGroupingProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null || AutoColorGroupingProfileCombo.SelectedItem is not HighlightColorGroupingProfile source)
            return;

        var template = source.Id.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
            ? _workingGroupingProfile?.Clone() ?? _workingChrome.HighlightColorGroupingCustomProfile.Clone()
            : source.Clone();
        var profile = HighlightColorGroupingProfileService.DuplicateProfile(_workingChrome, template);
        _selectedGroupingProfileId = profile.Id;
        _workingGroupingProfile = profile.Clone();
        _workingChrome.ActiveHighlightColorGroupingProfileId = profile.Id;
        SyncWorkingGroupingProfileToChrome();
        RefreshAutoColorGroupingProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenameGroupingProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (AutoColorGroupingProfileCombo.SelectedItem is not HighlightColorGroupingProfile profile || profile.IsBuiltIn)
            return;

        var owner = Window.GetWindow(this);
        var name = PromptForText(owner, "Rename grouping profile", "Profile name", profile.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        HighlightColorGroupingProfileService.RenameProfile(profile, name);
        RefreshAutoColorGroupingProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteGroupingProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null || AutoColorGroupingProfileCombo.SelectedItem is not HighlightColorGroupingProfile profile)
            return;

        var owner = Window.GetWindow(this);
        if (MessageBox.Show(owner, $"Delete grouping profile \"{profile.Name}\"?", "Delete grouping profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!HighlightColorGroupingProfileService.DeleteProfile(_workingChrome, profile.Id))
            return;

        _selectedGroupingProfileId = _workingChrome.ActiveHighlightColorGroupingProfileId;
        _workingGroupingProfile = HighlightColorGroupingProfileService.ResolveEffectiveProfile(_workingChrome);
        RefreshAutoColorGroupingProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveGroupingProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null)
            return;

        var working = ResolveEffectiveGroupingProfile()
            ?? _workingChrome.HighlightColorGroupingCustomProfile.Clone();

        if (AutoColorGroupingProfileCombo.SelectedItem is HighlightColorGroupingProfile { IsBuiltIn: false } profile
            && !profile.Id.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
            && !profile.Id.Equals(HighlightColorGroupingProfileIds.None, StringComparison.OrdinalIgnoreCase))
        {
            profile.Groups = working.Groups.Select(g => g.Clone()).ToList();
            profile.UnmatchedBehavior = working.UnmatchedBehavior;
            profile.Description = working.Description;
            _selectedGroupingProfileId = profile.Id;
            _workingChrome.ActiveHighlightColorGroupingProfileId = profile.Id;
            _workingGroupingProfile = profile.Clone();
        }
        else
        {
            var owner = Window.GetWindow(this);
            var name = PromptForText(owner, "Save grouping profile", "Profile name", "My groupings");
            if (string.IsNullOrWhiteSpace(name))
                return;

            var created = HighlightColorGroupingProfileService.CreateUserProfile(_workingChrome, name, working);
            _selectedGroupingProfileId = created.Id;
            _workingChrome.ActiveHighlightColorGroupingProfileId = created.Id;
            _workingGroupingProfile = created.Clone();
        }

        SyncWorkingGroupingProfileToChrome();
        RefreshAutoColorGroupingProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private HighlightColorAssignmentOptions ResolveEffectiveColorOptions()
    {
        var options = _workingColorOptions.Clone();
        options.AssignmentSalt = _palettePreviewSalt;
        return options;
    }

    private void SyncWorkingColorOptionsToChrome()
    {
        if (_workingChrome is null)
            return;

        _workingChrome.ActiveHighlightColorProfileId = _selectedColorProfileId;
        if (_selectedColorProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            _workingChrome.HighlightColorCustomOptions = _workingColorOptions.Clone();
    }

    private void RefreshAutoColorProfileCombo()
    {
        if (_workingChrome is null || AutoColorProfileCombo is null)
            return;

        _suppressProfileEvents = true;
        try
        {
            var profiles = HighlightColorProfileService.ListSelectableProfiles(_workingChrome).ToList();
            AutoColorProfileCombo.ItemsSource = profiles;
            var activeId = _selectedColorProfileId;
            AutoColorProfileCombo.SelectedItem = profiles.FirstOrDefault(p =>
                p.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase))
                ?? profiles.FirstOrDefault(p => p.Id.Equals(HighlightColorProfileIds.ThemeHarmony, StringComparison.OrdinalIgnoreCase));

            UpdateColorProfileButtons();
            UpdateAutoColorProfileDescription();
            RefreshAutoColorPalettePreview();
        }
        finally
        {
            _suppressProfileEvents = false;
        }
    }

    private void UpdateColorProfileButtons()
    {
        if (_workingChrome is null)
            return;

        var selected = AutoColorProfileCombo?.SelectedItem as HighlightColorAssignmentProfile;
        var isBuiltIn = selected?.IsBuiltIn == true;
        var isCustomSentinel = _selectedColorProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase);

        if (RenameColorProfileButton is not null)
            RenameColorProfileButton.IsEnabled = selected is not null && !isBuiltIn && !isCustomSentinel;
        if (DeleteColorProfileButton is not null)
            DeleteColorProfileButton.IsEnabled = selected is not null && !isBuiltIn && !isCustomSentinel;
        if (SaveColorProfileButton is not null)
            SaveColorProfileButton.IsEnabled = selected is not null && (!isBuiltIn || isCustomSentinel);
    }

    private void AutoColorProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileEvents || _workingChrome is null)
            return;

        if (AutoColorProfileCombo.SelectedItem is not HighlightColorAssignmentProfile profile)
            return;

        _selectedColorProfileId = profile.Id;
        _workingChrome.ActiveHighlightColorProfileId = profile.Id;
        _workingColorOptions = profile.Id.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
            ? _workingChrome.HighlightColorCustomOptions.Clone()
            : profile.Options.Clone();
        _palettePreviewSalt = _workingColorOptions.AssignmentSalt;

        UpdateColorProfileButtons();
        UpdateAutoColorProfileDescription();
        RefreshAutoColorPalettePreview();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NewColorProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null)
            return;

        var owner = Window.GetWindow(this);
        var name = PromptForText(owner, "New color profile", "Profile name", "My cast palette");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var profile = HighlightColorProfileService.CreateUserProfile(_workingChrome, name, _workingColorOptions);
        _selectedColorProfileId = profile.Id;
        _workingChrome.ActiveHighlightColorProfileId = profile.Id;
        SyncWorkingColorOptionsToChrome();
        RefreshAutoColorProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DuplicateColorProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null || AutoColorProfileCombo.SelectedItem is not HighlightColorAssignmentProfile source)
            return;

        var options = source.Id.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
            ? _workingColorOptions.Clone()
            : source.Options.Clone();
        var profile = HighlightColorProfileService.DuplicateProfile(_workingChrome, new HighlightColorAssignmentProfile
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            IsBuiltIn = source.IsBuiltIn,
            Options = options,
        });
        _selectedColorProfileId = profile.Id;
        _workingChrome.ActiveHighlightColorProfileId = profile.Id;
        _workingColorOptions = profile.Options.Clone();
        SyncWorkingColorOptionsToChrome();
        RefreshAutoColorProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenameColorProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (AutoColorProfileCombo.SelectedItem is not HighlightColorAssignmentProfile profile || profile.IsBuiltIn)
            return;

        var owner = Window.GetWindow(this);
        var name = PromptForText(owner, "Rename color profile", "Profile name", profile.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        HighlightColorProfileService.RenameProfile(profile, name);
        RefreshAutoColorProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteColorProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null || AutoColorProfileCombo.SelectedItem is not HighlightColorAssignmentProfile profile)
            return;

        var owner = Window.GetWindow(this);
        if (MessageBox.Show(owner, $"Delete profile \"{profile.Name}\"?", "Delete color profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!HighlightColorProfileService.DeleteProfile(_workingChrome, profile.Id))
            return;

        _selectedColorProfileId = _workingChrome.ActiveHighlightColorProfileId;
        _workingColorOptions = HighlightColorProfileService.ResolveEffectiveOptions(_workingChrome);
        RefreshAutoColorProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveColorProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null)
            return;

        if (AutoColorProfileCombo.SelectedItem is HighlightColorAssignmentProfile { IsBuiltIn: false } profile
            && !profile.Id.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            HighlightColorProfileService.SaveOptionsToProfile(profile, _workingColorOptions);
            _selectedColorProfileId = profile.Id;
            _workingChrome.ActiveHighlightColorProfileId = profile.Id;
        }
        else
        {
            var owner = Window.GetWindow(this);
            var name = PromptForText(owner, "Save color profile", "Profile name", "My cast palette");
            if (string.IsNullOrWhiteSpace(name))
                return;

            var created = HighlightColorProfileService.CreateUserProfile(_workingChrome, name, _workingColorOptions);
            _selectedColorProfileId = created.Id;
            _workingChrome.ActiveHighlightColorProfileId = created.Id;
        }

        SyncWorkingColorOptionsToChrome();
        RefreshAutoColorProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RerollPalettePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        _palettePreviewSalt++;
        RefreshAutoColorPalettePreview();
    }

    private void RerollAllRuleColorsButton_Click(object sender, RoutedEventArgs e) =>
        ReassignRuleColors(PhraseHighlightReassignScope.All, confirmAll: true);

    private void ReassignColorsButton_Click(object sender, RoutedEventArgs e) =>
        ReassignRuleColors(PhraseHighlightReassignScope.Selected, confirmAll: false);

    private void CustomizeAutoColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null)
            return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow as Window;
        if (HighlightColorAssignmentDialog.Show(owner, _workingChrome, out var profileId, out var options) != true)
            return;

        _workingChrome.ActiveHighlightColorProfileId = profileId;
        _selectedColorProfileId = profileId;
        _workingColorOptions = options.Clone();
        if (profileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            _workingChrome.HighlightColorCustomOptions = options.Clone();

        SyncWorkingColorOptionsToChrome();
        RefreshAutoColorProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAutoColorProfileDescription()
    {
        if (_workingChrome is null)
            return;

        if (AutoColorProfileStatusText is not null)
        {
            AutoColorProfileStatusText.Text = HighlightColorProfileService.DescribeProfileStatus(
                _workingChrome,
                _workingColorOptions,
                _selectedColorProfileId);
        }

        if (AutoColorProfileDescriptionText is null)
            return;

        if (_selectedColorProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            AutoColorProfileDescriptionText.Text =
                "Custom palette and assignment options — open Customize to edit or save as a profile.";
            return;
        }

        AutoColorProfileDescriptionText.Text =
            HighlightColorProfileLibrary.Find(_workingChrome.HighlightColorProfiles, _selectedColorProfileId)?.Description
            ?? string.Empty;
    }

    private void RefreshAutoColorPalettePreview()
    {
        if (AutoColorPalettePreviewPanel is null)
            return;

        AutoColorPalettePreviewPanel.Children.Clear();
        if (_workingChrome is null)
            return;

        var options = ResolveEffectiveColorOptions();
        var theme = ThemeRuntime.Current;
        var canvas = ResolveHighlightCanvasBackground();
        var ruleCount = _rows.Count(r => !string.IsNullOrWhiteSpace(r.Phrase));
        var minimumDistinct = ruleCount > 0
            ? HighlightColorCapacityAnalyzer.EstimateNewDistinctColorsNeeded(
                _rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
                    .Select(r => (r.Phrase.Trim(), PhraseHighlightColorAssignmentService.InferAssignmentRole(
                        r.ToRule(),
                        _rows.Select(x => x.ToRule()).ToList()), AlreadyExists: false)),
                options)
            : (int?)null;

        var palette = HighlightColorAssignmentEngine.BuildPalette(
            options, theme, canvas, minimumDistinct, ResolveReservedForegroundColors());
        foreach (var color in palette.Take(AutoColorPreviewCount))
            AutoColorPalettePreviewPanel.Children.Add(CreatePaletteSwatch(color, selectable: false));
    }

    private void AutoColorPalettePreviewBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        RerollPalettePreviewButton_Click(sender, e);
    }

    private void ReassignRuleColors(PhraseHighlightReassignScope scope, bool confirmAll)
    {
        CommitEditorToSelection();
        var selected = GetSelectedRows();
        if (scope == PhraseHighlightReassignScope.Selected && selected.Count == 0)
            return;

        var rules = GetRules().ToList();
        if (rules.Count == 0)
            return;

        var owner = Window.GetWindow(this);
        if (confirmAll)
        {
            if (MessageBox.Show(
                    owner,
                    $"Reroll colors for all {rules.Count} rules using the \"{ResolveActiveProfileName()}\" preset?",
                    "Reroll all colors",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
        }
        else if (MessageBox.Show(
                     owner,
                     $"Reroll colors for {selected.Count} selected rule{(selected.Count == 1 ? "" : "s")} using \"{ResolveActiveProfileName()}\"?",
                     "Reroll selected",
                     MessageBoxButton.YesNo,
                     MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _palettePreviewSalt++;
        _workingColorOptions.AssignmentSalt = _palettePreviewSalt;
        SyncWorkingColorOptionsToChrome();

        var selectedRules = selected.Select(r => r.ToRule()).ToList();
        var theme = ThemeRuntime.Current;
        var canvas = ResolveHighlightCanvasBackground();
        var options = _workingColorOptions.Clone();

        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            rules,
            options,
            theme,
            canvas,
            scope,
            selectedRules,
            assignmentSalt: _palettePreviewSalt,
            groupingProfile: ResolveEffectiveGroupingProfile(),
            reservedForegroundColors: ResolveReservedForegroundColors());

        ApplyReassignedRulesToRows(rules);
        RefreshAutoColorPalettePreview();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyReassignedRulesToRows(IReadOnlyList<PhraseHighlightRule> rules)
    {
        var byPhrase = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .GroupBy(r => r.Phrase.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows)
        {
            if (!byPhrase.TryGetValue(row.Phrase.Trim(), out var rule))
                continue;

            row.ApplySyncedStyleFrom(rule);
            row.SyncOverride = rule.SyncOverride;
            row.GroupOverride = rule.GroupOverride;
        }

        RefreshAllGroupingDisplays();

        if (_activeEditorRow is not null)
            LoadEditor(_activeEditorRow);

        UpdateRulesEmptyState();
        UpdateRuleCountText();
        NotifyRulesChanged();
    }

    private IReadOnlyList<string> ResolveReservedForegroundColors()
    {
        var theme = ThemeRuntime.Current;
        var format = _workingChrome?.ContinuousViewFormat;
        return HighlightColorReservedColors.Resolve(theme, format);
    }

    private string ResolveActiveProfileName()
    {
        if (AutoColorProfileCombo?.SelectedItem is HighlightColorAssignmentProfile profile)
            return profile.Name;

        return HighlightColorProfileLibrary.Find(
            _workingChrome?.HighlightColorProfiles ?? [],
            _selectedColorProfileId)?.Name
            ?? "active preset";
    }

    private static string? PromptForText(Window? owner, string title, string label, string defaultValue)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)Application.Current.FindResource("BgBaseBrush"),
        };

        var box = new TextBox { Text = defaultValue, MinHeight = 28, Margin = new Thickness(0, 0, 0, 12) };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    public void RefreshImportAvailability()
    {
        var canImport = ResolveActiveAdventureId?.Invoke() is not null;
        ImportFromAdventureButton.IsEnabled = canImport;
        ImportFromAdventureButton.ToolTip = canImport
            ? "Import player, party, and cast names from the active adventure."
            : "Open an adventure in Play or Design mode to import cast names.";
    }

    public void LoadRules(IEnumerable<PhraseHighlightRule> existingRules)
    {
        _activeEditorRow = null;
        _rows.Clear();
        var rules = existingRules.Select(r => r.Clone()).ToList();
        PhraseHighlightRuleService.AlignEntityCardAliases(rules);
        foreach (var rule in rules)
            _rows.Add(RuleRow.FromRule(rule));

        if (_rows.Count > 0)
            RulesListView.SelectedIndex = 0;
        else
            ClearEditor();

        RefreshAllGroupingDisplays();
        RefreshArrangementMetadata();
        UpdateRulesEmptyState();
        UpdateRuleCountText();
        UpdateFilterStatusText();
        RefreshAutoColorPalettePreview();
    }

    public IReadOnlyList<PhraseHighlightRule> GetRules()
    {
        CommitEditorToSelection();
        var rules = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => r.ToRule())
            .ToList();
        PhraseHighlightRuleService.AlignEntityCardAliases(rules);
        return rules;
    }

    private void ImportFromAdventureButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow as Window;
        var adventureId = ResolveActiveAdventureId?.Invoke();
        if (adventureId is null)
        {
            MessageBox.Show(owner, "Open an adventure in Play or Design mode to import cast names.", "Import from adventure");
            return;
        }

        var bundle = AdventureStore.Load(adventureId.Value);
        if (bundle is null)
        {
            MessageBox.Show(
                owner,
                "Could not load the active adventure. Try reopening it from the dashboard.",
                "Import from adventure",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (CastPhraseImportDialog.Show(
                owner,
                bundle,
                out var imported,
                _workingChrome is not null
                    ? ResolveEffectiveColorOptions()
                    : null,
                ResolveHighlightCanvasBackground(),
                GetRules(),
                ResolveEffectiveGroupingProfile(),
                _workingChrome?.ContinuousViewFormat) != true)
            return;

        foreach (var rule in imported)
        {
            if (_rows.Any(r => string.Equals(r.Phrase, rule.Phrase, StringComparison.OrdinalIgnoreCase)))
                continue;

            _rows.Add(RuleRow.FromRule(rule));
        }

        MergeSyncedAliasRows();

        if (_rows.Count > 0)
            RulesListView.SelectedIndex = 0;
        NotifyRulesChanged();
        UpdateRulesEmptyState();
        UpdateRuleCountText();
        UpdateFilterStatusText();
    }

    private void ImportHighlightsJsonButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow as Window;
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import phrase highlights",
        };

        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var imported = ParseHighlightsJson(json);
            if (imported.Count == 0)
            {
                MessageBox.Show(owner, "No phrase highlight rules found in the file.", "Import highlights JSON");
                return;
            }

            var added = 0;
            foreach (var rule in imported)
            {
                if (_rows.Any(r => string.Equals(r.Phrase, rule.Phrase, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _rows.Add(RuleRow.FromRule(rule));
                added++;
            }

            if (_rows.Count > 0 && RulesListView.SelectedIndex < 0)
                RulesListView.SelectedIndex = 0;

            MergeSyncedAliasRows();
            NotifyRulesChanged();
            UpdateRulesEmptyState();
            UpdateRuleCountText();
            UpdateFilterStatusText();

            MessageBox.Show(
                owner,
                added > 0
                    ? $"Added {added} rule{(added == 1 ? "" : "s")}. Duplicate phrases were skipped."
                    : "All rules in the file already exist (matched by phrase).",
                "Import highlights JSON");
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Could not import highlights: {ex.Message}", "Import highlights JSON", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportHighlightsJsonButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow as Window;
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export phrase highlights",
            FileName = "phrase-highlights.json",
        };

        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            var payload = new PhraseHighlightsExportPayload { PhraseHighlightRules = GetRules().ToList() };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(payload, HighlightsJsonOptions));
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Could not export highlights: {ex.Message}", "Export highlights JSON", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static List<PhraseHighlightRule> ParseHighlightsJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return [];

        if (!doc.RootElement.TryGetProperty("phraseHighlightRules", out var rulesElement)
            && !doc.RootElement.TryGetProperty("PhraseHighlightRules", out rulesElement))
        {
            return [];
        }

        if (rulesElement.ValueKind != JsonValueKind.Array)
            return [];

        return JsonSerializer.Deserialize<List<PhraseHighlightRule>>(rulesElement.GetRawText()) ?? [];
    }

    public bool TryValidate(out string? errorMessage)
    {
        CommitEditorToSelection();

        var nonEmpty = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .ToList();

        if (_rows.Any(r => !string.IsNullOrWhiteSpace(r.Phrase)) && nonEmpty.Count == 0)
        {
            errorMessage = "Each rule needs a non-empty phrase.";
            ShowValidation(errorMessage);
            return false;
        }

        if (_rows.Any(r => string.IsNullOrWhiteSpace(r.Phrase) && HasOtherFields(r)))
        {
            errorMessage = "Remove empty rules or enter a phrase for each row.";
            ShowValidation(errorMessage);
            return false;
        }

        HideValidation();
        errorMessage = null;
        return true;
    }

    private Brush SwatchBorderBrush =>
        (Brush)FindResource("BorderStrongBrush");

    private void BuildSwatches(WrapPanel panel, string kind)
    {
        panel.Children.Clear();
        foreach (var color in PresetColors)
        {
            var swatch = CreatePaletteSwatch(color, selectable: true);
            swatch.MouseLeftButtonUp += (_, _) => OnSwatchClicked(kind, color);
            panel.Children.Add(swatch);
        }
    }

    private Border CreatePaletteSwatch(string color, bool selectable)
    {
        var swatch = new Border
        {
            Width = SwatchSize,
            Height = SwatchSize,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(SwatchRadius),
            Background = CreateBrush(color),
            BorderBrush = SwatchBorderBrush,
            BorderThickness = new Thickness(1),
            Tag = color,
            ToolTip = color,
        };

        if (selectable)
            swatch.Cursor = Cursors.Hand;

        return swatch;
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    private void OnSwatchClicked(string kind, string color)
    {
        if (_activeEditorRow is not RuleRow row)
            return;

        if (kind == "text")
        {
            var canvas = string.IsNullOrWhiteSpace(row.BackgroundColor)
                ? ResolveHighlightCanvasBackground()
                : NormalizeColor(row.BackgroundColor, row.BackgroundColor);
            var readable = ThemeContrast.EnsureReadable(NormalizeColor(color, DefaultColor), canvas);
            row.Color = readable;
            ColorTextBox.Text = readable;
        }
        else if (kind == "border")
        {
            var border = NormalizeColor(color, color);
            row.BorderColor = border;
            BorderColorTextBox.Text = border;
        }
        else
        {
            var bg = NormalizeColor(color, color);
            row.BackgroundColor = bg;
            BackgroundColorTextBox.Text = bg;
            var canvas = ResolveHighlightCanvasBackground();
            var readable = ThemeContrast.EnsureReadable(NormalizeColor(row.Color, DefaultColor), bg);
            if (!string.Equals(row.Color, readable, StringComparison.OrdinalIgnoreCase))
            {
                row.Color = readable;
                ColorTextBox.Text = readable;
            }
        }

        UpdateSwatchSelection();
        UpdatePreview();
        PropagateAllSyncFromRow(row);
        NotifyRulesChanged();
    }

    private void PickTextColorButton_Click(object sender, RoutedEventArgs e) =>
        PickColorForField("text");

    private void PickBackgroundColorButton_Click(object sender, RoutedEventArgs e) =>
        PickColorForField("background");

    private void PickBorderColorButton_Click(object sender, RoutedEventArgs e) =>
        PickColorForField("border");

    private void TextTransformCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RuleField_Changed(sender, e);

    private void PickColorForField(string kind)
    {
        if (_activeEditorRow is null)
            return;

        var current = kind switch
        {
            "text" => ColorTextBox.Text.Trim(),
            "border" => BorderColorTextBox.Text.Trim(),
            _ => BackgroundColorTextBox.Text.Trim(),
        };
        if (string.IsNullOrWhiteSpace(current))
            current = DefaultColor;

        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        var background = kind == "text"
            ? ColorPickerContextResolver.ResolveHighlightTextBackground(
                _activeEditorRow.BackgroundColor,
                userSegmentBackground: null,
                assistantSegmentBackground: null,
                ResolveHighlightCanvasBackground())
            : ResolveHighlightCanvasBackground();
        var context = kind == "text"
            ? ColorPickerContextFactory.ForHighlightText(
                _activeEditorRow.BackgroundColor,
                background,
                ColorTextBox.Text.Trim())
            : ColorPickerContextFactory.ForHighlightBackground(background);

        if (!ColorPickerWorkflow.TryPickHex(owner, current, background, context, out var selected))
            return;

        if (kind == "text")
            ColorTextBox.Text = selected;
        else if (kind == "border")
            BorderColorTextBox.Text = selected;
        else
            BackgroundColorTextBox.Text = selected;

        RuleField_Changed(this, new RoutedEventArgs());
    }

    private void AssignColorButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0)
            return;

        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        var seed = selected[0].Color;
        var canvas = ResolveHighlightCanvasBackground();
        var context = ColorPickerContextFactory.ForHighlightText(null, canvas, seed);
        if (!ColorPickerWorkflow.TryPickHex(owner, seed, canvas, context, out var picked))
            return;

        var color = ThemeContrast.EnsureReadable(picked, canvas);
        foreach (var row in selected)
            row.Color = color;

        if (_activeEditorRow is RuleRow active)
            ColorTextBox.Text = active.Color;

        foreach (var row in selected)
            PropagateAllSyncFromRow(row);

        UpdateSwatchSelection();
        UpdatePreview();
        NotifyRulesChanged();
    }

    private void ColorSettingsButton_Click(object sender, RoutedEventArgs e) => ShowEditorView(rules: false);

    private void BackToRulesButton_Click(object sender, RoutedEventArgs e) => ShowEditorView(rules: true);

    private void ShowEditorView(bool rules)
    {
        if (RulesViewPanel is not null)
            RulesViewPanel.Visibility = rules ? Visibility.Visible : Visibility.Collapsed;
        if (AutoColorsViewPanel is not null)
            AutoColorsViewPanel.Visibility = rules ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RulesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitActiveEditorRow();

        var selected = GetSelectedRows();
        var hasSelection = selected.Count > 0;
        var singleSelection = selected.Count == 1;
        var multiSelection = selected.Count > 1;

        RemoveButton.IsEnabled = hasSelection;
        DuplicateButton.IsEnabled = hasSelection;
        AssignColorButton.IsEnabled = hasSelection;
        ReassignColorsButton.IsEnabled = hasSelection;
        UpdateMoveButtons();

        if (singleSelection)
        {
            SingleRuleEditorPanel.Visibility = Visibility.Visible;
            BulkInspectorPanel.Visibility = Visibility.Collapsed;
            LoadEditor(selected[0]);
        }
        else if (multiSelection)
        {
            SingleRuleEditorPanel.Visibility = Visibility.Collapsed;
            BulkInspectorPanel.Visibility = Visibility.Visible;
            BulkSelectionTitleText.Text = $"{selected.Count} rules selected";
            _activeEditorRow = null;
        }
        else
        {
            SingleRuleEditorPanel.Visibility = Visibility.Visible;
            BulkInspectorPanel.Visibility = Visibility.Collapsed;
            ClearEditor();
        }

        UpdateFilterStatusText();
        NotifyRulesChanged();
    }

    private void RefreshArrangementMetadata()
    {
        var rules = _rows.Select(r => r.ToRule()).ToList();
        var profile = ResolveEffectiveGroupingProfile();
        foreach (var row in _rows)
        {
            row.RefreshGroupingDisplay(rules, profile);
            row.RefreshArrangementMetadata(rules, profile);
        }

        ApplyRuleListArrangement();
    }

    private void ApplyRuleListArrangement() =>
        PhraseHighlightRuleListArrangement.Apply(_rulesViewSource.View, _ruleSortMode, _ruleGroupMode);

    private void SetRuleSortMode(PhraseHighlightRuleSortMode mode)
    {
        _ruleSortMode = mode;
        ApplyRuleListArrangement();
        UpdateMoveButtons();
        UpdateFilterStatusText();
    }

    private void SetRuleGroupMode(PhraseHighlightRuleGroupMode mode)
    {
        _ruleGroupMode = mode;
        ApplyRuleListArrangement();
        UpdateMoveButtons();
        UpdateFilterStatusText();
    }

    private void SelectAliasFamilyForRows(IEnumerable<RuleRow> seeds)
    {
        var rules = _rows.Select(r => r.ToRule()).ToList();
        var selected = new HashSet<RuleRow>();
        foreach (var seed in seeds)
        {
            foreach (var rule in PhraseHighlightRuleListArrangement.ResolveStyleSyncGroup(rules, seed.ToRule()))
            {
                var row = FindRowForRule(rule);
                if (row is not null)
                    selected.Add(row);
            }
        }

        if (selected.Count == 0)
            return;

        SetSelection(selected);
    }

    private RuleRow? FindRowForRule(PhraseHighlightRule rule) =>
        _rows.FirstOrDefault(r =>
            string.Equals(r.Phrase, rule.Phrase, StringComparison.OrdinalIgnoreCase)
            && r.EntityId == rule.EntityId
            && string.Equals(r.EntityCategory, rule.EntityCategory, StringComparison.OrdinalIgnoreCase));

    private void RulesListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var item = ItemsControl.ContainerFromElement(RulesListView, source) as ListViewItem;
        if (item?.DataContext is not RuleRow row)
            return;

        if (!RulesListView.SelectedItems.Contains(row))
        {
            RulesListView.SelectedItems.Clear();
            RulesListView.SelectedItems.Add(row);
        }
    }

    private void RulesListContextMenu_Opened(object sender, RoutedEventArgs e) =>
        UpdateRulesListContextMenu();

    private void UpdateRulesListContextMenu()
    {
        var selected = GetSelectedRows();
        var hasSelection = selected.Count > 0;
        var singleSelection = selected.Count == 1;
        var rules = _rows.Select(r => r.ToRule()).ToList();
        var canSelectAliasFamily = selected.Any(row =>
            PhraseHighlightRuleListArrangement.HasExpandableStyleSyncGroup(rules, row.ToRule()));
        var canMove = singleSelection
            && PhraseHighlightRuleListArrangement.CanMoveInManualOrder(_ruleSortMode, _ruleGroupMode);

        if (SelectAliasFamilyMenuItem is not null)
            SelectAliasFamilyMenuItem.IsEnabled = canSelectAliasFamily;
        if (ContextDuplicateMenuItem is not null)
            ContextDuplicateMenuItem.IsEnabled = hasSelection;
        if (ContextRemoveMenuItem is not null)
            ContextRemoveMenuItem.IsEnabled = hasSelection;
        if (ContextMoveUpMenuItem is not null)
            ContextMoveUpMenuItem.IsEnabled = canMove;
        if (ContextMoveDownMenuItem is not null)
            ContextMoveDownMenuItem.IsEnabled = canMove;
        if (ContextBulkEnableMenuItem is not null)
            ContextBulkEnableMenuItem.IsEnabled = hasSelection;
        if (ContextBulkDisableMenuItem is not null)
            ContextBulkDisableMenuItem.IsEnabled = hasSelection;
        if (ContextBulkBolderMenuItem is not null)
            ContextBulkBolderMenuItem.IsEnabled = hasSelection;
        if (ContextBulkMatchTextMenuItem is not null)
            ContextBulkMatchTextMenuItem.IsEnabled = hasSelection;
        if (ContextBulkItalicOnMenuItem is not null)
            ContextBulkItalicOnMenuItem.IsEnabled = hasSelection;
        if (ContextBulkItalicOffMenuItem is not null)
            ContextBulkItalicOffMenuItem.IsEnabled = hasSelection;
        if (ContextBulkClearBackgroundMenuItem is not null)
            ContextBulkClearBackgroundMenuItem.IsEnabled = hasSelection;
        if (ContextBulkPickColorMenuItem is not null)
            ContextBulkPickColorMenuItem.IsEnabled = hasSelection;
        if (ContextBulkRerollMenuItem is not null)
            ContextBulkRerollMenuItem.IsEnabled = hasSelection;

        UpdateArrangementMenuChecks();
    }

    private void UpdateArrangementMenuChecks()
    {
        SetMenuItemChecked(SortManualMenuItem, _ruleSortMode == PhraseHighlightRuleSortMode.Manual);
        SetMenuItemChecked(SortPhraseAscendingMenuItem, _ruleSortMode == PhraseHighlightRuleSortMode.PhraseAscending);
        SetMenuItemChecked(SortPhraseDescendingMenuItem, _ruleSortMode == PhraseHighlightRuleSortMode.PhraseDescending);
        SetMenuItemChecked(SortEntityTypeMenuItem, _ruleSortMode == PhraseHighlightRuleSortMode.EntityType);
        SetMenuItemChecked(SortColorGroupMenuItem, _ruleSortMode == PhraseHighlightRuleSortMode.ColorGroup);
        SetMenuItemChecked(SortLinkTypeMenuItem, _ruleSortMode == PhraseHighlightRuleSortMode.LinkType);
        SetMenuItemChecked(SortEnabledFirstMenuItem, _ruleSortMode == PhraseHighlightRuleSortMode.EnabledFirst);

        SetMenuItemChecked(GroupNoneMenuItem, _ruleGroupMode == PhraseHighlightRuleGroupMode.None);
        SetMenuItemChecked(GroupPrimaryAliasMenuItem, _ruleGroupMode == PhraseHighlightRuleGroupMode.PrimaryAliasFamily);
        SetMenuItemChecked(GroupEntityTypeMenuItem, _ruleGroupMode == PhraseHighlightRuleGroupMode.EntityType);
        SetMenuItemChecked(GroupColorGroupMenuItem, _ruleGroupMode == PhraseHighlightRuleGroupMode.ColorGroup);
        SetMenuItemChecked(GroupLinkTypeMenuItem, _ruleGroupMode == PhraseHighlightRuleGroupMode.LinkType);
    }

    private static void SetMenuItemChecked(MenuItem? item, bool isChecked)
    {
        if (item is not null)
            item.IsChecked = isChecked;
    }

    private void SelectAliasFamilyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var seeds = GetSelectedRows();
        if (seeds.Count == 0 && RulesListView.SelectedItem is RuleRow row)
            seeds = [row];
        SelectAliasFamilyForRows(seeds);
    }

    private void SortManualMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleSortMode(PhraseHighlightRuleSortMode.Manual);

    private void SortPhraseAscendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleSortMode(PhraseHighlightRuleSortMode.PhraseAscending);

    private void SortPhraseDescendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleSortMode(PhraseHighlightRuleSortMode.PhraseDescending);

    private void SortEntityTypeMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleSortMode(PhraseHighlightRuleSortMode.EntityType);

    private void SortColorGroupMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleSortMode(PhraseHighlightRuleSortMode.ColorGroup);

    private void SortLinkTypeMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleSortMode(PhraseHighlightRuleSortMode.LinkType);

    private void SortEnabledFirstMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleSortMode(PhraseHighlightRuleSortMode.EnabledFirst);

    private void GroupNoneMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleGroupMode(PhraseHighlightRuleGroupMode.None);

    private void GroupPrimaryAliasMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleGroupMode(PhraseHighlightRuleGroupMode.PrimaryAliasFamily);

    private void GroupEntityTypeMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleGroupMode(PhraseHighlightRuleGroupMode.EntityType);

    private void GroupColorGroupMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleGroupMode(PhraseHighlightRuleGroupMode.ColorGroup);

    private void GroupLinkTypeMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetRuleGroupMode(PhraseHighlightRuleGroupMode.LinkType);

    private void UpdateMoveButtons()
    {
        var selected = GetSelectedRows();
        var singleSelection = selected.Count == 1;
        var canReorder = PhraseHighlightRuleListArrangement.CanMoveInManualOrder(_ruleSortMode, _ruleGroupMode);
        var primaryIndex = singleSelection ? _rows.IndexOf(selected[0]) : -1;
        var canMoveUp = canReorder && singleSelection && primaryIndex > 0;
        var canMoveDown = canReorder && singleSelection && primaryIndex >= 0 && primaryIndex < _rows.Count - 1;

        if (MoveUpButton is not null)
            MoveUpButton.IsEnabled = canMoveUp;
        if (MoveDownButton is not null)
            MoveDownButton.IsEnabled = canMoveDown;
        if (MoveUpMenuItem is not null)
            MoveUpMenuItem.IsEnabled = canMoveUp;
        if (MoveDownMenuItem is not null)
            MoveDownMenuItem.IsEnabled = canMoveDown;
    }

    private void SelectAllRulesButton_Click(object sender, RoutedEventArgs e) =>
        SetSelection(_rows);

    private void SelectNoneRulesButton_Click(object sender, RoutedEventArgs e) =>
        RulesListView.SelectedItems.Clear();

    private void SelectFilteredRulesButton_Click(object sender, RoutedEventArgs e) =>
        SetSelection(GetVisibleRows());

    private void InvertSelectionRulesButton_Click(object sender, RoutedEventArgs e)
    {
        var visible = GetVisibleRows().ToHashSet();
        var next = visible.Where(r => !RulesListView.SelectedItems.Contains(r))
            .Concat(RulesListView.SelectedItems.Cast<RuleRow>().Where(r => !visible.Contains(r)))
            .ToList();
        SetSelection(next);
    }

    private void BulkEnableButton_Click(object sender, RoutedEventArgs e) => SetEnabledOnSelected(true);

    private void BulkDisableButton_Click(object sender, RoutedEventArgs e) => SetEnabledOnSelected(false);

    private void BulkBolderButton_Click(object sender, RoutedEventArgs e) =>
        SetFontWeightModeOnSelected(PhraseHighlightFontWeightMode.Bolder);

    private void BulkMatchTextWeightButton_Click(object sender, RoutedEventArgs e) =>
        SetFontWeightModeOnSelected(PhraseHighlightFontWeightMode.MatchRole);

    private void BulkItalicOnButton_Click(object sender, RoutedEventArgs e) => SetItalicOnSelected(true);

    private void BulkItalicOffButton_Click(object sender, RoutedEventArgs e) => SetItalicOnSelected(false);

    private void BulkClearBackgroundButton_Click(object sender, RoutedEventArgs e) => ClearBackgroundOnSelected();

    private void SetEnabledOnSelected(bool enabled)
    {
        foreach (var row in GetSelectedRows())
            row.Enabled = enabled;
        if (_activeEditorRow is not null)
            EnabledCheckBox.IsChecked = enabled;
        foreach (var row in GetSelectedRows())
            PropagateAllSyncFromRow(row);
        NotifyRulesChanged();
    }

    private void SetFontWeightModeOnSelected(PhraseHighlightFontWeightMode mode)
    {
        foreach (var row in GetSelectedRows())
            ApplyFontWeightModeToRow(row, mode);

        if (_activeEditorRow is not null)
            LoadFontWeightChoiceFromRow(_activeEditorRow);

        foreach (var row in GetSelectedRows())
            PropagateAllSyncFromRow(row);

        NotifyRulesChanged();
    }

    private static void ApplyFontWeightModeToRow(RuleRow row, PhraseHighlightFontWeightMode mode, int? absolute = null) =>
        row.ApplyFontWeightChoice(mode, absolute);

    private void SetItalicOnSelected(bool italic)
    {
        foreach (var row in GetSelectedRows())
            row.Italic = italic;
        if (_activeEditorRow is not null)
            ItalicCheckBox.IsChecked = italic;
        foreach (var row in GetSelectedRows())
            PropagateAllSyncFromRow(row);
        NotifyRulesChanged();
    }

    private void ClearBackgroundOnSelected()
    {
        foreach (var row in GetSelectedRows())
            row.BackgroundColor = null;
        if (_activeEditorRow is not null)
            BackgroundColorTextBox.Text = "";
        foreach (var row in GetSelectedRows())
            PropagateAllSyncFromRow(row);
        NotifyRulesChanged();
    }

    private List<RuleRow> GetVisibleRows() =>
        _rulesViewSource.View?.Cast<RuleRow>().ToList() ?? [];

    private void SetSelection(IEnumerable<RuleRow> rows)
    {
        RulesListView.SelectedItems.Clear();
        foreach (var row in rows)
            RulesListView.SelectedItems.Add(row);
    }

    private void RulesListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && GetSelectedRows().Count > 0)
        {
            RemoveButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void RootControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FilterTextBox.Focus();
            FilterTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control
            && !PhraseTextBox.IsKeyboardFocusWithin
            && !FilterTextBox.IsKeyboardFocusWithin)
        {
            SetSelection(string.IsNullOrWhiteSpace(_filterText) ? _rows : GetVisibleRows());
            e.Handled = true;
        }
    }

    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterText = FilterTextBox.Text.Trim();
        _rulesViewSource.View?.Refresh();
        UpdateFilterStatusText();
    }

    private void RulesView_Filter(object sender, FilterEventArgs e)
    {
        if (e.Item is not RuleRow row)
        {
            e.Accepted = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(_filterText))
        {
            e.Accepted = true;
            return;
        }

        var query = _filterText;
        e.Accepted = row.Phrase.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.EntityTypeSummary.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.GroupSummary.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.AliasSummary.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.StyleSummary.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool _fontWeightChoiceComboInitialized;

    private void EnsureFontWeightChoiceComboItems()
    {
        if (_fontWeightChoiceComboInitialized)
            return;

        _fontWeightChoiceComboInitialized = true;
        FontWeightChoiceCombo.Items.Add(new ComboBoxItem
        {
            Content = "Match message text",
            Tag = PhraseHighlightFontWeightChoice.MatchRoleTag,
        });
        FontWeightChoiceCombo.Items.Add(new ComboBoxItem
        {
            Content = "Bolder than message text",
            Tag = PhraseHighlightFontWeightChoice.BolderTag,
        });
        FontWeightChoiceCombo.Items.Add(new ComboBoxItem { Content = "—", IsEnabled = false });
        foreach (var (value, label) in FormatFontWeights.NamedSteps)
        {
            FontWeightChoiceCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{label} ({value})",
                Tag = value.ToString(),
            });
        }

        FontWeightChoiceCombo.Items.Add(new ComboBoxItem
        {
            Content = "Custom weight…",
            Tag = PhraseHighlightFontWeightChoice.CustomTag,
        });
    }

    private void LoadFontWeightChoiceFromRow(RuleRow? row)
    {
        EnsureFontWeightChoiceComboItems();

        var rule = row?.ToRule() ?? new PhraseHighlightRule();
        var tag = PhraseHighlightFontWeightChoice.TryResolveComboTag(rule);
        SelectComboItemByTag(FontWeightChoiceCombo, tag);

        if (tag == PhraseHighlightFontWeightChoice.CustomTag && rule.FontWeight is int weight)
        {
            FontWeightCustomTextBox.Text = weight.ToString();
            FontWeightCustomTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            FontWeightCustomTextBox.Text = "";
            FontWeightCustomTextBox.Visibility = Visibility.Collapsed;
        }

        UpdateFontWeightHint(rule);
    }

    private void ApplyFontWeightChoiceToRow(RuleRow row)
    {
        var tag = GetSelectedFontWeightTag();
        if (tag is null)
            return;

        if (tag == PhraseHighlightFontWeightChoice.CustomTag)
        {
            var weight = ParseOptionalInt(FontWeightCustomTextBox.Text, 100, 900);
            if (weight is null)
                return;

            row.ApplyFontWeightChoice(PhraseHighlightFontWeightMode.Absolute, weight);
            UpdateFontWeightHint(row.ToRule());
            return;
        }

        var mode = tag switch
        {
            PhraseHighlightFontWeightChoice.MatchRoleTag => PhraseHighlightFontWeightMode.MatchRole,
            PhraseHighlightFontWeightChoice.BolderTag => PhraseHighlightFontWeightMode.Bolder,
            _ when int.TryParse(tag, out var named) => PhraseHighlightFontWeightMode.Absolute,
            _ => PhraseHighlightFontWeightMode.MatchRole,
        };

        int? absolute = mode == PhraseHighlightFontWeightMode.Absolute && int.TryParse(tag, out var value)
            ? value
            : null;

        if (mode == PhraseHighlightFontWeightMode.Absolute && absolute is null)
            return;

        row.ApplyFontWeightChoice(mode, absolute);
        UpdateFontWeightHint(row.ToRule());
    }

    private void FontWeightChoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEditorEvents)
            return;

        var tag = GetSelectedFontWeightTag();
        FontWeightCustomTextBox.Visibility = tag == PhraseHighlightFontWeightChoice.CustomTag
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (tag == PhraseHighlightFontWeightChoice.CustomTag)
        {
            UpdateFontWeightHint(BuildPreviewRule());
            return;
        }

        RuleField_Changed(sender, e);
    }

    private string? GetSelectedFontWeightTag() =>
        FontWeightChoiceCombo.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString()
            : null;

    private static void SelectComboItemByTag(ComboBox combo, string? tag)
    {
        combo.SelectedItem = null;
        if (string.IsNullOrWhiteSpace(tag))
            return;

        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void UpdateFontWeightHint(PhraseHighlightRule rule)
    {
        if (FontWeightHintText is null)
            return;

        FontWeightHintText.Text = PhraseHighlightFontWeightChoice.DescribeResolvedHint(
            rule,
            ResolvePreviewRoleFontWeight());
    }

    private int ResolvePreviewRoleFontWeight() =>
        _workingChrome?.ContinuousViewFormat?.AssistantFontWeight ?? 400;

    private void LoadEditor(RuleRow row)
    {
        _activeEditorRow = row;
        _suppressEditorEvents = true;
        try
        {
            EnabledCheckBox.IsChecked = row.Enabled;
            PhraseTextBox.Text = row.Phrase;
            ColorTextBox.Text = row.Color;
            BackgroundColorTextBox.Text = row.BackgroundColor ?? "";
            LoadFontWeightChoiceFromRow(row);
            ItalicCheckBox.IsChecked = row.Italic;
            UnderlineCheckBox.IsChecked = row.Underline;
            StrikethroughCheckBox.IsChecked = row.Strikethrough;
            FontSizeScaleTextBox.Text = FormatOptionalDouble(row.FontSizeScale);
            LetterSpacingTextBox.Text = FormatOptionalDouble(row.LetterSpacingEm);
            FontFamilyTextBox.Text = row.FontFamily ?? "";
            OpacityTextBox.Text = FormatOptionalDouble(row.Opacity);
            BorderWidthTextBox.Text = row.BorderWidthPx?.ToString() ?? "";
            BorderRadiusTextBox.Text = row.BorderRadiusPx?.ToString() ?? "";
            BorderColorTextBox.Text = row.BorderColor ?? "";
            PaddingXTextBox.Text = FormatOptionalDouble(row.PaddingXEm);
            PaddingYTextBox.Text = FormatOptionalDouble(row.PaddingYEm);
            TextShadowTextBox.Text = row.TextShadow ?? "";
            BoxShadowTextBox.Text = row.BoxShadow ?? "";
            SelectTextTransformCombo(row.TextTransform);
            SyncOverrideCheckBox.IsChecked = row.SyncOverride;
            GroupOverrideCheckBox.IsChecked = row.GroupOverride;
            UpdateAliasLinkUi(row);
            UpdateGroupLinkUi(row);
            UpdateSwatchSelection();
            UpdatePreview();
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private void ClearEditor()
    {
        _activeEditorRow = null;
        _suppressEditorEvents = true;
        try
        {
            EnabledCheckBox.IsChecked = true;
            PhraseTextBox.Text = "";
            ColorTextBox.Text = DefaultColor;
            BackgroundColorTextBox.Text = "";
            LoadFontWeightChoiceFromRow(null);
            ItalicCheckBox.IsChecked = false;
            UnderlineCheckBox.IsChecked = false;
            StrikethroughCheckBox.IsChecked = false;
            FontSizeScaleTextBox.Text = "";
            LetterSpacingTextBox.Text = "";
            FontFamilyTextBox.Text = "";
            OpacityTextBox.Text = "";
            BorderWidthTextBox.Text = "";
            BorderRadiusTextBox.Text = "";
            BorderColorTextBox.Text = "";
            PaddingXTextBox.Text = "";
            PaddingYTextBox.Text = "";
            TextShadowTextBox.Text = "";
            BoxShadowTextBox.Text = "";
            SelectTextTransformCombo(null);
            SyncOverrideCheckBox.IsChecked = false;
            GroupOverrideCheckBox.IsChecked = false;
            AliasLinkTextBlock.Visibility = Visibility.Collapsed;
            SyncOverrideCheckBox.Visibility = Visibility.Collapsed;
            GroupLinkTextBlock.Visibility = Visibility.Collapsed;
            GroupOverrideCheckBox.Visibility = Visibility.Collapsed;
            UpdateSharingExpanderVisibility();
            UpdateSwatchSelection();
            UpdatePreview();
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private void RuleField_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEditorEvents)
            return;

        if (_activeEditorRow is not RuleRow row)
            return;

        var textColor = NormalizeColor(ColorTextBox.Text, DefaultColor);
        ApplyEditorFieldsToRow(row);

        if (!string.Equals(row.Color, textColor, StringComparison.OrdinalIgnoreCase))
            ColorTextBox.Text = row.Color;

        UpdateSwatchSelection();
        UpdatePreview();
        UpdateAliasLinkUi(row);
        PropagateStyleSyncFromRow(row);
        PropagateGroupSyncFromRow(row);
        NotifyRulesChanged();
    }

    private void SyncOverrideCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEditorEvents || _activeEditorRow is not RuleRow row)
            return;

        row.SyncOverride = SyncOverrideCheckBox.IsChecked == true;
        UpdateAliasLinkUi(row);
        if (!row.SyncOverride)
            PropagateAllSyncFromRow(row);
        NotifyRulesChanged();
    }

    private void GroupOverrideCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEditorEvents || _activeEditorRow is not RuleRow row)
            return;

        row.GroupOverride = GroupOverrideCheckBox.IsChecked == true;
        UpdateGroupLinkUi(row);
        RefreshAllGroupingDisplays();
        if (!row.GroupOverride)
            PropagateGroupSyncFromRow(row);
        NotifyRulesChanged();
    }

    private void ApplyEditorFieldsToRow(RuleRow row)
    {
        row.Enabled = EnabledCheckBox.IsChecked != false;
        row.Phrase = PhraseTextBox.Text.Trim();
        var textColor = NormalizeColor(ColorTextBox.Text, DefaultColor);
        var bg = BackgroundColorTextBox.Text.Trim();
        var backgroundColor = string.IsNullOrWhiteSpace(bg)
            ? null
            : NormalizeColor(bg, bg);
        var effectiveBackground = backgroundColor ?? ResolveHighlightCanvasBackground();
        row.Color = ThemeContrast.EnsureReadable(textColor, effectiveBackground);
        row.BackgroundColor = backgroundColor;
        ApplyFontWeightChoiceToRow(row);
        row.Italic = ItalicCheckBox.IsChecked == true;
        row.Underline = UnderlineCheckBox.IsChecked == true;
        row.Strikethrough = StrikethroughCheckBox.IsChecked == true;
        row.FontSizeScale = ParseOptionalDouble(FontSizeScaleTextBox.Text, 0.5, 2.5);
        row.LetterSpacingEm = ParseOptionalDouble(LetterSpacingTextBox.Text, -0.2, 0.5);
        row.FontFamily = string.IsNullOrWhiteSpace(FontFamilyTextBox.Text)
            ? null
            : FontFamilyTextBox.Text.Trim();
        row.TextTransform = ReadTextTransformSelection();
        row.Opacity = ParseOptionalDouble(OpacityTextBox.Text, 0.05, 1.0);
        row.BorderWidthPx = ParseOptionalInt(BorderWidthTextBox.Text, 0, 8);
        row.BorderRadiusPx = ParseOptionalInt(BorderRadiusTextBox.Text, 0, 24);
        var borderColor = BorderColorTextBox.Text.Trim();
        row.BorderColor = string.IsNullOrWhiteSpace(borderColor)
            ? null
            : NormalizeColor(borderColor, borderColor);
        row.PaddingXEm = ParseOptionalDouble(PaddingXTextBox.Text, 0, 1.0);
        row.PaddingYEm = ParseOptionalDouble(PaddingYTextBox.Text, 0, 1.0);
        row.TextShadow = string.IsNullOrWhiteSpace(TextShadowTextBox.Text)
            ? null
            : TextShadowTextBox.Text.Trim();
        row.BoxShadow = string.IsNullOrWhiteSpace(BoxShadowTextBox.Text)
            ? null
            : BoxShadowTextBox.Text.Trim();
        row.SyncOverride = SyncOverrideCheckBox.IsChecked == true;
        row.GroupOverride = GroupOverrideCheckBox.IsChecked == true;
    }

    private void ClearBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEditorRow is not RuleRow row)
            return;

        row.BackgroundColor = null;
        BackgroundColorTextBox.Text = "";
        UpdateSwatchSelection();
        UpdatePreview();
        PropagateAllSyncFromRow(row);
        NotifyRulesChanged();
    }

    private void UpdateAliasLinkUi(RuleRow row)
    {
        if (AliasLinkTextBlock is null || SyncOverrideCheckBox is null)
            return;

        var rules = _rows.Select(r => r.ToRule()).ToList();
        var group = PhraseHighlightRuleService.ResolveStyleSyncGroup(rules, row.ToRule());
        var isLinked = group.Count > 1
            || !string.IsNullOrWhiteSpace(row.SyncWithPhrase)
            || (row.EntityId is not null && CountEntityAliasPeers(row) > 0);

        if (!isLinked)
        {
            AliasLinkTextBlock.Visibility = Visibility.Collapsed;
            SyncOverrideCheckBox.Visibility = Visibility.Collapsed;
            UpdateSharingExpanderVisibility();
            return;
        }

        AliasLinkTextBlock.Visibility = Visibility.Visible;
        SyncOverrideCheckBox.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(row.SyncWithPhrase))
        {
            var aliasCount = group.Count - 1;
            AliasLinkTextBlock.Text = row.SyncOverride
                ? $"Alias of “{row.SyncWithPhrase}” — sync overridden"
                : aliasCount > 0
                    ? $"Alias of “{row.SyncWithPhrase}” — synced with {aliasCount} linked rule{(aliasCount == 1 ? "" : "s")}"
                    : $"Alias of “{row.SyncWithPhrase}”";
            UpdateSharingExpanderVisibility();
            return;
        }

        var peerCount = group.Count - 1;
        AliasLinkTextBlock.Text = row.SyncOverride
            ? "Primary rule — sync overridden"
            : peerCount > 0
                ? $"Primary rule — {peerCount} alias{(peerCount == 1 ? "" : "es")} synced"
                : "Primary rule";
        UpdateSharingExpanderVisibility();
    }

    private int CountEntityAliasPeers(RuleRow row)
    {
        if (row.EntityId is null)
            return 0;

        return _rows.Count(r =>
            r != row
            && r.EntityId == row.EntityId
            && string.Equals(r.EntityCategory, row.EntityCategory, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(r.Phrase, row.Phrase, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateGroupLinkUi(RuleRow row)
    {
        if (GroupLinkTextBlock is null || GroupOverrideCheckBox is null)
            return;

        var profile = ResolveEffectiveGroupingProfile();
        var rules = _rows.Where(r => !string.IsNullOrWhiteSpace(r.Phrase)).Select(r => r.ToRule()).ToList();
        var display = PhraseHighlightGroupSyncService.ResolveDisplay(row.ToRule(), rules, profile);
        var peers = PhraseHighlightGroupSyncService.ResolveSharedColorGroupPeers(rules, row.ToRule(), profile).Count;

        if (!display.IsGroupingActive)
        {
            GroupLinkTextBlock.Visibility = Visibility.Collapsed;
            GroupOverrideCheckBox.Visibility = Visibility.Collapsed;
            UpdateSharingExpanderVisibility();
            return;
        }

        GroupLinkTextBlock.Visibility = Visibility.Visible;
        GroupOverrideCheckBox.Visibility = display.ShareColorWithinGroup ? Visibility.Visible : Visibility.Collapsed;

        if (display.IsExcluded)
        {
            GroupLinkTextBlock.Text = $"Excluded from automatic coloring ({display.GroupName}).";
            UpdateSharingExpanderVisibility();
            return;
        }

        if (!display.ShareColorWithinGroup)
        {
            GroupLinkTextBlock.Text = $"Group: {display.GroupName} — distinct color within group.";
            UpdateSharingExpanderVisibility();
            return;
        }

        GroupLinkTextBlock.Text = row.GroupOverride
            ? $"Group: {display.GroupName} — override active"
            : peers > 0
                ? $"Group: {display.GroupName} — shared with {peers} other rule{(peers == 1 ? "" : "s")}"
                : $"Group: {display.GroupName} — shared color";
        UpdateSharingExpanderVisibility();
    }

    private void UpdateSharingExpanderVisibility()
    {
        if (SharingExpander is null)
            return;

        var hasAlias = AliasLinkTextBlock?.Visibility == Visibility.Visible
            || SyncOverrideCheckBox?.Visibility == Visibility.Visible;
        var hasGroup = GroupLinkTextBlock?.Visibility == Visibility.Visible
            || GroupOverrideCheckBox?.Visibility == Visibility.Visible;
        SharingExpander.Visibility = hasAlias || hasGroup ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PropagateAllSyncFromRow(RuleRow source)
    {
        PropagateStyleSyncFromRow(source);
        PropagateGroupSyncFromRow(source);
    }

    private void PropagateStyleSyncFromRow(RuleRow source)
    {
        if (source.SyncOverride)
            return;

        var rules = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => r.ToRule())
            .ToList();
        var sourceRule = rules.FirstOrDefault(r =>
            string.Equals(r.Phrase, source.Phrase, StringComparison.OrdinalIgnoreCase)
            && r.EntityId == source.EntityId
            && string.Equals(r.EntityCategory, source.EntityCategory, StringComparison.OrdinalIgnoreCase));
        if (sourceRule is null)
            return;

        PhraseHighlightRuleService.PropagateStyleSync(rules, sourceRule);

        foreach (var rule in rules)
        {
            if (string.Equals(rule.Phrase, source.Phrase, StringComparison.OrdinalIgnoreCase)
                && rule.EntityId == source.EntityId
                && string.Equals(rule.EntityCategory, source.EntityCategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var row = _rows.FirstOrDefault(r =>
                string.Equals(r.Phrase, rule.Phrase, StringComparison.OrdinalIgnoreCase)
                && r.EntityId == rule.EntityId
                && string.Equals(r.EntityCategory, rule.EntityCategory, StringComparison.OrdinalIgnoreCase));
            if (row is null)
                continue;

            ApplyRuleStyleToRow(row, rule);
        }

        if (_activeEditorRow is not null
            && !ReferenceEquals(_activeEditorRow, source)
            && _rows.Contains(_activeEditorRow))
        {
            _suppressEditorEvents = true;
            try
            {
                LoadEditor(_activeEditorRow);
            }
            finally
            {
                _suppressEditorEvents = false;
            }
        }
    }

    private void PropagateGroupSyncFromRow(RuleRow source)
    {
        if (source.GroupOverride)
            return;

        var profile = ResolveEffectiveGroupingProfile();
        if (profile is null)
            return;

        var rules = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => r.ToRule())
            .ToList();
        var sourceRule = rules.FirstOrDefault(r =>
            string.Equals(r.Phrase, source.Phrase, StringComparison.OrdinalIgnoreCase)
            && r.EntityId == source.EntityId
            && string.Equals(r.EntityCategory, source.EntityCategory, StringComparison.OrdinalIgnoreCase));
        if (sourceRule is null)
            return;

        PhraseHighlightGroupSyncService.PropagateGroupStyleSync(rules, sourceRule, profile);

        foreach (var rule in rules)
        {
            if (string.Equals(rule.Phrase, source.Phrase, StringComparison.OrdinalIgnoreCase)
                && rule.EntityId == source.EntityId
                && string.Equals(rule.EntityCategory, source.EntityCategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var row = _rows.FirstOrDefault(r =>
                string.Equals(r.Phrase, rule.Phrase, StringComparison.OrdinalIgnoreCase)
                && r.EntityId == rule.EntityId
                && string.Equals(r.EntityCategory, rule.EntityCategory, StringComparison.OrdinalIgnoreCase));
            if (row is null)
                continue;

            ApplyRuleStyleToRow(row, rule);
        }

        RefreshAllGroupingDisplays();

        if (_activeEditorRow is not null
            && !ReferenceEquals(_activeEditorRow, source)
            && _rows.Contains(_activeEditorRow))
        {
            _suppressEditorEvents = true;
            try
            {
                LoadEditor(_activeEditorRow);
            }
            finally
            {
                _suppressEditorEvents = false;
            }
        }
    }

    private void RefreshAllGroupingDisplays()
    {
        var profile = ResolveEffectiveGroupingProfile();
        var rules = _rows.Where(r => !string.IsNullOrWhiteSpace(r.Phrase)).Select(r => r.ToRule()).ToList();
        foreach (var row in _rows)
            row.RefreshGroupingDisplay(rules, profile);
    }

    private void ReconcileSharedGroupColors()
    {
        var profile = ResolveEffectiveGroupingProfile();
        if (profile is null)
            return;

        var rules = _rows.Where(r => !string.IsNullOrWhiteSpace(r.Phrase)).Select(r => r.ToRule()).ToList();
        PhraseHighlightGroupSyncService.ReconcileSharedGroupColors(rules, profile);
        ApplyReassignedRulesToRows(rules);
        RefreshAllGroupingDisplays();
    }

    private static void ApplyRuleStyleToRow(RuleRow row, PhraseHighlightRule rule) =>
        row.ApplySyncedStyleFrom(rule);

    private void UpdateSwatchSelection()
    {
        var textColor = NormalizeColor(ColorTextBox.Text, DefaultColor);
        var bgColor = BackgroundColorTextBox.Text.Trim();
        var bgNormalized = string.IsNullOrWhiteSpace(bgColor)
            ? null
            : NormalizeColor(bgColor, bgColor);

        UpdateSwatchPanelSelection(TextColorSwatchesPanel, textColor);
        UpdateSwatchPanelSelection(BackgroundColorSwatchesPanel, bgNormalized);
    }

    private void UpdateSwatchPanelSelection(Panel panel, string? selectedColor)
    {
        foreach (var child in panel.Children)
        {
            if (child is not Border border || border.Tag is not string swatchColor)
                continue;

            var selected = !string.IsNullOrWhiteSpace(selectedColor)
                && string.Equals(swatchColor, selectedColor, StringComparison.OrdinalIgnoreCase);
            border.BorderBrush = selected
                ? new SolidColorBrush(Colors.White)
                : SwatchBorderBrush;
            border.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void UpdateRulesEmptyState()
    {
        if (RulesEmptyStateBorder is null || RulesListView is null)
            return;

        var isEmpty = _rows.Count == 0;
        RulesEmptyStateBorder.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RulesListView.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (RulesListCommandBar is not null)
            RulesListCommandBar.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateRuleCountText() => UpdateFilterStatusText();

    private void UpdateFilterStatusText()
    {
        if (FilterStatusText is null)
            return;

        var total = _rows.Count(r => !string.IsNullOrWhiteSpace(r.Phrase));
        if (total == 0)
        {
            FilterStatusText.Text = "";
            return;
        }

        var selected = GetSelectedRows().Count;
        var distinctColors = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => r.Color)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var selectionPrefix = selected > 0 ? $"{selected} selected · " : "";
        var arrangementSuffix = _ruleSortMode != PhraseHighlightRuleSortMode.Manual
            || _ruleGroupMode != PhraseHighlightRuleGroupMode.None
            ? $" · {DescribeArrangementSummary()}"
            : "";

        if (string.IsNullOrWhiteSpace(_filterText))
        {
            FilterStatusText.Text = selectionPrefix + (distinctColors > 0
                ? $"{total} · {distinctColors} colors{arrangementSuffix}"
                : $"{total} rule{(total == 1 ? "" : "s")}{arrangementSuffix}");
            return;
        }

        var visible = _rulesViewSource.View?.Cast<object>().Count() ?? 0;
        FilterStatusText.Text = $"{selectionPrefix}{visible}/{total}{arrangementSuffix}";
    }

    private string DescribeArrangementSummary()
    {
        if (_ruleGroupMode != PhraseHighlightRuleGroupMode.None
            && _ruleSortMode == PhraseHighlightRuleSortMode.Manual
            && _ruleGroupMode == PhraseHighlightRuleGroupMode.PrimaryAliasFamily)
        {
            return $"grouped: {PhraseHighlightRuleListArrangement.DescribeGroupMode(_ruleGroupMode)}";
        }

        if (_ruleGroupMode != PhraseHighlightRuleGroupMode.None
            && _ruleSortMode != PhraseHighlightRuleSortMode.Manual)
        {
            return $"{PhraseHighlightRuleListArrangement.DescribeSortMode(_ruleSortMode)}, {PhraseHighlightRuleListArrangement.DescribeGroupMode(_ruleGroupMode).ToLowerInvariant()}";
        }

        if (_ruleGroupMode != PhraseHighlightRuleGroupMode.None)
            return $"grouped: {PhraseHighlightRuleListArrangement.DescribeGroupMode(_ruleGroupMode)}";

        return PhraseHighlightRuleListArrangement.DescribeSortMode(_ruleSortMode);
    }

    private void UpdatePreview()
    {
        var phrase = string.IsNullOrWhiteSpace(PhraseTextBox.Text)
            ? "Sample phrase"
            : PhraseTextBox.Text.Trim();
        var previewRule = BuildPreviewRule();
        PreviewTextBlock.Text = ApplyTextTransformPreview(phrase, previewRule.TextTransform);

        var canvas = string.IsNullOrWhiteSpace(previewRule.BackgroundColor)
            ? ResolveHighlightCanvasBackground()
            : previewRule.BackgroundColor!;
        var readableText = ThemeContrast.EnsureReadable(previewRule.Color, canvas);

        PreviewTextBlock.Foreground = CreateBrush(readableText);
        PreviewTextBlock.Background = string.IsNullOrWhiteSpace(previewRule.BackgroundColor)
            ? Brushes.Transparent
            : CreateBrush(previewRule.BackgroundColor);

        PreviewTextBlock.FontWeight = FontWeight.FromOpenTypeWeight(
            PhraseHighlightStyleResolver.ResolveFontWeight(previewRule, ResolvePreviewRoleFontWeight()));
        PreviewTextBlock.FontStyle = previewRule.Italic ? FontStyles.Italic : FontStyles.Normal;
        PreviewTextBlock.TextDecorations = BuildPreviewDecorations(previewRule);
        PreviewTextBlock.FontSize = previewRule.FontSizeScale is double scale ? 14 * scale : 14;
        PreviewTextBlock.Opacity = previewRule.Opacity ?? (previewRule.Enabled ? 1.0 : 0.55);

        if (!string.IsNullOrWhiteSpace(previewRule.FontFamily))
        {
            var family = FormatFontFamilies.ResolveWpfFontFamily(previewRule.FontFamily);
            if (family is not null)
                PreviewTextBlock.FontFamily = family;
        }
        else
        {
            PreviewTextBlock.ClearValue(TextBlock.FontFamilyProperty);
        }

        PreviewTextBlock.Padding = new Thickness(
            (previewRule.PaddingXEm ?? 0) * 14,
            (previewRule.PaddingYEm ?? 0) * 14,
            (previewRule.PaddingXEm ?? 0) * 14,
            (previewRule.PaddingYEm ?? 0) * 14);

        if (previewRule.BorderWidthPx is > 0)
        {
            var borderBrush = string.IsNullOrWhiteSpace(previewRule.BorderColor)
                ? readableText
                : previewRule.BorderColor!;
            PreviewSampleBorder.BorderBrush = CreateBrush(NormalizeColor(borderBrush, borderBrush));
            PreviewSampleBorder.BorderThickness = new Thickness(previewRule.BorderWidthPx.Value);
        }
        else
        {
            PreviewSampleBorder.BorderBrush = (Brush)FindResource("BorderSubtleBrush");
            PreviewSampleBorder.BorderThickness = new Thickness(1);
        }

        PreviewSampleBorder.CornerRadius = previewRule.BorderRadiusPx is int radius
            ? new CornerRadius(radius)
            : (CornerRadius)FindResource("RadiusControl");

        UpdateFontWeightHint(previewRule);
    }

    private PhraseHighlightRule BuildPreviewRule()
    {
        if (_activeEditorRow is RuleRow row)
        {
            ApplyEditorFieldsToRow(row);
            return row.ToRule();
        }

        var draft = new RuleRow();
        ApplyEditorFieldsToRow(draft);
        return draft.ToRule();
    }

    private static string ApplyTextTransformPreview(string text, string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform))
            return text;

        return transform.Trim().ToLowerInvariant() switch
        {
            "uppercase" => text.ToUpperInvariant(),
            "lowercase" => text.ToLowerInvariant(),
            "capitalize" => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
            _ => text,
        };
    }

    private static TextDecorationCollection? BuildPreviewDecorations(PhraseHighlightRule rule)
    {
        if (!rule.Underline && !rule.Strikethrough)
            return null;

        var decorations = new TextDecorationCollection();
        if (rule.Underline)
            decorations.Add(TextDecorations.Underline);
        if (rule.Strikethrough)
            decorations.Add(TextDecorations.Strikethrough);
        return decorations;
    }

    private static string FormatOptionalDouble(double? value) =>
        value is null ? "" : value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static int? ParseOptionalInt(string? text, int min, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return int.TryParse(trimmed, out var value)
            ? Math.Clamp(value, min, max)
            : null;
    }

    private static double? ParseOptionalDouble(string? text, double min, double max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : null;
    }

    private void SelectTextTransformCombo(string? value)
    {
        if (TextTransformCombo is null)
            return;

        _suppressEditorEvents = true;
        try
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
            foreach (ComboBoxItem item in TextTransformCombo.Items)
            {
                var tag = item.Tag?.ToString() ?? "";
                if (tag.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    TextTransformCombo.SelectedItem = item;
                    return;
                }
            }

            TextTransformCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private string? ReadTextTransformSelection()
    {
        if (TextTransformCombo?.SelectedItem is not ComboBoxItem item)
            return null;

        var tag = item.Tag?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        HideValidation();
        var row = new RuleRow
        {
            Phrase = "example",
            Color = DefaultColor,
        };
        _rows.Add(row);
        RulesListView.SelectedIndex = _rows.Count - 1;
        RulesListView.Focus();
        PhraseTextBox.Focus();
        PhraseTextBox.SelectAll();
        NotifyRulesChanged();
        UpdateRulesEmptyState();
        UpdateRuleCountText();
        UpdateFilterStatusText();
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0)
            return;

        CommitEditorToSelection();
        HideValidation();

        var insertIndex = _rows.Count;
        foreach (var source in selected)
        {
            var clone = RuleRow.FromRule(source.ToRule());
            clone.Phrase = source.Phrase + " copy";
            clone.EntityId = null;
            clone.EntityCategory = null;
            clone.SyncWithPhrase = null;
            clone.SyncOverride = false;
            clone.GroupOverride = false;
            _rows.Insert(insertIndex++, clone);
        }

        if (_rows.Count > 0)
            RulesListView.SelectedIndex = Math.Min(insertIndex - 1, _rows.Count - 1);
        NotifyRulesChanged();
        UpdateRulesEmptyState();
        UpdateRuleCountText();
        UpdateFilterStatusText();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0)
            return;

        HideValidation();
        var firstIndex = _rows.IndexOf(selected[0]);
        foreach (var row in selected)
            _rows.Remove(row);

        if (_rows.Count == 0)
        {
            ClearEditor();
            NotifyRulesChanged();
            UpdateRulesEmptyState();
            UpdateRuleCountText();
            UpdateFilterStatusText();
            return;
        }

        RulesListView.SelectedIndex = Math.Min(firstIndex, _rows.Count - 1);
        NotifyRulesChanged();
        UpdateRulesEmptyState();
        UpdateRuleCountText();
        UpdateFilterStatusText();
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRow(-1);

    private void MoveDownButton_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedRow(1);

    private void MoveSelectedRow(int delta)
    {
        if (RulesListView.SelectedItem is not RuleRow row)
            return;

        CommitEditorToSelection();
        var index = _rows.IndexOf(row);
        var target = index + delta;
        if (target < 0 || target >= _rows.Count)
            return;

        _rows.Move(index, target);
        RulesListView.SelectedItem = row;
        NotifyRulesChanged();
        UpdateFilterStatusText();
    }

    private List<RuleRow> GetSelectedRows() =>
        RulesListView.SelectedItems.Cast<RuleRow>().ToList();

    private void CommitEditorToSelection() => CommitActiveEditorRow();

    private void CommitActiveEditorRow()
    {
        if (_activeEditorRow is not RuleRow row)
            return;

        _suppressRulesNotify = true;
        try
        {
            ApplyEditorFieldsToRow(row);
        }
        finally
        {
            _suppressRulesNotify = false;
        }
    }

    private static bool HasOtherFields(RuleRow row) =>
        !string.IsNullOrWhiteSpace(row.Color) && row.Color != DefaultColor
        || !string.IsNullOrWhiteSpace(row.BackgroundColor)
        || row.Bold
        || row.Italic
        || !row.Enabled;

    private string ResolveHighlightCanvasBackground()
    {
        if (Application.Current?.Resources["BgBaseBrush"] is SolidColorBrush brush)
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";

        return ThemeRuntime.Current.GetHex("BgBase");
    }

    private static string NormalizeColor(string value, string fallback)
    {
        var trimmed = (value ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
            return fallback;

        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;

        return HexColorRegex().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : fallback;
    }

    private void ShowValidation(string message)
    {
        ValidationTextBlock.Text = message;
        ValidationTextBlock.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationTextBlock.Visibility = Visibility.Collapsed;
        ValidationTextBlock.Text = "";
    }

    private void MergeSyncedAliasRows()
    {
        var rules = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => r.ToRule())
            .ToList();
        PhraseHighlightRuleService.AlignEntityCardAliases(rules);

        foreach (var rule in rules)
        {
            var row = _rows.FirstOrDefault(r =>
                string.Equals(r.Phrase, rule.Phrase, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                _rows.Add(RuleRow.FromRule(rule));
                continue;
            }

            var refreshed = RuleRow.FromRule(rule);
            row.Phrase = refreshed.Phrase;
            row.ApplySyncedStyleFrom(rule);
            row.EntityId = refreshed.EntityId;
            row.EntityCategory = refreshed.EntityCategory;
            row.SyncOverride = refreshed.SyncOverride;
            row.GroupOverride = refreshed.GroupOverride;
        }
    }

    private void NotifyRulesChanged()
    {
        if (_suppressRulesNotify)
            return;

        UpdateRuleCountText();
        UpdateFilterStatusText();
        RefreshArrangementMetadata();
        _rulesViewSource.View?.Refresh();
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$")]
    private static partial Regex HexColorRegex();

    private sealed class PhraseHighlightsExportPayload
    {
        public List<PhraseHighlightRule> PhraseHighlightRules { get; set; } = [];
    }

    private sealed class RuleRow : INotifyPropertyChanged, IRuleListArrangementRow
    {
        private string _phrase = "";
        private string _color = DefaultColor;
        private string? _backgroundColor;
        private bool _bold;
        private bool _italic;
        private bool _enabled = true;
        private string? _syncWithPhrase;
        private bool _syncOverride;
        private bool _groupOverride;
        private string _entityTypeSummary = "—";
        private string _groupSummary = "—";
        private string _primaryFamilyKey = "";
        private int _linkTypeSortRank = 3;
        private string _entityTypeSortKey = "—";
        private string _colorGroupSortKey = "—";
        private string _linkTypeGroupKey = "Unlinked";

        public Guid? EntityId { get; set; }

        public string? EntityCategory { get; set; }

        public string? SyncWithPhrase
        {
            get => _syncWithPhrase;
            set
            {
                if (_syncWithPhrase == value)
                    return;
                _syncWithPhrase = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AliasSummary));
            }
        }

        public bool SyncOverride
        {
            get => _syncOverride;
            set
            {
                if (_syncOverride == value)
                    return;
                _syncOverride = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AliasSummary));
            }
        }

        public bool GroupOverride
        {
            get => _groupOverride;
            set
            {
                if (_groupOverride == value)
                    return;
                _groupOverride = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GroupSummary));
            }
        }

        public string EntityTypeSummary
        {
            get => _entityTypeSummary;
            private set
            {
                if (_entityTypeSummary == value)
                    return;
                _entityTypeSummary = value;
                OnPropertyChanged();
            }
        }

        public string GroupSummary
        {
            get => _groupSummary;
            private set
            {
                if (_groupSummary == value)
                    return;
                _groupSummary = value;
                OnPropertyChanged();
            }
        }

        public void RefreshGroupingDisplay(
            IReadOnlyList<PhraseHighlightRule> allRules,
            HighlightColorGroupingProfile? profile)
        {
            var display = PhraseHighlightGroupSyncService.ResolveDisplay(ToRule(), allRules, profile);
            EntityTypeSummary = display.EntityType;
            GroupSummary = PhraseHighlightGroupSyncService.FormatGroupSummary(display, GroupOverride);
        }

        public void RefreshArrangementMetadata(
            IReadOnlyList<PhraseHighlightRule> allRules,
            HighlightColorGroupingProfile? profile)
        {
            var metadata = PhraseHighlightRuleListArrangement.ResolveMetadata(ToRule(), allRules, profile);
            PrimaryFamilyKey = metadata.PrimaryFamilyKey;
            LinkTypeSortRank = metadata.LinkTypeSortRank;
            EntityTypeSortKey = metadata.EntityTypeSortKey;
            ColorGroupSortKey = metadata.ColorGroupSortKey;
            LinkTypeGroupKey = metadata.LinkTypeGroupKey;
        }

        public string PhraseSortKey => Phrase;

        public string PrimaryFamilyKey
        {
            get => _primaryFamilyKey;
            private set
            {
                if (_primaryFamilyKey == value)
                    return;
                _primaryFamilyKey = value;
                OnPropertyChanged();
            }
        }

        public int LinkTypeSortRank
        {
            get => _linkTypeSortRank;
            private set
            {
                if (_linkTypeSortRank == value)
                    return;
                _linkTypeSortRank = value;
                OnPropertyChanged();
            }
        }

        public string EntityTypeSortKey
        {
            get => _entityTypeSortKey;
            private set
            {
                if (_entityTypeSortKey == value)
                    return;
                _entityTypeSortKey = value;
                OnPropertyChanged();
            }
        }

        public string ColorGroupSortKey
        {
            get => _colorGroupSortKey;
            private set
            {
                if (_colorGroupSortKey == value)
                    return;
                _colorGroupSortKey = value;
                OnPropertyChanged();
            }
        }

        public string LinkTypeGroupKey
        {
            get => _linkTypeGroupKey;
            private set
            {
                if (_linkTypeGroupKey == value)
                    return;
                _linkTypeGroupKey = value;
                OnPropertyChanged();
            }
        }

        public bool EnabledSortKey => Enabled;

        public string AliasSummary
        {
            get
            {
                if (SyncOverride)
                    return "Override";
                if (!string.IsNullOrWhiteSpace(SyncWithPhrase))
                    return "Alias";
                if (EntityId is not null)
                    return "Primary";
                return "—";
            }
        }

        public string Phrase
        {
            get => _phrase;
            set
            {
                if (_phrase == value)
                    return;
                _phrase = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PhraseSortKey));
            }
        }

        public string Color
        {
            get => _color;
            set
            {
                var normalized = NormalizeColor(value, DefaultColor);
                if (string.Equals(_color, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
                _color = normalized;
                OnPropertyChanged();
            }
        }

        public string? BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor == value)
                    return;
                _backgroundColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StyleSummary));
            }
        }

        public bool Bold
        {
            get => _bold;
            set
            {
                if (_bold == value)
                    return;
                _bold = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StyleSummary));
            }
        }

        public bool Italic
        {
            get => _italic;
            set
            {
                if (_italic == value)
                    return;
                _italic = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StyleSummary));
            }
        }

        public int? FontWeight { get; set; }

        public bool Underline { get; set; }

        public bool Strikethrough { get; set; }

        public double? FontSizeScale { get; set; }

        public double? LetterSpacingEm { get; set; }

        public string? FontFamily { get; set; }

        public string? TextTransform { get; set; }

        public double? Opacity { get; set; }

        public string? BorderColor { get; set; }

        public int? BorderWidthPx { get; set; }

        public int? BorderRadiusPx { get; set; }

        public double? PaddingXEm { get; set; }

        public double? PaddingYEm { get; set; }

        public string? TextShadow { get; set; }

        public string? BoxShadow { get; set; }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StyleSummary));
                OnPropertyChanged(nameof(EnabledSortKey));
            }
        }

        public string StyleSummary
        {
            get
            {
                var summary = PhraseHighlightStyleResolver.FormatStyleSummary(ToRule());
                if (IsReadableAgainstCanvas())
                    return summary;

                return summary == "—" ? "Low contrast" : $"{summary}, Low contrast";
            }
        }

        private bool IsReadableAgainstCanvas()
        {
            var canvas = ThemeRuntime.Current.GetHex("BgBase");
            var background = string.IsNullOrWhiteSpace(BackgroundColor) ? canvas : BackgroundColor!;
            return ThemeContrast.IsReadable(Color, background);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static RuleRow FromRule(PhraseHighlightRule rule) =>
            new()
            {
                Phrase = rule.Phrase,
                Color = NormalizeColor(rule.Color, DefaultColor),
                BackgroundColor = string.IsNullOrWhiteSpace(rule.BackgroundColor)
                    ? null
                    : NormalizeColor(rule.BackgroundColor!, rule.BackgroundColor!),
                FontWeight = rule.FontWeight,
                Bold = rule.Bold,
                Italic = rule.Italic,
                Underline = rule.Underline,
                Strikethrough = rule.Strikethrough,
                FontSizeScale = rule.FontSizeScale,
                LetterSpacingEm = rule.LetterSpacingEm,
                FontFamily = rule.FontFamily,
                TextTransform = rule.TextTransform,
                Opacity = rule.Opacity,
                BorderColor = string.IsNullOrWhiteSpace(rule.BorderColor)
                    ? null
                    : NormalizeColor(rule.BorderColor!, rule.BorderColor!),
                BorderWidthPx = rule.BorderWidthPx,
                BorderRadiusPx = rule.BorderRadiusPx,
                PaddingXEm = rule.PaddingXEm,
                PaddingYEm = rule.PaddingYEm,
                TextShadow = rule.TextShadow,
                BoxShadow = rule.BoxShadow,
                Enabled = rule.Enabled,
                EntityId = rule.EntityId,
                EntityCategory = rule.EntityCategory,
                SyncWithPhrase = rule.SyncWithPhrase,
                SyncOverride = rule.SyncOverride,
                GroupOverride = rule.GroupOverride,
            };

        public PhraseHighlightRule ToRule() =>
            new()
            {
                Phrase = Phrase.Trim(),
                Color = NormalizeColor(Color, DefaultColor),
                BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor)
                    ? null
                    : NormalizeColor(BackgroundColor, BackgroundColor),
                FontWeight = FontWeight,
                Bold = Bold,
                Italic = Italic,
                Underline = Underline,
                Strikethrough = Strikethrough,
                FontSizeScale = FontSizeScale,
                LetterSpacingEm = LetterSpacingEm,
                FontFamily = FontFamily,
                TextTransform = TextTransform,
                Opacity = Opacity,
                BorderColor = string.IsNullOrWhiteSpace(BorderColor)
                    ? null
                    : NormalizeColor(BorderColor, BorderColor),
                BorderWidthPx = BorderWidthPx,
                BorderRadiusPx = BorderRadiusPx,
                PaddingXEm = PaddingXEm,
                PaddingYEm = PaddingYEm,
                TextShadow = TextShadow,
                BoxShadow = BoxShadow,
                Enabled = Enabled,
                EntityId = EntityId,
                EntityCategory = EntityCategory,
                SyncWithPhrase = SyncWithPhrase,
                SyncOverride = SyncOverride,
                GroupOverride = GroupOverride,
            };

        public void ApplySyncedStyleFrom(PhraseHighlightRule rule)
        {
            var refreshed = FromRule(rule);
            Color = refreshed.Color;
            BackgroundColor = refreshed.BackgroundColor;
            FontWeight = refreshed.FontWeight;
            Bold = refreshed.Bold;
            Italic = refreshed.Italic;
            Underline = refreshed.Underline;
            Strikethrough = refreshed.Strikethrough;
            FontSizeScale = refreshed.FontSizeScale;
            LetterSpacingEm = refreshed.LetterSpacingEm;
            FontFamily = refreshed.FontFamily;
            TextTransform = refreshed.TextTransform;
            Opacity = refreshed.Opacity;
            BorderColor = refreshed.BorderColor;
            BorderWidthPx = refreshed.BorderWidthPx;
            BorderRadiusPx = refreshed.BorderRadiusPx;
            PaddingXEm = refreshed.PaddingXEm;
            PaddingYEm = refreshed.PaddingYEm;
            TextShadow = refreshed.TextShadow;
            BoxShadow = refreshed.BoxShadow;
            Enabled = refreshed.Enabled;
            SyncWithPhrase = refreshed.SyncWithPhrase;
            OnPropertyChanged(nameof(StyleSummary));
        }

        public void ApplyFontWeightChoice(PhraseHighlightFontWeightMode mode, int? absolute = null)
        {
            var rule = ToRule();
            PhraseHighlightFontWeightChoice.Apply(rule, mode, absolute);
            FontWeight = rule.FontWeight;
            Bold = rule.Bold;
            OnPropertyChanged(nameof(StyleSummary));
        }
    }
}
