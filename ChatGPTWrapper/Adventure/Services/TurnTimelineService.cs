using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class TurnTimelineService
{
    public static TurnRecord CreateTurn(AdventureBundle bundle, string playerText, Guid? parentTurnId = null)
    {
        var index = bundle.Log.Turns.Count > 0
            ? bundle.Log.Turns.Max(t => t.Index) + 1
            : 0;

        var turn = new TurnRecord
        {
            Index = index,
            PlayerText = playerText,
            Status = TurnStatus.Pending,
            ParentTurnId = parentTurnId,
        };

        bundle.Log.Turns.Add(turn);
        AdventureSessionService.AttachTurnToSession(bundle, turn);
        return turn;
    }

    public static void AcceptTurn(TurnRecord turn, string narratorText)
    {
        turn.NarratorText = narratorText;
        turn.Status = TurnStatus.Accepted;
    }

    public static void LeavePendingIncompleteCapture(TurnRecord turn, string? partialNarratorText)
    {
        turn.NarratorText = partialNarratorText;
        turn.Status = TurnStatus.Pending;
    }

    public static bool RemovePendingTurn(AdventureBundle bundle, TurnRecord turn)
    {
        if (turn.Status != TurnStatus.Pending)
            return false;

        return bundle.Log.Turns.Remove(turn);
    }

    public static bool UndoLast(AdventureBundle bundle)
    {
        var last = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderByDescending(t => t.Index)
            .FirstOrDefault();

        if (last is null)
            return false;

        bundle.Log.Turns.Remove(last);
        return true;
    }

    public static void EditTurn(TurnRecord turn, string? playerText, string? narratorText)
    {
        if (playerText is not null)
            turn.PlayerText = playerText;

        if (narratorText is not null)
            turn.NarratorText = narratorText;
    }

    /// <summary>Removes accepted log turns after an edit invalidation point.</summary>
    public static int TrimAcceptedTurnsAfterIndex(AdventureBundle bundle, int turnIndex)
    {
        var toRemove = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted && t.Index > turnIndex)
            .ToList();

        foreach (var turn in toRemove)
            bundle.Log.Turns.Remove(turn);

        return toRemove.Count;
    }

    public static AdventureBundle BranchFrom(AdventureBundle source, int fromTurnIndex, string newTitle)
    {
        var clone = AdventureStore.CreateNew(newTitle, source.Scenario);
        var turns = source.Log.Turns
            .Where(t => t.Index <= fromTurnIndex && t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .ToList();

        clone.Scenario = source.Scenario;
        clone.Summary = source.Summary;
        clone.State = source.State;
        clone.Memory = source.Memory;
        clone.Entities = source.Entities;
        clone.Cards = source.Cards;

        foreach (var t in turns)
        {
            clone.Log.Turns.Add(new TurnRecord
            {
                Index = t.Index,
                PlayerText = t.PlayerText,
                NarratorText = t.NarratorText,
                Status = TurnStatus.Accepted,
                At = t.At,
                ParentTurnId = t.ParentTurnId,
            });
        }

        AdventureStore.Save(clone);
        return clone;
    }

    public static string CreateSaveState(AdventureBundle bundle, string name)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var folder = Path.Combine(bundle.DirectoryPath, "save-states", $"{stamp}-{safeName}");
        Directory.CreateDirectory(folder);
        AdventureStore.Save(bundle);

        foreach (var file in Directory.EnumerateFiles(bundle.DirectoryPath))
        {
            var fn = Path.GetFileName(file);
            if (fn is "save-states")
                continue;
            File.Copy(file, Path.Combine(folder, fn), overwrite: true);
        }

        return folder;
    }

    public static void RestoreSaveState(AdventureBundle bundle, string saveStateFolder)
    {
        foreach (var file in Directory.EnumerateFiles(saveStateFolder))
        {
            var dest = Path.Combine(bundle.DirectoryPath, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
    }

    public static void ArchiveAlternate(TurnRecord turn, string text, bool fromRegenerate)
    {
        turn.Attempts.Add(new ResponseAttempt
        {
            NarratorText = text,
            Accepted = false,
            FromRegenerate = fromRegenerate,
        });
    }
}
