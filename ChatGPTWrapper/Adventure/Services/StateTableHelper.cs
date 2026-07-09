using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class StateTableRow
{
    public required string Field { get; init; }

    public required string Value { get; init; }
}

public static class StateTableHelper
{
    public static IReadOnlyList<StateTableRow> BuildRows(AdventureBundle bundle)
    {
        var s = bundle.State;
        var rows = new List<StateTableRow>();

        Add(rows, "Location", s.CurrentLocation);
        Add(rows, "Player condition", s.PlayerCondition);
        Add(rows, "Objectives", s.OpenObjectives);
        Add(rows, "Threats", s.ActiveThreats);
        Add(rows, "Mysteries", s.UnresolvedMysteries);
        Add(rows, "Consequences", s.RecentConsequences);
        Add(rows, "Map notes", s.MapNotes);
        Add(rows, "Flags", FormatFlags(s.Flags));

        var scene = s.Scene;
        Add(rows, "Scene location", scene.Location);
        Add(rows, "Scene participants", scene.Participants);
        Add(rows, "Scene conflict", scene.ImmediateConflict);
        Add(rows, "Scene atmosphere", scene.Atmosphere);
        Add(rows, "Scene exits", scene.AvailableExits);
        Add(rows, "Scene clues", scene.VisibleClues);
        Add(rows, "Scene dangers", scene.ActiveDangers);

        var time = s.Time;
        Add(rows, "In-world time", time.InWorldTime);
        Add(rows, "Deadlines", time.Deadlines);
        Add(rows, "Scheduled consequences", time.ScheduledConsequences);

        Add(rows, "Rolling summary", bundle.Summary.RollingSummary);

        return rows;
    }

    private static void Add(List<StateTableRow> rows, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        rows.Add(new StateTableRow { Field = field, Value = value.Trim() });
    }

    private static string FormatFlags(IReadOnlyDictionary<string, bool> flags)
    {
        if (flags.Count == 0)
            return "";

        return string.Join(", ", flags.Select(kv => $"{kv.Key}={kv.Value.ToString().ToLowerInvariant()}"));
    }
}
