using ChatGPTWrapper;
using ChatGPTWrapper.Format;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class FormatEssentialsPage : UserControl
{
    private readonly UiChromeSettings _original;
    private readonly UiChromeSettings _working;
    private readonly Action<UiChromeSettings, bool, int?> _applySettings;
    private readonly int _previewRevisionBase;
    private readonly string _originalSelectedProfileId;
    private int _previewNonce;
    private bool _suppressEvents;
    private string _selectedProfileId = FormatProfileIds.Default;

    public FormatEssentialsPage(
        UiChromeSettings chrome,
        Action<UiChromeSettings, bool, int?> applySettings)
    {
        _applySettings = applySettings;
        _original = CloneSettings(chrome);
        _working = CloneSettings(chrome);
        FormatDialogChangeService.NormalizeForDialog(_working);
        FormatDialogChangeService.NormalizeForDialog(_original);
        _previewRevisionBase = _working.ChromePreferencesRevision;
        _originalSelectedProfileId = FormatProfileService.ResolveInitialProfileId(_original.ActiveModeSettings());
        _selectedProfileId = FormatProfileService.ResolveInitialProfileId(_working.ActiveModeSettings());

        InitializeComponent();
        Loaded += (_, _) => BindFromWorking();
    }

    public UiChromeSettings ResultSettings => CloneSettings(_working);

    internal UiChromeSettings WorkingSettings => _working;

    public bool HasUnsavedChanges =>
        FormatDialogChangeService.HasUnsavedChanges(
            _original,
            _working,
            _originalSelectedProfileId,
            _selectedProfileId);

    public void Commit()
    {
        _working.ChromePreferencesRevision++;
        UiChromeStore.Save(_working);
        _applySettings(_working, true, null);
    }

    public void RevertPreview() =>
        _applySettings(_original, false, _original.ChromePreferencesRevision);

    private void BindFromWorking()
    {
        _suppressEvents = true;
        try
        {
            var mode = _working.ActiveModeSettings();
            ProfileCombo.ItemsSource = mode.FormatProfiles
                .Select(p => new FormatProfileItem(p.Id, p.Name))
                .ToList();
            ProfileCombo.DisplayMemberPath = nameof(FormatProfileItem.Name);
            ProfileCombo.SelectedValuePath = nameof(FormatProfileItem.Id);
            ProfileCombo.SelectedValue = _selectedProfileId;

            HideEditPromptsCheck.IsChecked = mode.HideAssistantEditArtifacts;
            HideContextTagsCheck.IsChecked = mode.HideContextTagsInThread;
            ExpandHiddenContextCheck.IsChecked = mode.ExpandHiddenContextInThread;
            PhraseHighlightsCheck.IsChecked = mode.PhraseHighlightsEnabled;

            ModeLine.Text =
                $"Editing format for {_working.TranscriptViewMode} mode.";
            UpdateProfileHint();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ProfileCombo.SelectedValue is not string profileId)
            return;

        _selectedProfileId = profileId;
        if (FormatProfileLibrary.Find(_working.ActiveModeSettings().FormatProfiles, profileId) is { } profile)
        {
            _working.ActiveModeSettings().ContinuousViewFormat = profile.Format.Clone();
            _working.ActiveModeSettings().ActiveFormatProfileId = profileId;
        }
        else if (profileId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            _working.ActiveModeSettings().ActiveFormatProfileId = FormatProfileIds.Custom;
        }

        UpdateProfileHint();
        PushLivePreview();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;

        var mode = _working.ActiveModeSettings();
        mode.HideAssistantEditArtifacts = HideEditPromptsCheck.IsChecked == true;
        mode.HideContextTagsInThread = HideContextTagsCheck.IsChecked == true;
        mode.ExpandHiddenContextInThread = ExpandHiddenContextCheck.IsChecked == true;
        mode.PhraseHighlightsEnabled = PhraseHighlightsCheck.IsChecked == true;
        mode.ActiveFormatProfileId = FormatProfileIds.Custom;
        _selectedProfileId = FormatProfileIds.Custom;

        PushLivePreview();
    }

    private void UpdateProfileHint()
    {
        if (FormatProfileLibrary.Find(_working.ActiveModeSettings().FormatProfiles, _selectedProfileId) is { } profile)
            ProfileHint.Text = string.IsNullOrWhiteSpace(profile.Description) ? profile.Name : profile.Description;
        else
            ProfileHint.Text = "Custom — settings differ from a saved profile.";
    }

    private void PushLivePreview()
    {
        _previewNonce++;
        var revision = _previewRevisionBase + _previewNonce;
        _applySettings(_working, false, revision);
    }

    private static UiChromeSettings CloneSettings(UiChromeSettings source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<UiChromeSettings>(json)
               ?? new UiChromeSettings();
    }

    private sealed record FormatProfileItem(string Id, string Name);
}
