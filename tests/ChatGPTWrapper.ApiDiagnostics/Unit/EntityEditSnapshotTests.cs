using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityEditSnapshotTests
{
    [Fact]
    public void EntityEditSnapshot_matches_unchanged_model()
    {
        var model = EntityEditMapper.CreateNew("Characters", Guid.NewGuid());
        model.Name = "Mara";
        model.Description = "Guide.";

        var snapshot = EntityEditSnapshot.Capture(model);
        Assert.True(snapshot.Matches(model));
    }

    [Fact]
    public void EntityEditSnapshot_detects_name_change()
    {
        var model = EntityEditMapper.CreateNew("Characters", Guid.NewGuid());
        model.Name = "Mara";
        var snapshot = EntityEditSnapshot.Capture(model);

        model.Name = "Mira";
        Assert.False(snapshot.Matches(model));
    }
}
