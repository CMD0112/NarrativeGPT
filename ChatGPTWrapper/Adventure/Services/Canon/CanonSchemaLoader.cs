using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonSchemaLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static CanonSchemaCatalog? _catalog;

    public static CanonSchemaCatalog Catalog => _catalog ??= Load();

    public static void Initialize() => _catalog = Load();

    public static CanonSchemaCatalog Load(string? jsonPath = null)
    {
        var json = TryReadJson(jsonPath);
        if (json is not null)
        {
            var document = JsonSerializer.Deserialize<CanonSchemaDocument>(json, JsonOptions)
                           ?? throw new InvalidOperationException("canon-schema.json deserialized to null.");
            return BuildCatalog(document);
        }

        return CanonSchemaBootstrap.Build();
    }

    private static string? TryReadJson(string? jsonPath)
    {
        if (!string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath))
            return File.ReadAllText(jsonPath);

        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "ChatGPTWrapper.Adventure.Schema.canon-schema.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static CanonSchemaCatalog BuildCatalog(CanonSchemaDocument document)
    {
        var categories = document.Categories.Count > 0
            ? document.Categories.Select(ToCategorySpec).ToList()
            : CanonEntityCategoryBootstrap.All.ToList();
        CanonEntityCategoryRegistry.Initialize(categories);

        var categoryById = categories.ToDictionary(c => c.CategoryId, StringComparer.OrdinalIgnoreCase);
        var kinds = document.Kinds.Select(k => ToKindSpec(k, categoryById)).ToList();
        return new CanonSchemaCatalog
        {
            SchemaVersion = document.SchemaVersion,
            AllKinds = kinds,
            Player = RequireKind(kinds, CanonSchemaRegistry.PlayerKind),
            Party = RequireKind(kinds, CanonSchemaRegistry.PartyKind),
            Npc = RequireKind(kinds, CanonSchemaRegistry.NpcKind),
            Location = RequireKind(kinds, CanonSchemaRegistry.LocationKind),
            Faction = RequireKind(kinds, CanonSchemaRegistry.FactionKind),
            Concept = RequireKind(kinds, CanonSchemaRegistry.ConceptKind),
            Quest = RequireKind(kinds, CanonSchemaRegistry.QuestKind),
            Mystery = RequireKind(kinds, CanonSchemaRegistry.MysteryKind),
            Conflict = RequireKind(kinds, CanonSchemaRegistry.ConflictKind),
            Consequence = RequireKind(kinds, CanonSchemaRegistry.ConsequenceKind),
            Inventory = RequireKind(kinds, CanonSchemaRegistry.InventoryKind),
            Scenario = RequireKind(kinds, CanonSchemaRegistry.ScenarioKind),
            Lexicon = RequireKind(kinds, CanonSchemaRegistry.LexiconKind),
            Custom = RequireKind(kinds, CanonSchemaRegistry.CustomKind),
        };
    }

    private static CanonEntityKindSpec RequireKind(IReadOnlyList<CanonEntityKindSpec> kinds, string kindId) =>
        kinds.FirstOrDefault(k => string.Equals(k.KindId, kindId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"canon-schema.json missing kind '{kindId}'.");

    private static CanonEntityCategorySpec ToCategorySpec(CanonCategoryDocument category) =>
        new()
        {
            CategoryId = category.CategoryId,
            DisplayLabel = category.DisplayLabel,
            ShowTags = category.ShowTags,
            ShowAliases = category.ShowAliases,
            ShowImage = category.ShowImage,
            ListShellFields = category.ListShellFields.Select(ToFieldSpec).ToList(),
            SingletonShellFields = category.SingletonShellFields.Select(ToFieldSpec).ToList(),
        };

    private static CanonEntityKindSpec ToKindSpec(
        CanonKindDocument kind,
        IReadOnlyDictionary<string, CanonEntityCategorySpec> categories)
    {
        CanonEntityCategorySpec? category = null;
        var fields = kind.Fields.Select(ToFieldSpec).ToList();
        if (!string.IsNullOrWhiteSpace(kind.ParentCategory)
            && categories.TryGetValue(kind.ParentCategory, out category))
        {
            var shared = kind.IsSingleton ? category.SingletonShellFields : category.ListShellFields;
            fields = MergeFields(shared, fields);
        }

        return new()
        {
            KindId = kind.KindId,
            ParentCategory = string.IsNullOrWhiteSpace(kind.ParentCategory) ? null : kind.ParentCategory,
            CategorySpec = category,
            CollectionKey = kind.CollectionKey,
            SectionId = kind.SectionId,
            SourceFile = kind.SourceFile,
            UiCategory = kind.UiCategory,
            TypeLabel = kind.TypeLabel,
            ShowInPlayGrid = kind.ShowInPlayGrid,
            IsSingleton = kind.IsSingleton,
            TitleProperty = kind.TitleProperty,
            SecondaryProperty = kind.SecondaryProperty,
            SnippetProperty = kind.SnippetProperty,
            Fields = fields,
        };
    }

    private static List<CanonFieldSpec> MergeFields(
        IReadOnlyList<CanonFieldSpec> shared,
        IReadOnlyList<CanonFieldSpec> kindFields) =>
        MergeFieldsForBootstrap(shared, kindFields);

    internal static List<CanonFieldSpec> MergeFieldsForBootstrap(
        IReadOnlyList<CanonFieldSpec> shared,
        IReadOnlyList<CanonFieldSpec> kindFields)
    {
        var kindKeys = new HashSet<string>(kindFields.Select(f => f.JsonKey), StringComparer.OrdinalIgnoreCase);
        var merged = new List<CanonFieldSpec>();
        foreach (var field in shared)
        {
            if (!kindKeys.Contains(field.JsonKey))
                merged.Add(field);
        }

        merged.AddRange(kindFields);
        return merged;
    }

    private static CanonFieldSpec ToFieldSpec(CanonFieldDocument field) =>
        new()
        {
            Label = field.Label,
            JsonKey = field.JsonKey,
            Format = Enum.Parse<CanonFieldFormat>(field.Format, ignoreCase: true),
            Role = Enum.Parse<CanonFieldRole>(field.Role, ignoreCase: true),
            Multiline = field.Multiline,
            ControlType = Enum.Parse<CanonFieldControlType>(field.ControlType, ignoreCase: true),
            AlternateLabels = field.AlternateLabels,
        };
}
