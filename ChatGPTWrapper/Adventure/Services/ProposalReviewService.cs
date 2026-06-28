using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

public enum ProposalReviewCategory
{
    Entity,
    Memory,
    Summary,
    Card,
    SourceEdit,
    JsonImport,
    ContinuityWarning,
}

public sealed class ProposalReviewCategorySummary
{
    public ProposalReviewCategory Category { get; init; }

    public string Label { get; init; } = "";

    public int Count { get; init; }
}

public sealed class ProposalReviewItemKey
{
    public ProposalReviewCategory Category { get; init; }

    public Guid Id { get; init; }

    public override bool Equals(object? obj) =>
        obj is ProposalReviewItemKey other
        && Category == other.Category
        && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(Category, Id);
}

public sealed class ProposalReviewListItem
{
    public ProposalReviewItemKey Key { get; init; } = new();

    public string Title { get; init; } = "";

    public string Preview { get; init; } = "";

    public string? Subtitle { get; init; }

    public bool CanAccept { get; init; } = true;

    public bool CanDismiss { get; init; } = true;

    public bool OpensDetailedReview { get; init; }
}

public static class ProposalReviewService
{
    public static IReadOnlyList<ProposalReviewCategorySummary> ListCategories(AdventureBundle bundle)
    {
        var counts = PendingReviewService.GetCounts(bundle);
        var list = new List<ProposalReviewCategorySummary>();

        void Add(ProposalReviewCategory category, string label, int count)
        {
            if (count > 0)
                list.Add(new ProposalReviewCategorySummary { Category = category, Label = label, Count = count });
        }

        Add(ProposalReviewCategory.Entity, "Entities", counts.Entities);
        Add(ProposalReviewCategory.Memory, "Memories", counts.Memories);
        Add(ProposalReviewCategory.Summary, "Summary", counts.Summary);
        Add(ProposalReviewCategory.Card, "Story cards", counts.Cards);
        Add(ProposalReviewCategory.SourceEdit, "Source edits", counts.SourceEdits);
        Add(ProposalReviewCategory.JsonImport, "JSON import", counts.JsonImports);
        Add(ProposalReviewCategory.ContinuityWarning, "Continuity warnings", counts.ContinuityWarnings);

        return list;
    }

    public static bool HasAny(AdventureBundle bundle) =>
        ListCategories(bundle).Count > 0;

    public static IReadOnlyList<ProposalReviewListItem> ListItems(
        AdventureBundle bundle,
        ProposalReviewCategory category)
    {
        return category switch
        {
            ProposalReviewCategory.Entity => bundle.Entities.ReviewQueue
                .Select(e => new ProposalReviewListItem
                {
                    Key = new ProposalReviewItemKey { Category = category, Id = e.Id },
                    Title = FormatEntityTitle(e),
                    Preview = FormatEntityPreview(e),
                    Subtitle = e.CreatedAt.ToString("g"),
                })
                .ToList(),
            ProposalReviewCategory.Memory => bundle.Memory.ReviewQueue
                .Select(m => new ProposalReviewListItem
                {
                    Key = new ProposalReviewItemKey { Category = category, Id = m.Id },
                    Title = Truncate(m.Text, 72),
                    Preview = FormatMemoryPreview(m),
                    Subtitle = m.Tags.Count > 0 ? string.Join(", ", m.Tags) : null,
                })
                .ToList(),
            ProposalReviewCategory.Summary => SummaryReviewService.IsPending(bundle.Summary)
                ?
                [
                    new ProposalReviewListItem
                    {
                        Key = new ProposalReviewItemKey { Category = category, Id = SummaryProposalKey },
                        Title = "Rolling summary update",
                        Preview = Truncate(bundle.Summary.ProposedSummary ?? "", 120),
                    },
                ]
                : [],
            ProposalReviewCategory.Card => bundle.Cards.ReviewQueue
                .Select(c => new ProposalReviewListItem
                {
                    Key = new ProposalReviewItemKey { Category = category, Id = c.Id },
                    Title = FormatCardTitle(c),
                    Preview = Truncate(c.ProposedChange, 120),
                    Subtitle = c.CreatedAt.ToString("g"),
                })
                .ToList(),
            ProposalReviewCategory.SourceEdit => SourceEditReviewPresentationService
                .ListVisibleProposals(bundle)
                .Select(s => new ProposalReviewListItem
                {
                    Key = new ProposalReviewItemKey { Category = category, Id = s.Id },
                    Title = SourceEditReviewPresentationService.FormatListLabel(s),
                    Preview = Truncate(s.Rationale, 160),
                })
                .ToList(),
            ProposalReviewCategory.JsonImport => BuildJsonImportItems(bundle),
            ProposalReviewCategory.ContinuityWarning => ActiveContinuityWarnings(bundle)
                .Select(w => new ProposalReviewListItem
                {
                    Key = new ProposalReviewItemKey { Category = category, Id = w.Id },
                    Title = w.Severity,
                    Preview = w.Message,
                    Subtitle = w.CreatedAt.ToString("g"),
                    CanAccept = false,
                })
                .ToList(),
            _ => [],
        };
    }

