using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityEditModel
{
    public Guid Id { get; init; }

    public Guid AdventureId { get; set; }

    public string Category { get; init; } = "Characters";

    public string TypeLabel { get; init; } = "Character";

    public string Name { get; set; } = "";

    public string SecondaryLabel { get; init; } = "Role";

    public string SecondaryValue { get; set; } = "";

    public string Description { get; set; } = "";

    public bool Pinned { get; set; }

    public bool CanPin { get; init; }

    public bool IsNew { get; init; }

    public bool ShowQuestStatus { get; init; }

    public QuestStatus QuestStatus { get; set; } = QuestStatus.Active;

    public string ImagePath { get; set; } = "";

    public string? PendingImageSourcePath { get; set; }

    public bool ClearImage { get; set; }

    public string TagsText { get; set; } = "";

    public string AliasesText { get; set; } = "";

    public bool ShowTags { get; init; }

    public bool ShowAliases { get; init; }

    public List<EntityEditField> Fields { get; } = [];

    public Dictionary<string, string> ExtendedFields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> HeaderLabels { get; private set; } = [];

    public void RefreshHeaderLabels()
    {
        var labels = new List<string> { TypeLabel };
        if (Pinned)
            labels.Add("Pinned");
        if (ShowQuestStatus)
            labels.Add(QuestStatus.ToString());
        if (!string.IsNullOrWhiteSpace(SecondaryValue))
            labels.Add(SecondaryValue.Trim());
        foreach (var tag in EntityEditMapper.ParseTags(TagsText).Take(3))
            labels.Add(tag);
        HeaderLabels = labels;
    }
}

public static class EntityEditMapper
{
    public static readonly Guid PlayerEntityId = Guid.Empty;

    public static EntityEditModel CreateNew(string category, Guid adventureId)
    {
        var spec = CanonEntityResolver.TryGetSpec(category);
        var model = new EntityEditModel
        {
            Id = category == "Player" ? PlayerEntityId : Guid.NewGuid(),
            AdventureId = adventureId,
            Category = category,
            TypeLabel = spec?.TypeLabel ?? TypeLabelForCategory(category),
            SecondaryLabel = SecondaryLabelForCategory(category, spec),
            CanPin = category is "Characters" or "Locations" or "Concepts",
            IsNew = category != "Player",
            ShowQuestStatus = category == "Quests",
            ShowTags = spec?.ShowTags ?? false,
            ShowAliases = spec?.ShowAliases ?? false,
        };
        AddFieldsForCategory(model, category);
        model.RefreshHeaderLabels();
        return model;
    }

    public static EntityEditModel? Load(EntitiesDocument entities, Guid id, string category, Guid adventureId)
    {
        var model = category switch
        {
            "Player" => LoadPlayer(entities, adventureId),
            "Party" => LoadParty(entities, id, adventureId),
            _ when CanonEntityResolver.TryGetSpec(category) is { } spec =>
                LoadFromSpec(entities, id, category, adventureId, spec),
            _ => null,
        };

        if (model is null)
            return null;

        model.RefreshHeaderLabels();
        return model;
    }

    public static bool Apply(EntitiesDocument entities, EntityEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return false;

        FinalizeImage(model);

        return model.Category switch
        {
            "Player" => ApplyPlayer(entities, model),
            "Party" => ApplyParty(entities, model),
            _ when CanonEntityResolver.TryGetSpec(model.Category) is { } spec =>
                ApplyFromSpec(entities, model, spec),
            _ => false,
        };
    }

    public static bool Delete(EntitiesDocument entities, EntityEditModel model)
    {
        EntityMediaService.Delete(model.AdventureId, model.ImagePath);
        return Delete(entities, model.Id, model.Category);
    }

    public static bool Delete(EntitiesDocument entities, Guid id, string category) =>
        CanonEntityResolver.DeleteEntity(entities, category, id);

    public static AdventurePlayEntityKind KindForCategory(string category) =>
        category switch
        {
            "Player" => AdventurePlayEntityKind.Player,
            "Party" => AdventurePlayEntityKind.PartyCompanion,
            "Locations" => AdventurePlayEntityKind.Location,
            "Quests" => AdventurePlayEntityKind.Quest,
            CanonEntityResolver.ThingsCategory => AdventurePlayEntityKind.Thing,
            "Factions" => AdventurePlayEntityKind.Faction,
            "Concepts" => AdventurePlayEntityKind.Concept,
            _ => AdventurePlayEntityKind.Character,
        };

    public static string CategoryForEntityKind(AdventurePlayEntityKind kind) =>
        kind switch
        {
            AdventurePlayEntityKind.Player => "Player",
            AdventurePlayEntityKind.PartyCompanion => "Party",
            AdventurePlayEntityKind.Location => "Locations",
            AdventurePlayEntityKind.Quest => "Quests",
            AdventurePlayEntityKind.Thing => CanonEntityResolver.ThingsCategory,
            AdventurePlayEntityKind.Faction => "Factions",
            AdventurePlayEntityKind.Concept => "Concepts",
            _ => "Characters",
        };

