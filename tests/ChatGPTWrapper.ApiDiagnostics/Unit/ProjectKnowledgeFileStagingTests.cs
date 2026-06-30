using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProjectKnowledgeFileStagingTests
{
    [Theory]
    [InlineData("scenario.md", "scenario.md")]
    [InlineData("subdir/world.md", "world.md")]
    [InlineData(@"notes\cast.md", "cast.md")]
    public void SanitizeFileName_strips_path(string input, string expected)
    {
        Assert.Equal(expected, ProjectKnowledgeFileStaging.SanitizeFileName(input));
    }

    [Theory]
    [InlineData("test.md", "test.md", true)]
    [InlineData("subdir/test.md", "TEST.MD", true)]
    [InlineData("other.md", "test.md", false)]
    public void RemoteFileMatchesName_compares_basenames(
        string remoteName,
        string fileName,
        bool expected)
    {
        var file = new GizmoFileRef { FileId = "f1", Name = fileName };
        Assert.Equal(expected, ProjectKnowledgeFileStaging.RemoteFileMatchesName(file, remoteName));
    }

    [Theory]
    [InlineData(ProjectSourceBindingStrategy.SnorlaxDomEscalation, false)]
    public void BindingStrategy_dom_escalation_is_not_upsert_fallback(
        ProjectSourceBindingStrategy strategy,
        bool expected)
    {
        Assert.Equal(expected, strategy.UsedUpsertFallback());
    }
}
