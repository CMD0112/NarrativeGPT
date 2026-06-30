using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class AiToolActionRowTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public AiToolActionRowTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-AiToolRows-" + Guid.NewGuid().ToString("N"));
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
    public void Build_without_bundle_disables_scoped_jobs_with_reason()
    {
        var rows = AiToolActionRowBuilder.Build(bundle: null, includeReview: false);

        var process = rows.Single(r => r.ActionKey == "ProcessLastExchange");
        Assert.False(process.IsEnabled);
        Assert.Contains("play turn", process.DisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_without_linked_project_disables_digest_and_continuity()
    {
        var bundle = AdventureStore.CreateNew("No project");
        AdventureStore.Save(bundle);

        var rows = AiToolActionRowBuilder.Build(bundle, includeReview: false);

        var digest = rows.Single(r => r.ActionKey == "Digest");
        var continuity = rows.Single(r => r.ActionKey == "Continuity");
        Assert.False(digest.IsEnabled);
        Assert.False(continuity.IsEnabled);
        Assert.Contains("Project", digest.DisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_excludes_review_row()
    {
        var bundle = AdventureStore.CreateNew("No review row");
        AdventureStore.Save(bundle);

        var rows = AiToolActionRowBuilder.Build(bundle);

        Assert.DoesNotContain(rows, r => r.ActionKey == "Review");
    }
}
