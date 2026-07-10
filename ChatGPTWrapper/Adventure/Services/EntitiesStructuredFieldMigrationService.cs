using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Promotes labeled lines embedded in freeform description blobs into typed canon fields.
/// </summary>
internal static class EntitiesStructuredFieldMigrationService
{
    public static bool Migrate(EntitiesDocument entities)
    {
        var changed = false;

        if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entities.Player, CanonSchemaRegistry.Player))
            changed = true;

        foreach (var entry in entities.Party)
        {
            if (MigratePartyExtendedFieldAliases(entry))
                changed = true;
            if (CanonFieldMapper.TryPromoteKnownExtendedFields(entry, CanonSchemaRegistry.Party))
                changed = true;
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Party))
                changed = true;
        }

        foreach (var entry in entities.Characters)
        {
            if (CanonFieldMapper.TryPromoteKnownExtendedFields(entry, CanonSchemaRegistry.Npc))
                changed = true;
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Npc))
                changed = true;
        }

        foreach (var entry in entities.Locations)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Location))
                changed = true;
        }

        foreach (var entry in entities.Factions)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Faction))
                changed = true;
        }

        foreach (var entry in entities.Concepts)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Concept))
                changed = true;
        }

        foreach (var entry in entities.Quests)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Quest))
                changed = true;
        }

        foreach (var entry in entities.Mysteries)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Mystery))
                changed = true;
        }

        foreach (var entry in entities.Conflicts)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Conflict))
                changed = true;
        }

        foreach (var entry in entities.Consequences)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Consequence))
                changed = true;
        }

        foreach (var entry in entities.Inventory)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Inventory))
                changed = true;
        }

        foreach (var entry in entities.CustomEntries)
        {
            if (CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entry, CanonSchemaRegistry.Custom))
                changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Party entries sometimes inherit NPC-style labels in extendedFields (Role, Motives, Status).
    /// </summary>
    private static bool MigratePartyExtendedFieldAliases(CompanionEntry entry)
    {
        entry.ExtendedFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (entry.ExtendedFields.Count == 0)
            return false;

        var changed = false;
        changed |= MoveExtendedToCompanionField(entry, "Role", static e => e.Condition, (e, v) => e.Condition = v);
        changed |= MoveExtendedToCompanionField(entry, "Motives", static e => e.Goals, (e, v) => e.Goals = v);
        changed |= MoveExtendedToCompanionField(entry, "Status", static e => e.Attitude, (e, v) => e.Attitude = v);
        changed |= MoveExtendedToCompanionField(entry, "Relationship", static e => e.Relationship, (e, v) => e.Relationship = v);

        if (entry.ExtendedFields.Remove("> Flavor", out var flavorBlock)
            || entry.ExtendedFields.Remove("Flavor", out flavorBlock))
        {
            if (!string.IsNullOrWhiteSpace(flavorBlock) && string.IsNullOrWhiteSpace(entry.Flavor))
                entry.Flavor = flavorBlock.Trim().Trim('"');
            changed = true;
        }

        return changed;
    }

    private static bool MoveExtendedToCompanionField(
        CompanionEntry entry,
        string extendedKey,
        Func<CompanionEntry, string> getField,
        Action<CompanionEntry, string> setField)
    {
        if (!entry.ExtendedFields.TryGetValue(extendedKey, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(getField(entry)))
            setField(entry, value.Trim());

        entry.ExtendedFields.Remove(extendedKey);
        return true;
    }
}
