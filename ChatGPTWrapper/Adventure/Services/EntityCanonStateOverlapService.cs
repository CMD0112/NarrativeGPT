using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class CanonStateDivergence
{
    public required string StatePath { get; init; }

    public required string CanonFieldKey { get; init; }

    public required string StateValue { get; init; }

    public required string CanonValue { get; init; }

    public required string Message { get; init; }
}

/// <summary>
/// Canon ↔ play-state overlap mapping, seed, reset, divergence, and baseline labels (CMD-466/469/471/472).
/// </summary>
internal static class EntityCanonStateOverlapService
{
    private static readonly string[] StateBlockRoots =
    [
        "emotional", "physical", "social", "motivation", "knowledge", "equipment",
        "presence", "identity", "tactical", "narrative", "resources", "flags",
    ];

    internal static readonly HashSet<string> CanonOnlyJsonKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "description", "role", "roleOrStatus", "personality", "useInPlay", "name",
        "abilities", "weaknesses", "flavor", "relationship", "relationshipToPlayer",
        "aliases", "imagePath", "extendedFields", "category", "entityType", "action",
    };

    public static string? GetBaselineLabelForStatePath(string statePath)
    {
        var normalized = NormalizeStatePath(statePath);
        return MappedFields.FirstOrDefault(m =>
            string.Equals(NormalizeStatePath(m.StatePath), normalized, StringComparison.OrdinalIgnoreCase))?.BaselineLabel;
    }

    public static bool TryResolveCanonEntity(
        AdventureBundle bundle,
        string kindId,
        Guid entityId,
        out object? entity,
        out CanonEntityKindSpec? spec)
    {
        entity = null;
        spec = null;

        if (string.Equals(kindId, EntityInternalStateKind.Player, StringComparison.OrdinalIgnoreCase))
        {
            entity = bundle.Entities.Player;
            spec = CanonSchemaRegistry.Player;
            return true;
        }

        if (string.Equals(kindId, EntityInternalStateKind.Vehicle, StringComparison.OrdinalIgnoreCase))
        {
            entity = bundle.Entities.Vehicles.FirstOrDefault(v => v.Id == entityId);
            spec = null;
            return entity is not null;
        }

        spec = ResolveSpecForKindId(kindId);
        if (spec is null)
            return false;

        var resolvedSpec = spec;
        entity = CanonEntityResolver.EnumerateEntities(bundle.Entities, resolvedSpec)
            .FirstOrDefault(e => CanonEntityResolver.GetEntityId(e, resolvedSpec) == entityId);
        return entity is not null;
    }

    public static string ResolveEntityName(AdventureBundle bundle, string kindId, Guid entityId)
    {
        if (!TryResolveCanonEntity(bundle, kindId, entityId, out var entity, out var spec) || entity is null || spec is null)
            return entityId == Guid.Empty ? "Player" : entityId.ToString("N")[..8];

        var name = CanonFieldMapper.GetTitle(entity, spec);
        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
    }

    public static void SeedFromCanon(AdventureBundle bundle, EntityStateRecord record, string kindId)
    {
        if (!TryResolveCanonEntity(bundle, kindId, record.EntityId, out var entity, out var spec) || entity is null)
            return;

        var state = EntityInternalStateService.GetStateObject(record, kindId);
        if (state is null)
            return;

        foreach (var mapping in MappedFields)
        {
            if (!mapping.AppliesToKind(kindId))
                continue;

            var canonValue = mapping.ResolveCanon(entity, spec, kindId, bundle);
            if (string.IsNullOrWhiteSpace(canonValue))
                continue;

            EntityInternalStatePathAccessor.TrySetDisplayValue(
                state, mapping.StatePath, mapping.ValueKind, canonValue);
        }

        EntityInternalStateService.SetStateObject(record, kindId, state);
    }

    public static void ResetMappedFieldsFromCanon(AdventureBundle bundle, EntityStateRecord record, string kindId)
    {
        InitializeEmptyMappedBlocks(record, kindId);
        SeedFromCanon(bundle, record, kindId);
    }

    public static IReadOnlyList<CanonStateDivergence> DetectDivergences(
        AdventureBundle bundle,
        string kindId,
        Guid entityId)
    {
        var record = EntityInternalStateService.TryGet(bundle, kindId, entityId);
        if (record is null
            || !TryResolveCanonEntity(bundle, kindId, entityId, out var entity, out var spec)
            || entity is null)
        {
            return [];
        }

        var state = EntityInternalStateService.GetStateObject(record, kindId);
        if (state is null)
            return [];

        var list = new List<CanonStateDivergence>();
        foreach (var mapping in MappedFields)
        {
            if (!mapping.AppliesToKind(kindId))
                continue;

            var canonValue = mapping.ResolveCanon(entity, spec, kindId, bundle);
            if (string.IsNullOrWhiteSpace(canonValue))
                continue;

            CompareMappedField(list, mapping, state, canonValue);
        }

        return list;
    }

    /// <summary>Live divergence check for a single mapped field (uses current editor value, not only persisted state).</summary>
    public static string? DescribeLiveDivergence(
        AdventureBundle bundle,
        string kindId,
        Guid entityId,
        string statePath,
        string stateValue)
    {
        if (string.IsNullOrWhiteSpace(stateValue)
            || !TryResolveCanonEntity(bundle, kindId, entityId, out var entity, out var spec)
            || entity is null)
        {
            return null;
        }

        if (!TryResolveMappedField(statePath, out var mapping)
            || !mapping.AppliesToKind(kindId))
        {
            return null;
        }

        var canonValue = mapping.ResolveCanon(entity, spec, kindId, bundle);
        if (string.IsNullOrWhiteSpace(canonValue))
            return null;

        if (ValuesEquivalent(mapping.ValueKind, canonValue, stateValue))
            return null;

        return $"{NormalizeStatePath(statePath)} ({Truncate(stateValue, 80)}) differs from canon {mapping.CanonFieldKey} ({Truncate(canonValue, 80)})";
    }

    internal static string NormalizeStatePath(string path) =>
        path.Replace('.', '/').ToLowerInvariant();

    private static bool TryResolveMappedField(string statePath, out CanonStateFieldMapping mapping)
    {
        var normalized = NormalizeStatePath(statePath);
        foreach (var candidate in MappedFields)
        {
            if (string.Equals(NormalizeStatePath(candidate.StatePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                mapping = candidate;
                return true;
            }
        }

        mapping = default!;
        return false;
    }

    public static bool TryGetDivergenceForPath(
        IReadOnlyList<CanonStateDivergence> divergences,
        string statePath,
        out CanonStateDivergence? divergence)
    {
        var normalized = NormalizeStatePath(statePath);
        divergence = divergences.FirstOrDefault(d =>
            string.Equals(NormalizeStatePath(d.StatePath), normalized, StringComparison.OrdinalIgnoreCase));
        return divergence is not null;
    }

    public static bool TryBuildPromoteDraft(
        AdventureBundle bundle,
        string kindId,
        Guid entityId,
        CanonStateDivergence divergence,
        out CanonEvolutionProposalEntry? draft)
    {
        draft = null;
        if (!TryResolveCanonEntity(bundle, kindId, entityId, out _, out _))
            return false;

        draft = new CanonEvolutionProposalEntry
        {
            EntityId = entityId,
            KindId = kindId,
            EntityName = ResolveEntityName(bundle, kindId, entityId),
            CanonFieldKey = divergence.CanonFieldKey,
            SourceStatePath = divergence.StatePath,
            ProposedCanonValue = divergence.StateValue,
            Rationale = $"Promote live state at {divergence.StatePath} to canon {divergence.CanonFieldKey}.",
        };
        return true;
    }

    public static bool ShouldIncludeInPlaySkim(AdventureBundle bundle, EntityStateRecord record)
    {
        if (EntityInternalStateKind.PlayTracked.Contains(record.KindId) == false)
            return false;

        if (string.Equals(record.KindId, EntityInternalStateKind.Player, StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryResolveCanonEntity(bundle, record.KindId, record.EntityId, out var entity, out var spec) && entity is not null)
        {
            if (CanonFieldMapper.GetPinned(entity))
                return true;
        }

        var state = EntityInternalStateService.GetStateObject(record, record.KindId);
        if (state is null)
            return false;

        if (EntityInternalStatePathAccessor.TryGetDisplayValue(state, "presence.isPresent", EntityInternalStateFieldKind.Bool, out var present)
            && string.Equals(present, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(EntityInternalStateSummary.Build(record.KindId, state));
    }

    public static IEnumerable<ContinuityWarning> AnalyzeCrossLayer(AdventureBundle bundle)
    {
        foreach (var record in bundle.EntityInternalState.Entries)
        {
            foreach (var divergence in DetectDivergences(bundle, record.KindId, record.EntityId))
            {
                var hasQueuedPromotion = bundle.Entities.CanonEvolutionReviewQueue.Any(p =>
                    p.EntityId == record.EntityId
                    && string.Equals(p.CanonFieldKey, divergence.CanonFieldKey, StringComparison.OrdinalIgnoreCase));

                if (hasQueuedPromotion)
                    continue;

                yield return new ContinuityWarning
                {
                    Message =
                        $"{ResolveEntityName(bundle, record.KindId, record.EntityId)}: live state ({divergence.StatePath}) "
                        + $"differs from canon ({divergence.CanonFieldKey}). Consider promote or reset.",
                    Severity = "warning",
                };
            }
        }
    }

    internal static bool LooksLikeStateBlockKey(string key) =>
        StateBlockRoots.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static CanonEntityKindSpec? ResolveSpecForKindId(string kindId) => kindId switch
    {
        EntityInternalStateKind.Party => CanonSchemaRegistry.Party,
        EntityInternalStateKind.Npc => CanonSchemaRegistry.Npc,
        EntityInternalStateKind.Location => CanonSchemaRegistry.Location,
        EntityInternalStateKind.Faction => CanonSchemaRegistry.Faction,
        EntityInternalStateKind.Concept => CanonSchemaRegistry.Concept,
        EntityInternalStateKind.Quest => CanonSchemaRegistry.Quest,
        EntityInternalStateKind.Inventory => CanonSchemaRegistry.Inventory,
        _ => null,
    };

    private static void InitializeEmptyMappedBlocks(EntityStateRecord record, string kindId)
    {
        var state = EntityInternalStateService.GetStateObject(record, kindId);
        if (state is null)
            return;

        foreach (var mapping in MappedFields)
        {
            if (!mapping.AppliesToKind(kindId))
                continue;

            if (mapping.ValueKind == EntityInternalStateFieldKind.StringList)
                EntityInternalStatePathAccessor.TrySetDisplayValue(state, mapping.StatePath, mapping.ValueKind, "");
            else if (mapping.ValueKind == EntityInternalStateFieldKind.String)
                ClearStringField(state, mapping.StatePath);
        }

        EntityInternalStateService.SetStateObject(record, kindId, state);
    }

    private static void ClearStringField(object state, string path)
    {
        if (EntityInternalStatePathAccessor.TrySetDisplayValue(state, path, EntityInternalStateFieldKind.String, ""))
            return;
    }

    private static void CompareMappedField(
        List<CanonStateDivergence> list,
        CanonStateFieldMapping mapping,
        object state,
        string canonValue)
    {
        if (!EntityInternalStatePathAccessor.TryGetDisplayValue(
                state, mapping.StatePath, mapping.ValueKind, out var stateValue)
            || string.IsNullOrWhiteSpace(stateValue))
        {
            return;
        }

        if (ValuesEquivalent(mapping.ValueKind, canonValue, stateValue))
            return;

        AddIfBothDiffer(list, mapping.CanonFieldKey, mapping.StatePath, canonValue, stateValue);
    }

    private static bool ValuesEquivalent(
        EntityInternalStateFieldKind kind,
        string canonValue,
        string stateValue)
    {
        if (kind == EntityInternalStateFieldKind.StringList)
        {
            return string.Equals(
                NormalizeMultiline(canonValue),
                NormalizeMultiline(stateValue),
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(canonValue.Trim(), stateValue.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMultiline(string value) =>
        string.Join(
            '\n',
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

    private static string FormatGoalLines(string text) => NormalizeMultiline(text);

    private sealed class CanonStateFieldMapping
    {
        public required string StatePath { get; init; }

        public required string CanonFieldKey { get; init; }

        public required string BaselineLabel { get; init; }

        public EntityInternalStateFieldKind ValueKind { get; init; } = EntityInternalStateFieldKind.String;

        public required HashSet<string> KindIds { get; init; }

        public required Func<object, CanonEntityKindSpec?, string, AdventureBundle, string?> ResolveCanon { get; init; }

        public bool AppliesToKind(string kindId) =>
            KindIds.Contains(kindId, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly CanonStateFieldMapping[] MappedFields =
    [
        new()
        {
            StatePath = "social.disposition",
            CanonFieldKey = "role",
            BaselineLabel = "Canon baseline: Role/status (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Npc,
                EntityInternalStateKind.Party,
            },
            ResolveCanon = (entity, spec, kindId, _) =>
                string.Equals(kindId, EntityInternalStateKind.Party, StringComparison.OrdinalIgnoreCase)
                    ? GetPartyAttitude(entity)
                    : GetRoleOrStatus(entity, spec, kindId),
        },
        new()
        {
            StatePath = "social.trustTowardPlayer",
            CanonFieldKey = "relationship",
            BaselineLabel = "Canon baseline: Relationship (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Npc,
                EntityInternalStateKind.Party,
            },
            ResolveCanon = (entity, _, kindId, _) => GetRelationship(entity, kindId),
        },
        new()
        {
            StatePath = "emotional.mood",
            CanonFieldKey = "personality",
            BaselineLabel = "Canon baseline: Personality tone (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Player,
                EntityInternalStateKind.Npc,
                EntityInternalStateKind.Party,
            },
            ResolveCanon = (entity, spec, kindId, _) =>
                TruncatePersonality(GetPersonality(entity, spec, kindId)),
        },
        new()
        {
            StatePath = "emotional.stability",
            CanonFieldKey = "personality",
            BaselineLabel = "Canon baseline: Personality steadiness (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Player,
                EntityInternalStateKind.Npc,
                EntityInternalStateKind.Party,
            },
            ResolveCanon = (entity, spec, kindId, _) =>
                TruncatePersonality(GetPersonality(entity, spec, kindId)),
        },
        new()
        {
            StatePath = "physical.condition",
            CanonFieldKey = "condition",
            BaselineLabel = "Canon baseline: Condition (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Player,
                EntityInternalStateKind.Party,
                EntityInternalStateKind.Inventory,
                EntityInternalStateKind.Vehicle,
            },
            ResolveCanon = (entity, spec, kindId, bundle) => GetPhysicalConditionCanon(entity, spec, kindId, bundle),
        },
        new()
        {
            StatePath = "motivation.goals",
            CanonFieldKey = "goals",
            BaselineLabel = "Canon baseline: Goals/motives (entities.json)",
            ValueKind = EntityInternalStateFieldKind.StringList,
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Player,
                EntityInternalStateKind.Npc,
                EntityInternalStateKind.Party,
            },
            ResolveCanon = (entity, spec, kindId, _) =>
            {
                var raw = GetGoalsOrMotives(entity, spec, kindId);
                return string.IsNullOrWhiteSpace(raw) ? null : FormatGoalLines(raw);
            },
        },
        new()
        {
            StatePath = "motivation.motivation",
            CanonFieldKey = "motives",
            BaselineLabel = "Canon baseline: Motives (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Npc,
                EntityInternalStateKind.Party,
            },
            ResolveCanon = (entity, spec, kindId, _) => GetMotivationSummary(entity, spec, kindId),
        },
        new()
        {
            StatePath = "presence.currentLocation",
            CanonFieldKey = "location",
            BaselineLabel = "Canon baseline: Location (entities.json / session)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EntityInternalStateKind.Player,
                EntityInternalStateKind.Npc,
                EntityInternalStateKind.Party,
                EntityInternalStateKind.Vehicle,
            },
            ResolveCanon = (entity, spec, kindId, bundle) => GetPresenceLocationCanon(entity, spec, kindId, bundle),
        },
        new()
        {
            StatePath = "Progress",
            CanonFieldKey = "status",
            BaselineLabel = "Canon baseline: Quest status (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EntityInternalStateKind.Quest },
            ResolveCanon = (entity, _, _, _) =>
                entity is QuestEntry quest ? quest.Status.ToString() : null,
        },
        new()
        {
            StatePath = "Atmosphere",
            CanonFieldKey = "status",
            BaselineLabel = "Canon baseline: Location status (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EntityInternalStateKind.Location },
            ResolveCanon = (entity, spec, _, _) =>
                entity is LocationEntry location
                    ? FirstNonEmpty(location.Status, spec is null ? null : CanonFieldMapper.GetField(entity, spec, "status"))
                    : null,
        },
        new()
        {
            StatePath = "Condition",
            CanonFieldKey = "status",
            BaselineLabel = "Canon baseline: Item status (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EntityInternalStateKind.Inventory },
            ResolveCanon = (entity, spec, _, _) =>
                entity is InventoryEntry item
                    ? FirstNonEmpty(item.Status, spec is null ? null : CanonFieldMapper.GetField(entity, spec, "status"))
                    : null,
        },
        new()
        {
            StatePath = "StanceTowardPlayer",
            CanonFieldKey = "reputation",
            BaselineLabel = "Canon baseline: Faction reputation (entities.json)",
            KindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EntityInternalStateKind.Faction },
            ResolveCanon = (entity, spec, _, _) =>
                entity is FactionEntry faction
                    ? FirstNonEmpty(
                        faction.Reputation,
                        faction.Relationships,
                        spec is null ? null : CanonFieldMapper.GetField(entity, spec, "reputation"))
                    : null,
        },
    ];

    private static string? GetPartyAttitude(object entity) =>
        entity is CompanionEntry companion
            ? FirstNonEmpty(companion.Attitude, companion.Condition)
            : null;

    private static string? GetPhysicalConditionCanon(
        object entity,
        CanonEntityKindSpec? spec,
        string kindId,
        AdventureBundle bundle)
    {
        if (string.Equals(kindId, EntityInternalStateKind.Player, StringComparison.OrdinalIgnoreCase))
            return FirstNonEmpty(bundle.State.PlayerCondition);

        if (entity is CompanionEntry companion)
            return FirstNonEmpty(companion.Condition);

        if (entity is InventoryEntry item)
            return FirstNonEmpty(item.Status, spec is null ? null : CanonFieldMapper.GetField(entity, spec, "status"));

        if (entity is VehicleEntry vehicle)
            return FirstNonEmpty(vehicle.Condition, vehicle.Status);

        return spec is null
            ? null
            : FirstNonEmpty(
                CanonFieldMapper.GetField(entity, spec, spec.SecondaryProperty),
                CanonFieldMapper.GetField(entity, spec, "condition"),
                CanonFieldMapper.GetField(entity, spec, "status"));
    }

    private static string? GetGoalsOrMotives(object entity, CanonEntityKindSpec? spec, string kindId)
    {
        if (entity is PlayerCharacterSheet player)
            return player.Goals;

        if (entity is CompanionEntry companion)
            return companion.Goals;

        if (entity is CharacterEntry character)
            return FirstNonEmpty(character.Motives, spec is null ? null : CanonFieldMapper.GetField(entity, spec, "motives"));

        return spec is null
            ? null
            : FirstNonEmpty(
                CanonFieldMapper.GetField(entity, spec, "goals"),
                CanonFieldMapper.GetField(entity, spec, "motives"));
    }

    private static string? GetMotivationSummary(object entity, CanonEntityKindSpec? spec, string kindId)
    {
        if (entity is CompanionEntry companion)
            return FirstNonEmpty(companion.Goals, companion.Personality);

        if (entity is CharacterEntry character)
            return FirstNonEmpty(character.Motives, character.Personality);

        return spec is null ? null : CanonFieldMapper.GetField(entity, spec, "motives");
    }

    private static string? GetPresenceLocationCanon(
        object entity,
        CanonEntityKindSpec? spec,
        string kindId,
        AdventureBundle bundle)
    {
        if (entity is CharacterEntry character)
            return FirstNonEmpty(character.Location, bundle.State.CurrentLocation);

        if (entity is VehicleEntry vehicle)
            return FirstNonEmpty(vehicle.Location, bundle.State.CurrentLocation);

        if (entity is CompanionEntry)
            return FirstNonEmpty(bundle.State.CurrentLocation);

        if (string.Equals(kindId, EntityInternalStateKind.Player, StringComparison.OrdinalIgnoreCase))
            return FirstNonEmpty(bundle.State.CurrentLocation);

        return FirstNonEmpty(
            spec is null ? null : CanonFieldMapper.GetField(entity, spec, "location"),
            bundle.State.CurrentLocation);
    }

    private static string? TruncatePersonality(string? personality)
    {
        if (string.IsNullOrWhiteSpace(personality))
            return null;

        return Truncate(personality.Trim(), 120);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string GetRoleOrStatus(object entity, CanonEntityKindSpec? spec, string kindId)
    {
        if (entity is QuestEntry quest)
            return quest.Status.ToString();

        if (entity is CharacterEntry character)
        {
            if (!string.IsNullOrWhiteSpace(character.Role))
                return character.Role;
            if (!string.IsNullOrWhiteSpace(character.Status))
                return character.Status;
        }

        return spec is null
            ? ""
            : CanonFieldMapper.GetField(entity, spec, spec.SecondaryProperty)
               ?? CanonFieldMapper.GetField(entity, spec, "role")
               ?? CanonFieldMapper.GetField(entity, spec, "status")
               ?? "";
    }

    private static string GetPersonality(object entity, CanonEntityKindSpec? spec, string kindId)
    {
        if (entity is PlayerCharacterSheet player)
            return player.Personality;
        if (entity is CompanionEntry companion)
            return companion.Personality;
        if (entity is CharacterEntry character)
            return character.Personality;
        return spec is null ? "" : CanonFieldMapper.GetField(entity, spec, "personality") ?? "";
    }

    private static string? GetRelationship(object entity, string kindId)
    {
        if (entity is CompanionEntry companion)
            return FirstNonEmpty(companion.Relationship);
        if (entity is CharacterEntry character)
            return FirstNonEmpty(character.RelationshipToPlayer);
        return null;
    }

    private static void AddIfBothDiffer(
        List<CanonStateDivergence> list,
        string canonKey,
        string statePath,
        string canonValue,
        string stateValue)
    {
        if (string.Equals(canonValue.Trim(), stateValue.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        list.Add(new CanonStateDivergence
        {
            CanonFieldKey = canonKey,
            StatePath = statePath,
            CanonValue = Truncate(canonValue, 160),
            StateValue = Truncate(stateValue, 160),
            Message = $"{statePath} ({stateValue}) differs from canon {canonKey} ({canonValue})",
        });
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
