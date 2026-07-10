using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.NarratorScales;

namespace ChatGPTWrapper.Adventure.Services;

public static class NarratorOverrideResolver
{
    public const string InheritLabel = "— inherit —";

    public static PlaySessionNarratorOverrides GetOrCreateSessionOverrides(AdventureBundle bundle)
    {
        if (bundle.CurrentSessionId is not { } sid)
            return new PlaySessionNarratorOverrides();

        var key = sid.ToString();
        var settings = bundle.Metadata.Settings;
        if (!settings.SessionNarratorOverrides.TryGetValue(key, out var overrides))
        {
            overrides = new PlaySessionNarratorOverrides();
            settings.SessionNarratorOverrides[key] = overrides;
        }

        return overrides;
    }

    public static PlaySessionNarratorOverrides? GetSessionOverrides(AdventureBundle bundle)
    {
        if (bundle.CurrentSessionId is not { } sid)
            return null;

        return bundle.Metadata.Settings.SessionNarratorOverrides.TryGetValue(sid.ToString(), out var overrides)
            ? overrides
            : null;
    }

    public static string ResolveBaselineTone(AdventureBundle bundle)
    {
        var tone = bundle.Metadata.Settings.Tone;
        return string.IsNullOrWhiteSpace(tone) ? bundle.Scenario.Tone ?? "" : tone.Trim();
    }

    public static string ResolveResponseLength(AdventureBundle bundle) =>
        Coalesce(
            bundle.Metadata.Settings.PlayTurnOverrides.ResponseLength,
            GetSessionOverrides(bundle)?.ResponseLength,
            "normal");

    public static string ResolveDetailLevel(AdventureBundle bundle) =>
        Coalesce(
            bundle.Metadata.Settings.PlayTurnOverrides.DetailLevel,
            GetSessionOverrides(bundle)?.DetailLevel,
            bundle.Metadata.Settings.DetailLevel);

    public static string ResolveTone(AdventureBundle bundle) =>
        Coalesce(
            bundle.Metadata.Settings.PlayTurnOverrides.Tone,
            GetSessionOverrides(bundle)?.Tone,
            ResolveBaselineTone(bundle));

    public static string ResolveDifficulty(AdventureBundle bundle) =>
        Coalesce(
            bundle.Metadata.Settings.PlayTurnOverrides.Difficulty,
            GetSessionOverrides(bundle)?.Difficulty,
            bundle.Metadata.Settings.Difficulty);

    public static string ResolveViolenceLevel(AdventureBundle bundle) =>
        Coalesce(
            bundle.Metadata.Settings.PlayTurnOverrides.ViolenceLevel,
            GetSessionOverrides(bundle)?.ViolenceLevel,
            bundle.Metadata.Settings.ViolenceLevel?.Trim() ?? "moderate");

    public static string ResolveNarrativePacing(AdventureBundle bundle) =>
        Coalesce(
            bundle.Metadata.Settings.PlayTurnOverrides.NarrativePacing,
            GetSessionOverrides(bundle)?.NarrativePacing,
            bundle.Metadata.Settings.NarrativePacing);

    public static string ResolveConsequenceWeight(AdventureBundle bundle) =>
        Coalesce(
            bundle.Metadata.Settings.PlayTurnOverrides.ConsequenceWeight,
            GetSessionOverrides(bundle)?.ConsequenceWeight,
            bundle.Metadata.Settings.ConsequenceWeight);

    public static NarratorOverrideScope ReadPersistedScope(AdventureSettings settings) =>
        Enum.TryParse<NarratorOverrideScope>(settings.LastNarratorOverrideScope, ignoreCase: true, out var scope)
            ? scope
            : NarratorOverrideScope.Turn;

    public static void PersistScope(AdventureSettings settings, NarratorOverrideScope scope) =>
        settings.LastNarratorOverrideScope = scope.ToString();

