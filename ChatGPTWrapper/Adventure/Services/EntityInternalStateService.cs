using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Read/write helpers for <see cref="EntityInternalStateDocument"/>.</summary>
public static class EntityInternalStateService
{
    public const string FileName = "entity-state.json";

    public static EntityStateRecord? TryGet(AdventureBundle bundle, string kindId, Guid entityId)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.EntityInternalState.Entries.FirstOrDefault(e =>
            e.EntityId == entityId
            && string.Equals(e.KindId, kindId, StringComparison.OrdinalIgnoreCase));
    }

    public static EntityStateRecord GetOrCreate(AdventureBundle bundle, string kindId, Guid entityId, bool seedFromCanon = true)
    {
        var existing = TryGet(bundle, kindId, entityId);
        if (existing is not null)
            return existing;

        var record = new EntityStateRecord
        {
            EntityId = entityId,
            KindId = kindId,
            Revision = 0,
        };
        InitializeEmptyState(record, kindId);
        if (seedFromCanon)
        {
            EntityCanonStateOverlapService.SeedFromCanon(bundle, record, kindId);
            record.Revision = 1;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }

        bundle.EntityInternalState.Entries.Add(record);
        return record;
    }

    public static EntityStateRecord BindIfMissing(AdventureBundle bundle, string kindId, Guid entityId) =>
        GetOrCreate(bundle, kindId, entityId, seedFromCanon: true);

    /// <summary>
    /// Creates a seeded play-state record when missing. Existing records are never re-seeded.
    /// </summary>
    public static bool EnsureTracked(AdventureBundle bundle, string kindId, Guid entityId)
    {
        if (TryGet(bundle, kindId, entityId) is not null)
            return false;

        BindIfMissing(bundle, kindId, entityId);
        return true;
    }

    /// <summary>
    /// Ensures every canon entity has a play-state row with baseline fields seeded from profile.
    /// Author edits on existing rows are preserved (no re-seed).
    /// </summary>
    public static int EnsureAllCanonEntitiesTracked(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var created = 0;
        created += EnsureTracked(bundle, EntityInternalStateKind.Player, EntityEditMapper.PlayerEntityId) ? 1 : 0;

        foreach (var spec in CanonSchemaRegistry.AllKinds)
        {
            if (string.Equals(spec.KindId, EntityInternalStateKind.Player, StringComparison.OrdinalIgnoreCase))
                continue;

            if (spec.IsSingleton || !EntityInternalStateKind.All.Contains(spec.KindId))
                continue;

            foreach (var entity in CanonEntityResolver.EnumerateEntities(bundle.Entities, spec))
            {
                var entityId = CanonEntityResolver.GetEntityId(entity, spec);
                if (entityId == Guid.Empty)
                    continue;

                if (EnsureTracked(bundle, spec.KindId, entityId))
                    created++;
            }
        }

        foreach (var vehicle in bundle.Entities.Vehicles)
        {
            if (EnsureTracked(bundle, EntityInternalStateKind.Vehicle, vehicle.Id))
                created++;
        }

        return created;
    }

    public static void Upsert(AdventureBundle bundle, EntityStateRecord record)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(record);

        record.UpdatedAt = DateTimeOffset.UtcNow;
        record.Revision++;

        var index = bundle.EntityInternalState.Entries.FindIndex(e =>
            e.EntityId == record.EntityId
            && string.Equals(e.KindId, record.KindId, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
            bundle.EntityInternalState.Entries[index] = record;
        else
            bundle.EntityInternalState.Entries.Add(record);
    }

    public static bool Remove(AdventureBundle bundle, string kindId, Guid entityId)
    {
        var removed = bundle.EntityInternalState.Entries.RemoveAll(e =>
            e.EntityId == entityId
            && string.Equals(e.KindId, kindId, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    public static string ResolveKindId(AdventurePlayEntityKind kind, string? uiCategory = null)
    {
        if (!string.IsNullOrWhiteSpace(uiCategory)
            && CanonEntityResolver.TryGetSpec(uiCategory) is { } spec)
        {
            return spec.KindId;
        }

        return kind switch
        {
            AdventurePlayEntityKind.Player => EntityInternalStateKind.Player,
            AdventurePlayEntityKind.PartyCompanion => EntityInternalStateKind.Party,
            AdventurePlayEntityKind.Location => EntityInternalStateKind.Location,
            AdventurePlayEntityKind.Quest => EntityInternalStateKind.Quest,
            AdventurePlayEntityKind.Thing => EntityInternalStateKind.Inventory,
            AdventurePlayEntityKind.Faction => EntityInternalStateKind.Faction,
            AdventurePlayEntityKind.Concept => EntityInternalStateKind.Concept,
            _ => EntityInternalStateKind.Npc,
        };
    }

    public static string ResolveKindIdFromExtractionType(string? entityType) => EntityTypeNormalizer.Normalize(entityType) switch
    {
        "person" => EntityInternalStateKind.Npc,
        "place" => EntityInternalStateKind.Location,
        "thing" => EntityInternalStateKind.Inventory,
        "faction" => EntityInternalStateKind.Faction,
        "quest" => EntityInternalStateKind.Quest,
        "concept" => EntityInternalStateKind.Concept,
        "vehicle" => EntityInternalStateKind.Vehicle,
        "mystery" => EntityInternalStateKind.Mystery,
        "conflict" => EntityInternalStateKind.Conflict,
        "consequence" => EntityInternalStateKind.Consequence,
        _ => entityType ?? "",
    };

    public static object CreateEmptyStateObject(string kindId)
    {
        var record = new EntityStateRecord { KindId = kindId };
        InitializeEmptyState(record, kindId);
        return GetStateObject(record, kindId) ?? new CustomInternalState();
    }

    public static object? GetStateObject(EntityStateRecord record, string kindId) => kindId switch
    {
        EntityInternalStateKind.Player => record.Player,
        EntityInternalStateKind.Party => record.Companion,
        EntityInternalStateKind.Npc => record.Character,
        EntityInternalStateKind.Location => record.Location,
        EntityInternalStateKind.Faction => record.Faction,
        EntityInternalStateKind.Concept => record.Concept,
        EntityInternalStateKind.Quest => record.Quest,
        EntityInternalStateKind.Mystery => record.Mystery,
        EntityInternalStateKind.Conflict => record.Conflict,
        EntityInternalStateKind.Consequence => record.Consequence,
        EntityInternalStateKind.Inventory => record.Item,
        EntityInternalStateKind.Vehicle => record.Vehicle,
        EntityInternalStateKind.Custom => record.Custom,
        _ => null,
    };

    public static void SetStateObject(EntityStateRecord record, string kindId, object state)
    {
        switch (kindId)
        {
            case EntityInternalStateKind.Player:
                record.Player = (PlayerInternalState)state;
                break;
            case EntityInternalStateKind.Party:
                record.Companion = (CompanionInternalState)state;
                break;
            case EntityInternalStateKind.Npc:
                record.Character = (CharacterInternalState)state;
                break;
            case EntityInternalStateKind.Location:
                record.Location = (LocationInternalState)state;
                break;
            case EntityInternalStateKind.Faction:
                record.Faction = (FactionInternalState)state;
                break;
            case EntityInternalStateKind.Concept:
                record.Concept = (ConceptInternalState)state;
                break;
            case EntityInternalStateKind.Quest:
                record.Quest = (QuestInternalState)state;
                break;
            case EntityInternalStateKind.Mystery:
                record.Mystery = (MysteryInternalState)state;
                break;
            case EntityInternalStateKind.Conflict:
                record.Conflict = (ConflictInternalState)state;
                break;
            case EntityInternalStateKind.Consequence:
                record.Consequence = (ConsequenceInternalState)state;
                break;
            case EntityInternalStateKind.Inventory:
                record.Item = (ItemInternalState)state;
                break;
            case EntityInternalStateKind.Vehicle:
                record.Vehicle = (VehicleInternalState)state;
                break;
            case EntityInternalStateKind.Custom:
                record.Custom = (CustomInternalState)state;
                break;
        }
    }

    public static string ResolveKindIdForCategory(string category, AdventurePlayEntityKind kind) =>
        ResolveKindId(kind, category);

    internal static void InitializeEmptyStatePublic(EntityStateRecord record, string kindId) =>
        InitializeEmptyState(record, kindId);

    private static void InitializeEmptyState(EntityStateRecord record, string kindId)
    {
        switch (kindId)
        {
            case EntityInternalStateKind.Player:
                record.Player = new PlayerInternalState();
                break;
            case EntityInternalStateKind.Party:
                record.Companion = new CompanionInternalState();
                break;
            case EntityInternalStateKind.Npc:
                record.Character = new CharacterInternalState();
                break;
            case EntityInternalStateKind.Location:
                record.Location = new LocationInternalState();
                break;
            case EntityInternalStateKind.Faction:
                record.Faction = new FactionInternalState();
                break;
            case EntityInternalStateKind.Concept:
                record.Concept = new ConceptInternalState();
                break;
            case EntityInternalStateKind.Quest:
                record.Quest = new QuestInternalState();
                break;
            case EntityInternalStateKind.Mystery:
                record.Mystery = new MysteryInternalState();
                break;
            case EntityInternalStateKind.Conflict:
                record.Conflict = new ConflictInternalState();
                break;
            case EntityInternalStateKind.Consequence:
                record.Consequence = new ConsequenceInternalState();
                break;
            case EntityInternalStateKind.Inventory:
                record.Item = new ItemInternalState();
                break;
            case EntityInternalStateKind.Vehicle:
                record.Vehicle = new VehicleInternalState();
                break;
            case EntityInternalStateKind.Custom:
                record.Custom = new CustomInternalState();
                break;
        }
    }
}
