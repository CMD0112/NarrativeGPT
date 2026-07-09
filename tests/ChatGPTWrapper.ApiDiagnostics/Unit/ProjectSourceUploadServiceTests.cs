using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi.ProjectSource;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProjectSourceUploadServiceTests
{
    [Theory]
    [InlineData("scenario.md", "text/markdown")]
    [InlineData("world.MD", "text/markdown")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("data.json", "application/json")]
    public void ResolveMimeType_maps_markdown_and_binary(string path, string expectedMime)
    {
        Assert.Equal(expectedMime, ProjectSourceUploadService.ResolveMimeType(path));
    }

    [Theory]
    [InlineData("scenario.md", "scenario.md")]
    [InlineData("subdir/world.md", "subdir/world.md")]
    [InlineData(@"\plot.md", "plot.md")]
    public void NormalizeRemoteFileName_accepts_simple_paths(string input, string expected)
    {
        Assert.Equal(expected, ProjectSourceUploadService.NormalizeRemoteFileName(input));
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("foo/../bar.md")]
    [InlineData("")]
    public void NormalizeRemoteFileName_rejects_unsafe_names(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => ProjectSourceUploadService.NormalizeRemoteFileName(input));
    }

    [Theory]
    [InlineData(ProjectSourceBindingStrategy.SnorlaxProjectFilesApi, false)]
    [InlineData(ProjectSourceBindingStrategy.SnorlaxDetailUpsert, true)]
    [InlineData(ProjectSourceBindingStrategy.SnorlaxLibraryEscalation, false)]
    [InlineData(ProjectSourceBindingStrategy.LegacyUpsert, false)]
    public void BindingStrategy_UsedUpsertFallback_flags_detail_upsert_only(
        ProjectSourceBindingStrategy strategy,
        bool expected)
    {
        Assert.Equal(expected, strategy.UsedUpsertFallback());
    }

    [Theory]
    [InlineData(false, ProjectSourceBindingStrategy.SnorlaxProjectFilesApi)]
    [InlineData(true, ProjectSourceBindingStrategy.SnorlaxDetailUpsert)]
    public void ResolveSnorlaxSyncAttachStrategy_maps_sync_attach_ladder(
        bool usedDetailUpsertFallback,
        ProjectSourceBindingStrategy expected)
    {
        Assert.Equal(
            expected,
            ProjectSourceBindingOrchestrator.ResolveSnorlaxSyncAttachStrategy(usedDetailUpsertFallback));
    }
}
