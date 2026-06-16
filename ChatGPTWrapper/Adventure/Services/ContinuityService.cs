using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class ContinuityWarning
{
    public required string Message { get; init; }

    public string Severity { get; init; } = "warning";
}

internal static class ContinuityService
{
    public static List<ContinuityWarning> Analyze(AdventureBundle bundle)
    {
        var warnings = new List<ContinuityWarning>();
        var accepted = bundle.Log.Turns.Where(t => t.Status == TurnStatus.Accepted).OrderBy(t => t.Index).ToList();

        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(bundle.State.CurrentLocation))
            locations.Add(bundle.State.CurrentLocation.Trim());

        foreach (var loc in bundle.Entities.Locations)
        {
            if (!string.IsNullOrWhiteSpace(loc.Name))
                locations.Add(loc.Name.Trim());
        }

        var lastLoc = bundle.State.CurrentLocation;
        foreach (var turn in accepted.TakeLast(6))
        {
            var text = (turn.NarratorText ?? "") + " " + turn.PlayerText;
            foreach (var loc in bundle.Entities.Locations)
            {
                if (string.IsNullOrWhiteSpace(loc.Name))
                    continue;

                if (text.Contains(loc.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(lastLoc) &&
                    !string.Equals(lastLoc, loc.Name, StringComparison.OrdinalIgnoreCase) &&
                    text.Contains("at the same time", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(new ContinuityWarning
                    {
                        Message = $"Possible dual-location mention involving {loc.Name} while state says {lastLoc}.",
                    });
                }
            }
        }

        var lostItems = bundle.Entities.Inventory
            .Where(i => i.Status.Contains("lost", StringComparison.OrdinalIgnoreCase)
                        || i.Status.Contains("destroyed", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        foreach (var item in lostItems)
        {
            var recent = string.Join(" ", accepted.TakeLast(4).Select(t => t.NarratorText ?? ""));
            if (recent.Contains(item, StringComparison.OrdinalIgnoreCase) &&
                recent.Contains("use", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new ContinuityWarning
                {
                    Message = $"Item \"{item}\" was marked lost/destroyed but may be used in recent narration.",
                    Severity = "high",
                });
            }
        }

        if (bundle.Summary.PendingReview)
        {
            warnings.Add(new ContinuityWarning
            {
                Message = "Rolling summary has a pending review.",
                Severity = "info",
            });
        }

        if (bundle.Memory.ReviewQueue.Count > 0)
        {
            warnings.Add(new ContinuityWarning
            {
                Message = $"{bundle.Memory.ReviewQueue.Count} memory entries await review.",
                Severity = "info",
            });
        }

        return warnings;
    }
}
