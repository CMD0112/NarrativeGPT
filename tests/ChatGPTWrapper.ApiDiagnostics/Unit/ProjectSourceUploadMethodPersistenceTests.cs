using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class ProjectSourceUploadMethodPersistenceTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public ProjectSourceUploadMethodPersistenceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-UploadMethod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
        WrapperSettingsStore.Initialize();
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
    public void Metadata_save_persists_ProjectSourceUploadMethod()
    {
        var bundle = AdventureStore.CreateNew("Persist");
        bundle.Metadata.Settings.ProjectSourceUploadMethod = ProjectSourceUploadMethod.PureApi;
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(ProjectSourceUploadMethod.PureApi, reloaded.Metadata.Settings.ProjectSourceUploadMethod);
    }

    [Fact]
    public void New_adventure_defaults_to_HeadlessBrowser_upload_method()
    {
        var bundle = AdventureStore.CreateNew("Default");
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(ProjectSourceUploadMethod.HeadlessBrowser, reloaded.Metadata.Settings.ProjectSourceUploadMethod);
    }

    [Fact]
    public void JsonConverter_reads_legacy_external_browser_as_headless()
    {
        var json = """{"ProjectSourceUploadMethod":"ExternalBrowser"}""";
        var settings = JsonSerializer.Deserialize<AdventureSettings>(json, AdventureJson.Options)!;
        Assert.Equal(ProjectSourceUploadMethod.HeadlessBrowser, settings.ProjectSourceUploadMethod);
    }

    [Fact]
    public void JsonConverter_reads_pure_api()
    {
        var json = """{"settings":{"projectSourceUploadMethod":"PureApi"}}""";
        var meta = JsonSerializer.Deserialize<AdventureMetadata>(json, AdventureJson.Options)!;
        Assert.Equal(ProjectSourceUploadMethod.PureApi, meta.Settings.ProjectSourceUploadMethod);
    }

    [Fact]
    public void JsonConverter_reads_legacy_webview_as_headless()
    {
        var json = """{"ProjectSourceUploadMethod":"WebView2Dom"}""";
        var settings = JsonSerializer.Deserialize<AdventureSettings>(json, AdventureJson.Options)!;
        Assert.Equal(ProjectSourceUploadMethod.HeadlessBrowser, settings.ProjectSourceUploadMethod);
    }

    [Fact]
    public void JsonConverter_writes_PureApi_string()
    {
        var settings = new AdventureSettings { ProjectSourceUploadMethod = ProjectSourceUploadMethod.PureApi };
        var json = JsonSerializer.Serialize(settings, AdventureJson.Options);
        Assert.Contains("\"PureApi\"", json, StringComparison.Ordinal);
    }
}
