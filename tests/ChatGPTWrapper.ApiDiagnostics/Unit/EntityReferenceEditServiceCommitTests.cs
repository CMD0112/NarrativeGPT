using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityReferenceEditServiceCommitTests : IDisposable
{
    private readonly string _root;

    public EntityReferenceEditServiceCommitTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-entity-commit-" + Guid.NewGuid().ToString("N"));
        AppDirectories.TestRootOverride = _root;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public void TryCommitModel_apply_persists_character()
    {
        var bundle = AdventureStore.CreateNew("Commit test");
        var model = EntityEditMapper.CreateNew("Characters", bundle.Metadata.Id)!;
        model.Name = "Scout";
        model.SecondaryValue = "Guide";
        model.Description = "Knows the paths";

        Assert.True(EntityReferenceEditService.TryCommitModel(
            bundle,
            model,
            deleted: false,
            "Characters",
            priorName: null,
            owner: null,
            callbacks: null,
            promptCanonReconcile: false));

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var character = Assert.Single(reloaded.Entities.Characters);
        Assert.Equal("Scout", character.Name);
        Assert.Equal("Guide", character.Role);
    }

    [Fact]
    public void TryCommitModel_delete_removes_character()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Delete commit");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Temp" });
        AdventureStore.Save(bundle);

        var model = EntityEditMapper.Load(bundle.Entities, id, "Characters", bundle.Metadata.Id)!;

        Assert.True(EntityReferenceEditService.TryCommitModel(
            bundle,
            model,
            deleted: true,
            "Characters",
            priorName: "Temp",
            owner: null,
            callbacks: null,
            promptCanonReconcile: false));

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Empty(reloaded.Entities.Characters);
    }

    [Fact]
    public void PrepareModel_loads_existing_character()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Prepare");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Ari" });
        AdventureStore.Save(bundle);

        var row = new EntityReferenceRow
        {
            Id = id,
            Kind = AdventurePlayEntityKind.Character,
            Name = "Ari",
        };

        var model = EntityReferenceEditService.PrepareModel(bundle, "Characters", row, isNew: false);

        Assert.NotNull(model);
        Assert.Equal("Ari", model!.Name);
    }
}
