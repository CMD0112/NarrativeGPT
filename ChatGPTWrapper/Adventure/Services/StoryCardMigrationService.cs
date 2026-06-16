using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class StoryCardMigrationResult
{
    public int CardsProcessed { get; init; }

    public int EntitiesCreated { get; init; }

    public int IndexEntriesCreated { get; init; }

    public int ReviewItemsCreated { get; init; }
}

internal static class StoryCardMigrationService
{
    public static StoryCardMigrationResult Migrate(AdventureBundle bundle)
    {
        if (bundle.Metadata.SectionInjectionMigratedAt is not null)
            return new StoryCardMigrationResult();

        var result = new MigrationCounters();
        foreach (var card in bundle.Cards.Cards.Where(c => c.Enabled).ToList())
            MigrateCard(bundle, card, result);

        foreach (var card in bundle.Cards.Cards)
            card.Enabled = false;

        bundle.Metadata.SectionInjectionMigratedAt = DateTimeOffset.UtcNow;
        bundle.Metadata.Settings.UseSectionInjection = true;
        ProjectSourceExportService.ExportForce(bundle);

        return new StoryCardMigrationResult
        {
            CardsProcessed = result.CardsProcessed,
            EntitiesCreated = result.EntitiesCreated,
            IndexEntriesCreated = result.IndexEntriesCreated,
            ReviewItemsCreated = result.ReviewItemsCreated,
        };
    }

    private sealed class MigrationCounters
    {
        public int CardsProcessed;
        public int EntitiesCreated;
        public int IndexEntriesCreated;
        public int ReviewItemsCreated;
    }

    private static void MigrateCard(AdventureBundle bundle, StoryCard card, MigrationCounters result)
    {
        result.CardsProcessed++;
        var character = bundle.Entities.Characters.FirstOrDefault(c =>
            string.Equals(c.Name, card.Name, StringComparison.OrdinalIgnoreCase));

        if (character is null && IsCharacterLike(card.Type))
        {
            character = new CharacterEntry
            {
                Name = card.Name,
                Description = card.Content,
            };
            bundle.Entities.Characters.Add(character);
            result.EntitiesCreated++;
        }
        else if (character is not null)
        {
            MergeCardIntoCharacter(bundle, character, card, result);
        }
        else if (card.Triggers.Count > 0)
        {
            var target = InferTarget(bundle, card);
            bundle.ContextIndex.Entries.Add(new ContextIndexEntry
            {
                Id = SectionSlugHelper.FromName(card.Name),
                Target = target,
                Kind = MapKind(card.Type),
                Triggers = card.Triggers.ToList(),
            });
            result.IndexEntriesCreated++;
        }
        else
        {
            bundle.Scenario.SourceEditReviewQueue.Add(new SourceEditReviewItem
            {
                TargetFile = SectionSchema.CastFile,
                Content = card.Content,
                Rationale = $"Unmigrated story card: {card.Name}",
            });
            result.ReviewItemsCreated++;
        }

        if (card.Triggers.Count > 0 && character is not null)
        {
            foreach (var t in card.Triggers)
            {
                if (!character.Aliases.Contains(t, StringComparer.OrdinalIgnoreCase))
                    character.Aliases.Add(t);
            }
        }
    }

    private static void MergeCardIntoCharacter(
        AdventureBundle bundle,
        CharacterEntry character,
        StoryCard card,
        MigrationCounters result)
    {
        if (string.IsNullOrWhiteSpace(card.Content))
            return;

        if (string.IsNullOrWhiteSpace(character.Description))
        {
            character.Description = card.Content;
            return;
        }

        if (character.Description.Contains(card.Content, StringComparison.OrdinalIgnoreCase))
            return;

        if (card.Content.Contains(character.Description, StringComparison.OrdinalIgnoreCase))
        {
            character.Description = card.Content;
            return;
        }

        character.Flavor = string.IsNullOrWhiteSpace(character.Flavor)
            ? card.Content
            : character.Flavor + "\n" + card.Content;
        result.ReviewItemsCreated++;
    }

    private static bool IsCharacterLike(StoryCardType type) =>
        type is StoryCardType.Character or StoryCardType.Lore or StoryCardType.Organization;

    private static string InferTarget(AdventureBundle bundle, StoryCard card)
    {
        var slug = SectionSlugHelper.FromName(card.Name);
        return card.Type switch
        {
            StoryCardType.Place => $"{SectionSchema.WorldFile}#locations/{slug}",
            StoryCardType.Faction or StoryCardType.Organization => $"{SectionSchema.WorldFile}#factions/{slug}",
            StoryCardType.Item or StoryCardType.Rule or StoryCardType.Creature => $"{SectionSchema.WorldFile}#concepts/{slug}",
            _ => $"{SectionSchema.PlotFile}#mysteries/{slug}",
        };
    }

    private static string MapKind(StoryCardType type) => type switch
    {
        StoryCardType.Character => "person",
        StoryCardType.Place => "place",
        StoryCardType.Faction or StoryCardType.Organization => "faction",
        StoryCardType.Item => "concept",
        StoryCardType.Rule => "rule",
        _ => "concept",
    };
}
