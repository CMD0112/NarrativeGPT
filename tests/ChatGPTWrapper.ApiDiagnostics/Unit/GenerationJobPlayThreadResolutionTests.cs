using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class GenerationJobPlayThreadResolutionTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public GenerationJobPlayThreadResolutionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-GenJobPlay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void ResolvePlayConversationId_uses_thread_registry_when_legacy_field_cleared()
    {
        var bundle = AdventureStore.CreateNew("Registry play job");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        PlayThreadBindingService.MarkVerified(bundle, "6a38ac60-f3d0-83ea-a670-6e08858ba993");
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Null(reloaded.Metadata.LinkedConversationId);

        var resolved = GenerationJobService.ResolvePlayConversationId(reloaded, playCore: null);
        Assert.Equal("6a38ac60-f3d0-83ea-a670-6e08858ba993", resolved);
    }

    [Fact]
    public void BuildJobPrompt_includes_registry_play_thread_line()
    {
        var bundle = AdventureStore.CreateNew("Registry prompt line");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        PlayThreadBindingService.MarkVerified(bundle, "thread-registry-line");
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var prompt = GenerationJobHandlers.BuildJobPrompt(
            reloaded,
            GenerationJobId.UpdateSummary,
            new GenerationJobContext());

        Assert.Contains("Play thread: thread-registry-line", prompt, StringComparison.Ordinal);
    }
}