    private static EntityEditModel LoadPlayer(EntitiesDocument entities, Guid adventureId)
    {
        var spec = CanonSchemaRegistry.Player;
        var player = entities.Player;
        var model = new EntityEditModel
        {
            Id = PlayerEntityId,
            AdventureId = adventureId,
            Category = "Player",
            TypeLabel = spec.TypeLabel,
            Name = player.Name,
            SecondaryLabel = "Background",
            SecondaryValue = player.Background,
            ImagePath = player.ImagePath,
            TagsText = JoinList(player.Tags),
            AliasesText = JoinList(player.Aliases),
            ShowTags = spec.ShowTags,
            ShowAliases = spec.ShowAliases,
            CanPin = false,
        };
        PopulateRegistryFields(model, player, spec);
        return model;
    }

    private static EntityEditModel? LoadParty(EntitiesDocument entities, Guid id, Guid adventureId)
    {
        if (entities.Party.FirstOrDefault(e => e.Id == id) is not { } companion)
            return null;

        var spec = CanonSchemaRegistry.Party;
        var model = new EntityEditModel
        {
            Id = companion.Id,
            AdventureId = adventureId,
            Category = "Party",
            TypeLabel = spec.TypeLabel,
            Name = companion.Name,
            SecondaryLabel = "Condition",
            SecondaryValue = companion.Condition,
            Description = companion.Relationship,
            ImagePath = companion.ImagePath,
            TagsText = JoinList(companion.Tags),
            AliasesText = JoinList(companion.Aliases),
            ShowTags = spec.ShowTags,
            ShowAliases = spec.ShowAliases,
            CanPin = false,
        };
        PopulateRegistryFields(model, companion, spec);
        return model;
    }

    private static EntityEditModel? LoadFromSpec(
        EntitiesDocument entities,
        Guid id,
        string category,
        Guid adventureId,
        CanonEntityKindSpec spec)
    {
        if (CanonEntityResolver.ResolveEntity(entities, category, id) is not { } entity)
            return null;

        var model = new EntityEditModel
        {
            Id = CanonEntityResolver.GetEntityId(entity, spec),
            AdventureId = adventureId,
            Category = category,
            TypeLabel = spec.TypeLabel,
            Name = CanonFieldMapper.GetField(entity, spec, spec.TitleProperty) ?? "",
            SecondaryLabel = SecondaryLabelForSpec(spec),
            SecondaryValue = CanonFieldMapper.GetField(entity, spec, spec.SecondaryProperty) ?? "",
            Description = GetDescriptionShell(entity, spec),
            Pinned = GetPinned(entity),
            ImagePath = GetImagePath(entity),
            TagsText = JoinList(GetTags(entity)),
            AliasesText = JoinList(GetAliases(entity)),
            CanPin = entity is CharacterEntry or LocationEntry or ConceptEntry,
            ShowQuestStatus = entity is QuestEntry,
            QuestStatus = entity is QuestEntry q ? q.Status : QuestStatus.Active,
            ShowTags = spec.ShowTags,
            ShowAliases = spec.ShowAliases,
        };
        PopulateRegistryFields(model, entity, spec);
        return model;
    }

    private static bool ApplyPlayer(EntitiesDocument entities, EntityEditModel model)
    {
        var player = entities.Player;
        player.Name = model.Name.Trim();
        player.Background = model.SecondaryValue.Trim();
        player.Tags = ParseList(model.TagsText);
        player.Aliases = ParseList(model.AliasesText);
        FinalizeImage(model);
        player.ImagePath = model.ImagePath.Trim();
        ApplyRegistryFields(player, model, CanonSchemaRegistry.Player);
        return true;
    }

    private static bool ApplyParty(EntitiesDocument entities, EntityEditModel model)
    {
        var companion = model.IsNew
            ? new CompanionEntry { Id = model.Id }
            : entities.Party.FirstOrDefault(e => e.Id == model.Id);

        if (companion is null)
            return false;

        companion.Name = model.Name.Trim();
        companion.Condition = model.SecondaryValue.Trim();
        companion.Relationship = model.Description.Trim();
        ApplyEntityShell(companion, model);
        ApplyRegistryFields(companion, model, CanonSchemaRegistry.Party);

        if (model.IsNew)
            entities.Party.Add(companion);

        return true;
    }

