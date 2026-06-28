using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EntityReferenceRowBuilderTests
{
    [Fact]
    public void BuildRows_characters_includes_name_and_role()
    {
        var id = Guid.NewGuid();
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid(), Title = "Test" },
            Entities = new EntitiesDocument
            {
                Characters =
                [
                    new CharacterEntry
                    {
                        Id = id,
                        Name = "Merchant",
                        Role = "Trader",
                        Description = "Runs the stall",
                    },
                ],
            },
        };

        var layout = PlayLayoutCapabilities.FromContentWidth(480);
        var rows = EntityReferenceRowBuilder.BuildRows(bundle, "Characters", layout);

        var row = Assert.Single(rows);
        Assert.Equal(id, row.Id);
        Assert.Equal("Merchant", row.Name);
        Assert.Equal("Trader", row.RoleOrStatus);
        Assert.Contains("stall", row.DescriptionSnippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRow_returns_matching_entity()
    {
        var adventureId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = adventureId, Title = "Test" },
            Entities = new EntitiesDocument
            {
                Characters = [new CharacterEntry { Id = characterId, Name = "Ari" }],
            },
        };

        var layout = PlayLayoutCapabilities.FromContentWidth(320);
        var row = EntityReferenceRowBuilder.FindRow(bundle, "Characters", characterId, layout);

        Assert.NotNull(row);
        Assert.Equal("Ari", row!.Name);
    }

    [Fact]
    public void ResolveFilters_uses_panel_subset_when_provided()
    {
        var options = new EntityReferencePanelOptions
        {
            CategoryFilters = ["Player", "Characters"],
        };

        var filters = EntityReferenceRowBuilder.ResolveFilters(options);

        Assert.Equal(2, filters.Count);
        Assert.Contains("Player", filters, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Characters", filters, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterDisplayLabel_uses_compact_label_for_characters()
    {
        Assert.Equal("Cast", EntityReferenceRowBuilder.FilterDisplayLabel("Characters", compact: true));
        Assert.Equal("Characters", EntityReferenceRowBuilder.FilterDisplayLabel("Characters", compact: false));
    }

    [Fact]
    public void FilterAndSortRows_filters_by_name_role_and_aliases()
    {
        var rows = new[]
        {
            new EntityReferenceRow { Name = "Mara", RoleOrStatus = "Guide", AliasesSearchText = "" },
            new EntityReferenceRow { Name = "Tom", RoleOrStatus = "Merchant", AliasesSearchText = "the trader" },
        };

        var filtered = EntityReferenceRowBuilder.FilterAndSortRows(rows, "trader", EntityListSortMode.NameAscending, pinSortEnabled: false);

        var row = Assert.Single(filtered);
        Assert.Equal("Tom", row.Name);
    }

    [Fact]
    public void FilterAndSortRows_pinned_first_when_enabled()
    {
        var rows = new[]
        {
            new EntityReferenceRow { Name = "Beta", Pinned = false },
            new EntityReferenceRow { Name = "Alpha", Pinned = true },
        };

        var sorted = EntityReferenceRowBuilder.FilterAndSortRows(rows, null, EntityListSortMode.PinnedFirst, pinSortEnabled: true);

        Assert.Equal("Alpha", sorted[0].Name);
        Assert.Equal("Beta", sorted[1].Name);
    }

    [Fact]
    public void FilterAndSortRows_recently_edited_orders_by_last_edited()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow;
        var rows = new[]
        {
            new EntityReferenceRow { Name = "Older", LastEditedUtc = older },
            new EntityReferenceRow { Name = "Newer", LastEditedUtc = newer },
        };

        var sorted = EntityReferenceRowBuilder.FilterAndSortRows(rows, null, EntityListSortMode.RecentlyEdited, pinSortEnabled: false);

        Assert.Equal("Newer", sorted[0].Name);
        Assert.Equal("Older", sorted[1].Name);
    }
}
