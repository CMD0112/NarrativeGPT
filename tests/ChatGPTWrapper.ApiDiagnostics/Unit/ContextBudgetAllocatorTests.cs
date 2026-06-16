using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ContextBudgetAllocatorTests
{
    [Fact]
    public void ApplyBudget_downgrades_inline_before_dropping_pointers()
    {
        var pointers = new List<ContextPointer>
        {
            new()
            {
                MachineId = "cast.md#npcs/mara",
                FileName = "cast.md",
                SectionId = "npcs/mara",
                Title = "Mara",
                Kind = "person",
                Score = 50,
                Source = PointerSource.NameMatch,
                Mode = RenderMode.InlineFull,
                BodyCache = new string('x', 400),
            },
            new()
            {
                MachineId = "world.md#locations/dock",
                FileName = "world.md",
                SectionId = "locations/dock",
                Title = "Dock",
                Kind = "place",
                Score = 30,
                Source = PointerSource.NameMatch,
                Mode = RenderMode.InlineFull,
                BodyCache = "Harbor district.",
            },
        };

        ContextBudgetAllocator.ApplyBudget(pointers, budgetChars: 120, fatFallback: false);

        Assert.Contains(pointers, p => p.MachineId == "cast.md#npcs/mara");
        Assert.True(pointers.All(p => p.Mode != RenderMode.InlineFull));
    }
}
