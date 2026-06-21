using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonSchemaBootstrap
{
    public static CanonSchemaCatalog Build()
    {
        var all = new[]
        {
            Player, Party, Npc, Location, Faction, Concept, Quest, Mystery, Conflict, Consequence,
            Inventory, Scenario, Lexicon, Custom,
        };

        return new CanonSchemaCatalog
        {
            SchemaVersion = 1,
            AllKinds = all,
            Player = Player,
            Party = Party,
            Npc = Npc,
            Location = Location,
            Faction = Faction,
            Concept = Concept,
            Quest = Quest,
            Mystery = Mystery,
            Conflict = Conflict,
            Consequence = Consequence,
            Inventory = Inventory,
            Scenario = Scenario,
            Lexicon = Lexicon,
            Custom = Custom,
        };
    }

    internal static readonly CanonEntityKindSpec Player = new()
    {
        KindId = CanonSchemaRegistry.PlayerKind,
        CollectionKey = "player",
        SectionId = "player",
        SourceFile = SectionSchema.CastFile,
        UiCategory = "Player",
        TypeLabel = "Player",
        ShowInPlayGrid = true,
        IsSingleton = true,
        TitleProperty = "name",
        SecondaryProperty = "background",
        SnippetProperty = "goals",
        Fields =
        [
            F("Name", "name", CanonFieldFormat.BoldLine, CanonFieldRole.Primary),
            F("Background", "background", CanonFieldFormat.BoldLine, CanonFieldRole.Secondary, multiline: true),
            F("Appearance", "appearance", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true),
            F("Personality", "personality", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true),
            F("Abilities", "abilities", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true),
            F("Weaknesses", "weaknesses", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true),
            F("Goals", "goals", CanonFieldFormat.BoldLine, CanonFieldRole.Snippet, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Party = new()
    {
        KindId = CanonSchemaRegistry.PartyKind,
        CollectionKey = "party",
        SectionId = "party",
        SourceFile = SectionSchema.CastFile,
        UiCategory = "Party",
        TypeLabel = "Companion",
        ShowInPlayGrid = true,
        TitleProperty = "name",
        SecondaryProperty = "condition",
        SnippetProperty = "goals",
        Fields =
        [
            F("Condition", "condition", CanonFieldRole.Secondary, alternateLabels: ["Role"]),
            F("Relationship", "relationship", CanonFieldRole.Extra, multiline: true),
            F("Attitude", "attitude", CanonFieldRole.Extra, alternateLabels: ["Status"]),
            F("Goals", "goals", CanonFieldRole.Snippet, multiline: true),
            F("Secrets", "secrets", CanonFieldRole.Extra, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Npc = new()
    {
        KindId = CanonSchemaRegistry.NpcKind,
        CollectionKey = "characters",
        SectionId = "npcs",
        SourceFile = SectionSchema.CastFile,
        UiCategory = "Characters",
        TypeLabel = "Character",
        ShowInPlayGrid = true,
        TitleProperty = "name",
        SecondaryProperty = "role",
        SnippetProperty = "description",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("Aliases", "aliases", CanonFieldRole.Shell),
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Role", "role", CanonFieldRole.Secondary),
            F("Relationship", "relationshipToPlayer", CanonFieldRole.Extra, multiline: true),
            F("Motives", "motives", CanonFieldRole.Extra, multiline: true),
            F("Status", "status", CanonFieldRole.Extra),
            F("Location", "location", CanonFieldRole.Extra),
            F("History", "history", CanonFieldRole.Extra, multiline: true),
            F("Flavor", "flavor", CanonFieldFormat.BlockquoteFlavor, CanonFieldRole.Extra, multiline: true),
            F("Pinned", "pinned", CanonFieldRole.Shell),
        ],
    };

    internal static readonly CanonEntityKindSpec Location = new()
    {
        KindId = CanonSchemaRegistry.LocationKind,
        CollectionKey = "locations",
        SectionId = "locations",
        SourceFile = SectionSchema.WorldFile,
        UiCategory = "Locations",
        TypeLabel = "Location",
        ShowInPlayGrid = true,
        TitleProperty = "name",
        SecondaryProperty = "status",
        SnippetProperty = "description",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("Aliases", "aliases", CanonFieldRole.Shell),
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Features", "features", CanonFieldRole.Extra, multiline: true),
            F("Connected places", "connectedPlaces", CanonFieldRole.Extra, multiline: true, alternateLabels: ["Connected Places"]),
            F("Dangers", "dangers", CanonFieldRole.Extra, multiline: true),
            F("Mysteries", "mysteries", CanonFieldRole.Extra, multiline: true),
            F("Status", "status", CanonFieldRole.Secondary),
            F("Pinned", "pinned", CanonFieldRole.Shell),
        ],
    };

    internal static readonly CanonEntityKindSpec Faction = new()
    {
        KindId = CanonSchemaRegistry.FactionKind,
        CollectionKey = "factions",
        SectionId = "factions",
        SourceFile = SectionSchema.WorldFile,
        UiCategory = "Factions",
        TypeLabel = "Faction",
        ShowInPlayGrid = true,
        TitleProperty = "name",
        SecondaryProperty = "reputation",
        SnippetProperty = "goals",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("Goals", "goals", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Members", "members", CanonFieldRole.Extra, multiline: true),
            F("Relationships", "relationships", CanonFieldRole.Extra, multiline: true),
            F("Reputation", "reputation", CanonFieldRole.Secondary),
            F("Conflicts", "conflicts", CanonFieldRole.Extra, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Concept = new()
    {
        KindId = CanonSchemaRegistry.ConceptKind,
        CollectionKey = "concepts",
        SectionId = "concepts",
        SourceFile = SectionSchema.WorldFile,
        UiCategory = "Concepts",
        TypeLabel = "Concept",
        ShowInPlayGrid = true,
        TitleProperty = "name",
        SecondaryProperty = "category",
        SnippetProperty = "description",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Category", "category", CanonFieldRole.Secondary),
            F("Pinned", "pinned", CanonFieldRole.Shell),
        ],
    };

    internal static readonly CanonEntityKindSpec Quest = new()
    {
        KindId = CanonSchemaRegistry.QuestKind,
        CollectionKey = "quests",
        SectionId = "quests",
        SourceFile = SectionSchema.PlotFile,
        UiCategory = "Quests",
        TypeLabel = "Quest",
        ShowInPlayGrid = true,
        TitleProperty = "title",
        SecondaryProperty = "notes",
        SnippetProperty = "description",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("Status", "status", CanonFieldFormat.PlainLine, CanonFieldRole.Secondary, false, CanonFieldControlType.Enum),
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Notes", "notes", CanonFieldRole.Extra, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Mystery = new()
    {
        KindId = CanonSchemaRegistry.MysteryKind,
        CollectionKey = "mysteries",
        SectionId = "mysteries",
        SourceFile = SectionSchema.PlotFile,
        UiCategory = "Concepts",
        TypeLabel = "Mystery",
        ShowInPlayGrid = false,
        TitleProperty = "question",
        SecondaryProperty = "",
        SnippetProperty = "clues",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("Clues", "clues", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Theories", "theories", CanonFieldRole.Extra, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Conflict = new()
    {
        KindId = CanonSchemaRegistry.ConflictKind,
        CollectionKey = "conflicts",
        SectionId = "conflicts",
        SourceFile = SectionSchema.PlotFile,
        UiCategory = "Concepts",
        TypeLabel = "Conflict",
        ShowInPlayGrid = false,
        TitleProperty = "title",
        SecondaryProperty = "status",
        SnippetProperty = "description",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("Status", "status", CanonFieldRole.Secondary),
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Consequence = new()
    {
        KindId = CanonSchemaRegistry.ConsequenceKind,
        CollectionKey = "consequences",
        SectionId = "consequences",
        SourceFile = SectionSchema.PlotFile,
        UiCategory = "Concepts",
        TypeLabel = "Consequence",
        ShowInPlayGrid = false,
        TitleProperty = "trigger",
        SecondaryProperty = "",
        SnippetProperty = "effect",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("Effect", "effect", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Due when", "dueWhen", CanonFieldRole.Extra, alternateLabels: ["DueWhen"]),
        ],
    };

    internal static readonly CanonEntityKindSpec Inventory = new()
    {
        KindId = CanonSchemaRegistry.InventoryKind,
        CollectionKey = "inventory",
        SectionId = "inventory",
        SourceFile = "",
        UiCategory = "Things",
        TypeLabel = "Thing",
        ShowInPlayGrid = true,
        TitleProperty = "name",
        SecondaryProperty = "status",
        SnippetProperty = "description",
        Fields =
        [
            F("Source", "source", CanonFieldRole.Extra),
            F("Notes", "notes", CanonFieldRole.Extra, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Scenario = new()
    {
        KindId = CanonSchemaRegistry.ScenarioKind,
        CollectionKey = "scenario",
        SectionId = "opening",
        SourceFile = SectionSchema.ScenarioFile,
        UiCategory = "Scenario",
        TypeLabel = "Scenario",
        ShowInPlayGrid = false,
        IsSingleton = true,
        TitleProperty = "title",
        SecondaryProperty = "genre",
        SnippetProperty = "openingSituation",
        Fields =
        [
            F("Setting", "setting", CanonFieldRole.Extra, multiline: true),
            F("Genre", "genre", CanonFieldRole.Secondary),
            F("Tone", "tone", CanonFieldRole.Extra),
            F("Opening situation", "openingSituation", CanonFieldRole.Snippet, multiline: true, alternateLabels: ["Opening Situation"]),
        ],
    };

    internal static readonly CanonEntityKindSpec Lexicon = new()
    {
        KindId = CanonSchemaRegistry.LexiconKind,
        CollectionKey = "lexicon",
        SectionId = "rules",
        SourceFile = SectionSchema.LexiconFile,
        UiCategory = "Lexicon",
        TypeLabel = "Lexicon",
        ShowInPlayGrid = false,
        IsSingleton = true,
        TitleProperty = "rules",
        SecondaryProperty = "",
        SnippetProperty = "pools",
        Fields =
        [
            F("Rules", "rules", CanonFieldFormat.FreeformBody, CanonFieldRole.Primary, multiline: true),
            F("Pools", "pools", CanonFieldRole.Snippet, multiline: true),
            F("Avoid", "avoid", CanonFieldRole.Extra, multiline: true),
        ],
    };

    internal static readonly CanonEntityKindSpec Custom = new()
    {
        KindId = CanonSchemaRegistry.CustomKind,
        CollectionKey = "custom",
        SectionId = "custom",
        SourceFile = SectionSchema.WorldFile,
        UiCategory = "Custom",
        TypeLabel = "Custom",
        ShowInPlayGrid = false,
        TitleProperty = "name",
        SecondaryProperty = "category",
        SnippetProperty = "description",
        Fields =
        [
            F("Id", "id", CanonFieldRole.Shell),
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Category", "category", CanonFieldRole.Secondary),
        ],
    };

    private static CanonFieldSpec F(
        string label,
        string jsonKey,
        CanonFieldRole role = CanonFieldRole.Extra,
        bool multiline = false,
        params string[] alternateLabels) =>
        F(label, jsonKey, CanonFieldFormat.PlainLine, role, multiline, CanonFieldControlType.Text, alternateLabels);

    private static CanonFieldSpec F(
        string label,
        string jsonKey,
        CanonFieldFormat format,
        CanonFieldRole role,
        bool multiline = false,
        params string[] alternateLabels) =>
        F(label, jsonKey, format, role, multiline, multiline ? CanonFieldControlType.Multiline : CanonFieldControlType.Text, alternateLabels);

    private static CanonFieldSpec F(
        string label,
        string jsonKey,
        CanonFieldFormat format,
        CanonFieldRole role,
        bool multiline,
        CanonFieldControlType controlType,
        params string[] alternateLabels) =>
        new()
        {
            Label = label,
            JsonKey = jsonKey,
            Format = format,
            Role = role,
            Multiline = multiline,
            ControlType = controlType,
            AlternateLabels = alternateLabels,
        };
}
