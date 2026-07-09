using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Snapshot of persisted play-settings domains for dirty detection.
/// Compare working copy (after UI flush) to baseline — not UI controls to bundle.
/// </summary>
internal sealed class PlaySettingsEditorBaseline
{
    private readonly string _settingsJson;
    private readonly string _guideOverridesJson;
    private readonly string _rollingSummary;
    private readonly string _location;
    private readonly string _objectives;
    private readonly string _authorsNote;
    private readonly IReadOnlyList<string> _continuationQueue;
    private readonly string _previewPlayerLine;
    private readonly string _instructionsDomainHash;
    private readonly string _playChromeJson;
    private readonly string _narratorFingerprint;

    private PlaySettingsEditorBaseline(
        string settingsJson,
        string guideOverridesJson,
        string rollingSummary,
        string location,
        string objectives,
        string authorsNote,
        IReadOnlyList<string> continuationQueue,
        string previewPlayerLine,
        string instructionsDomainHash,
        string playChromeJson,
        string narratorFingerprint)
    {
        _settingsJson = settingsJson;
        _guideOverridesJson = guideOverridesJson;
        _rollingSummary = rollingSummary;
        _location = location;
        _objectives = objectives;
        _authorsNote = authorsNote;
        _continuationQueue = continuationQueue;
        _previewPlayerLine = previewPlayerLine;
        _instructionsDomainHash = instructionsDomainHash;
        _playChromeJson = playChromeJson;
        _narratorFingerprint = narratorFingerprint;
    }

    public static PlaySettingsEditorBaseline Capture(
        AdventureBundle bundle,
        UiChromeSettings chrome,
        string previewPlayerLine,
        AdventureSettings narratorSettings)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(narratorSettings);

