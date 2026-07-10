using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class CanonSchemaRegistryTests
{
    [Fact]
    public void All_kinds_have_import_export_field_coverage_for_cast()
    {
        Assert.NotEmpty(CanonSchemaRegistry.Player.BodyFields);
        Assert.NotEmpty(CanonSchemaRegistry.Party.BodyFields);
        Assert.NotEmpty(CanonSchemaRegistry.Npc.BodyFields);
        Assert.Contains(CanonSchemaRegistry.Party.BodyFields, f => f.JsonKey == "condition");
        Assert.DoesNotContain(CanonSchemaRegistry.Party.BodyFields, f => f.Format == CanonFieldFormat.PositionalLine);
    }

    [Fact]
    public void Party_round_trip_preserves_labeled_fields_and_cast_shell_fields()
    {
        var companion = new CompanionEntry
        {
            Name = "Nessa Vale",
            Condition = "Wounded shoulder",
            Relationship = "Old friend",
            Attitude = "Wary but loyal",
            Goals = "Find her brother",
            Tags = ["scout"],
            Aliases = ["Nessa"],
        };

        var body = CanonFieldMapper.BuildEntryBody(companion, CanonSchemaRegistry.Party);
        Assert.DoesNotContain("Nessa Vale", body.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("Condition:", body, StringComparison.Ordinal);
        Assert.Contains("Tags: scout", body, StringComparison.Ordinal);

        var entry = new ParsedMarkdownEntry
        {
            Title = companion.Name,
            Body = body,
        };
        entry.Aliases.Add("Nessa");
        var imported = new CompanionEntry();
        CanonFieldMapper.ApplyEntry(imported, CanonSchemaRegistry.Party, entry);

        Assert.Equal(companion.Name, imported.Name);
        Assert.Equal(companion.Condition, imported.Condition);
        Assert.Equal(companion.Relationship, imported.Relationship);
        Assert.Equal(companion.Attitude, imported.Attitude);
        Assert.Equal(companion.Goals, imported.Goals);
        Assert.Equal(companion.Tags, imported.Tags);
        Assert.Equal(companion.Aliases, imported.Aliases);
    }

    [Fact]
    public void Cast_kinds_share_parent_category_and_shell_fields()
    {
        foreach (var kind in new[] { CanonSchemaRegistry.Player, CanonSchemaRegistry.Party, CanonSchemaRegistry.Npc })
        {
            Assert.Equal(CanonEntityCategoryRegistry.Cast, kind.ParentCategory);
            Assert.NotNull(kind.CategorySpec);
            Assert.True(kind.ShowTags);
            Assert.True(kind.ShowAliases);
        }

        Assert.Contains(CanonSchemaRegistry.Party.Fields, f => f.JsonKey == "aliases");
        Assert.Contains(CanonSchemaRegistry.Party.Fields, f => f.JsonKey == "tags");
    }

    [Fact]
    public void Party_round_trip_preserves_labeled_fields()
    {
        var companion = new CompanionEntry
        {
            Name = "Nessa Vale",
            Condition = "Wounded shoulder",
            Relationship = "Old friend",
            Attitude = "Wary but loyal",
            Goals = "Find her brother",
        };

        var body = CanonFieldMapper.BuildEntryBody(companion, CanonSchemaRegistry.Party);
        Assert.DoesNotContain("Nessa Vale", body.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("Condition:", body, StringComparison.Ordinal);

        var entry = new ParsedMarkdownEntry
        {
            Title = companion.Name,
            Body = body,
        };
        var imported = new CompanionEntry();
        CanonFieldMapper.ApplyEntry(imported, CanonSchemaRegistry.Party, entry);

        Assert.Equal(companion.Name, imported.Name);
        Assert.Equal(companion.Condition, imported.Condition);
        Assert.Equal(companion.Relationship, imported.Relationship);
        Assert.Equal(companion.Attitude, imported.Attitude);
        Assert.Equal(companion.Goals, imported.Goals);
    }

    [Fact]
    public void Extended_field_round_trips_without_mapper_code_change()
    {
        var character = new CharacterEntry
        {
            Name = "Test NPC",
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customTrait"] = "Scarred left hand",
            },
        };

        CanonFieldMapper.SetField(character, CanonSchemaRegistry.Npc, "customTrait", "Scarred left hand");
        Assert.Equal("Scarred left hand", CanonFieldMapper.GetField(character, CanonSchemaRegistry.Npc, "customTrait"));
    }

    [Fact]
    public void EntitiesDocumentMigration_sets_current_schema_version()
    {
        var entities = new EntitiesDocument { SchemaVersion = 1 };
        Assert.True(EntitiesDocumentMigration.Migrate(entities));
        Assert.Equal(EntitiesDocument.CurrentSchemaVersion, entities.SchemaVersion);
        Assert.NotNull(entities.Player.ExtendedFields);
    }
}
