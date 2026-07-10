namespace ChatGPTWrapper.Adventure.Models;

public enum EntityChangeIntent
{
    Update,
    Rename,
    Delete,
    Merge,
    Retire,
    Create,
}

public enum EntityTextReplacementAction
{
    Replace,
    AliasOnly,
    Skip,
}

public sealed class EntityTextReplacement
{
    public string File { get; set; } = "";

    public string? SectionId { get; set; }

    public string Prior { get; set; } = "";

    public string New { get; set; } = "";

    public EntityTextReplacementAction Action { get; set; } = EntityTextReplacementAction.Replace;

    public bool Approved => Action != EntityTextReplacementAction.Skip;
}

public sealed class EntityChangePlan
{
    public Guid PlanId { get; set; } = Guid.NewGuid();

    public EntityChangeIntent Intent { get; set; }

    public Guid EntityId { get; set; }

    public Guid? TargetEntityId { get; set; }

    public string Category { get; set; } = "";

    public string? PriorName { get; set; }

    public string? NewName { get; set; }

    public bool IsDelete { get; set; }

    public List<string> SectionTargets { get; set; } = [];

    public List<EntityTextReplacement> TextReplacements { get; set; } = [];

    public List<string> AffectedFiles { get; set; } = [];

    public List<string> PhraseHighlightUpdates { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Summary
    {
        get
        {
            var baseSummary = Intent switch
            {
                EntityChangeIntent.Rename => $"Rename {PriorName} → {NewName}",
                EntityChangeIntent.Delete => $"Delete {PriorName ?? NewName}",
                EntityChangeIntent.Merge => $"Merge into {NewName}",
                EntityChangeIntent.Retire => $"Retire {PriorName ?? NewName}",
                EntityChangeIntent.Create => $"Create {NewName}",
                _ => $"Update {NewName ?? PriorName}",
            };

            if (PhraseHighlightUpdates.Count == 0)
                return baseSummary;

            return $"{baseSummary} · {PhraseHighlightUpdates.Count} highlight rule{(PhraseHighlightUpdates.Count == 1 ? "" : "s")}";
        }
    }
}
