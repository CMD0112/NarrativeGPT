using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class StateUpdateService
{
    public static string BuildPrompt(AdventureBundle bundle, UtilityTranscriptScope? scope, bool omitTurnSlice)
    {
        var scopeBlock = scope is null
            ? "=== SCOPE ===\nTarget: newest play exchange (offset 0)."
            : UtilityTranscriptScopeService.FormatScopeBlock(scope);
        var pair = scope?.TargetPair;
        var exchange = omitTurnSlice || pair is null
            ? ""
            : $"""

              === EXCHANGE ===
              PLAYER: {pair.PlayerText}
              NARRATOR: {pair.NarratorText}
              """;

        return $"""
            === STATE UPDATE JOB ===
            Return JSON object only. Omit unchanged keys.
            Keys: location (string), objectives (string[]), objectivesRemove (string[]), flags (object bool map), time (string), rationale (string).
            Use objectivesRemove only for explicitly completed/abandoned objectives.

            === CURRENT STATE ===
            Location: {bundle.State.CurrentLocation}
            Objectives: {bundle.State.OpenObjectives}
            Time: {bundle.State.Time.InWorldTime}

            {scopeBlock}
            {exchange}
            """;
    }

    public static StateProposalEntry? ParseResponse(string response, GenerationJobContext? context = null)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(response);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            var proposal = new StateProposalEntry
            {
                Location = JsonElementParsing.GetStringProperty(root, "location"),
                Time = JsonElementParsing.GetStringProperty(root, "time"),
                Rationale = JsonElementParsing.GetStringProperty(root, "rationale"),
                Objectives = ParseStringArray(root, "objectives"),
                ObjectivesRemove = ParseStringArray(root, "objectivesRemove"),
                Flags = ParseFlags(root),
                InferenceSource = context?.InferenceSource,
                UtilityRunId = context?.UtilityRunId,
            };

            return HasMeaningfulContent(proposal) ? proposal : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool ApplyProposal(StateDocument state, StateProposalEntry proposal)
    {
        if (!HasMeaningfulContent(proposal))
            return false;

        if (!string.IsNullOrWhiteSpace(proposal.Location))
            state.CurrentLocation = proposal.Location.Trim();
        if (!string.IsNullOrWhiteSpace(proposal.Time))
            state.Time.InWorldTime = proposal.Time.Trim();

        var objectives = ParseObjectives(state.OpenObjectives);
        foreach (var remove in proposal.ObjectivesRemove)
            objectives.RemoveAll(x => string.Equals(x, remove, StringComparison.OrdinalIgnoreCase));
        foreach (var add in proposal.Objectives)
        {
            if (objectives.Any(x => string.Equals(x, add, StringComparison.OrdinalIgnoreCase)))
                continue;
            objectives.Add(add);
        }

        state.OpenObjectives = string.Join("; ", objectives.Where(x => !string.IsNullOrWhiteSpace(x)));
        foreach (var kv in proposal.Flags)
            state.Flags[kv.Key] = kv.Value;

        return true;
    }

    private static bool HasMeaningfulContent(StateProposalEntry proposal) =>
        !string.IsNullOrWhiteSpace(proposal.Location)
        || !string.IsNullOrWhiteSpace(proposal.Time)
        || proposal.Objectives.Count > 0
        || proposal.ObjectivesRemove.Count > 0
        || proposal.Flags.Count > 0;

    private static List<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var value = JsonElementParsing.GetStringOrNull(item);
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(value.Trim());
        }

        return list;
    }

    private static Dictionary<string, bool> ParseFlags(JsonElement root)
    {
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("flags", out var obj) || obj.ValueKind != JsonValueKind.Object)
            return flags;

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                flags[prop.Name] = prop.Value.ValueKind == JsonValueKind.True;
        }

        return flags;
    }

    private static List<string> ParseObjectives(string current)
    {
        if (string.IsNullOrWhiteSpace(current))
            return [];

        return current.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