    private static bool ApplyFromSpec(EntitiesDocument entities, EntityEditModel model, CanonEntityKindSpec spec)
    {
        object entity;
        if (model.IsNew)
        {
            entity = CanonEntityResolver.CreateEntity(model.Category, model.Id);
            CanonEntityResolver.AddEntity(entities, model.Category, entity);
        }
        else if (CanonEntityResolver.ResolveEntity(entities, model.Category, model.Id) is not { } existing)
        {
            return false;
        }
        else
        {
            entity = existing;
        }

        CanonFieldMapper.SetField(entity, spec, spec.TitleProperty, model.Name.Trim());
        if (!string.IsNullOrWhiteSpace(spec.SecondaryProperty))
            CanonFieldMapper.SetField(entity, spec, spec.SecondaryProperty, model.SecondaryValue.Trim());

        var descriptionField = GetDescriptionField(spec);
        if (descriptionField is not null)
            CanonFieldMapper.SetField(entity, spec, descriptionField.JsonKey, model.Description.Trim());

        ApplyEntityShell(entity, model);
        ApplyRegistryFields(entity, model, spec);
        return true;
    }

    private static void ApplyEntityShell(object entity, EntityEditModel model)
    {
        switch (entity)
        {
            case CharacterEntry c:
                c.Pinned = model.Pinned;
                c.ImagePath = model.ImagePath.Trim();
                c.Tags = ParseList(model.TagsText);
                c.Aliases = ParseList(model.AliasesText);
                break;
            case CompanionEntry c:
                c.ImagePath = model.ImagePath.Trim();
                c.Tags = ParseList(model.TagsText);
                c.Aliases = ParseList(model.AliasesText);
                break;
            case LocationEntry l:
                l.Pinned = model.Pinned;
                l.ImagePath = model.ImagePath.Trim();
                l.Aliases = ParseList(model.AliasesText);
                break;
            case ConceptEntry c:
                c.Pinned = model.Pinned;
                c.ImagePath = model.ImagePath.Trim();
                c.Tags = ParseList(model.TagsText);
                break;
            case QuestEntry q:
                q.ImagePath = model.ImagePath.Trim();
                q.Status = model.QuestStatus;
                break;
            case FactionEntry f:
                f.ImagePath = model.ImagePath.Trim();
                break;
            case InventoryEntry i:
                i.ImagePath = model.ImagePath.Trim();
                break;
        }
    }

