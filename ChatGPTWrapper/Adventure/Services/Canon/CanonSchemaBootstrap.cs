using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonSchemaBootstrap
{
    public static CanonSchemaCatalog Build()
    {
        CanonEntityCategoryRegistry.Initialize(CanonEntityCategoryBootstrap.All);
        var categoryById = CanonEntityCategoryRegistry.ById;
        var all = new[]
        {
            ToKindSpec(Player, categoryById),
            ToKindSpec(Party, categoryById),
            ToKindSpec(Npc, categoryById),
            ToKindSpec(Location, categoryById),
            ToKindSpec(Faction, categoryById),
            ToKindSpec(Concept, categoryById),
            ToKindSpec(Quest, categoryById),
            ToKindSpec(Mystery, categoryById),
            ToKindSpec(Conflict, categoryById),
            ToKindSpec(Consequence, categoryById),
            ToKindSpec(Inventory, categoryById),
            ToKindSpec(Scenario, categoryById),
            ToKindSpec(Lexicon, categoryById),
            ToKindSpec(Custom, categoryById),
        };

        return new CanonSchemaCatalog
        {
            SchemaVersion = 1,
            AllKinds = all,
            Player = (CanonEntityKindSpec)all[0],
            Party = (CanonEntityKindSpec)all[1],
            Npc = (CanonEntityKindSpec)all[2],
            Location = (CanonEntityKindSpec)all[3],
            Faction = (CanonEntityKindSpec)all[4],
            Concept = (CanonEntityKindSpec)all[5],
            Quest = (CanonEntityKindSpec)all[6],
            Mystery = (CanonEntityKindSpec)all[7],
            Conflict = (CanonEntityKindSpec)all[8],
            Consequence = (CanonEntityKindSpec)all[9],
            Inventory = (CanonEntityKindSpec)all[10],
            Scenario = (CanonEntityKindSpec)all[11],
            Lexicon = (CanonEntityKindSpec)all[12],
            Custom = (CanonEntityKindSpec)all[13],
        };
    }

    private static CanonEntityKindSpec ToKindSpec(
        CanonEntityKindSpec kind,
        IReadOnlyDictionary<string, CanonEntityCategorySpec> categories)
    {
        if (string.IsNullOrWhiteSpace(kind.ParentCategory)
            || !categories.TryGetValue(kind.ParentCategory, out var category))
        {
            return kind;
        }

        var shared = kind.IsSingleton ? category.SingletonShellFields : category.ListShellFields;
        return new()
        {
            KindId = kind.KindId,
            ParentCategory = kind.ParentCategory,
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
            Fields = CanonSchemaLoader.MergeFieldsForBootstrap(shared, kind.Fields),
        };
    }

    internal static readonly CanonEntityKindSpec Player = new()
    {
        KindId = CanonSchemaRegistry.PlayerKind,
        ParentCategory = CanonEntityCategoryRegistry.Cast,
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
            F("Name", "name", CanonFieldFormat.BoldLine, CanonFieldRole.Primary, group: CanonFieldGroup.Identity),
            F("Background", "background", CanonFieldFormat.BoldLine, CanonFieldRole.Secondary, multiline: true, group: CanonFieldGroup.Story),
            F("Appearance", "appearance", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Identity),
            F("Personality", "personality", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Abilities", "abilities", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Capabilities),
            F("Weaknesses", "weaknesses", CanonFieldFormat.BoldLine, CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Capabilities),
            F("Goals", "goals", CanonFieldFormat.BoldLine, CanonFieldRole.Snippet, multiline: true, group: CanonFieldGroup.Story),
        ],
    };

    internal static readonly CanonEntityKindSpec Party = new()
    {
        KindId = CanonSchemaRegistry.PartyKind,
        ParentCategory = CanonEntityCategoryRegistry.Cast,
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
            F("Condition", "condition", CanonFieldRole.Secondary, alternateLabels: ["Role"], group: CanonFieldGroup.Identity),
            F("Relationship", "relationship", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Relations),
            F("Attitude", "attitude", CanonFieldRole.Extra, alternateLabels: ["Status"], group: CanonFieldGroup.Relations),
            F("Goals", "goals", CanonFieldRole.Snippet, multiline: true, alternateLabels: ["Motives"], group: CanonFieldGroup.Story),
            F("Secrets", "secrets", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Personality", "personality", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Abilities", "abilities", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Capabilities),
            F("Weaknesses", "weaknesses", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Capabilities),
            F("Flavor", "flavor", CanonFieldFormat.BlockquoteFlavor, CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
        ],
    };

    internal static readonly CanonEntityKindSpec Npc = new()
    {
        KindId = CanonSchemaRegistry.NpcKind,
        ParentCategory = CanonEntityCategoryRegistry.Cast,
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
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true, group: CanonFieldGroup.Identity),
            F("Role", "role", CanonFieldRole.Secondary, group: CanonFieldGroup.Identity),
            F("Relationship", "relationshipToPlayer", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Relations),
            F("Motives", "motives", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Status", "status", CanonFieldRole.Extra, group: CanonFieldGroup.Capabilities),
            F("Location", "location", CanonFieldRole.Extra, group: CanonFieldGroup.Relations),
            F("History", "history", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Personality", "personality", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Author guidance", "useInPlay", CanonFieldRole.Extra, multiline: true,
                alternateLabels: ["Use in play", "Use in Play", "Plot function", "Practical function"],
                group: CanonFieldGroup.Story),
            F("Flavor", "flavor", CanonFieldFormat.BlockquoteFlavor, CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Pinned", "pinned", CanonFieldRole.Shell),
        ],
    };

    internal static readonly CanonEntityKindSpec Location = new()
    {
        KindId = CanonSchemaRegistry.LocationKind,
        ParentCategory = CanonEntityCategoryRegistry.Place,
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
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true, group: CanonFieldGroup.Identity),
            F("Features", "features", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Connected places", "connectedPlaces", CanonFieldRole.Extra, multiline: true, alternateLabels: ["Connected Places"], group: CanonFieldGroup.Relations),
            F("Dangers", "dangers", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Capabilities),
            F("Mysteries", "mysteries", CanonFieldRole.Extra, multiline: true, group: CanonFieldGroup.Story),
            F("Status", "status", CanonFieldRole.Secondary, group: CanonFieldGroup.Identity),
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
        ParentCategory = CanonEntityCategoryRegistry.Lore,
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
            F("description", "description", CanonFieldFormat.FreeformBody, CanonFieldRole.Snippet, multiline: true),
            F("Category", "category", CanonFieldRole.Secondary),
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
            F("Status", "status", CanonFieldFormat.PlainLine, CanonFieldRole.Secondary, false, CanonFieldControlType.Enum, CanonFieldGroup.Identity),
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
        UiCategory = "Mysteries",
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
        UiCategory = "Conflicts",
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
        UiCategory = "Consequences",
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
        string group = CanonFieldGroup.Story,
        params string[] alternateLabels) =>
        F(label, jsonKey, CanonFieldFormat.PlainLine, role, multiline, CanonFieldControlType.Text, group, alternateLabels);

    private static CanonFieldSpec F(
        string label,
        string jsonKey,
        CanonFieldFormat format,
        CanonFieldRole role,
        bool multiline = false,
        string group = CanonFieldGroup.Story,
        params string[] alternateLabels) =>
        F(label, jsonKey, format, role, multiline, multiline ? CanonFieldControlType.Multiline : CanonFieldControlType.Text, group, alternateLabels);

    private static CanonFieldSpec F(
        string label,
        string jsonKey,
        CanonFieldFormat format,
        CanonFieldRole role,
        bool multiline,
        CanonFieldControlType controlType,
        string group = CanonFieldGroup.Story,
        params string[] alternateLabels) =>
        new()
        {
            Label = label,
            JsonKey = jsonKey,
            Format = format,
            Role = role,
            Multiline = multiline,
            ControlType = controlType,
            FieldGroup = group,
            AlternateLabels = alternateLabels,
        };
}
