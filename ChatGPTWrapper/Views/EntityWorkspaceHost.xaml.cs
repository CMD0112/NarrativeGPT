using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public enum EntityWorkspaceTab
{
    Profile,
    Sources,
    Mentions,
    History,
}

public partial class EntityWorkspaceHost : UserControl
{
    private AdventureBundle? _bundle;
    private EntityEditModel? _model;
    private string _category = "";

    public EntityWorkspaceHost()
    {
        InitializeComponent();
    }

    public EntityEditFormHost ProfileFormHost => ProfileForm;

    public event EventHandler? SaveRequested
    {
        add => ProfileForm.SaveRequested += value;
        remove => ProfileForm.SaveRequested -= value;
    }

    public event EventHandler? CancelRequested
    {
        add => ProfileForm.CancelRequested += value;
        remove => ProfileForm.CancelRequested -= value;
    }

    public event EventHandler? DeleteRequested
    {
        add => ProfileForm.DeleteRequested += value;
        remove => ProfileForm.DeleteRequested -= value;
    }

    public void LoadModel(
        AdventureBundle bundle,
        EntityEditModel model,
        string category,
        EntityReferenceEditCallbacks? callbacks = null)
    {
        _bundle = bundle;
        _model = model;
        _category = category;
        ProfileForm.LoadModel(model, callbacks);
        BindSourcesTab();
        BindMentionsTab();
        BindHistoryTab();
    }

    public bool TryHarvestModel(out string? validationMessage) =>
        ProfileForm.TryHarvestModel(out validationMessage);

    public void RefreshTabs()
    {
        if (_bundle is null || _model is null)
            return;

        BindSourcesTab();
        BindMentionsTab();
        BindHistoryTab();
    }

    public UIElement DetachTabContent(EntityWorkspaceTab tab)
    {
        var tabItem = ResolveTabItem(tab);
        if (tabItem.Content is not UIElement content)
            return new Grid();

        tabItem.Content = new Grid();
        return content;
    }

    public void SelectTab(EntityWorkspaceTab tab) =>
        WorkspaceTabs.SelectedItem = ResolveTabItem(tab);

    private TabItem ResolveTabItem(EntityWorkspaceTab tab) => tab switch
    {
        EntityWorkspaceTab.Profile => ProfileTabItem,
        EntityWorkspaceTab.Sources => SourcesTabItem,
        EntityWorkspaceTab.Mentions => MentionsTabItem,
        EntityWorkspaceTab.History => HistoryTabItem,
        _ => ProfileTabItem,
    };

    private void BindSourcesTab()
    {
        if (_bundle is null || _model is null)
        {
            SourcesPreviewBox.Text = "";
            SourcesStatusBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var entityId = _model.Id.ToString();
        var sections = _bundle.SourceManifest.Entries
            .SelectMany(e => e.Sections
                .Where(s => string.Equals(s.SourceEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                .Select(s => (Entry: e, Section: s)))
            .ToList();

        if (sections.Count == 0)
        {
            SourcesPreviewBox.Text = "No published source sections yet.";
            SourcesPreviewBox.FontStyle = FontStyles.Italic;
            SourcesStatusBadge.Visibility = Visibility.Collapsed;
            return;
        }

        SourcesPreviewBox.FontStyle = FontStyles.Normal;

        var status = EntitySyncStatusService.GetStatus(_bundle, _model.Id, _category);
        if (status != EntitySyncStatus.InSync)
        {
            SourcesStatusBadge.Text = EntitySyncStatusService.BadgeText(status);
            SourcesStatusBadge.Foreground = status switch
            {
                EntitySyncStatus.UnresolvedDrift => (Brush)FindResource("WarningBrush"),
                EntitySyncStatus.NeedsPublish => (Brush)FindResource("AccentPrimaryBrush"),
                _ => (Brush)FindResource("TextMutedBrush"),
            };
            SourcesStatusBadge.Visibility = Visibility.Visible;
        }
        else
        {
            SourcesStatusBadge.Visibility = Visibility.Collapsed;
        }

        var parts = new List<string>();
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(_bundle);
        foreach (var (entry, section) in sections)
        {
            parts.Add($"=== {entry.RelativePath}#{section.Id} ===");
            if (!string.IsNullOrWhiteSpace(section.BodyCache))
                parts.Add(section.BodyCache.Trim());
            else
            {
                var path = Path.Combine(sourcesDir, entry.RelativePath);
                if (File.Exists(path))
                    parts.Add(ExtractSectionBody(File.ReadAllText(path), section.Id));
                else
                    parts.Add("(section not on disk)");
            }

            parts.Add("");
        }

        SourcesPreviewBox.Text = string.Join(Environment.NewLine, parts).TrimEnd();
    }

    private void BindMentionsTab()
    {
        if (_bundle is null || _model is null)
        {
            MentionsList.ItemsSource = null;
            MentionsHintBlock.Text = "";
            return;
        }

        var terms = CanonMentionIndexService.CollectSearchTerms(_bundle, _model.Id, _category);
        if (terms.Count == 0)
        {
            MentionsHintBlock.Text = "Add a name or aliases on the Profile tab to scan lore for mentions.";
            MentionsList.ItemsSource = null;
            return;
        }

        var hits = CanonMentionIndexService.FindMentions(_bundle, terms);
        MentionsHintBlock.Text = hits.Count == 0
            ? $"No mentions of {string.Join(", ", terms)} in core lore yet."
            : $"{hits.Count} mention(s) across lore files and JSON.";
        MentionsList.ItemsSource = hits;
    }

    private void BindHistoryTab()
    {
        if (_bundle is null || _model is null)
        {
            HistoryList.ItemsSource = null;
            HistoryFileLabel.Text = "";
            return;
        }

        var homeFile = CanonReconciliationService.FileForCategory(_category) ?? "cast.md";
        HistoryFileLabel.Text = $"Change history for {homeFile}";
        var history = SourceFileHistoryService.ListHistory(_bundle.Metadata.Id, homeFile);
        HistoryList.ItemsSource = history;
        if (history.Count == 0)
            HistoryFileLabel.Text += " — no snapshots yet";
    }

    private static string ExtractSectionBody(string fileContent, string sectionId)
    {
        var marker = $"## {sectionId}";
        var altMarker = $"## {sectionId.Replace("/", " / ")}";
        var idx = fileContent.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            idx = fileContent.IndexOf(altMarker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return fileContent.Trim();

        var next = fileContent.IndexOf("\n## ", idx + marker.Length, StringComparison.Ordinal);
        var body = next < 0
            ? fileContent[idx..]
            : fileContent[idx..next];
        return body.Trim();
    }
}
