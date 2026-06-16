using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class InstructionDesignerDialog : Window
{
    private readonly AdventureBundle _bundle;
    private bool _suppressRefresh;

    public bool Saved { get; private set; }

    public InstructionDesignerDialog(AdventureBundle bundle)
    {
        _bundle = bundle;
        InitializeComponent();
        LoadFields();
        RefreshPreview();
        RefreshDriftLine();
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
        PerspectiveBox.Text = settings.Perspective;
        TenseBox.Text = settings.Tense;
        DetailBox.Text = settings.DetailLevel;
        ToneBox.Text = settings.Tone;
        DifficultyBox.Text = settings.Difficulty;
        ViolenceBox.Text = settings.ViolenceLevel;
        AuthorsNoteBox.Text = _bundle.Scenario.AuthorsNote;
        BoundariesBox.Text = string.Join(Environment.NewLine, settings.ContentBoundaries);
        PortrayalBox.Text = InstructionContractService.SerializeCharacterPortrayalRules(
            settings.CharacterPortrayalRules);
        AddendumBox.Text = settings.InstructionAddendum;
        _suppressRefresh = false;
    }

    private void ApplyFieldsToBundle()
    {
        InstructionContractService.ApplyDesignerFields(
            _bundle,
            PerspectiveBox.Text,
            TenseBox.Text,
            DetailBox.Text,
            ToneBox.Text,
            AuthorsNoteBox.Text,
            BoundariesBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            InstructionContractService.ParseCharacterPortrayalRules(PortrayalBox.Text) ?? [],
            AddendumBox.Text,
            DifficultyBox.Text,
            ViolenceBox.Text);
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

    private void Fields_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressRefresh)
            return;

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
        DialogResult = true;
    }
}
