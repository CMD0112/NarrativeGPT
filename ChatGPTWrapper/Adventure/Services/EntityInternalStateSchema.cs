using System.Collections.Concurrent;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public enum EntityInternalStateFieldKind
{
    String,
    Bool,
    StringList,
    StringDictionary,
}

public sealed class EntityInternalStateFieldBinding
{
    public required string Path { get; init; }

    public required string Label { get; init; }

    public required string GroupId { get; init; }

    public EntityInternalStateFieldKind Kind { get; init; } = EntityInternalStateFieldKind.String;

    public int Order { get; init; }

    public string Hint { get; init; } = "";
}

public sealed class EntityInternalStateSectionDefinition
{
    public required string GroupId { get; init; }

    public required string Title { get; init; }

    public int Order { get; init; }

    public IReadOnlyList<EntityInternalStateFieldBinding> Fields { get; init; } = [];
}

public sealed class EntityInternalStateEditModel
{
    public Guid EntityId { get; init; }

    public string KindId { get; init; } = "";

    public string EntityName { get; set; } = "";

    public List<EntityInternalStateFieldValue> FieldValues { get; set; } = [];

    public string SummaryLine { get; set; } = "";

    internal string Snapshot { get; set; } = "";
}

internal static class EntityInternalStateSchema
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<EntityInternalStateSectionDefinition>> SectionCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] BlockPropertyOrder =
    [
        "Presence", "Identity", "Emotional", "Motivation", "Physical", "Knowledge",
        "Equipment", "Resources", "Social", "Tactical", "Narrative", "Flags",
    ];

    public static IReadOnlyList<EntityInternalStateSectionDefinition> GetSections(string kindId) =>
        SectionCache.GetOrAdd(kindId, BuildSections);

    internal static EntityInternalStateEditModel LoadModelInner(
        AdventureBundle bundle,
        Guid entityId,
        string kindId,
        string entityName)
    {
        var record = EntityInternalStateService.GetOrCreate(bundle, kindId, entityId, seedFromCanon: true);
        var state = EntityInternalStateService.GetStateObject(record, kindId)
                    ?? EntityInternalStateService.CreateEmptyStateObject(kindId);

        var model = new EntityInternalStateEditModel
        {
            EntityId = entityId,
            KindId = kindId,
            EntityName = entityName,
        };

        var values = new List<EntityInternalStateFieldValue>();
        foreach (var section in GetSections(kindId))
        {
            foreach (var binding in section.Fields)
            {
                EntityInternalStatePathAccessor.TryGetDisplayValue(state, binding.Path, binding.Kind, out var value);
                values.Add(new EntityInternalStateFieldValue(binding, value));
            }
        }

        model.FieldValues = values;
        model.SummaryLine = EntityInternalStateSummary.Build(kindId, state);
        model.Snapshot = SerializeSnapshot(values);
        return model;
    }

    public static void ApplyModel(EntityInternalStateEditModel model, EntityStateRecord record, string kindId)
    {
        var state = EntityInternalStateService.GetStateObject(record, kindId)
                    ?? EntityInternalStateService.CreateEmptyStateObject(kindId);

        foreach (var field in model.FieldValues)
            EntityInternalStatePathAccessor.TrySetDisplayValue(state, field.Binding.Path, field.Binding.Kind, field.Value);

        EntityInternalStateService.SetStateObject(record, kindId, state);
    }

    public static bool HasChanges(EntityInternalStateEditModel model) =>
        !string.Equals(model.Snapshot, SerializeSnapshot(model.FieldValues), StringComparison.Ordinal);

    private static string SerializeSnapshot(IReadOnlyList<EntityInternalStateFieldValue> values) =>
        string.Join('\u001e', values.Select(v => $"{v.Binding.Path}\u001f{v.Value}"));

    private static IReadOnlyList<EntityInternalStateSectionDefinition> BuildSections(string kindId)
    {
        var state = EntityInternalStateService.CreateEmptyStateObject(kindId);
        var sections = new List<EntityInternalStateSectionDefinition>();
        var order = 0;

        foreach (var blockName in BlockPropertyOrder)
        {
            var prop = state.GetType().GetProperty(blockName);
            if (prop?.GetValue(state) is not { } block)
                continue;

            var fields = BuildFieldsForObject(block, blockName, ref order, groupId: blockName.ToLowerInvariant());
            if (fields.Count == 0)
                continue;

            sections.Add(new EntityInternalStateSectionDefinition
            {
                GroupId = blockName.ToLowerInvariant(),
                Title = EntityInternalStateFieldGroups.DisplayLabel(blockName.ToLowerInvariant()),
                Order = EntityInternalStateFieldGroups.Order(blockName.ToLowerInvariant()),
                Fields = fields,
            });
        }

        var detailFields = new List<EntityInternalStateFieldBinding>();
        foreach (var prop in state.GetType().GetProperties())
        {
            if (BlockPropertyOrder.Contains(prop.Name, StringComparer.Ordinal))
                continue;
            if (prop.Name is nameof(CustomInternalState.ExtendedFields))
            {
                detailFields.AddRange(BuildFieldsForObject(
                    prop.GetValue(state) ?? new Dictionary<string, string>(),
                    "ExtendedFields",
                    ref order,
                    groupId: "details",
                    forceDictionary: true));
                continue;
            }

            detailFields.AddRange(BuildFieldForProperty(prop, prop.Name, ref order, "details"));
        }

        if (detailFields.Count > 0)
        {
            sections.Add(new EntityInternalStateSectionDefinition
            {
                GroupId = "details",
                Title = EntityInternalStateFieldGroups.DisplayLabel("details"),
                Order = EntityInternalStateFieldGroups.Order("details"),
                Fields = detailFields,
            });
        }

        return sections.OrderBy(s => s.Order).ToList();
    }

    private static List<EntityInternalStateFieldBinding> BuildFieldsForObject(
        object target,
        string prefix,
        ref int order,
        string groupId,
        bool forceDictionary = false)
    {
        var fields = new List<EntityInternalStateFieldBinding>();
        if (forceDictionary && target is Dictionary<string, string>)
        {
            fields.Add(new EntityInternalStateFieldBinding
            {
                Path = prefix,
                Label = "Custom fields",
                GroupId = groupId,
                Kind = EntityInternalStateFieldKind.StringDictionary,
                Order = order++,
                Hint = "One key: value pair per line.",
            });
            return fields;
        }

        foreach (var prop in target.GetType().GetProperties())
            fields.AddRange(BuildFieldForProperty(prop, $"{prefix}.{prop.Name}", ref order, groupId));

        return fields;
    }

    private static List<EntityInternalStateFieldBinding> BuildFieldForProperty(
        System.Reflection.PropertyInfo prop,
        string path,
        ref int order,
        string groupId)
    {
        var fields = new List<EntityInternalStateFieldBinding>();
        var type = prop.PropertyType;

        if (type == typeof(string))
        {
            fields.Add(new EntityInternalStateFieldBinding
            {
                Path = path,
                Label = Humanize(prop.Name),
                GroupId = groupId,
                Kind = EntityInternalStateFieldKind.String,
                Order = order++,
                Hint = AppendBaselineHint(path, MultilineHint(prop.Name)),
            });
            return fields;
        }

        if (type == typeof(bool))
        {
            fields.Add(new EntityInternalStateFieldBinding
            {
                Path = path,
                Label = Humanize(prop.Name),
                GroupId = groupId,
                Kind = EntityInternalStateFieldKind.Bool,
                Order = order++,
            });
            return fields;
        }

        if (type == typeof(List<string>))
        {
            fields.Add(new EntityInternalStateFieldBinding
            {
                Path = path,
                Label = Humanize(prop.Name),
                GroupId = groupId,
                Kind = EntityInternalStateFieldKind.StringList,
                Order = order++,
                Hint = "One item per line.",
            });
            return fields;
        }

        if (type == typeof(Dictionary<string, string>))
        {
            fields.Add(new EntityInternalStateFieldBinding
            {
                Path = path,
                Label = Humanize(prop.Name),
                GroupId = groupId,
                Kind = EntityInternalStateFieldKind.StringDictionary,
                Order = order++,
                Hint = "One key: value pair per line.",
            });
            return fields;
        }

        if (type == typeof(Dictionary<string, bool>) || type == typeof(Dictionary<string, int>))
        {
            fields.Add(new EntityInternalStateFieldBinding
            {
                Path = path,
                Label = Humanize(prop.Name),
                GroupId = groupId,
                Kind = EntityInternalStateFieldKind.StringDictionary,
                Order = order++,
                Hint = "One key: value pair per line.",
            });
        }

        return fields;
    }

    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        return sb.ToString();
    }

    private static string MultilineHint(string name) => name switch
    {
        "Notes" or "Motivation" or "Progress" or "PartialAnswer" or "VoiceNotes"
            or "LastPlayerInteraction" or "LastBondingMoment" or "LastMajorBeat" => "Free text; may span multiple lines.",
        _ => "",
    };

    private static string AppendBaselineHint(string path, string existingHint)
    {
        var baseline = EntityCanonStateOverlapService.GetBaselineLabelForStatePath(path);
        if (string.IsNullOrWhiteSpace(baseline))
            return existingHint;

        return string.IsNullOrWhiteSpace(existingHint)
            ? baseline
            : $"{existingHint} {baseline}";
    }
}

