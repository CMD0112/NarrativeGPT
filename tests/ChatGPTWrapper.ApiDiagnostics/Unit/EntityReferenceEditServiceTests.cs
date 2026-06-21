using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityReferenceEditServiceTests : IDisposable
{
    private readonly string _root;

    public EntityReferenceEditServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-entity-ref-" + Guid.NewGuid().ToString("N"));
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
    public void TryTogglePin_flips_character_and_persists()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Pin test");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Scout", Pinned = false });
        AdventureStore.Save(bundle);

        var row = new EntityReferenceRow
        {
            Id = id,
            Kind = AdventurePlayEntityKind.Character,
            Name = "Scout",
        };

        Assert.True(EntityReferenceEditService.TryTogglePin(bundle, row));
        Assert.True(bundle.Entities.Characters.Single().Pinned);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Entities.Characters.Single().Pinned);
    }

    [Fact]
    public void TryTogglePin_returns_false_for_quest()
    {
        var bundle = AdventureStore.CreateNew("Quest pin");
        var row = new EntityReferenceRow
        {
            Id = Guid.NewGuid(),
            Kind = AdventurePlayEntityKind.Quest,
            Name = "Find the key",
        };

        Assert.False(EntityReferenceEditService.TryTogglePin(bundle, row));
    }
}
