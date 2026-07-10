using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Removes duplicate plot entries left behind after partial renames
/// (e.g. both "Protect Nessa" and "Protect Anwen" when Anwen is the canonical name).
/// </summary>
public static class CanonRenameOrphanCleanupService
{
    public static int CountOrphans(AdventureBundle bundle)
    {
        var count = 0;
        foreach (var (prior, current) in CollectAliasPairs(bundle))
            count += CountOrphans(bundle, prior, current);
        return count;
    }

    public static int PruneAllFromEntityAliases(AdventureBundle bundle)
    {
        var removed = 0;
        foreach (var (prior, current) in CollectAliasPairs(bundle))
            removed += PruneRenameOrphans(bundle, prior, current);
        return removed;
    }

    public static int PruneRenameOrphans(AdventureBundle bundle, string priorName, string newName)
    {
        if (string.IsNullOrWhiteSpace(priorName) || string.IsNullOrWhiteSpace(newName))
            return 0;

        if (string.Equals(priorName.Trim(), newName.Trim(), StringComparison.OrdinalIgnoreCase))
            return 0;

        var removed = 0;
        removed += PruneList(bundle.Entities.Quests, q => q.Title, priorName, newName);
        removed += PruneList(bundle.Entities.Mysteries, m => m.Question, priorName, newName);
        removed += PruneList(bundle.Entities.Conflicts, c => c.Title, priorName, newName);
        removed += PruneList(bundle.Entities.Consequences, c => c.Trigger, priorName, newName);
        return removed;
    }

    private static int CountOrphans(AdventureBundle bundle, string priorName, string newName)
    {
        if (!HasNewVersion(bundle, priorName, newName))
            return 0;

        return CountInList(bundle.Entities.Quests, q => q.Title, priorName)
            + CountInList(bundle.Entities.Mysteries, m => m.Question, priorName)
            + CountInList(bundle.Entities.Conflicts, c => c.Title, priorName)
            + CountInList(bundle.Entities.Consequences, c => c.Trigger, priorName);
    }

    private static int PruneList<T>(
        List<T> list,
        Func<T, string> getText,
        string priorName,
        string newName)
    {
        if (!HasNewVersion(list, getText, priorName, newName))
            return 0;

        var toRemove = list.Where(item => ContainsWholeWord(getText(item), priorName)).ToList();
        foreach (var item in toRemove)
            list.Remove(item);

        return toRemove.Count;
    }

    private static bool HasNewVersion(AdventureBundle bundle, string priorName, string newName) =>
        HasNewVersion(bundle.Entities.Quests, q => q.Title, priorName, newName)
        || HasNewVersion(bundle.Entities.Mysteries, m => m.Question, priorName, newName)
        || HasNewVersion(bundle.Entities.Conflicts, c => c.Title, priorName, newName)
        || HasNewVersion(bundle.Entities.Consequences, c => c.Trigger, priorName, newName);

    private static bool HasNewVersion<T>(IEnumerable<T> items, Func<T, string> getText, string priorName, string newName) =>
        items.Any(item => ContainsWholeWord(getText(item), newName));

    private static int CountInList<T>(IEnumerable<T> items, Func<T, string> getText, string priorName) =>
        items.Count(item => ContainsWholeWord(getText(item), priorName));

    private static IEnumerable<(string Prior, string Current)> CollectAliasPairs(AdventureBundle bundle)
    {
        foreach (var character in bundle.Entities.Characters)
        {
            foreach (var pair in PairsForName(character.Name, character.Aliases))
                yield return pair;
        }
    }

    private static IEnumerable<(string Prior, string Current)> PairsForName(string name, IReadOnlyList<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            if (string.Equals(alias.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            yield return (alias.Trim(), name.Trim());
        }
    }

    private static bool ContainsWholeWord(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term))
            return false;

        var pattern = $@"\b{Regex.Escape(term.Trim())}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
