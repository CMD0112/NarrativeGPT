using ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class DomFileStagingCoreTests
{
    [Theory]
    [InlineData(DomFileInputTarget.Composer, "cdp-staging")]
    [InlineData(DomFileInputTarget.ProjectKnowledge, "project-knowledge")]
    public void GetStagingDirectory_includes_target_subfolder(DomFileInputTarget target, string segment)
    {
        var dir = DomFileStagingUtilities.GetStagingDirectory(target);
        Assert.Contains(segment, dir.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupStagedFiles_is_idempotent()
    {
        DomFileStagingCore.TrackStagingPaths([]);
        DomFileStagingCore.CleanupStagedFiles();
        DomFileStagingCore.CleanupStagedFiles();
        Assert.Empty(DomFileStagingCore.ActivePaths);
    }

    [Theory]
    [InlineData("notes/test.md", "test.md")]
    [InlineData(null, "attachment")]
    public void SanitizeFileName_strips_path(string? input, string expected)
    {
        Assert.Equal(expected, DomFileStagingUtilities.SanitizeFileName(input));
    }
}