    public static string? GetScopedOverride(
        AdventureBundle bundle,
        NarratorParameter parameter,
        NarratorOverrideScope scope) =>
        scope switch
        {
            NarratorOverrideScope.Turn => GetTurnOverride(bundle, parameter),
            NarratorOverrideScope.Session => GetSessionOverride(bundle, parameter),
            NarratorOverrideScope.Adventure => GetAdventureBaseline(bundle, parameter),
            _ => null,
        };

    public static void SetScopedOverride(
        AdventureBundle bundle,
        NarratorParameter parameter,
        NarratorOverrideScope scope,
        string? value)
    {
        var normalized = NormalizeOverrideValue(parameter, value);
        switch (scope)
        {
            case NarratorOverrideScope.Turn:
                SetTurnOverride(bundle.Metadata.Settings.PlayTurnOverrides, parameter, normalized);
                break;
            case NarratorOverrideScope.Session:
                SetSessionOverride(GetOrCreateSessionOverrides(bundle), parameter, normalized);
                break;
            case NarratorOverrideScope.Adventure:
                ApplyAdventureBaseline(bundle, parameter, normalized);
                break;
        }
    }

    public static void ResetScope(AdventureBundle bundle, NarratorOverrideScope scope)
    {
        switch (scope)
        {
            case NarratorOverrideScope.Turn:
                ClearTurnOverrides(bundle.Metadata.Settings);
                break;
            case NarratorOverrideScope.Session:
                ClearSessionOverridesForCurrent(bundle);
                break;
            case NarratorOverrideScope.Adventure:
                break;
        }
    }

    public static void ClearTurnOverrides(AdventureSettings settings) =>
        settings.PlayTurnOverrides = new PlayTurnOverrideSettings();

    public static void ClearSessionOverrides(AdventureBundle bundle, Guid sessionId) =>
        bundle.Metadata.Settings.SessionNarratorOverrides.Remove(sessionId.ToString());

    public static void ClearSessionOverridesForCurrent(AdventureBundle bundle)
    {
        if (bundle.CurrentSessionId is { } sid)
            ClearSessionOverrides(bundle, sid);
    }

    public static IReadOnlyList<string> GetActiveOverrideChips(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;
        var session = GetSessionOverrides(bundle);
        var chips = new List<string>();

        AddChipIfSet(chips, "length", settings.PlayTurnOverrides.ResponseLength);
        AddChipIfSet(chips, "detail", settings.PlayTurnOverrides.DetailLevel);
        AddChipIfSet(chips, "tone", settings.PlayTurnOverrides.Tone);
        AddChipIfSet(chips, "combat", settings.PlayTurnOverrides.Difficulty);
        AddChipIfSet(chips, "violence", settings.PlayTurnOverrides.ViolenceLevel);
        AddChipIfSet(chips, "pacing", settings.PlayTurnOverrides.NarrativePacing);
        AddChipIfSet(chips, "consequences", settings.PlayTurnOverrides.ConsequenceWeight);

        if (session is not null)
        {
            AddChipIfSet(chips, "session length", session.ResponseLength);
            AddChipIfSet(chips, "session detail", session.DetailLevel);
            AddChipIfSet(chips, "session tone", session.Tone);
            AddChipIfSet(chips, "session combat", session.Difficulty);
            AddChipIfSet(chips, "session violence", session.ViolenceLevel);
            AddChipIfSet(chips, "session pacing", session.NarrativePacing);
            AddChipIfSet(chips, "session consequences", session.ConsequenceWeight);
        }

        if (!string.IsNullOrWhiteSpace(settings.PlayTurnOverrides.TurnDirective))
            chips.Add("directive");

        return chips;
    }