    private static void PopulateRegistryFields(EntityEditModel model, object entity, CanonEntityKindSpec spec)
    {
        model.Fields.Clear();
        model.ExtendedFields.Clear();
        CopyExtendedFields(model, entity);
        AddFieldsForCategory(model, model.Category);

        foreach (var field in spec.EditorFields)
        {
            if (string.Equals(field.JsonKey, spec.SecondaryProperty, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(field.JsonKey, "relationship", StringComparison.OrdinalIgnoreCase)
                && model.Category == "Party")
                continue;

            var value = CanonFieldMapper.GetField(entity, spec, field.JsonKey) ?? "";
            SetField(model, field.JsonKey, value);
        }
    }

    private static void ApplyRegistryFields(object entity, EntityEditModel model, CanonEntityKindSpec spec)
    {
        foreach (var field in spec.EditorFields)
        {
            if (string.Equals(field.JsonKey, spec.SecondaryProperty, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(field.JsonKey, "relationship", StringComparison.OrdinalIgnoreCase)
                && model.Category == "Party")
                continue;

            var value = GetField(model, field.JsonKey);
            CanonFieldMapper.SetField(entity, spec, field.JsonKey, value);
        }

        ApplyExtendedFields(entity, model);
    }

    private static void CopyExtendedFields(EntityEditModel model, object entity)
    {
        var source = entity switch
        {
            PlayerCharacterSheet p => p.ExtendedFields,
            CompanionEntry c => c.ExtendedFields,
            CharacterEntry c => c.ExtendedFields,
            LocationEntry l => l.ExtendedFields,
            FactionEntry f => f.ExtendedFields,
            ConceptEntry c => c.ExtendedFields,
            QuestEntry q => q.ExtendedFields,
            MysteryEntry m => m.ExtendedFields,
            ConflictEntry c => c.ExtendedFields,
            ConsequenceEntry c => c.ExtendedFields,
            _ => null,
        };

        if (source is null)
            return;

        foreach (var (key, value) in source)
            model.ExtendedFields[key] = value;
    }

    private static void ApplyExtendedFields(object entity, EntityEditModel model)
    {
        var target = entity switch
        {
            PlayerCharacterSheet p => p.ExtendedFields,
            CompanionEntry c => c.ExtendedFields,
            CharacterEntry c => c.ExtendedFields,
            LocationEntry l => l.ExtendedFields,
            FactionEntry f => f.ExtendedFields,
            ConceptEntry c => c.ExtendedFields,
            QuestEntry q => q.ExtendedFields,
            MysteryEntry m => m.ExtendedFields,
            ConflictEntry c => c.ExtendedFields,
            ConsequenceEntry c => c.ExtendedFields,
            _ => null,
        };

        if (target is null)
            return;

        target.Clear();
        foreach (var (key, value) in model.ExtendedFields)
            target[key] = value.Trim();
    }

    private static void AddFieldsForCategory(EntityEditModel model, string category)
    {
        if (category is "Quests")
            return;

        if (CanonEntityResolver.TryGetSpec(category) is not { } spec)
            return;

        var order = 1;
        foreach (var field in spec.EditorFields)
        {
            if (string.Equals(field.JsonKey, spec.SecondaryProperty, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(field.JsonKey, "relationship", StringComparison.OrdinalIgnoreCase)
                && category == "Party")
                continue;

            AddField(model, field.JsonKey, field.Label, order++, field.Multiline, field.FieldGroup);
        }
    }

    private static CanonFieldSpec? GetDescriptionField(CanonEntityKindSpec spec) =>
        spec.BodyFields.FirstOrDefault(f => f.Format == CanonFieldFormat.FreeformBody)
        ?? spec.BodyFields.FirstOrDefault(f => string.Equals(f.JsonKey, spec.SnippetProperty, StringComparison.OrdinalIgnoreCase));

    private static string GetDescriptionShell(object entity, CanonEntityKindSpec spec)
    {
        var field = GetDescriptionField(spec);
        return field is null ? "" : CanonFieldMapper.GetField(entity, spec, field.JsonKey) ?? "";
    }

    private static bool GetPinned(object entity) =>
        entity switch
        {
            CharacterEntry c => c.Pinned,
            LocationEntry l => l.Pinned,
            ConceptEntry c => c.Pinned,
            _ => false,
        };

    private static string GetImagePath(object entity) =>
        CanonEntityPropertyGraph.TryGetValue(entity, "imagePath", out var value) && value is string path
            ? path
            : "";

    private static IEnumerable<string> GetTags(object entity) => GetStringList(entity, "tags");

    private static IEnumerable<string> GetAliases(object entity) => GetStringList(entity, "aliases");

    private static IEnumerable<string> GetStringList(object entity, string jsonKey)
    {
        if (!CanonEntityPropertyGraph.TryGetValue(entity, jsonKey, out var value)
            || value is not IEnumerable<string> list)
        {
            return [];
        }

        return list;
    }

    private static void FinalizeImage(EntityEditModel model)
    {
        if (model.ClearImage)
        {
            EntityMediaService.Delete(model.AdventureId, model.ImagePath);
            model.ImagePath = "";
            return;
        }

        if (string.IsNullOrWhiteSpace(model.PendingImageSourcePath))
            return;

        var imported = EntityMediaService.ImportFromFile(model.AdventureId, model.Id, model.PendingImageSourcePath);
        if (imported is not null)
            model.ImagePath = imported;
    }

    private static void AddField(
        EntityEditModel model,
        string key,
        string label,
        int order,
        bool multiline = false,
        string groupId = CanonFieldGroup.Story) =>
        model.Fields.Add(new EntityEditField
        {
            Key = key,
            Label = label,
            Order = order,
            DisplayOrder = order,
            Multiline = multiline,
            GroupId = groupId,
        });

    private static void SetField(EntityEditModel model, string key, string value)
    {
        var field = model.Fields.FirstOrDefault(f => f.Key == key);
        if (field is not null)
            field.Value = value;
    }

    private static string GetField(EntityEditModel model, string key) =>
        model.Fields.FirstOrDefault(f => f.Key == key)?.Value.Trim() ?? "";

    public static IReadOnlyList<string> ParseTags(string text) => ParseList(text);

    private static List<string> ParseList(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string JoinList(IEnumerable<string> values) =>
        string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string SecondaryLabelForSpec(CanonEntityKindSpec spec) =>
        spec.EditorFields.FirstOrDefault(f =>
            string.Equals(f.JsonKey, spec.SecondaryProperty, StringComparison.OrdinalIgnoreCase))?.Label
        ?? spec.SecondaryProperty;

    private static string SecondaryLabelForCategory(string category, CanonEntityKindSpec? spec = null) =>
        category switch
        {
            "Player" => "Background",
            "Party" => "Condition",
            CanonEntityResolver.ThingsCategory => "Status",
            _ => SecondaryLabelForSpec(spec ?? CanonEntityResolver.TryGetSpec(category) ?? CanonSchemaRegistry.Npc),
        };

    private static string TypeLabelForCategory(string category) =>
        CanonEntityResolver.TryGetSpec(category)?.TypeLabel
        ?? category switch
        {
            CanonEntityResolver.ThingsCategory => "Thing",
            _ => "Character",
        };
}

public enum AdventurePlayEntityKind
{
    Player,
    PartyCompanion,
    Character,
    Location,
    Quest,
    Thing,
    Faction,
    Concept,
}
