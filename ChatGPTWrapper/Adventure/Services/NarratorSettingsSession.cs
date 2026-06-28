using System.Text.Json;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Single working session for narrator override UI: scope selection, per-scope parameters,
/// and commit to disk. Shared by the play cockpit and Play settings dialog.
/// </summary>
public sealed class NarratorSettingsSession
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private AdventureBundle _bundle;

    private NarratorSettingsSession(AdventureBundle bundle)
    {
        _bundle = bundle;
        SelectedScope = NarratorOverrideResolver.ReadPersistedScope(bundle.Metadata.Settings);
    }

    public AdventureBundle Bundle => _bundle;

    /// <summary>
    /// Repoints the session after the play-settings dialog reloads adventure state from disk.
    /// Narrator overrides are expected to already live on <paramref name="bundle"/>.
    /// </summary>
    public void RepointWorkingBundle(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (ReferenceEquals(_bundle, bundle))
            return;

        _bundle = bundle;
        SelectedScope = NarratorOverrideResolver.ReadPersistedScope(bundle.Metadata.Settings);
    }

    public NarratorOverrideScope SelectedScope { get; private set; }

    /// <summary>When true, panel changes are written to <see cref="Bundle"/> and saved immediately.</summary>
    public bool AutoCommitToDisk { get; set; }

    public static NarratorSettingsSession Attach(AdventureBundle bundle) => new(bundle);

    public static NarratorSettingsSession LoadFromDisk(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId)
                     ?? throw new InvalidOperationException($"Adventure {adventureId} not found.");
        return new NarratorSettingsSession(bundle);
    }

    public void SyncFromDisk()
    {
        var fresh = AdventureStore.Load(Bundle.Metadata.Id);
        if (fresh is null)
            return;

        CopyNarratorSettings(fresh.Metadata.Settings, Bundle.Metadata.Settings);
        Bundle.CurrentSessionId = fresh.CurrentSessionId;
        SelectedScope = NarratorOverrideResolver.ReadPersistedScope(Bundle.Metadata.Settings);
    }

    public void Rebind(AdventureBundle bundle)
    {
        CopyNarratorSettings(Bundle.Metadata.Settings, bundle.Metadata.Settings);
        NarratorOverrideResolver.PersistScope(bundle.Metadata.Settings, SelectedScope);
        // Bundle reference stays with panel host; callers replace their _bundle pointer.
    }

    public void BindScopeToUi(
        RadioButton turnRadio,
        RadioButton sessionRadio,
        RadioButton adventureRadio) =>
        NarratorBehaviorPanelBinder.BindScope(turnRadio, sessionRadio, adventureRadio, SelectedScope);

    public NarratorOverrideScope ReadScopeFromUi(
        RadioButton turnRadio,
        RadioButton sessionRadio,
        RadioButton adventureRadio) =>
        NarratorBehaviorPanelBinder.ReadScope(turnRadio, sessionRadio, adventureRadio);

    public void BindParameterCombos(
        NarratorOverrideScope scope,
        IReadOnlyDictionary<NarratorParameter, ComboBox> combos) =>
        NarratorBehaviorPanelBinder.BindParameterCombos(Bundle, scope, combos);

    public void HandleScopeChange(
        NarratorOverrideScope newScope,
        IReadOnlyDictionary<NarratorParameter, ComboBox> combos)
    {
        if (newScope != SelectedScope)
            FlushCombosToScope(SelectedScope, combos);

        SelectedScope = newScope;
        NarratorOverrideResolver.PersistScope(Bundle.Metadata.Settings, SelectedScope);
        CommitIfAuto();
    }

    public void FlushFromPanel(IReadOnlyDictionary<NarratorParameter, ComboBox> combos)
    {
        FlushCombosToScope(SelectedScope, combos);
        NarratorOverrideResolver.PersistScope(Bundle.Metadata.Settings, SelectedScope);
    }

    public void FlushCombosToScope(
        NarratorOverrideScope scope,
        IReadOnlyDictionary<NarratorParameter, ComboBox> combos) =>
        NarratorBehaviorPanelBinder.SaveParameterCombos(Bundle, scope, combos);

    public void CommitIfAuto()
    {
        if (!AutoCommitToDisk)
            return;

        AdventureStore.Save(Bundle);
    }

    public void CommitPlaySettingsDialog()
    {
        AdventureStore.SavePlaySettingsFromDialog(_bundle);
    }

    public AdventureBundle CreatePreviewBundle(AdventureBundle? template = null)
    {
        var source = template ?? Bundle;
        var staging = InjectionSettingsStaging.CloneBundleForStaging(source);
        CopyNarratorSettings(Bundle.Metadata.Settings, staging.Metadata.Settings);
        NarratorOverrideResolver.PersistScope(staging.Metadata.Settings, SelectedScope);
        return staging;
    }

    public string FormatOverrideChips() =>
        NarratorBehaviorPanelBinder.FormatOverrideChips(Bundle);

    public static AdventureSettings CaptureNarratorBaseline(AdventureSettings source)
    {
        var snapshot = new AdventureSettings();
        CopyNarratorSettings(source, snapshot);
        return snapshot;
    }

    public static void ApplyNarratorBaseline(AdventureSettings target, AdventureSettings baseline) =>
        CopyNarratorSettings(baseline, target);

    public static void CopyNarratorSettings(AdventureSettings source, AdventureSettings target)
    {
        target.LastNarratorOverrideScope = source.LastNarratorOverrideScope;
        target.DetailLevel = source.DetailLevel;
        target.Tone = source.Tone;
        target.Difficulty = source.Difficulty;
        target.ViolenceLevel = source.ViolenceLevel;
        target.NarrativePacing = source.NarrativePacing;
        target.ConsequenceWeight = source.ConsequenceWeight;
        target.PlayTurnOverrides = CloneJson(source.PlayTurnOverrides);
        target.SessionNarratorOverrides = CloneJson(source.SessionNarratorOverrides);
    }

    public static bool NarratorSettingsEqual(AdventureSettings left, AdventureSettings right) =>
        string.Equals(
            JsonSerializer.Serialize(Snapshot(left), JsonOptions),
            JsonSerializer.Serialize(Snapshot(right), JsonOptions),
            StringComparison.Ordinal);

    private static object Snapshot(AdventureSettings settings) => new
    {
        settings.LastNarratorOverrideScope,
        settings.DetailLevel,
        settings.Tone,
        settings.Difficulty,
        settings.ViolenceLevel,
        settings.NarrativePacing,
        settings.ConsequenceWeight,
        settings.PlayTurnOverrides,
        settings.SessionNarratorOverrides,
    };

    private static T CloneJson<T>(T value) where T : class, new() =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions) ?? new();
}
