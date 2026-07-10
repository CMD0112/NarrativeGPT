using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class CanonTextReplacement
{
    private static readonly HashSet<string> SkipPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(CharacterEntry.Id),
        nameof(CharacterEntry.ImagePath),
        nameof(EntitiesDocument.SchemaVersion),
        nameof(ScenarioDocument.SchemaVersion),
        "Aliases",
    };

    public static string ReplaceWholeWord(string text, string priorName, string newName)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(priorName))
            return text;

        var pattern = $@"\b{Regex.Escape(priorName.Trim())}\b";
        return Regex.Replace(
            text,
            pattern,
            newName.Trim(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static void ReplaceInScenario(ScenarioDocument scenario, string priorName, string newName)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ReplaceInObject(scenario, priorName, newName);
    }

    public static void ReplaceInEntities(EntitiesDocument entities, string priorName, string newName)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ReplaceInObject(entities.Player, priorName, newName);

        foreach (var character in entities.Characters)
            ReplaceInObject(character, priorName, newName);
        foreach (var companion in entities.Party)
            ReplaceInObject(companion, priorName, newName);
        foreach (var location in entities.Locations)
            ReplaceInObject(location, priorName, newName);
        foreach (var item in entities.Inventory)
            ReplaceInObject(item, priorName, newName);
        foreach (var quest in entities.Quests)
            ReplaceInObject(quest, priorName, newName);
        foreach (var faction in entities.Factions)
            ReplaceInObject(faction, priorName, newName);
        foreach (var concept in entities.Concepts)
            ReplaceInObject(concept, priorName, newName);
        foreach (var relationship in entities.Relationships)
            ReplaceInObject(relationship, priorName, newName);
        foreach (var mystery in entities.Mysteries)
            ReplaceInObject(mystery, priorName, newName);
        foreach (var conflict in entities.Conflicts)
            ReplaceInObject(conflict, priorName, newName);
        foreach (var consequence in entities.Consequences)
            ReplaceInObject(consequence, priorName, newName);
        foreach (var custom in entities.CustomEntries)
            ReplaceInObject(custom, priorName, newName);
    }

    public static void ReplaceInObject(object target, string priorName, string newName)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || SkipPropertyNames.Contains(property.Name))
                continue;

            if (property.PropertyType == typeof(string))
            {
                var value = (string?)property.GetValue(target);
                if (string.IsNullOrEmpty(value))
                    continue;

                var replaced = ReplaceWholeWord(value, priorName, newName);
                if (!string.Equals(replaced, value, StringComparison.Ordinal))
                    property.SetValue(target, replaced);
                continue;
            }

            if (property.PropertyType == typeof(Dictionary<string, string>))
            {
                if (property.GetValue(target) is not Dictionary<string, string> dict)
                    continue;

                foreach (var key in dict.Keys.ToList())
                {
                    var replaced = ReplaceWholeWord(dict[key], priorName, newName);
                    if (!string.Equals(replaced, dict[key], StringComparison.Ordinal))
                        dict[key] = replaced;
                }

                continue;
            }

            if (property.PropertyType == typeof(List<string>)
                && property.GetValue(target) is IList { } list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is not string item || string.IsNullOrEmpty(item))
                        continue;

                    var replaced = ReplaceWholeWord(item, priorName, newName);
                    if (!string.Equals(replaced, item, StringComparison.Ordinal))
                        list[i] = replaced;
                }
            }
        }
    }
}
