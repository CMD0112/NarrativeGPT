using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Read-only canon profile excerpts for play-state jobs (CMD-470).
/// </summary>
internal static class EntityCanonConstraintService
{
    public static string BuildPromptBlock(AdventureBundle bundle, IReadOnlyList<EntityReferenceRow> targets)
    {
        if (targets.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("=== CANON PROFILE (read-only for this job) ===");
        sb.AppendLine(
            "Stable fields below live in entities.json. Do NOT patch them via entity-state — "
            + "use entity extraction for canon facts or propose_canon_evolution for reviewed profile changes.");
        sb.AppendLine();

        foreach (var row in targets)
        {
            var kindId = EntityInternalStateService.ResolveKindId(row.Kind);
            sb.Append($"- {row.Name} · id={row.Id:N} · kindId={kindId}");

            if (!string.IsNullOrWhiteSpace(row.RoleOrStatus))
                sb.Append($" · Role/status: {Truncate(row.RoleOrStatus, 120)}");

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(row.DescriptionSnippet))
                sb.AppendLine($"  description: {Truncate(row.DescriptionSnippet, 280)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