    public static string BuildDetail(AdventureBundle bundle, ProposalReviewItemKey key)
    {
        return key.Category switch
        {
            ProposalReviewCategory.Entity when FindEntity(bundle, key.Id) is { } entity =>
                $"""
                Type: {entity.EntityType}
                Created: {entity.CreatedAt:g}

                Proposed change:
                {FormatJsonForDisplay(entity.ProposedChange)}
                """,
            ProposalReviewCategory.Memory when FindMemory(bundle, key.Id) is { } memory =>
                $"""
                Tags: {(memory.Tags.Count > 0 ? string.Join(", ", memory.Tags) : "(none)")}
                Pinned: {memory.Pinned}
                {(string.IsNullOrWhiteSpace(memory.Outcome) ? "" : $"Outcome: {memory.Outcome}{Environment.NewLine}")}

                {memory.Text}
                """,
            ProposalReviewCategory.Summary when SummaryReviewService.IsPending(bundle.Summary) =>
                bundle.Summary.ProposedSummary ?? "",
            ProposalReviewCategory.Card when FindCard(bundle, key.Id) is { } card =>
                $"""
                Created: {card.CreatedAt:g}

                Proposed change:
                {FormatJsonForDisplay(card.ProposedChange)}
                """,
            ProposalReviewCategory.SourceEdit when FindSourceEdit(bundle, key.Id) is { } edit =>
                SourceEditDiffPreviewService.BuildPreview(bundle, edit),
            ProposalReviewCategory.JsonImport when FindJsonImport(bundle, key.Id) is { } json =>
                BuildJsonImportDetail(bundle, json),
            ProposalReviewCategory.ContinuityWarning when FindContinuityWarning(bundle, key.Id) is { } warning =>
                $"""
                Severity: {warning.Severity}
                Source: {warning.Source}
                Created: {warning.CreatedAt:g}

                {warning.Message}
                """,
            _ => "",
        };
    }

