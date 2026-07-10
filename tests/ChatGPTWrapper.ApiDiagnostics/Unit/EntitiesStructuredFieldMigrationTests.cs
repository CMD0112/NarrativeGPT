using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EntitiesStructuredFieldMigrationTests
{
    [Fact]
    public void ApplyFreeformBody_on_character_description_blob_populates_fields()
    {
        var character = new CharacterEntry
        {
            Name = "Mara Holt",
            Description = "Role: Merchant\nMotives: Turn a profit",
        };

        CanonFieldMapper.ApplyFreeformBody(character, CanonSchemaRegistry.Npc, character.Description);
        Assert.Equal("Merchant", character.Role);
        Assert.Equal("Turn a profit", character.Motives);
        Assert.True(string.IsNullOrWhiteSpace(character.Description));
    }

    [Fact]
    public void TryPromoteStructuredFieldsFromBody_splits_npc_description_blob()
    {
        var character = new CharacterEntry
        {
            Name = "Mara Holt",
            Description = "Role: Merchant\nMotives: Turn a profit",
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        Assert.True(CanonFieldMapper.TryPromoteStructuredFieldsFromBody(character, CanonSchemaRegistry.Npc));
        Assert.Equal("Merchant", character.Role);
        Assert.Equal("Turn a profit", character.Motives);
        Assert.True(string.IsNullOrWhiteSpace(character.Description));
    }

    [Fact]
    public void ApplyFreeformBody_splits_long_npc_description_blob()
    {
        const string body = """
            Role: Garran's wife, who survived eight years believing him dead.
            Relationship: Married to Garran before the war.
            Motives: Protect her children; preserve the life she built under pressure.
            Personality: Guarded, practical, emotionally controlled in public.
            Potential arc: Mara may move from shock toward a more complex reckoning.
            """;

        var character = new CharacterEntry
        {
            Name = "Mara Holt",
            Description = body,
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        CanonFieldMapper.ApplyFreeformBody(character, CanonSchemaRegistry.Npc, body);
        Assert.Contains("Garran's wife", character.Role, StringComparison.Ordinal);
        Assert.Equal("Guarded, practical, emotionally controlled in public.", character.Personality);
    }

    [Fact]
    public void TryPromoteStructuredFieldsFromBody_moves_unknown_labels_to_extended_fields()
    {
        var character = new CharacterEntry
        {
            Name = "Mara Holt",
            Description = """
                Role: Garran's wife, who survived eight years believing him dead.
                Relationship: Married to Garran before the war.
                Motives: Protect her children; preserve the life she built under pressure.
                Personality: Guarded, practical, emotionally controlled in public.
                Potential arc: Mara may move from shock toward a more complex reckoning.
                """,
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        var promoted = CanonFieldMapper.TryPromoteStructuredFieldsFromBody(character, CanonSchemaRegistry.Npc);
        Assert.Contains("Garran's wife", character.Role, StringComparison.Ordinal);
        Assert.Contains("Married to Garran", character.RelationshipToPlayer, StringComparison.Ordinal);
        Assert.Contains("Protect her children", character.Motives, StringComparison.Ordinal);
        Assert.Equal("Guarded, practical, emotionally controlled in public.", character.Personality);
        Assert.Contains("complex reckoning", character.ExtendedFields["Potential arc"], StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(character.Description));
        Assert.True(promoted);
    }

    [Fact]
    public void ApplyEntry_strips_labeled_lines_from_freeform_description()
    {
        const string body = """
            Role: Town guard captain
            Relationship: Suspicious of strangers
            Motives: Keep order on the docks
            A plain narrative note that should remain.
            """;

        var entry = new ParsedMarkdownEntry { Title = "Harbor Captain", Body = body };
        var imported = new CharacterEntry();
        CanonFieldMapper.ApplyEntry(imported, CanonSchemaRegistry.Npc, entry);

        Assert.Equal("Town guard captain", imported.Role);
        Assert.Equal("Suspicious of strangers", imported.RelationshipToPlayer);
        Assert.Equal("Keep order on the docks", imported.Motives);
        Assert.Contains("plain narrative note", imported.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Role:", imported.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void EntitiesDocumentMigration_promotes_structured_fields_for_legacy_blob_entities()
    {
        var entities = new EntitiesDocument
        {
            SchemaVersion = EntitiesDocument.CurrentSchemaVersion,
            Characters =
            [
                new CharacterEntry
                {
                    Name = "Test NPC",
                    Description = "Role: Merchant\nMotives: Turn a profit",
                    ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                },
            ],
        };

        Assert.True(EntitiesDocumentMigration.Migrate(entities));
        var npc = entities.Characters[0];
        Assert.Equal("Merchant", npc.Role);
        Assert.Equal("Turn a profit", npc.Motives);
        Assert.True(string.IsNullOrWhiteSpace(npc.Description));
    }

    [Fact]
    public void MigratePartyExtendedFieldAliases_moves_npc_labels_into_companion_fields()
    {
        var companion = new CompanionEntry
        {
            Name = "Nessa",
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Role"] = "Ten-year-old girl rescued by Garran.",
                ["Motives"] = "Stay free of the Crown.",
                ["Status"] = "Alive and guarded.",
            },
        };

        var entities = new EntitiesDocument
        {
            SchemaVersion = EntitiesDocument.CurrentSchemaVersion,
            Party = [companion],
        };

        Assert.True(EntitiesDocumentMigration.Migrate(entities));
        Assert.Equal("Ten-year-old girl rescued by Garran.", companion.Condition);
        Assert.Equal("Stay free of the Crown.", companion.Goals);
        Assert.Equal("Alive and guarded.", companion.Attitude);
        Assert.False(companion.ExtendedFields.ContainsKey("Role"));
        Assert.False(companion.ExtendedFields.ContainsKey("Motives"));
    }

    [Fact]
    public void TryPromote_does_not_overwrite_existing_typed_fields()
    {
        var character = new CharacterEntry
        {
            Name = "Test NPC",
            Role = "Existing role",
            Description = "Role: Should not replace\nMotives: New motive",
        };

        Assert.True(CanonFieldMapper.TryPromoteStructuredFieldsFromBody(character, CanonSchemaRegistry.Npc));
        Assert.Equal("Existing role", character.Role);
        Assert.Equal("New motive", character.Motives);
    }

    [Fact]
    public void TryPromoteKnownExtendedFields_moves_tier_a_npc_keys_to_typed_properties()
    {
        var character = new CharacterEntry
        {
            Name = "Test NPC",
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Personality"] = "Dry and skeptical.",
                ["Use in play"] = "Reveal the conspiracy in act two.",
                ["Plot function"] = "Should not overwrite useInPlay",
            },
        };

        Assert.True(CanonFieldMapper.TryPromoteKnownExtendedFields(character, CanonSchemaRegistry.Npc));
        Assert.Equal("Dry and skeptical.", character.Personality);
        Assert.Equal("Reveal the conspiracy in act two.", character.UseInPlay);
        Assert.False(character.ExtendedFields.ContainsKey("Personality"));
        Assert.False(character.ExtendedFields.ContainsKey("Use in play"));
    }

    [Fact]
    public void TryPromoteKnownExtendedFields_moves_tier_a_party_keys_to_typed_properties()
    {
        var companion = new CompanionEntry
        {
            Name = "Nessa",
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Personality"] = "Brave but impulsive.",
                ["Abilities"] = "Climbs and picks locks.",
                ["Weaknesses"] = "Trusts too easily.",
                ["Flavor"] = "I won't go back in that cellar.",
            },
        };

        Assert.True(CanonFieldMapper.TryPromoteKnownExtendedFields(companion, CanonSchemaRegistry.Party));
        Assert.Equal("Brave but impulsive.", companion.Personality);
        Assert.Equal("Climbs and picks locks.", companion.Abilities);
        Assert.Equal("Trusts too easily.", companion.Weaknesses);
        Assert.Equal("I won't go back in that cellar.", companion.Flavor);
        Assert.Empty(companion.ExtendedFields);
    }

    [Fact]
    public void Migrate_promotes_personality_from_description_and_extended_fields()
    {
        var character = new CharacterEntry
        {
            Name = "Test NPC",
            Description = "Personality: Wry and patient.",
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Use in play"] = "Foreshadow the betrayal.",
            },
        };

        var entities = new EntitiesDocument
        {
            SchemaVersion = EntitiesDocument.CurrentSchemaVersion,
            Characters = [character],
        };

        Assert.True(EntitiesDocumentMigration.Migrate(entities));
        Assert.Equal("Wry and patient.", character.Personality);
        Assert.Equal("Foreshadow the betrayal.", character.UseInPlay);
        Assert.True(string.IsNullOrWhiteSpace(character.Description));
    }
}
