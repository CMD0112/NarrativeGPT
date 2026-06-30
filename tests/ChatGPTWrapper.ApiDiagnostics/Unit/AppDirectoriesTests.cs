using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class AppDirectoriesTests : IClassFixture<FileLockAwareFixture>
{
    private readonly FileLockAwareFixture _fixture;

    public AppDirectoriesTests(FileLockAwareFixture fixture) => _fixture = fixture;

    [Fact]
    public void EnsureCreated_does_not_clear_locations_while_enumerating()
    {
        var adventureId = Guid.NewGuid();
        var externalDir = Path.Combine(_fixture.Root, "external-adventure");
        Directory.CreateDirectory(externalDir);
        AdventureLocationStore.Set(adventureId, externalDir);

        foreach (var _ in AdventureLocationStore.All)
            AppDirectories.EnsureCreated();

        Assert.True(AdventureLocationStore.TryGet(adventureId) is not null);
    }

    [Fact]
    public void ListIndex_survives_reentrant_EnsureCreated()
    {
        var adventureId = Guid.NewGuid();
        var externalDir = Path.Combine(_fixture.Root, "listed-adventure");
        Directory.CreateDirectory(externalDir);
        File.WriteAllText(
            Path.Combine(externalDir, "adventure.json"),
            $$"""{"id":"{{adventureId:D}}","title":"External","schemaVersion":6}""");
        AdventureLocationStore.Set(adventureId, externalDir);

        AppDirectories.EnsureCreated();
        var list = AdventureStore.ListIndex();

        Assert.Contains(list, m => m.Id == adventureId);
    }
}
