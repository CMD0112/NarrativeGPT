using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Display row for adventure thread manager lists (WinUI + WPF).</summary>
public sealed class ThreadManagerRowModel
{
    public Guid EntryId { get; init; }

    public string Label { get; set; } = "";

    public string StatusDisplay { get; init; } = "";

    public string ConversationDisplay { get; init; } = "";

    public string TabTitleDisplay { get; init; } = "";

    public bool IsActive { get; init; }

    public bool IsArchived { get; init; }

    public bool HasPin { get; init; }

    public static ThreadManagerRowModel FromEntry(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        var isActive = AdventureThreadRegistryService.IsActiveEntry(bundle, entry.Id);
        var conversation = string.IsNullOrWhiteSpace(entry.ConversationId)
            ? "—"
            : entry.ConversationId.Length > 14
                ? entry.ConversationId[..14] + "…"
                : entry.ConversationId;

        return new ThreadManagerRowModel
        {
            EntryId = entry.Id,
            Label = entry.Label,
            StatusDisplay = isActive
                ? "Active"
                : entry.Status == AdventureThreadStatus.Archived
                    ? "Archived"
                    : "Inactive",
            ConversationDisplay = conversation,
            TabTitleDisplay = string.IsNullOrWhiteSpace(entry.PinnedTabTitle) ? "—" : entry.PinnedTabTitle,
            IsActive = isActive,
            IsArchived = entry.Status == AdventureThreadStatus.Archived,
            HasPin = AdventureThreadRegistryService.EntryHasPin(entry),
        };
    }
}
