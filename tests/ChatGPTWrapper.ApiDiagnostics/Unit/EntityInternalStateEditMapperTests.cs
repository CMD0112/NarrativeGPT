using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityInternalStateEditMapperTests
{
    private static AdventureBundle NewBundle() =>
        new() { Metadata = new AdventureMetadata { Title = "Test" } };

    [Fact]
    public void LoadModel_includesEmotionalFieldsForNpc()
    {
        var bundle = NewBundle();
        var id = Guid.NewGuid();
        var record = EntityInternalStateService.GetOrCreate(bundle, EntityInternalStateKind.Npc, id);
        record.Character!.Emotional.Mood = "anxious";
        EntityInternalStateService.Upsert(bundle, record);

        var model = EntityInternalStateEditMapper.Load(bundle, id, EntityInternalStateKind.Npc, "Rook");

        Assert.Contains(model.FieldValues, v => v.Binding.Path == "Emotional.Mood" && v.Value == "anxious");
        Assert.Contains("anxious", model.SummaryLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_persistsHarvestedChanges()
    {
        var bundle = NewBundle();
        var id = Guid.NewGuid();
        var model = EntityInternalStateEditMapper.Load(bundle, id, EntityInternalStateKind.Npc, "Rook");
        var mood = model.FieldValues.First(v => v.Binding.Path == "Emotional.Mood");
        mood.Value = "calm";

        EntityInternalStateEditMapper.Apply(bundle, model);

        var stored = EntityInternalStateService.TryGet(bundle, EntityInternalStateKind.Npc, id);
        Assert.Equal("calm", stored!.Character!.Emotional.Mood);
    }

    [Fact]
    public void Schema_sections_includePresenceForLocation()
    {
        var sections = EntityInternalStateSchema.GetSections(EntityInternalStateKind.Location);
        Assert.Contains(sections, s => s.GroupId == "presence");
        Assert.Contains(sections, s => s.GroupId == "details");
    }
}
