using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ThreadMetadataService
{
    public static void RecordPlayTurnExchange(
        AdventureBundle bundle,
        TurnRecord turn,
        string playerText,
        string? narratorText,
        string? packetHash = null,
        string? conversationId = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(turn);

        if (!string.IsNullOrWhiteSpace(conversationId))
            bundle.ThreadMetadata.ConversationId = conversationId;

        var userId = $"turn:{turn.Id}:user";
        var assistantId = $"turn:{turn.Id}:assistant";

        AppendMessage(bundle, new ThreadMessageRecord
        {
            MessageId = userId,
            Role = "user",
            PlayerLine = string.IsNullOrWhiteSpace(playerText) ? null : playerText.Trim(),
            PacketHash = packetHash,
            IsInjectedContext = ConversationStreamParser.IsInjectedContextUserMessage(playerText),
            IsUtility = ConversationStreamParser.IsUtilityUserMessage(playerText),
            LinkedTurnId = turn.Id,
            MessageKind = ThreadMessageKind.PlayUser,
        });

        if (!string.IsNullOrWhiteSpace(narratorText))
        {
            AppendMessage(bundle, new ThreadMessageRecord
            {
                MessageId = assistantId,
                Role = "assistant",
                BodyText = narratorText.Trim(),
                IsUtility = ConversationStreamParser.IsUtilityAssistantMessage(narratorText),
                LinkedTurnId = turn.Id,
                MessageKind = ThreadMessageKind.PlayAssistant,
            });
        }
    }

    public static void RecordNarratorComposerRevision(
        AdventureBundle bundle,
        TurnRecord turn,
        string playerText,
        string replacementText,
        string? revisionGroupId = null,
        string? revisionPromptText = null,
        string? assistantDomTurnId = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(turn);

        revisionGroupId ??= Guid.NewGuid().ToString("N");
        var supersededAssistantId = $"turn:{turn.Id}:assistant";

        AppendMessage(bundle, new ThreadMessageRecord
        {
            MessageId = $"turn:{turn.Id}:user",
            Role = "user",
            PlayerLine = string.IsNullOrWhiteSpace(playerText) ? null : playerText.Trim(),
            PacketHash = turn.PromptPacketHash,
            LinkedTurnId = turn.Id,
            MessageKind = ThreadMessageKind.PlayUser,
        });

        if (!string.IsNullOrWhiteSpace(revisionPromptText))
        {
            AppendMessage(bundle, new ThreadMessageRecord
            {
                MessageId = $"revision:{revisionGroupId}:prompt",
                Role = "user",
                PlayerLine = revisionPromptText.Trim(),
                HiddenInDisplay = true,
                MessageKind = ThreadMessageKind.NarratorRevisionPrompt,
                RevisionGroupId = revisionGroupId,
                SupersedesMessageId = supersededAssistantId,
                LinkedTurnId = turn.Id,
            });
        }

        AppendMessage(bundle, new ThreadMessageRecord
        {
            MessageId = $"revision:{revisionGroupId}:replacement",
            Role = "assistant",
            BodyText = replacementText.Trim(),
            MessageKind = ThreadMessageKind.NarratorReplacement,
            RevisionGroupId = revisionGroupId,
            SupersedesMessageId = supersededAssistantId,
            LinkedTurnId = turn.Id,
        });

        if (!string.IsNullOrWhiteSpace(assistantDomTurnId))
        {
            bundle.ThreadMetadata.RevisionAssistantDomTurnIds ??= new Dictionary<string, string>(StringComparer.Ordinal);
            bundle.ThreadMetadata.RevisionAssistantDomTurnIds[revisionGroupId] = assistantDomTurnId;
        }
    }

    public static IReadOnlyList<RevisionHideEntry> BuildRevisionHideEntries(AdventureBundle bundle)
    {
        var entries = new List<RevisionHideEntry>();
        var domIds = bundle.ThreadMetadata.RevisionAssistantDomTurnIds;

        foreach (var msg in bundle.ThreadMetadata.Messages)
        {
            if (!msg.HiddenInDisplay && !ThreadMessageKind.IsRevisionArtifact(msg.MessageKind))
                continue;

            string? domTurnId = null;
            if (!string.IsNullOrWhiteSpace(msg.RevisionGroupId)
                && domIds is not null
                && domIds.TryGetValue(msg.RevisionGroupId, out var stored))
            {
                domTurnId = stored;
            }

            entries.Add(new RevisionHideEntry
            {
                MessageId = msg.MessageId,
                MessageKind = msg.MessageKind,
                PromptPrefix = msg.MessageKind == ThreadMessageKind.NarratorRevisionPrompt
                    ? NarratorRevisionPrompt.Prefix
                    : null,
                AssistantDomTurnId = domTurnId,
            });
        }

        return entries;
    }

    public static IReadOnlyList<TranscriptTurnPair> ToTranscriptPairs(AdventureBundle bundle)
    {
        var pairs = new List<TranscriptTurnPair>();
        string? pendingPlayer = null;
        int? pendingIndex = null;

        foreach (var msg in ActiveMessages(bundle))
        {
            if (msg.IsUtility)
                continue;

            if (msg.HiddenInDisplay || ThreadMessageKind.IsRevisionArtifact(msg.MessageKind))
                continue;

            if (msg.Role == "user")
            {
                pendingPlayer = msg.PlayerLine ?? "";
                pendingIndex = ResolveTurnIndex(bundle, msg.LinkedTurnId);
                continue;
            }

            if (msg.Role != "assistant")
                continue;

            var narrator = msg.BodyText ?? "";
            if (string.IsNullOrWhiteSpace(pendingPlayer) && string.IsNullOrWhiteSpace(narrator))
                continue;

            pairs.Add(new TranscriptTurnPair
            {
                PlayerText = pendingPlayer ?? "",
                NarratorText = narrator,
                TurnIndex = pendingIndex,
            });
            pendingPlayer = null;
            pendingIndex = null;
        }

        return pairs;
    }

    public static void RecordUtilityExchange(
        AdventureBundle bundle,
        string jobId,
        string userPrompt,
        string? assistantResponse,
        string? conversationId = null,
        UtilityExecutionChannel? channel = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        if (!string.IsNullOrWhiteSpace(conversationId))
            bundle.ThreadMetadata.ConversationId = conversationId;

        var channelAttr = channel is null ? null : channel == UtilityExecutionChannel.AutoBackground ? "auto" : "manual";
        var stamp = Guid.NewGuid();
        AppendMessage(bundle, new ThreadMessageRecord
        {
            MessageId = $"utility:{jobId}:{stamp}:user",
            Role = "user",
            IsUtility = true,
            HiddenInDisplay = true,
            UtilityChannel = channelAttr,
            PlayerLine = TranscriptTextSanitizer.Sanitize(userPrompt),
        });

        if (!string.IsNullOrWhiteSpace(assistantResponse))
        {
            AppendMessage(bundle, new ThreadMessageRecord
            {
                MessageId = $"utility:{jobId}:{stamp}:assistant",
                Role = "assistant",
                BodyText = assistantResponse.Trim(),
                IsUtility = true,
                HiddenInDisplay = true,
                UtilityChannel = channelAttr,
            });
        }
    }

    public static void MarkTurnSuperseded(AdventureBundle bundle, Guid turnId)
    {
        foreach (var msg in bundle.ThreadMetadata.Messages)
        {
            if (msg.LinkedTurnId == turnId)
                msg.SupersededByEdit = true;
        }
    }

    public static IReadOnlyList<ThreadMessageRecord> ActiveMessages(AdventureBundle bundle) =>
        bundle.ThreadMetadata.Messages
            .Where(m => !m.SupersededByEdit)
            .OrderBy(m => m.Ordinal)
            .ToList();

    public static IReadOnlyDictionary<int, LogTurnLink> BuildLogTurnLinkMap(AdventureBundle bundle)
    {
        var turns = PlayTurnScopeService.GetPacketContextTurns(bundle);
        var map = new Dictionary<int, LogTurnLink>();

        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            var snippet = turn.PlayerText.Trim();
            if (snippet.Length > 80)
                snippet = snippet[..77] + "...";

            map[i] = new LogTurnLink
            {
                TurnId = turn.Id,
                TurnIndex = turn.Index,
                PlayerSnippet = snippet,
                DisplayTurnNumber = i + 1,
            };
        }

        return map;
    }

    public static IReadOnlyDictionary<string, int> BuildOrdinalMap(AdventureBundle bundle)
    {
        var map = ActiveMessages(bundle)
            .Where(m => !string.IsNullOrWhiteSpace(m.MessageId))
            .ToDictionary(m => m.MessageId!, m => m.Ordinal, StringComparer.Ordinal);

        var accepted = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .ToList();

        for (var i = 0; i < accepted.Count; i++)
        {
            var turn = accepted[i];
            var userDomKey = $"dom:{(i * 2) + 1}";
            var assistantDomKey = $"dom:{(i * 2) + 2}";
            if (map.TryGetValue($"turn:{turn.Id}:user", out var userOrdinal))
                map[userDomKey] = userOrdinal;
            if (map.TryGetValue($"turn:{turn.Id}:assistant", out var assistantOrdinal))
                map[assistantDomKey] = assistantOrdinal;
        }

        return map;
    }

    private static int? ResolveTurnIndex(AdventureBundle bundle, Guid? turnId)
    {
        if (turnId is null)
            return null;

        return bundle.Log.Turns.FirstOrDefault(t => t.Id == turnId)?.Index;
    }

    private static void AppendMessage(AdventureBundle bundle, ThreadMessageRecord record)
    {
        record.Ordinal = bundle.ThreadMetadata.Messages.Count > 0
            ? bundle.ThreadMetadata.Messages.Max(m => m.Ordinal) + 1
            : 0;
        record.RecordedAt = DateTimeOffset.UtcNow;
        bundle.ThreadMetadata.Messages.Add(record);
    }
}