        return new PlaySettingsEditorBaseline(
            Serialize(bundle.Metadata.Settings),
            Serialize(bundle.Metadata.UtilityJobGuideOverrides
                ?? new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase)),
            bundle.Summary.RollingSummary,
            bundle.State.CurrentLocation,
            bundle.State.OpenObjectives,
            bundle.Scenario.AuthorsNote,
            bundle.ContinuationQueue.ToList(),
            previewPlayerLine,
            InstructionSourcesPolicy.ComputeInstructionDomainHash(bundle),
            Serialize(chrome.PlaySurface),
            NarratorFingerprint(narratorSettings));
    }

    public PlaySettingsEditorBaseline WithPersistedSettings(AdventureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new PlaySettingsEditorBaseline(
            Serialize(settings),
            _guideOverridesJson,
            _rollingSummary,
            _location,
            _objectives,
            _authorsNote,
            _continuationQueue,
            _previewPlayerLine,
            _instructionsDomainHash,
            _playChromeJson,
            _narratorFingerprint);
    }

    public IReadOnlyList<string> Diff(
        AdventureBundle bundle,
        UiChromeSettings chrome,
        string previewPlayerLine,
        AdventureSettings narratorSettings)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(narratorSettings);

        var hints = new List<string>();

        if (!string.Equals(_settingsJson, Serialize(bundle.Metadata.Settings), StringComparison.Ordinal))
            hints.AddRange(ClassifySettingsDiff(_settingsJson, bundle.Metadata.Settings));

        if (!string.Equals(
                _guideOverridesJson,
                Serialize(bundle.Metadata.UtilityJobGuideOverrides
                    ?? new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase)),
                StringComparison.Ordinal))
        {
            hints.Add("job guides");
        }

        if (!string.Equals(_rollingSummary, bundle.Summary.RollingSummary, StringComparison.Ordinal))
            hints.Add("rolling summary");

        if (!string.Equals(_location, bundle.State.CurrentLocation, StringComparison.Ordinal))
            hints.Add("location");

        if (!string.Equals(_objectives, bundle.State.OpenObjectives, StringComparison.Ordinal))
            hints.Add("objectives");

        if (!string.Equals(_authorsNote, bundle.Scenario.AuthorsNote, StringComparison.Ordinal))
            hints.Add("author's note");

        if (!_continuationQueue.SequenceEqual(bundle.ContinuationQueue))
            hints.Add("continuation queue");

        if (!string.Equals(_previewPlayerLine, previewPlayerLine, StringComparison.Ordinal))
            hints.Add("preview player line");

        var instructionsHash = InstructionSourcesPolicy.ComputeInstructionDomainHash(bundle);
        if (!string.Equals(_instructionsDomainHash, instructionsHash, StringComparison.OrdinalIgnoreCase))
            hints.Add("project instructions");

        if (!string.Equals(_playChromeJson, Serialize(chrome.PlaySurface), StringComparison.Ordinal))
            hints.Add("play surface layout");

        if (!string.Equals(_narratorFingerprint, NarratorFingerprint(narratorSettings), StringComparison.Ordinal))
            hints.Add("narrator behavior");

        return hints.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ClassifySettingsDiff(string baselineJson, AdventureSettings current)
    {
        var baseline = Deserialize<AdventureSettings>(baselineJson) ?? new AdventureSettings();
        var hints = new List<string>();

        if (HasAutomationDiff(baseline, current))
            hints.Add("AI automation");

        if (HasTransportDiff(baseline, current))
            hints.Add("utility delivery");

        if (HasInjectionPolicyDiff(baseline, current))
            hints.Add("injection policy");

        if (baseline.ForceInlineLore != current.ForceInlineLore
            || baseline.PreferDomPlaySend != current.PreferDomPlaySend
            || baseline.UseWrapperComposer != current.UseWrapperComposer)
        {
            hints.Add("developer send options");
        }

        if (HasPlaySurfaceDiff(baseline, current))
            hints.Add("play surface");

        if (HasStoryContextDiff(baseline, current))
            hints.Add("story context");

        if (HasJobOverrideDiff(baseline, current))
            hints.Add("job overrides");

        if (HasTurnOverrideDiff(baseline, current))
            hints.Add("next-send overrides");

        if (HasAdventureContractDiff(baseline, current))
            hints.Add("narrator contract");

        if (hints.Count == 0)
            hints.Add("play settings");

        return hints;
    }

    private static bool HasAutomationDiff(AdventureSettings left, AdventureSettings right) =>
        left.AdventureAutomationEnabled != right.AdventureAutomationEnabled
        || left.AutoExtractEntities != right.AutoExtractEntities
        || left.AutoProposeMemories != right.AutoProposeMemories
        || left.AutoUpdateSummary != right.AutoUpdateSummary
        || left.AutoContinuityCheck != right.AutoContinuityCheck
        || left.AutoUpdateState != right.AutoUpdateState
        || left.AutoProposeEntityState != right.AutoProposeEntityState
        || left.AutoProposeCanonEvolution != right.AutoProposeCanonEvolution
        || left.AutoSyncProjectInstructions != right.AutoSyncProjectInstructions
        || left.SummaryUpdateIntervalTurns != right.SummaryUpdateIntervalTurns;

    private static bool HasTransportDiff(AdventureSettings left, AdventureSettings right) =>
        left.HideInlineUtilityDuringPlay != right.HideInlineUtilityDuringPlay
        || left.ShowInlineUtilityTraffic != right.ShowInlineUtilityTraffic
        || left.PlayUtilityInjectionMode != right.PlayUtilityInjectionMode
        || left.MaxUtilitySectionsPerSend != right.MaxUtilitySectionsPerSend
        || left.UtilityExecutionPolicy != right.UtilityExecutionPolicy
        || left.AutoSpillToWorker != right.AutoSpillToWorker
        || left.UseEphemeralUtilityWorkerChat != right.UseEphemeralUtilityWorkerChat
        || left.MaxParallelUtilityWorkerJobs != right.MaxParallelUtilityWorkerJobs
        || left.ForceUtilityWorkerDomAttach != right.ForceUtilityWorkerDomAttach
        || left.LocalUtilityInference.Enabled != right.LocalUtilityInference.Enabled
        || left.LocalUtilityInference.DualRun != right.LocalUtilityInference.DualRun
        || !string.Equals(left.LocalUtilityInference.BaseUrl, right.LocalUtilityInference.BaseUrl, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(left.LocalUtilityInference.Model, right.LocalUtilityInference.Model, StringComparison.OrdinalIgnoreCase);

    private static bool HasInjectionPolicyDiff(AdventureSettings left, AdventureSettings right)
    {
        var leftPolicy = PlayInjectionPolicyService.Resolve(left);
        var rightPolicy = PlayInjectionPolicyService.Resolve(right);
        return left.MaxPacketChars != right.MaxPacketChars
               || left.UseContextTags != right.UseContextTags
               || left.UseSectionInjection != right.UseSectionInjection
               || left.InjectAttachmentGuidance != right.InjectAttachmentGuidance
               || !string.Equals(leftPolicy.InjectionPresetId, rightPolicy.InjectionPresetId, StringComparison.OrdinalIgnoreCase)
               || leftPolicy.IncludeSummary != rightPolicy.IncludeSummary
               || leftPolicy.IncludeState != rightPolicy.IncludeState
               || leftPolicy.IncludePinnedMemory != rightPolicy.IncludePinnedMemory
               || leftPolicy.IncludeTranscript != rightPolicy.IncludeTranscript
               || leftPolicy.IncludeTriggeredCards != rightPolicy.IncludeTriggeredCards
               || leftPolicy.IncludeSourcesPointers != rightPolicy.IncludeSourcesPointers
               || leftPolicy.TranscriptMaxTurns != rightPolicy.TranscriptMaxTurns;
    }

    private static bool HasPlaySurfaceDiff(AdventureSettings left, AdventureSettings right) =>
        left.AttachmentContextMode != right.AttachmentContextMode
        || !string.Equals(left.AttachmentOnlyPlaceholder, right.AttachmentOnlyPlaceholder, StringComparison.Ordinal)
        || !DictionaryEquals(left.PlaySurfaceActions, right.PlaySurfaceActions)
        || !DictionaryEquals(left.PlayTabPlacement, right.PlayTabPlacement)
        || !string.Equals(left.PlayLayoutPresetId, right.PlayLayoutPresetId, StringComparison.OrdinalIgnoreCase);

    private static bool HasStoryContextDiff(AdventureSettings left, AdventureSettings right) =>
        !string.Equals(
            Serialize(left.UtilityStoryContext),
            Serialize(right.UtilityStoryContext),
            StringComparison.Ordinal);

    private static bool HasJobOverrideDiff(AdventureSettings left, AdventureSettings right) =>
        !string.Equals(Serialize(left.UtilityJobOverrides), Serialize(right.UtilityJobOverrides), StringComparison.Ordinal);

    private static bool HasTurnOverrideDiff(AdventureSettings left, AdventureSettings right) =>
        !string.Equals(Serialize(left.PlayTurnOverrides), Serialize(right.PlayTurnOverrides), StringComparison.Ordinal);

    private static bool HasAdventureContractDiff(AdventureSettings left, AdventureSettings right) =>
        left.MaxPacketChars != right.MaxPacketChars
        || !string.Equals(left.Perspective, right.Perspective, StringComparison.Ordinal)
        || !string.Equals(Serialize(left.ContentBoundaries), Serialize(right.ContentBoundaries), StringComparison.Ordinal)
        || !string.Equals(
            Serialize(left.CharacterPortrayalRules),
            Serialize(right.CharacterPortrayalRules),
            StringComparison.Ordinal)
        || !string.Equals(left.InstructionAddendum, right.InstructionAddendum, StringComparison.Ordinal);

    private static bool DictionaryEquals<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? left,
        IReadOnlyDictionary<TKey, TValue>? right)
        where TKey : notnull
    {
        left ??= new Dictionary<TKey, TValue>();
        right ??= new Dictionary<TKey, TValue>();
        return string.Equals(Serialize(left), Serialize(right), StringComparison.Ordinal);
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, AdventureJson.Options);

    private static T? Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, AdventureJson.Options);

    private static string NarratorFingerprint(AdventureSettings settings) =>
        Serialize(new
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
        });
}
