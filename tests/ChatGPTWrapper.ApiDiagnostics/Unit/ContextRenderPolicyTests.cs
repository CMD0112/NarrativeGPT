using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ContextRenderPolicyTests
{
    [Fact]
    public void PickRenderMode_inline_for_high_score_person()
    {
        var pointer = new ContextPointer
        {
            MachineId = "cast.md#npcs/mara",
            FileName = "cast.md",
            SectionId = "npcs/mara",
            Title = "Mara",
            Kind = "person",
            Score = 50,
            Source = PointerSource.NameMatch,
            BodyCache = "Description",
        };

        Assert.Equal(RenderMode.InlineFull, ContextRenderPolicy.PickRenderMode(pointer, fatFallback: false));
    }

    [Fact]
    public void ExtractFlavor_parses_flavor_line()
    {
        const string body = "Facts here.\n\n> Flavor: Copper and sage.";
        Assert.Equal("Copper and sage.", ContextRenderPolicy.ExtractFlavor(body));
    }

    [Fact]
    public void PickRenderMode_reference_file_never_inlines_even_in_fat_fallback()
    {
        var pointer = new ContextPointer
        {
            MachineId = "narrator-scales.md#narration-scales",
            FileName = SectionSchema.NarratorScalesFile,
            SectionId = "narration-scales",
            Title = "narration scales",
            Kind = "reference",
            Score = 100,
            Source = PointerSource.NameMatch,
            BodyCache = new string('x', 4000),
        };

        Assert.Equal(RenderMode.PointerOnly, ContextRenderPolicy.PickRenderMode(pointer, fatFallback: true));
    }

    [Fact]
    public void Baseline_always_pointer_only()
    {
        var pointer = new ContextPointer
        {
            MachineId = "scenario.md#opening",
            FileName = "scenario.md",
            SectionId = "opening",
            Title = "Opening",
            Kind = "scenario",
            Score = 100,
            Source = PointerSource.Baseline,
            BodyCache = "Opening",
        };

        Assert.Equal(RenderMode.PointerOnly, ContextRenderPolicy.PickRenderMode(pointer, fatFallback: false));
    }
}