    public static ProposalReviewResult Accept(AdventureBundle bundle, ProposalReviewItemKey key)
    {
        switch (key.Category)
        {
            case ProposalReviewCategory.Entity:
            {
                var item = FindEntity(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                if (!EntityExtractionService.ApplyAcceptedReviewItem(bundle.Entities, item))
                    return ProposalReviewResult.Failed;

                bundle.Entities.ReviewQueue.Remove(item);
                AdventureStore.Save(bundle, AdventureSaveScope.Entities);
                return ProposalReviewResult.Succeeded(requiresCanonReconcile: true);
            }
            case ProposalReviewCategory.Memory:
            {
                var item = FindMemory(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                bundle.Memory.Entries.Add(new MemoryEntry
                {
                    Text = item.Text,
                    Tags = item.Tags,
                    Pinned = item.Pinned,
                    Outcome = item.Outcome,
                    Anchor = item.Anchor,
                });
                bundle.Memory.ReviewQueue.Remove(item);
                AdventureStore.Save(bundle, AdventureSaveScope.Memory);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.Summary:
            {
                if (!SummaryReviewService.IsPending(bundle.Summary))
                    return ProposalReviewResult.NotFound;

                SummaryReviewService.AcceptProposal(bundle);
                AdventureStore.Save(bundle, AdventureSaveScope.Summary);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.Card:
            {
                var item = FindCard(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                if (!GenerationJobHandlers.ApplyAcceptedCardReviewItem(bundle.Cards, item))
                    return ProposalReviewResult.Failed;

                bundle.Cards.ReviewQueue.Remove(item);
                AdventureStore.Save(bundle, AdventureSaveScope.Cards);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.SourceEdit:
            {
                var item = FindSourceEdit(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                if (!SourceEditService.ApplyAcceptedEdit(bundle, item))
                    return ProposalReviewResult.Failed;

                bundle.Scenario.SourceEditReviewQueue.Remove(item);
                AdventureStore.Save(bundle);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.JsonImport:
            {
                var item = FindJsonImport(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                if (!SourceJsonImportService.ApplyAccepted(bundle, item))
                    return ProposalReviewResult.Failed;

                bundle.Scenario.JsonImportReviewQueue.Remove(item);
                if (bundle.Scenario.JsonImportReviewQueue.Count == 0)
                    bundle.Scenario.JsonImportProposedSnapshot = null;

                AdventureDesignService.HydrateFromScenario(bundle);
                AdventureStore.Save(bundle);
                return ProposalReviewResult.Succeeded();
            }
            default:
                return ProposalReviewResult.NotFound;
        }
    }

    public static ProposalReviewResult Dismiss(AdventureBundle bundle, ProposalReviewItemKey key)
    {
        switch (key.Category)
        {
            case ProposalReviewCategory.Entity:
            {
                var item = FindEntity(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                bundle.Entities.ReviewQueue.Remove(item);
                AdventureStore.Save(bundle, AdventureSaveScope.Entities);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.Memory:
            {
                var item = FindMemory(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                bundle.Memory.ReviewQueue.Remove(item);
                AdventureStore.Save(bundle, AdventureSaveScope.Memory);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.Summary:
            {
                if (!SummaryReviewService.IsPending(bundle.Summary))
                    return ProposalReviewResult.NotFound;

                SummaryReviewService.DismissProposal(bundle);
                AdventureStore.Save(bundle, AdventureSaveScope.Summary);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.Card:
            {
                var item = FindCard(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                bundle.Cards.ReviewQueue.Remove(item);
                AdventureStore.Save(bundle, AdventureSaveScope.Cards);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.SourceEdit:
            {
                var item = FindSourceEdit(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                bundle.Scenario.SourceEditReviewQueue.Remove(item);
                AdventureStore.Save(bundle, AdventureSaveScope.Scenario);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.JsonImport:
            {
                var item = FindJsonImport(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                bundle.Scenario.JsonImportReviewQueue.Remove(item);
                if (bundle.Scenario.JsonImportReviewQueue.Count == 0)
                    bundle.Scenario.JsonImportProposedSnapshot = null;

                AdventureStore.Save(bundle, AdventureSaveScope.Scenario);
                return ProposalReviewResult.Succeeded();
            }
            case ProposalReviewCategory.ContinuityWarning:
            {
                var item = FindContinuityWarning(bundle, key.Id);
                if (item is null)
                    return ProposalReviewResult.NotFound;

                ContinuityWarningDismissalService.Dismiss(bundle.Continuity, item.Message);
                AdventureStore.Save(bundle, AdventureSaveScope.Continuity);
                return ProposalReviewResult.Succeeded();
            }
            default:
                return ProposalReviewResult.NotFound;
        }
    }

    public static int AcceptAll(AdventureBundle bundle, ProposalReviewCategory category)
    {
        var applied = 0;
        foreach (var item in ListItems(bundle, category).ToList())
        {
            if (!item.CanAccept)
                continue;

            var result = Accept(bundle, item.Key);
            if (result.Status == ProposalReviewActionStatus.Succeeded)
                applied++;
        }

        return applied;
    }

    public static int DismissAll(AdventureBundle bundle, ProposalReviewCategory category)
    {
        var dismissed = 0;
        foreach (var item in ListItems(bundle, category).ToList())
        {
            if (!item.CanDismiss)
                continue;

            var result = Dismiss(bundle, item.Key);
            if (result.Status == ProposalReviewActionStatus.Succeeded)
                dismissed++;
        }

        return dismissed;
    }

    public static ProposalReviewCategory? ResolveCategoryForJob(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => null,
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => ProposalReviewCategory.Entity,
        GenerationJobId.ProposeMemories => ProposalReviewCategory.Memory,
        GenerationJobId.UpdateSummary => ProposalReviewCategory.Summary,
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard => ProposalReviewCategory.Card,
        GenerationJobId.ProposeSourceEdits => ProposalReviewCategory.SourceEdit,
        GenerationJobId.ProposeJsonImport => ProposalReviewCategory.JsonImport,
        GenerationJobId.ContinuityCheck => ProposalReviewCategory.ContinuityWarning,
        _ => null,
    };

    private static readonly Guid SummaryProposalKey = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static EntityReviewItem? FindEntity(AdventureBundle bundle, Guid id) =>
        bundle.Entities.ReviewQueue.FirstOrDefault(e => e.Id == id);

    private static MemoryEntry? FindMemory(AdventureBundle bundle, Guid id) =>
        bundle.Memory.ReviewQueue.FirstOrDefault(m => m.Id == id);

    private static CardReviewItem? FindCard(AdventureBundle bundle, Guid id) =>
        bundle.Cards.ReviewQueue.FirstOrDefault(c => c.Id == id);

    private static SourceEditReviewItem? FindSourceEdit(AdventureBundle bundle, Guid id) =>
        bundle.Scenario.SourceEditReviewQueue.FirstOrDefault(s => s.Id == id);

    private static JsonImportReviewItem? FindJsonImport(AdventureBundle bundle, Guid id) =>
        bundle.Scenario.JsonImportReviewQueue.FirstOrDefault(j => j.Id == id);

    private static ContinuityWarningEntry? FindContinuityWarning(AdventureBundle bundle, Guid id) =>
        ActiveContinuityWarnings(bundle).FirstOrDefault(w => w.Id == id);

    private static List<ContinuityWarningEntry> ActiveContinuityWarnings(AdventureBundle bundle) =>
        ContinuityWarningDismissalService.FilterActive(bundle.Continuity);

    private static IReadOnlyList<ProposalReviewListItem> BuildJsonImportItems(AdventureBundle bundle)
    {
        var analyses = JsonImportConflictService.AnalyzeQueue(bundle)
            .ToDictionary(a => a.ProposalId);

        return bundle.Scenario.JsonImportReviewQueue
            .Select(j =>
            {
                analyses.TryGetValue(j.Id, out var analysis);
                return new ProposalReviewListItem
                {
                    Key = new ProposalReviewItemKey { Category = ProposalReviewCategory.JsonImport, Id = j.Id },
                    Title = analysis?.DisplaySummary ?? FormatJsonImportTitle(j),
                    Preview = Truncate(j.Rationale, 120),
                    Subtitle = analysis?.Severity.ToString(),
                    OpensDetailedReview = true,
                };
            })
            .ToList();
    }

    private static string BuildJsonImportDetail(AdventureBundle bundle, JsonImportReviewItem item)
    {
        var analysis = JsonImportConflictService.AnalyzeQueue(bundle)
            .FirstOrDefault(a => a.ProposalId == item.Id);

        var parts = new List<string>
        {
            $"Kind: {item.Kind}",
            $"Action: {item.Action}",
        };

        if (!string.IsNullOrWhiteSpace(item.Field))
            parts.Add($"Field: {item.Field}");
        if (!string.IsNullOrWhiteSpace(item.EntityType))
            parts.Add($"Entity type: {item.EntityType}");
        if (!string.IsNullOrWhiteSpace(item.Name))
            parts.Add($"Name: {item.Name}");
        if (analysis is not null)
        {
            parts.Add($"Conflict: {analysis.Severity}");
            if (!string.IsNullOrWhiteSpace(analysis.SourceRef))
                parts.Add($"Source: {analysis.SourceRef}");
            if (!string.IsNullOrWhiteSpace(analysis.EntityLinkageHint))
                parts.Add(analysis.EntityLinkageHint);
        }

        if (!string.IsNullOrWhiteSpace(item.Rationale))
            parts.Add($"{Environment.NewLine}Rationale:{Environment.NewLine}{item.Rationale.Trim()}");

        if (!string.IsNullOrWhiteSpace(item.PriorValue))
            parts.Add($"{Environment.NewLine}Prior:{Environment.NewLine}{item.PriorValue.Trim()}");

        parts.Add($"{Environment.NewLine}Proposed:{Environment.NewLine}{item.Value.Trim()}");

        if (!string.IsNullOrWhiteSpace(analysis?.SourceExcerpt))
            parts.Add($"{Environment.NewLine}Source excerpt:{Environment.NewLine}{analysis.SourceExcerpt.Trim()}");

        return string.Join(Environment.NewLine, parts);
    }

    private static string FormatEntityTitle(EntityReviewItem item)
    {
        var name = TryReadJsonString(item.ProposedChange, "name");
        return string.IsNullOrWhiteSpace(name)
            ? item.EntityType
            : $"{item.EntityType}: {name}";
    }

    private static string FormatEntityPreview(EntityReviewItem item)
    {
        var description = TryReadJsonString(item.ProposedChange, "description");
        return string.IsNullOrWhiteSpace(description)
            ? Truncate(item.ProposedChange, 120)
            : Truncate(description, 120);
    }

    private static string FormatCardTitle(CardReviewItem item) =>
        TryReadJsonString(item.ProposedChange, "name") ?? "Story card proposal";

    private static string FormatMemoryPreview(MemoryEntry memory)
    {
        if (!string.IsNullOrWhiteSpace(memory.Outcome))
            return Truncate(memory.Outcome, 120);

        return memory.Pinned ? "Pinned memory event" : "Memory event";
    }

    private static string FormatJsonImportTitle(JsonImportReviewItem item) =>
        string.IsNullOrWhiteSpace(item.Name)
            ? $"{item.Kind} · {item.Action}"
            : $"{item.Kind}: {item.Name}";

    private static string? TryReadJsonString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonElementParsing.GetStringProperty(doc.RootElement, property);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatJsonForDisplay(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string Truncate(string text, int max)
    {
        var trimmed = text.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}

public enum ProposalReviewActionStatus
{
    NotFound,
    Failed,
    Succeeded,
}

public sealed class ProposalReviewResult
{
    public ProposalReviewActionStatus Status { get; init; }

    public bool RequiresCanonReconcile { get; init; }

    public static ProposalReviewResult NotFound => new() { Status = ProposalReviewActionStatus.NotFound };

    public static ProposalReviewResult Failed => new() { Status = ProposalReviewActionStatus.Failed };

    public static ProposalReviewResult Succeeded(bool requiresCanonReconcile = false) =>
        new()
        {
            Status = ProposalReviewActionStatus.Succeeded,
            RequiresCanonReconcile = requiresCanonReconcile,
        };
}
