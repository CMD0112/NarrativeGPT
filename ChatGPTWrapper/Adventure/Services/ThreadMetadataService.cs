using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ThreadMetadataService
{
    public static void RecordNarratorComposerRevision(
        AdventureBundle bundle,
        int? logTurnIndex,
        string? revisionGroupId,
        string? revisionPromptText,
        string? assistantDomTurnId,
        string? replacementText)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        revisionGroupId = string.IsNullOrWhiteSpace(revisionGroupId)
            ? Guid.NewGuid().ToString("N")
            : revisionGroupId;

        bundle.ThreadMetadata.RevisionAssistantDomTurnIds ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(assistantDomTurnId))
            bundle.ThreadMetadata.RevisionAssistantDomTurnIds[revisionGroupId] = assistantDomTurnId;

        Guid? linkedTurnId = null;
        if (logTurnIndex is >= 0)
        {
            var turns = PlayTurnScopeService.GetPacketContextTurns(bundle);
            if (logTurnIndex.Value < turns.Count)
                linkedTurnId = turns[logTurnIndex.Value].Id;
        }

        var messages = bundle.ThreadMetadata.Messages;
        var nextOrdinal = messages.Count > 0 ? messages.Max(m => m.Ordinal) + 1 : 0;

        if (!string.IsNullOrWhiteSpace(revisionPromptText))
        {
            messages.Add(new ThreadMessageRecord
            {
                MessageId = $"rev-prompt-{revisionGroupId}",
                Ordinal = nextOrdinal++,
                Role = "user",
                BodyText = revisionPromptText,
                MessageKind = ThreadMessageKind.NarratorRevisionPrompt,
                RevisionGroupId = revisionGroupId,
                HiddenInDisplay = true,
                LinkedTurnId = linkedTurnId,
            });
        }

        if (!string.IsNullOrWhiteSpace(assistantDomTurnId))
        {
            messages.Add(new ThreadMessageRecord
            {
                MessageId = $"rev-original-{revisionGroupId}",
                Ordinal = nextOrdinal++,
                Role = "assistant",
                MessageKind = ThreadMessageKind.NarratorOriginal,
                RevisionGroupId = revisionGroupId,
                HiddenInDisplay = true,
                LinkedTurnId = linkedTurnId,
            });
        }

        if (!string.IsNullOrWhiteSpace(replacementText))
        {
            messages.Add(new ThreadMessageRecord
            {
                MessageId = $"rev-replacement-{revisionGroupId}",
                Ordinal = nextOrdinal++,
                Role = "assistant",
                BodyText = replacementText,
                MessageKind = ThreadMessageKind.NarratorReplacement,
                RevisionGroupId = revisionGroupId,
                HiddenInDisplay = false,
                LinkedTurnId = linkedTurnId,
            });
        }
    }

    public static IReadOnlyList<RevisionHideEntry> BuildRevisionHideEntries(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var entries = new List<RevisionHideEntry>();
        var seenAssistantDomIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var msg in bundle.ThreadMetadata.Messages)
        {
            if (!msg.HiddenInDisplay && !ThreadMessageKind.IsRevisionArtifact(msg.MessageKind))
                continue;

            entries.Add(new RevisionHideEntry
            {
                MessageId = msg.MessageId,
                MessageKind = msg.MessageKind,
                PromptPrefix = string.Equals(
                    msg.MessageKind,
                    ThreadMessageKind.NarratorRevisionPrompt,
                    StringComparison.Ordinal)
                    ? NarratorRevisionPrompt.Prefix
                    : null,
            });
        }

        if (bundle.ThreadMetadata.RevisionAssistantDomTurnIds is { Count: > 0 } domMap)
        {
            foreach (var domTurnId in domMap.Values)
            {
                if (string.IsNullOrWhiteSpace(domTurnId) || !seenAssistantDomIds.Add(domTurnId))
                    continue;

                entries.Add(new RevisionHideEntry
                {
                    AssistantDomTurnId = domTurnId,
                    MessageKind = ThreadMessageKind.NarratorOriginal,
                });
            }
        }

        if (ThreadConversationLogReader.HasActivePlayLog(bundle))
        {
            var entry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play)!;
            foreach (var branchMsg in ThreadConversationLogReader.GetActiveBranchOrLatestSnapshot(bundle, entry))
            {
                if (!string.Equals(branchMsg.Role, "user", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = branchMsg.DisplayText ?? branchMsg.RawText;
                if (!NarratorRevisionPrompt.IsRevisionPromptUserMessage(text))
                    continue;

                entries.Add(new RevisionHideEntry
                {
                    MessageKind = ThreadMessageKind.NarratorRevisionPrompt,
                    PromptPrefix = NarratorRevisionPrompt.Prefix,
                });
            }
        }

        return entries;
    }
}
