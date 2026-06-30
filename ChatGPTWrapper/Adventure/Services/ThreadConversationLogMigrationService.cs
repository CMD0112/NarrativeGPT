using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ThreadConversationLogMigrationService
{
    public static bool MigrateIfNeeded(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var changed = false;

        foreach (var entry in bundle.Metadata.ThreadRegistry)
        {
            if (string.IsNullOrWhiteSpace(entry.ConversationId))
                continue;

            if (ThreadConversationLogStore.Exists(bundle.Metadata.Id, entry.Id))
                continue;

            changed |= MigrateThreadFromLegacy(bundle, entry);
        }

        return changed;
    }

    private static bool MigrateThreadFromLegacy(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        if (entry.Kind != AdventureThreadKind.Play)
            return false;

        return MigratePlayThreadFromLegacy(bundle, entry);
    }

    private static bool MigratePlayThreadFromLegacy(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        var accepted = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .ToList();

        if (accepted.Count == 0 && bundle.ThreadMetadata.Messages.Count == 0)
            return false;

        var branch = new List<ConversationBranchMessage>();
        var branchIndex = 0;

        if (bundle.ThreadMetadata.Messages.Count > 0)
        {
            foreach (var msg in bundle.ThreadMetadata.Messages.Where(m => !m.SupersededByEdit).OrderBy(m => m.Ordinal))
            {
                if (msg.IsUtility)
                    continue;

                var raw = msg.Role == "user"
                    ? msg.PlayerLine ?? ""
                    : msg.BodyText ?? "";

                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                branch.Add(new ConversationBranchMessage
                {
                    NodeId = msg.MessageId ?? $"migration:{branchIndex}",
                    Role = msg.Role,
                    RawText = raw,
                    DisplayText = msg.Role == "user"
                        ? ConversationStreamParser.ExtractTranscriptPlayerText(raw) ?? raw
                        : raw,
                    BranchIndex = branchIndex,
                    IsUtility = msg.IsUtility,
                    IsInjectedContext = msg.IsInjectedContext,
                });
                branchIndex++;
            }
        }
        else
        {
            foreach (var turn in accepted)
            {
                branch.Add(new ConversationBranchMessage
                {
                    NodeId = $"turn:{turn.Id}:user",
                    Role = "user",
                    RawText = turn.PlayerText,
                    DisplayText = turn.PlayerText,
                    BranchIndex = branchIndex++,
                    IsUtility = ConversationStreamParser.IsUtilityUserMessage(turn.PlayerText),
                    IsInjectedContext = ConversationStreamParser.IsInjectedContextUserMessage(turn.PlayerText),
                });

                if (!string.IsNullOrWhiteSpace(turn.NarratorText))
                {
                    branch.Add(new ConversationBranchMessage
                    {
                        NodeId = $"turn:{turn.Id}:assistant",
                        Role = "assistant",
                        RawText = turn.NarratorText,
                        DisplayText = turn.NarratorText,
                        BranchIndex = branchIndex++,
                        IsUtility = ConversationStreamParser.IsUtilityAssistantMessage(turn.NarratorText),
                    });
                }
            }
        }

        if (branch.Count == 0)
            return false;

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            branch,
            ThreadConversationLogCaptureSource.Migration);

        return true;
    }
}
