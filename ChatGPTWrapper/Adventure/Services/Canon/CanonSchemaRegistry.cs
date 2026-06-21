namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonSchemaRegistry
{
    public const string PlayerKind = "player";
    public const string PartyKind = "party";
    public const string NpcKind = "npc";
    public const string LocationKind = "location";
    public const string FactionKind = "faction";
    public const string ConceptKind = "concept";
    public const string QuestKind = "quest";
    public const string MysteryKind = "mystery";
    public const string ConflictKind = "conflict";
    public const string ConsequenceKind = "consequence";
    public const string InventoryKind = "inventory";
    public const string ScenarioKind = "scenario";
    public const string LexiconKind = "lexicon";
    public const string CustomKind = "custom";

    private static CanonSchemaCatalog Catalog => CanonSchemaLoader.Catalog;

    public static int SchemaVersion => Catalog.SchemaVersion;

    public static CanonEntityKindSpec Player => Catalog.Player;
    public static CanonEntityKindSpec Party => Catalog.Party;
    public static CanonEntityKindSpec Npc => Catalog.Npc;
    public static CanonEntityKindSpec Location => Catalog.Location;
    public static CanonEntityKindSpec Faction => Catalog.Faction;
    public static CanonEntityKindSpec Concept => Catalog.Concept;
    public static CanonEntityKindSpec Quest => Catalog.Quest;
    public static CanonEntityKindSpec Mystery => Catalog.Mystery;
    public static CanonEntityKindSpec Conflict => Catalog.Conflict;
    public static CanonEntityKindSpec Consequence => Catalog.Consequence;
    public static CanonEntityKindSpec Inventory => Catalog.Inventory;
    public static CanonEntityKindSpec Scenario => Catalog.Scenario;
    public static CanonEntityKindSpec Lexicon => Catalog.Lexicon;
    public static CanonEntityKindSpec Custom => Catalog.Custom;

    public static IReadOnlyList<CanonEntityKindSpec> AllKinds => Catalog.AllKinds;

    public static CanonEntityKindSpec? TryGetByKindId(string kindId) =>
        All.FirstOrDefault(k => string.Equals(k.KindId, kindId, StringComparison.OrdinalIgnoreCase));

    public static CanonEntityKindSpec? TryGetBySection(string sourceFile, string sectionId) =>
        All.FirstOrDefault(k =>
            !string.IsNullOrWhiteSpace(k.SourceFile)
            && string.Equals(k.SourceFile, sourceFile, StringComparison.OrdinalIgnoreCase)
            && string.Equals(k.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));

    public static CanonEntityKindSpec? TryGetByUiCategory(string uiCategory) =>
        All.FirstOrDefault(k => string.Equals(k.UiCategory, uiCategory, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<CanonEntityKindSpec> PlayGridKinds =>
        All.Where(k => k.ShowInPlayGrid).ToList();

    public static IReadOnlyList<string> PlayGridCategories =>
        PlayGridKinds.Select(k => k.UiCategory).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<string> PlayerFieldLabels =>
        Player.Fields.Select(f => f.Label).ToList();

    public static IReadOnlyList<string> EntryFieldPrefixes =>
        All.SelectMany(k => k.Fields)
            .Where(f => f.Role != CanonFieldRole.Shell && f.Format != CanonFieldFormat.FreeformBody)
            .Select(f => f.Label + ":")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<CanonEntityKindSpec> All => AllKinds;
}
