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

    [Fact]
    public void ApplyBudget_records_trimmed_pointers()
    {
        var pointers = new List<ContextPointer>
        {
            new()
            {
                MachineId = "opening",
                FileName = "scenario.md",
                SectionId = "opening",
                Title = "Opening",
                Kind = "scenario",
                Score = 100,
                Source = PointerSource.Baseline,
                Mode = RenderMode.PointerOnly,
            },
            new()
            {
                MachineId = "cast.md#npcs/mara",
                FileName = "cast.md",
                SectionId = "npcs/mara",
                Title = "Mara",
                Kind = "person",
                Score = 20,
                Source = PointerSource.NameMatch,
                Mode = RenderMode.InlineFull,
                BodyCache = new string('x', 500),
            },
        };

        var result = ContextBudgetAllocator.ApplyBudget(pointers, budgetChars: 80, fatFallback: false);

        Assert.DoesNotContain(pointers, p => p.MachineId == "cast.md#npcs/mara");
        Assert.Contains(result.Trimmed, t => t.Id == "cast.md#npcs/mara");
        Assert.Contains(pointers, p => p.MachineId == "opening");
    }

    [Fact]
    public void ApplyBudget_preserves_baseline_under_aggressive_budget()
    {
        var pointers = new List<ContextPointer>
        {
            new()
            {
                MachineId = "rules",
                FileName = "world.md",
                SectionId = "rules",
                Title = "Rules",
                Kind = "rule",
                Score = 100,
                Source = PointerSource.Baseline,
                Mode = RenderMode.PointerOnly,
            },
            new()
            {
                MachineId = "cast.md#npcs/low",
                FileName = "cast.md",
                SectionId = "npcs/low",
                Title = "Low",
                Kind = "person",
                Score = 10,
                Source = PointerSource.NameMatch,
                Mode = RenderMode.InlineFull,
                BodyCache = new string('y', 300),
            },
        };

        var result = ContextBudgetAllocator.ApplyBudget(pointers, budgetChars: 50, fatFallback: false);

        Assert.Single(pointers);
        Assert.Equal("rules", pointers[0].MachineId);
        Assert.NotEmpty(result.Trimmed);
    }
}