public sealed class EntityInternalStateFieldValue
{
    public EntityInternalStateFieldValue(EntityInternalStateFieldBinding binding, string value)
    {
        Binding = binding;
        Value = value;
    }

    public EntityInternalStateFieldBinding Binding { get; }

    public string Value { get; set; }
}

internal static class EntityInternalStateFieldGroups
{
    public static int Order(string groupId) => groupId switch
    {
        "presence" => 10,
        "identity" => 20,
        "emotional" => 30,
        "motivation" => 40,
        "physical" => 50,
        "knowledge" => 60,
        "equipment" => 70,
        "resources" => 80,
        "social" => 90,
        "tactical" => 100,
        "narrative" => 110,
        "details" => 120,
        "flags" => 130,
        _ => 200,
    };

    public static string DisplayLabel(string groupId) => groupId switch
    {
        "presence" => "Presence & scene",
        "identity" => "Identity & cover",
        "emotional" => "Emotional & mental",
        "motivation" => "Motivation & goals",
        "physical" => "Physical & health",
        "knowledge" => "Knowledge & secrets",
        "equipment" => "Equipment & inventory",
        "resources" => "Resources & pools",
        "social" => "Social & relationships",
        "tactical" => "Tactical & combat",
        "narrative" => "Narrative focus",
        "details" => "Kind-specific details",
        "flags" => "Flags & notes",
        _ => HumanizeFallback(groupId),
    };

