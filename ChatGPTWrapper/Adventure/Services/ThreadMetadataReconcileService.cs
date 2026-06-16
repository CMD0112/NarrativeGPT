using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class ThreadMetadataReconcileResult
{
    public bool Changed { get; init; }

    public string? DriftWarning { get; init; }
}

internal static class ThreadMetadataReconcileService
{
    public static ThreadMetadataReconcileResult Reconcile(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var changed = false;
        var accepted = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .ToList();

        if (bundle.ThreadMetadata.Messages.Count == 0 && accepted.Count > 0)
        {
            foreach (var turn in accepted)
            {
                if (HasActiveMessage(bundle, $"turn:{turn.Id}:user"))
                    continue;

                ThreadMetadataService.RecordPlayTurnExchange(
                    bundle,
                    turn,
                    turn.PlayerText,
                    turn.NarratorText,
                    turn.PromptPacketHash);
                changed = true;
            }
        }

        string? drift = null;
        if (accepted.Count > 0)
        {
            var activePlayMessages = ThreadMetadataService.ActiveMessages(bundle)
                .Count(m => !m.IsUtility && m.LinkedTurnId is not null);
            var expected = accepted.Count * 2;
            if (Math.Abs(activePlayMessages - expected) > 2)
            {
                drift =
                    $"thread-metadata play messages ({activePlayMessages}) diverges from log accepted turns ({accepted.Count}).";
            }
        }

        return new ThreadMetadataReconcileResult
        {
            Changed = changed,
            DriftWarning = drift,
        };
    }

    private static bool HasActiveMessage(AdventureBundle bundle, string messageId) =>
        bundle.ThreadMetadata.Messages.Any(m =>
            string.Equals(m.MessageId, messageId, StringComparison.Ordinal)
            && !m.SupersededByEdit);
}
