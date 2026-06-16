using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProjectSourceProbeServiceTests
{
    [Theory]
    [InlineData("abc", "abc", RemoteProbeMatch.Match)]
    [InlineData("abc", "def", RemoteProbeMatch.Differ)]
    [InlineData("abc", null, RemoteProbeMatch.NotDownloadable)]
    [InlineData(null, "abc", RemoteProbeMatch.Differ)]
    public void ClassifyMatch_handles_hash_pairs(string? local, string? remote, RemoteProbeMatch expected)
    {
        var match = ProjectSourceProbeService.ClassifyMatch(local, remote, hasRemote: true);
        Assert.Equal(expected, match);
    }

    [Fact]
    public void ClassifyMatch_missing_on_project_when_no_remote()
    {
        var match = ProjectSourceProbeService.ClassifyMatch("abc", null, hasRemote: false);
        Assert.Equal(RemoteProbeMatch.MissingOnProject, match);
    }

    [Fact]
    public void FormatProbeMatch_returns_human_labels()
    {
        Assert.Equal("Match", ProjectSourceProbeService.FormatProbeMatch(RemoteProbeMatch.Match));
        Assert.Equal("Differ", ProjectSourceProbeService.FormatProbeMatch(RemoteProbeMatch.Differ));
        Assert.Equal("Missing", ProjectSourceProbeService.FormatProbeMatch(RemoteProbeMatch.MissingOnProject));
    }
}