    private static string HumanizeFallback(string groupId) =>
        char.ToUpperInvariant(groupId[0]) + groupId[1..];
}

public static class EntityInternalStateSummary
{
    public static string Build(string kindId, object state) => kindId switch
    {
        EntityInternalStateKind.Npc or EntityInternalStateKind.Party or EntityInternalStateKind.Player
            => BuildCharacterSummary(state),
        EntityInternalStateKind.Location => BuildLocationSummary(state),
        EntityInternalStateKind.Quest => GetString(state, "Progress"),
        EntityInternalStateKind.Inventory => JoinParts(GetString(state, "Condition"), GetString(state, "HeldBy")),
        EntityInternalStateKind.Vehicle => JoinParts(GetString(state, "Condition"), GetString(state, "Destination")),
        EntityInternalStateKind.Faction => GetString(state, "StanceTowardPlayer"),
        _ => "",
    };

    private static string BuildCharacterSummary(object state)
    {
        var mood = GetNested(state, "Emotional.Mood");
        var condition = GetNested(state, "Physical.Condition");
        var trust = GetNested(state, "Social.TrustTowardPlayer");
        return JoinParts(
            string.IsNullOrWhiteSpace(mood) ? null : $"Mood: {mood}",
            string.IsNullOrWhiteSpace(condition) ? null : $"Condition: {condition}",
            string.IsNullOrWhiteSpace(trust) ? null : $"Trust: {trust}");
    }

    private static string BuildLocationSummary(object state)
    {
        var atmosphere = GetString(state, "Atmosphere");
        var occupants = GetListCount(state, "Occupants");
        return JoinParts(
            string.IsNullOrWhiteSpace(atmosphere) ? null : atmosphere,
            occupants > 0 ? $"{occupants} occupant(s)" : null);
    }

    private static string? GetNested(object state, string path)
    {
        var parts = path.Split('.');
        object? current = state;
        foreach (var part in parts)
        {
            if (current is null)
                return null;
            var prop = current.GetType().GetProperty(part);
            current = prop?.GetValue(current);
        }

        return current as string;
    }

    private static string GetString(object state, string propertyName) =>
        state.GetType().GetProperty(propertyName)?.GetValue(state) as string ?? "";

    private static int GetListCount(object state, string propertyName)
    {
        if (state.GetType().GetProperty(propertyName)?.GetValue(state) is List<string> list)
            return list.Count;
        return 0;
    }

    private static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
