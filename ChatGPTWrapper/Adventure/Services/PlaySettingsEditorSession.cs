using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Owns the Play Settings dialog working copy, commit path, and safe reload rules.
/// Dirty detection uses <see cref="PlaySettingsEditorBaseline"/> in the dialog — always flush UI to the
/// working bundle before comparing to the last persisted snapshot.
/// </summary>
public sealed class PlaySettingsEditorSession
{
    private AdventureBundle _bundle;

    private PlaySettingsEditorSession(AdventureBundle bundle)
    {
        _bundle = bundle;
        PlaySettingsStore.HydrateContinuationQueue(_bundle);
    }

    public AdventureBundle Bundle => _bundle;

    public Guid AdventureId => _bundle.Metadata.Id;

    /// <summary>When set, returns true if the dialog has unsaved edits.</summary>
    public Func<bool>? IsDirty { get; set; }

    public static PlaySettingsEditorSession Attach(AdventureBundle bundle) =>
        new(bundle);

    public void RepointWorkingBundle(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        _bundle = bundle;
        PlaySettingsStore.HydrateContinuationQueue(_bundle);
    }

    /// <summary>
    /// Reloads from disk when clean; otherwise syncs only review proposals and worker probe results.
    /// </summary>
    public void SyncFromDisk(bool preserveNarratorSettings, Action rebindAllControls, Action refreshExternalOnly)
    {
        if (IsDirty?.Invoke() == true)
        {
            PlaySettingsStore.SyncExternalDomains(_bundle, PlaySettingsStore.ExternalSync.All);
            refreshExternalOnly();
            return;
        }

        if (!AdventureStore.ReloadInto(_bundle, preserveNarratorSettings))
            return;

        PlaySettingsStore.HydrateContinuationQueue(_bundle);
        PlaySettingsStore.SyncExternalDomains(_bundle, PlaySettingsStore.ExternalSync.All);
        rebindAllControls();
    }

    public void RefreshExternalOnly(Action refreshExternalOnly)
    {
        PlaySettingsStore.SyncExternalDomains(_bundle, PlaySettingsStore.ExternalSync.All);
        refreshExternalOnly();
    }

    /// <summary>Flushes UI into the working bundle, commits to disk, and reloads the working copy.</summary>
    public void Commit(Action flushUiToBundle, bool preserveNarratorSettings, Action rebindAllControls)
    {
        flushUiToBundle();
        PlaySettingsStore.SyncExternalDomains(_bundle, PlaySettingsStore.ExternalSync.All);
        PlaySettingsStore.Commit(_bundle);
        AdventureStore.ReloadInto(_bundle, preserveNarratorSettings);
        PlaySettingsStore.HydrateContinuationQueue(_bundle);
        rebindAllControls();
    }
}