    public static string AppendOverrideBlocks(AdventureBundle bundle, string packet)
    {
        var settings = bundle.Metadata.Settings;
        var session = GetSessionOverrides(bundle);
        var lines = new List<string>();

        AddLineIfDifferent(lines, NarratorScaleLabels.ResponseLength, ResolveResponseLength(bundle), "normal");
        AddLineIfDifferent(lines, NarratorScaleLabels.DetailLevel, ResolveDetailLevel(bundle), settings.DetailLevel);
        AddLineIfDifferent(lines, NarratorScaleLabels.Tone, ResolveTone(bundle), ResolveBaselineTone(bundle));
        AddLineIfDifferent(lines, NarratorScaleLabels.CombatDifficulty, ResolveDifficulty(bundle), settings.Difficulty);
        AddLineIfDifferent(lines, NarratorScaleLabels.ViolenceLevel, ResolveViolenceLevel(bundle), settings.ViolenceLevel?.Trim() ?? "moderate");
        AddLineIfDifferent(lines, NarratorScaleLabels.NarrativePacing, ResolveNarrativePacing(bundle), settings.NarrativePacing);
        AddLineIfDifferent(lines, NarratorScaleLabels.ConsequenceWeight, ResolveConsequenceWeight(bundle), settings.ConsequenceWeight);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var label = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            lines[i] = NarratorScalesResolver.ExpandOverrideLine(label, value);
        }

        if (!string.IsNullOrWhiteSpace(session?.TemporaryAddendum))
            lines.Add($"Session note: {session.TemporaryAddendum.Trim()}");

        if (settings.PlayTurnOverrides.EmphasizeBoundaries || session?.EmphasizeBoundaries == true)
            lines.Add("Emphasize content boundaries for this response.");

        if (settings.PlayTurnOverrides.EmphasizePortrayalRules || session?.EmphasizePortrayalRules == true)
            lines.Add("Emphasize character portrayal rules for this response.");

        var result = packet;
        if (lines.Count > 0)
        {
            result = $"""
                {result}

                === TURN OVERRIDES ===
                {string.Join(Environment.NewLine, lines)}
                """;
        }

        var directive = settings.PlayTurnOverrides.TurnDirective?.Trim();
        if (!string.IsNullOrWhiteSpace(directive))
        {
            result = $"""
                {result}

                === TURN DIRECTIVE ===
                {directive}
                """;
        }

