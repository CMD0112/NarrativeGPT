using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

/// <summary>Shared state for play settings tab panels and preview/commit.</summary>
internal sealed class PlaySettingsWorkbenchContext
{
    public PlaySettingsWorkbenchContext(
        AdventureBundle bundle,
        PlaySettingsEditorSession playSession,
        NarratorSettingsSession narratorSession,
        UiChromeSettings chromeSettings,
        string? previewPlayerLineBaseline)
    {
        Bundle = bundle;
        PlaySession = playSession;
        NarratorSession = narratorSession;
        ChromeSettings = chromeSettings;
        PreviewPlayerLineBaseline = previewPlayerLineBaseline ?? "";
        PreviewPlayerLine = PreviewPlayerLineBaseline;
    }

    public AdventureBundle Bundle { get; private set; }

    public PlaySettingsEditorSession PlaySession { get; }

    public NarratorSettingsSession NarratorSession { get; }

    public UiChromeSettings ChromeSettings { get; }

    public string PreviewPlayerLineBaseline { get; set; }

    public string PreviewPlayerLine { get; set; }

    public IPlaySettingsHost? Host { get; set; }

    public Action<PlaySettingsTab>? NavigateToTab { get; set; }

    public PlaySettingsEditorBaseline PersistedBaseline { get; set; } = null!;

    public InjectionPreviewSnapshot? LastPreviewSnapshot { get; set; }

    public string LastMergedText { get; set; } = "";

    public bool Binding { get; set; }

    public event Action? SettingsChanged;

    public event Action? ReviewQueueChanged;

    public event Action? TransportSettingsCommitted;

    public void NotifySettingsChanged()
    {
        if (!Binding)
            SettingsChanged?.Invoke();
    }

    public void RepointBundle(AdventureBundle bundle)
    {
        Bundle = bundle;
        PlaySession.RepointWorkingBundle(bundle);
        NarratorSession.RepointWorkingBundle(bundle);
    }

    public void RaiseReviewQueueChanged() =>
        ReviewQueueChanged?.Invoke();

    public void RaiseTransportSettingsCommitted() =>
        TransportSettingsCommitted?.Invoke();
}

internal interface IPlaySettingsTabPanel
{
    event EventHandler? SettingsChanged;

    void Bind(PlaySettingsWorkbenchContext context);

    void Flush(PlaySettingsWorkbenchContext context);
}
