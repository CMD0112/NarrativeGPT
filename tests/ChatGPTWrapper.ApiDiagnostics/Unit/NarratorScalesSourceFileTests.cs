using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class NarratorScalesSourceFileTests
{
    [Fact]
    public void EnsureLayout_creates_narrator_scales_file()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");

        AdventureSourceFileService.EnsureLayout(bundle);

        var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.NarratorScalesFile);
        Assert.True(File.Exists(path));
        Assert.Contains("## narration-scales", File.ReadAllText(path));
    }

    [Fact]
    public void ReferenceSourceFiles_includes_narrator_scales()
    {
        Assert.Contains(SectionSchema.NarratorScalesFile, SectionSchema.ReferenceSourceFiles);
    }
}
