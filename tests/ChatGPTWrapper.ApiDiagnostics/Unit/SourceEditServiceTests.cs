using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(nameof(IsolatedAppRootCollection))]
public sealed class SourceEditServiceTests
{
    [Fact]
    public void TryParseImportRemovalContent_parses_npc_removal_line()
    {
        var ok = SourceEditService.TryParseImportRemovalContent(
            "npcs/mira-thorn (d3ba03bf34c44c5bbea02345c6b12a70): Mira Thorn",
            out var sectionId,
            out var entityId);

        Assert.True(ok);
        Assert.Equal("npcs/mira-thorn", sectionId);
        Guid.TryParse("d3ba03bf34c44c5bbea02345c6b12a70", out var expectedId);
        Assert.Equal(expectedId, entityId);
    }

    [Fact]
    public void ApplyImportRemoval_removes_npc_and_reexports_cast()
    {
        var bundle = AdventureStore.CreateNew("Removal Apply Test");
        Guid.TryParse("d3ba03bf34c44c5bbea02345c6b12a70", out var npcId);
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = npcId,
            Name = "Mira Thorn",
            Role = "Guide",
        });
        AdventureStore.Save(bundle);

        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            AdventureStore.Save(bundle);

            var item = new SourceEditReviewItem
            {
                TargetFile = "cast.md",
                Operation = "remove",
                Content = $"npcs/mira-thorn ({npcId:N}): Mira Thorn",
            };

            Assert.True(SourceEditService.ApplyAcceptedEdit(bundle, item));
            Assert.DoesNotContain(bundle.Entities.Characters, c => c.Id == npcId);

            var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.CastFile);
            var cast = File.ReadAllText(castPath);
            Assert.DoesNotContain("Mira Thorn", cast, StringComparison.Ordinal);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Import_does_not_duplicate_existing_removal_queue_items()
    {
        var bundle = AdventureStore.CreateNew("Removal Dedupe Test");
        var npcId = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = npcId,
            Name = "Temp NPC",
            Description = "Should be queued once.",
        });
        AdventureStore.Save(bundle);

        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            var castPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.CastFile);
            File.WriteAllText(castPath, "# Cast\n\n## player\n\n**Name:** Alex\n");

            ProjectSourceImportService.Import(bundle);
            ProjectSourceImportService.Import(bundle);

            Assert.Single(bundle.Scenario.SourceEditReviewQueue);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void ApplyImportRemoval_is_idempotent_when_entity_already_removed()
    {
        var bundle = AdventureStore.CreateNew("Removal Idempotent Test");
        Guid.TryParse("d3ba03bf34c44c5bbea02345c6b12a70", out var npcId);
        var item = new SourceEditReviewItem
        {
            TargetFile = "cast.md",
            Operation = "remove",
            Content = $"npcs/mira-thorn ({npcId:N}): Mira Thorn",
        };

        try
        {
            Assert.True(SourceEditService.ApplyAcceptedEdit(bundle, item));
            Assert.True(SourceEditService.ApplyAcceptedEdit(bundle, item));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void RemoveMatchingReviewProposals_collapses_duplicate_removal_rows()
    {
        Guid.TryParse("d3ba03bf34c44c5bbea02345c6b12a70", out var npcId);
        var bundle = AdventureStore.CreateNew("Collapse Proposals Test");
        var item = new SourceEditReviewItem
        {
            TargetFile = "cast.md",
            Operation = "remove",
            Content = $"npcs/mira-thorn ({npcId:N}): Mira Thorn",
        };

        try
        {
            bundle.Scenario.SourceEditReviewQueue.Add(item);
            bundle.Scenario.SourceEditReviewQueue.Add(new SourceEditReviewItem
            {
                TargetFile = "cast.md",
                Operation = "remove",
                Content = item.Content,
            });

            SourceEditService.RemoveMatchingReviewProposals(bundle, item);

            Assert.Empty(bundle.Scenario.SourceEditReviewQueue);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}
