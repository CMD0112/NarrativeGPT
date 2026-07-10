using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class InstructionDesignerDialog : ShellDialogWindow
{
    private readonly AdventureBundle _bundle;
    private bool _suppressRefresh;
    private bool _dirty;

    private static readonly string[] PerspectivePresets = ["second person", "first person", "third person limited", "third person omniscient"];
    private static readonly string[] TensePresets = ["present", "past"];
    private static readonly string[] DetailPresets = ["low", "medium", "high", "cinematic"];
    private static readonly string[] TonePresets = ["neutral", "dramatic", "whimsical", "grim", "hopeful", "tense", "lyrical"];

    public bool Saved { get; private set; }

    public InstructionDesignerDialog(AdventureBundle bundle)
    {
        _bundle = bundle;
        InitializeComponent();
        LoadFields();
        RefreshPreview();
        RefreshDriftLine();
        Closing += InstructionDesignerDialog_Closing;
    }

    private void InstructionDesignerDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty || Saved)
            return;

        var result = MessageBox.Show(
            this,
            "Discard unsaved instruction changes?",
            "Instructions designer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    public static bool? Show(Window owner, Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
        {
            MessageBox.Show(owner, "Adventure not found.", "Instructions designer");
            return false;
        }

        AdventureDesignService.EnsureWorkspace(bundle);
        InstructionContractService.HydrateDesignInstructionFields(bundle);

        var dialog = new InstructionDesignerDialog(bundle) { Owner = owner };
        return dialog.ShowDialog();
    }

    private void LoadFields()
    {
        _suppressRefresh = true;
        var settings = _bundle.Metadata.Settings;
        BindCombo(PerspectiveBox, PerspectivePresets, settings.Perspective);
        BindCombo(TenseBox, TensePresets, settings.Tense);
        BindCombo(DetailBox, DetailPresets, settings.DetailLevel);
        BindCombo(ToneBox, TonePresets, settings.Tone);
        BindCombo(DifficultyBox, NarratorPresetLibrary.PresetPacketValues("combat-difficulty"), settings.Difficulty);
        BindCombo(ViolenceBox, NarratorPresetLibrary.PresetPacketValues("violence-level"), settings.ViolenceLevel);
        BindCombo(NarrativePacingBox, NarratorPresetLibrary.PresetPacketValues("narrative-pacing"), settings.NarrativePacing);
        BindCombo(ConsequenceWeightBox, NarratorPresetLibrary.PresetPacketValues("consequence-weight"), settings.ConsequenceWeight);
        AuthorsNoteBox.Text = _bundle.Scenario.AuthorsNote;
        BoundariesBox.Text = string.Join(Environment.NewLine, settings.ContentBoundaries);
        PortrayalBox.Text = InstructionContractService.SerializeCharacterPortrayalRules(
            settings.CharacterPortrayalRules);
        AddendumBox.Text = settings.InstructionAddendum;
        _suppressRefresh = false;
        _dirty = false;
    }

    private static void BindCombo(ComboBox combo, IReadOnlyList<string> presets, string? value)
    {
        var current = string.IsNullOrWhiteSpace(value) ? presets[0] : value.Trim();
        combo.ItemsSource = presets.Contains(current, StringComparer.OrdinalIgnoreCase)
            ? presets
            : presets.Concat([current]).ToList();
        if (presets.Contains(current, StringComparer.OrdinalIgnoreCase))
            combo.SelectedItem = presets.First(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
        else
            combo.Text = current;
    }

    private static string ReadCombo(ComboBox combo) =>
        string.IsNullOrWhiteSpace(combo.Text)
            ? combo.SelectedItem as string ?? ""
            : combo.Text.Trim();

    private void ApplyFieldsToBundle()
    {
        InstructionContractService.ApplyDesignerFields(
            _bundle,
            ReadCombo(PerspectiveBox),
            ReadCombo(TenseBox),
            ReadCombo(DetailBox),
            ReadCombo(ToneBox),
            AuthorsNoteBox.Text,
            BoundariesBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            InstructionContractService.ParseCharacterPortrayalRules(PortrayalBox.Text) ?? [],
            AddendumBox.Text,
            ReadCombo(DifficultyBox),
            ReadCombo(ViolenceBox),
            ReadCombo(NarrativePacingBox),
            ReadCombo(ConsequenceWeightBox));
    }

    private void RefreshPreview()
    {
        ApplyFieldsToBundle();
        PreviewBox.Text = InstructionContractService.BuildInstructionsSnippetFileContent(_bundle);
    }

    private void RefreshDriftLine()
    {
        DriftLine.Text = InstructionSourcesPolicy.FormatInstructionDriftHint(_bundle);
    }

    private void Fields_TextChanged(object sender, TextChangedEventArgs e) => Fields_Changed(sender, e);

    private void Fields_Changed(object sender, EventArgs e)
    {
        if (_suppressRefresh)
            return;

        _dirty = true;
        RefreshPreview();
        RefreshDriftLine();
    }

    private void GenerateFile_Click(object sender, RoutedEventArgs e)
    {
        ApplyFieldsToBundle();
        if (!InstructionContractService.GenerateInstructionsSnippetFile(_bundle))
        {
            MessageBox.Show(this, "Could not write instructions-snippet.md.", "Generate instructions");
            return;
        }

        AdventureStore.Save(_bundle);
        RefreshPreview();
        MessageBox.Show(
            this,
            "Wrote instructions-snippet.md from the canonical preview (no AI).",
            "Generate instructions");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyFieldsToBundle();
            Clipboard.SetText(InstructionContractService.BuildCanonicalInstructionsBody(_bundle));
        }
        catch
        {
            MessageBox.Show(this, "Could not copy to clipboard.", "Copy instructions");
        }
    }

    private void MarkPasted_Click(object sender, RoutedEventArgs e)
    {
        ApplyFieldsToBundle();
        InstructionSourcesPolicy.RecordInstructionsManuallyPublished(_bundle);
        AdventureStore.Save(_bundle);
        RefreshDriftLine();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplyFieldsToBundle();
        AdventureStore.Save(_bundle);
        Saved = true;
        _dirty = false;
        DialogResult = true;
    }
}
