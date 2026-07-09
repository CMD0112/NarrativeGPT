using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilitySourceFileNamingTests
{
    [Fact]
    public void BuildInputRemotePath_uses_canonical_segments()
    {
        var adventureId = Guid.Parse("4e8faadf-e4af-403d-9686-ede4870f6acf");
        var runId = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        var path = UtilitySourceFileNaming.BuildInputRemotePath(
            adventureId,
            GenerationJobId.ProposeEntitiesFile,
            runId,
            "entities.json");

        Assert.Equal(
            "sources/cgw-utility-io/4e8faadf/propose-entities-file/6ba7b8109dad/in/entities.json",
            path);
        Assert.True(UtilitySourceFileNaming.IsCanonicalPath(path));
    }

    [Fact]
    public void TryParse_round_trips_canonical_path()
    {
        const string path = "sources/cgw-utility-io/4e8faadf/propose-entities-file/6ba7b8109dad/in/entities.json";
        Assert.True(UtilitySourceFileNaming.TryParse(path, out var parts));
        Assert.Equal("4e8faadf", parts.AdventureKey);
        Assert.Equal("propose-entities-file", parts.JobKey);
        Assert.Equal("6ba7b8109dad", parts.RunKey);
        Assert.Equal("entities.json", parts.FileName);
    }

    [Fact]
    public void BuildDiagnosticInputRemotePath_uses_diag_adventure_key()
    {
        var path = UtilitySourceFileNaming.BuildDiagnosticInputRemotePath(
            "source-io-e2e",
            "abc123",
            "diagnostic.md");

        Assert.StartsWith("sources/cgw-utility-io/diag/source-io-e2e/abc123/in/", path, StringComparison.Ordinal);
    }
}