        return result;
    }

    public static string? NormalizeOverrideValue(NarratorParameter parameter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), InheritLabel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (parameter == NarratorParameter.ResponseLength
            && string.Equals(trimmed, "normal", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if ((parameter is NarratorParameter.NarrativePacing or NarratorParameter.ConsequenceWeight)
            && string.Equals(trimmed, "balanced", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static string Coalesce(string? turn, string? session, string baseline)
    {
        if (!string.IsNullOrWhiteSpace(turn))
            return turn.Trim();
        if (!string.IsNullOrWhiteSpace(session))
            return session.Trim();
        return baseline;
    }

    private static void AddLineIfDifferent(List<string> lines, string label, string effective, string baseline)
    {
        if (string.IsNullOrWhiteSpace(effective))
            return;

        if (string.Equals(effective, baseline, StringComparison.OrdinalIgnoreCase))
            return;

        lines.Add($"{label}: {effective}");
    }

    private static void AddChipIfSet(List<string> chips, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            chips.Add($"{label}: {value.Trim()}");
    }

    private static string? GetTurnOverride(AdventureBundle bundle, NarratorParameter parameter) =>
        parameter switch
        {
            NarratorParameter.ResponseLength => bundle.Metadata.Settings.PlayTurnOverrides.ResponseLength,
            NarratorParameter.DetailLevel => bundle.Metadata.Settings.PlayTurnOverrides.DetailLevel,
            NarratorParameter.Tone => bundle.Metadata.Settings.PlayTurnOverrides.Tone,
            NarratorParameter.Difficulty => bundle.Metadata.Settings.PlayTurnOverrides.Difficulty,
            NarratorParameter.ViolenceLevel => bundle.Metadata.Settings.PlayTurnOverrides.ViolenceLevel,
            NarratorParameter.NarrativePacing => bundle.Metadata.Settings.PlayTurnOverrides.NarrativePacing,
            NarratorParameter.ConsequenceWeight => bundle.Metadata.Settings.PlayTurnOverrides.ConsequenceWeight,
            _ => null,
        };

    private static string? GetSessionOverride(AdventureBundle bundle, NarratorParameter parameter)
    {
        var session = GetSessionOverrides(bundle);
        if (session is null)
            return null;

        return parameter switch
        {
            NarratorParameter.ResponseLength => session.ResponseLength,
            NarratorParameter.DetailLevel => session.DetailLevel,
            NarratorParameter.Tone => session.Tone,
            NarratorParameter.Difficulty => session.Difficulty,
            NarratorParameter.ViolenceLevel => session.ViolenceLevel,
            NarratorParameter.NarrativePacing => session.NarrativePacing,
            NarratorParameter.ConsequenceWeight => session.ConsequenceWeight,
            _ => null,
        };
    }

    private static string? GetAdventureBaseline(AdventureBundle bundle, NarratorParameter parameter) =>
        parameter switch
        {
            NarratorParameter.ResponseLength => "normal",
            NarratorParameter.DetailLevel => bundle.Metadata.Settings.DetailLevel,
            NarratorParameter.Tone => ResolveBaselineTone(bundle),
            NarratorParameter.Difficulty => bundle.Metadata.Settings.Difficulty,
            NarratorParameter.ViolenceLevel => bundle.Metadata.Settings.ViolenceLevel?.Trim() ?? "moderate",
            NarratorParameter.NarrativePacing => bundle.Metadata.Settings.NarrativePacing,
            NarratorParameter.ConsequenceWeight => bundle.Metadata.Settings.ConsequenceWeight,
            _ => null,
        };

    private static void SetTurnOverride(
        PlayTurnOverrideSettings overrides,
        NarratorParameter parameter,
        string? value)
    {
        switch (parameter)
        {
            case NarratorParameter.ResponseLength:
                overrides.ResponseLength = value;
                break;
            case NarratorParameter.DetailLevel:
                overrides.DetailLevel = value;
                break;
            case NarratorParameter.Tone:
                overrides.Tone = value;
                break;
            case NarratorParameter.Difficulty:
                overrides.Difficulty = value;
                break;
            case NarratorParameter.ViolenceLevel:
                overrides.ViolenceLevel = value;
                break;
            case NarratorParameter.NarrativePacing:
                overrides.NarrativePacing = value;
                break;
            case NarratorParameter.ConsequenceWeight:
                overrides.ConsequenceWeight = value;
                break;
        }
    }

    private static void SetSessionOverride(
        PlaySessionNarratorOverrides overrides,
        NarratorParameter parameter,
        string? value)
    {
        switch (parameter)
        {
            case NarratorParameter.ResponseLength:
                overrides.ResponseLength = value;
                break;
            case NarratorParameter.DetailLevel:
                overrides.DetailLevel = value;
                break;
            case NarratorParameter.Tone:
                overrides.Tone = value;
                break;
            case NarratorParameter.Difficulty:
                overrides.Difficulty = value;
                break;
            case NarratorParameter.ViolenceLevel:
                overrides.ViolenceLevel = value;
                break;
            case NarratorParameter.NarrativePacing:
                overrides.NarrativePacing = value;
                break;
            case NarratorParameter.ConsequenceWeight:
                overrides.ConsequenceWeight = value;
                break;
        }
    }

    internal static void SetAdventureBaseline(
        AdventureBundle bundle,
        NarratorParameter parameter,
        string? value) =>
        ApplyAdventureBaseline(bundle, parameter, value);

    private static void ApplyAdventureBaseline(AdventureBundle bundle, NarratorParameter parameter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var settings = bundle.Metadata.Settings;
        switch (parameter)
        {
            case NarratorParameter.DetailLevel:
                settings.DetailLevel = value;
                break;
            case NarratorParameter.Tone:
                settings.Tone = value;
                break;
            case NarratorParameter.Difficulty:
                settings.Difficulty = value;
                break;
            case NarratorParameter.ViolenceLevel:
                settings.ViolenceLevel = value;
                break;
            case NarratorParameter.NarrativePacing:
                settings.NarrativePacing = value;
                break;
            case NarratorParameter.ConsequenceWeight:
                settings.ConsequenceWeight = value;
                break;
        }
    }
}
