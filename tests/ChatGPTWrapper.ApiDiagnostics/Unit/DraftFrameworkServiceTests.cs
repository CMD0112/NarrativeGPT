using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class DraftFrameworkServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public DraftFrameworkServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-DraftFramework-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        AppDirectories.ResetStoresForTests();
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
    public void WriteDraftToSources_creates_framework_file_under_drafts()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        AdventureStore.Save(bundle);

        var path = DraftFrameworkService.WriteDraftToSources(bundle, "# Framework\n\nOpening hooks.");
        Assert.StartsWith("drafts/framework-", path, StringComparison.Ordinal);
        Assert.EndsWith(".md", path, StringComparison.Ordinal);

        var text = DraftFrameworkService.TryReadRelative(bundle, path);
        Assert.Contains("Opening hooks", text);
    }
}
