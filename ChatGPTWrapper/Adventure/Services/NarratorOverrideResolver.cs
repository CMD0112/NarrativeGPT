using ChatGPTWrapper.Adventure.Models;

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
                SetAdventureBaseline(bundle, parameter, normalized ?? GetAdventureBaseline(bundle, parameter));
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
        AddChipIfSet(chips, "difficulty", settings.PlayTurnOverrides.Difficulty);

        if (session is not null)
        {
            AddChipIfSet(chips, "session length", session.ResponseLength);
            AddChipIfSet(chips, "session detail", session.DetailLevel);
            AddChipIfSet(chips, "session tone", session.Tone);
            AddChipIfSet(chips, "session difficulty", session.Difficulty);
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

        AddLineIfDifferent(lines, "Response length", ResolveResponseLength(bundle), "normal");
        AddLineIfDifferent(lines, "Detail level", ResolveDetailLevel(bundle), settings.DetailLevel);
        AddLineIfDifferent(lines, "Tone", ResolveTone(bundle), ResolveBaselineTone(bundle));
        AddLineIfDifferent(lines, "Difficulty", ResolveDifficulty(bundle), settings.Difficulty);

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
        }
    }

    private static void SetAdventureBaseline(AdventureBundle bundle, NarratorParameter parameter, string? value)
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
        }
    }
}
