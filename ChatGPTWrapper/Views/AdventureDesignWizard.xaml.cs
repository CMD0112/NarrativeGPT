using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class AdventureDesignWizard : Window
{
    private readonly Dictionary<string, TextBox> _fieldBoxes = new(StringComparer.OrdinalIgnoreCase);
    private AdventureBundle? _bundle;

    public Guid AdventureId => _bundle?.Metadata.Id ?? Guid.Empty;

    public bool ContinueToDesign { get; private set; }

    public Func<Task>? LinkProjectAsync { get; set; }

    public AdventureDesignWizard(Guid adventureId)
    {
        InitializeComponent();
        LoadAdventure(adventureId);
    }

    public AdventureDesignWizard()
    {
        InitializeComponent();
        var bundle = AdventureDesignService.CreateDesigningAdventure("Untitled adventure");
        LoadAdventure(bundle.Metadata.Id);
    }

    private void LoadAdventure(Guid adventureId)
    {
        _bundle = AdventureStore.Load(adventureId);
        if (_bundle is null)
        {
            MessageBox.Show(this, "Adventure not found.", "Design wizard", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        AdventureDesignService.EnsureWorkspace(_bundle);
        AdventureDesignService.HydrateFromScenario(_bundle);
        AdventureDesignService.GoToStep(_bundle, AdventureDesignStep.Setup);
        TitleLine.Text = $"Setup: {_bundle.Metadata.Title}";
        RefreshUi();
    }

    private void RefreshUi()
    {
        RefreshLinkMetadataFromDisk();
        if (_bundle is null)
            return;

        var hasProject = AdventureDesignChatService.CanUseChat(_bundle);
        StatusLine.Text = hasProject
            ? "Project linked — click Continue to open Design mode with a dedicated project thread."
            : "Link a ChatGPT Project to enable AI design. Local editing works without a project.";
        LinkProjectButton.Content = hasProject ? "Change Project…" : "Link Project…";
        ContinueButton.IsEnabled = true;

        RebuildDraftPanel();
    }

    private void RebuildDraftPanel()
    {
        DraftPanel.Children.Clear();
        _fieldBoxes.Clear();

        foreach (var field in AdventureDesignService.GetFieldDefinitions(AdventureDesignStep.Setup))
        {
            DraftPanel.Children.Add(new TextBlock
            {
                Text = field.Label,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var box = new TextBox
            {
                AcceptsReturn = field.Key is not "title",
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 36,
                Margin = new Thickness(0, 0, 0, 12),
                Tag = field.Key,
            };
            box.Text = AdventureDesignService.GetField(_bundle!, AdventureDesignStep.Setup, field.Key) ?? "";
            box.TextChanged += FieldBox_TextChanged;
            _fieldBoxes[field.Key] = box;
            DraftPanel.Children.Add(box);
        }
    }

    private void FieldBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_bundle is null || sender is not TextBox box || box.Tag is not string key)
            return;

        AdventureDesignService.SetField(_bundle, AdventureDesignStep.Setup, key, box.Text);
        if (key == "title")
            _bundle.Metadata.Title = box.Text.Trim();
        Save();
        TitleLine.Text = $"Setup: {_bundle.Metadata.Title}";
    }

    private void Save()
    {
        if (_bundle is null)
            return;

        var fresh = AdventureStore.Load(AdventureId);
        if (fresh is not null)
            AdventureProjectBindingService.MergeLinkMetadataFrom(_bundle.Metadata, fresh.Metadata);

        AdventureStore.Save(_bundle);
    }

    private void ReloadBundleFromDisk()
    {
        if (AdventureId == Guid.Empty)
            return;

        var fresh = AdventureStore.Load(AdventureId);
        if (fresh is null)
            return;

        if (_bundle is not null)
            AdventureProjectBindingService.MergeLinkMetadataFrom(fresh.Metadata, _bundle.Metadata);

        _bundle = fresh;
        AdventureDesignService.EnsureWorkspace(_bundle);
        AdventureDesignService.HydrateFromScenario(_bundle);
        AdventureDesignService.GoToStep(_bundle, AdventureDesignStep.Setup);
        TitleLine.Text = $"Setup: {_bundle.Metadata.Title}";
    }

    private void RefreshLinkMetadataFromDisk()
    {
        if (AdventureId == Guid.Empty || _bundle is null)
            return;

        var fresh = AdventureStore.Load(AdventureId);
        if (fresh is null)
            return;

        AdventureProjectBindingService.MergeLinkMetadataFrom(_bundle.Metadata, fresh.Metadata);
        AdventureProjectBindingService.SyncLinkedProjectFields(_bundle.Metadata);
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        ReloadBundleFromDisk();
        if (_bundle is null)
            return;

        if (!AdventureDesignChatService.CanUseChat(_bundle))
        {
            var choice = MessageBox.Show(
                this,
                "No ChatGPT Project is linked. AI design needs a project thread.\n\nLink a project now?",
                "Continue to Design",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
            {
                _ = LinkAndContinueAsync();
                return;
            }
        }

        FinishSetupAndContinue();
    }

    private async Task LinkAndContinueAsync()
    {
        if (LinkProjectAsync is null)
            return;

        LinkProjectButton.IsEnabled = false;
        try
        {
            await LinkProjectAsync();
            ReloadBundleFromDisk();
            RefreshUi();
            if (_bundle is not null && AdventureDesignChatService.CanUseChat(_bundle))
                FinishSetupAndContinue();
        }
        finally
        {
            LinkProjectButton.IsEnabled = true;
        }
    }

    private void FinishSetupAndContinue()
    {
        ReloadBundleFromDisk();
        if (_bundle is null)
            return;

        PersistFieldsFromUi();
        AdventureDesignService.MarkStepAccepted(_bundle, AdventureDesignStep.Setup);
        AdventureDesignService.ApplySetupToMetadata(_bundle);
        AdventureDesignService.GoToStep(_bundle, AdventureDesignStep.Concept);
        Save();

        ContinueToDesign = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void LinkProject_Click(object sender, RoutedEventArgs e)
    {
        if (LinkProjectAsync is null)
            return;

        LinkProjectButton.IsEnabled = false;
        try
        {
            await LinkProjectAsync();
            ReloadBundleFromDisk();
            RefreshUi();
        }
        finally
        {
            LinkProjectButton.IsEnabled = true;
        }
    }

    private void PersistFieldsFromUi()
    {
        if (_bundle is null)
            return;

        foreach (var (key, box) in _fieldBoxes)
            AdventureDesignService.SetField(_bundle, AdventureDesignStep.Setup, key, box.Text);
    }
}
