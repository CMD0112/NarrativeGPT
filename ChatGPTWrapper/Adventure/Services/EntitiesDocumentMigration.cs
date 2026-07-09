using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntitiesDocumentMigration
{
    public static bool Migrate(EntitiesDocument entities)
    {
        var changed = false;
        if (entities.SchemaVersion < EntitiesDocument.CurrentSchemaVersion)
        {
            entities.ExtendedFieldsEnsureInitialized();
            entities.SchemaVersion = EntitiesDocument.CurrentSchemaVersion;
            changed = true;
        }

        if (EntitiesCanonHygieneService.Apply(entities))
            changed = true;

        if (EntitiesStructuredFieldMigrationService.Migrate(entities))
            changed = true;

        return changed;
    }
}

internal static class EntitiesDocumentMigrationExtensions
{
    public static void ExtendedFieldsEnsureInitialized(this EntitiesDocument entities)
    {
        entities.Player.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        entities.Player.Tags ??= [];
        entities.Player.Aliases ??= [];

        foreach (var entry in entities.Party)
        {
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            entry.Tags ??= [];
            entry.Aliases ??= [];
        }

        foreach (var entry in entities.Characters)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Locations)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Quests)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Factions)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Concepts)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Mysteries)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Conflicts)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Consequences)
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entities.Inventory)
        {
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            entry.Tags ??= [];
        }

        entities.Vehicles ??= [];
        foreach (var entry in entities.Vehicles)
        {
            entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            entry.Tags ??= [];
            entry.Aliases ??= [];
        }
    }
}
