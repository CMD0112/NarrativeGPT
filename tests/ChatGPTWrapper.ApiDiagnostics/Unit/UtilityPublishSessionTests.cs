using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityPublishSessionTests : IDisposable
{
    private readonly string _tempRoot;

    public UtilityPublishSessionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-utility-publish-" + Guid.NewGuid().ToString("N"));
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void BuildPublishPlan_extract_entities_includes_entities_and_scenario_when_present()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        var adventureDir = AppDirectories.AdventureDirectory(bundle.Metadata.Id);
        Directory.CreateDirectory(adventureDir);
        File.WriteAllText(Path.Combine(adventureDir, "entities.json"), "{}");
        File.WriteAllText(Path.Combine(adventureDir, "scenario.json"), "{}");

        var runId = Guid.NewGuid();
        var plan = UtilityPublishSession.BuildPublishPlan(
            bundle,
            GenerationJobId.ExtractEntities,
            runId);

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, p => p.FileName == "entities.json");
        Assert.Contains(plan, p => p.FileName == "scenario.json");
        Assert.All(plan, p => Assert.Contains("extract-entities", p.RemotePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsPublishComplete_true_when_registry_has_all_paths()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        var adventureDir = AppDirectories.AdventureDirectory(bundle.Metadata.Id);
        Directory.CreateDirectory(adventureDir);
        File.WriteAllText(Path.Combine(adventureDir, "entities.json"), "{}");

        var runId = Guid.NewGuid();
        var remotePath = EntityExtractionService.BuildCanonicalInputRemotePath(
            bundle,
            GenerationJobId.ExpandEntity,
            runId,
            "entities.json");

        UtilitySourceFileLifecycleService.RegisterPublishedFile(
            bundle.Metadata.Id,
            GenerationJobId.ExpandEntity,
            runId,
            remotePath,
            "file_test123",
            UtilitySourceFileIoService.ComputeContentSha256("{}"u8.ToArray()));

        Assert.True(UtilityPublishSession.IsPublishComplete(
            bundle.Metadata.Id,
            runId,
            GenerationJobId.ExpandEntity,
            bundle));
    }

    [Fact]
    public void Registry_TryFindVerified_matches_sha256()
    {
        var adventureId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var remotePath = UtilitySourceFileNaming.BuildInputRemotePath(
            adventureId,
            GenerationJobId.ExtractEntities,
            runId,
            "entities.json");
        const string sha = "abc123";

        UtilitySourceFileLifecycleService.RegisterPublishedFile(
            adventureId,
            GenerationJobId.ExtractEntities,
            runId,
            remotePath,
            "file_abc",
            sha);

        Assert.NotNull(UtilitySourceFileRegistryStore.TryFindVerified(adventureId, runId, remotePath, sha));
        Assert.Null(UtilitySourceFileRegistryStore.TryFindVerified(adventureId, runId, remotePath, "different"));
    }

    [Fact]
    public void ComputeContentSha256_is_lowercase_hex()
    {
        var hash = UtilitySourceFileIoService.ComputeContentSha256("hello"u8.ToArray());
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }
}
