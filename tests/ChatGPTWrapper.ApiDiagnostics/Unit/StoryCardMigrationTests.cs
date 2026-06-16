using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class StoryCardMigrationTests
{
    [Fact]
    public void Migrate_moves_card_to_character_and_disables_card()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Cards.Cards.Add(new StoryCard
        {
            Name = "Mara Voss",
            Content = "She sells herbs.",
            Triggers = ["Mara"],
            Enabled = true,
        });

        var result = StoryCardMigrationService.Migrate(bundle);

        Assert.Equal(1, result.CardsProcessed);
        Assert.Contains(bundle.Entities.Characters, c => c.Name == "Mara Voss");
        Assert.All(bundle.Cards.Cards, c => Assert.False(c.Enabled));
        Assert.NotNull(bundle.Metadata.SectionInjectionMigratedAt);
        Assert.True(bundle.Metadata.Settings.UseSectionInjection);
    }
}
