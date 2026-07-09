using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class ProjectSourceUploadMethodResolverTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public ProjectSourceUploadMethodResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-UploadResolver-" + Guid.NewGuid().ToString("N"));
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
    public void Resolve_prefers_ui_selection_over_bundle_and_wrapper()
    {
        WrapperSettingsStore.Save(new WrapperSettings
        {
            PublicationLabDomUploadMethod = ProjectSourceUploadMethod.HeadlessBrowser,
        });

        var bundle = AdventureStore.CreateNew("Resolver");
        bundle.Metadata.Settings.ProjectSourceUploadMethod = ProjectSourceUploadMethod.PureApi;
        AdventureStore.Save(bundle);

        Assert.Equal(
            ProjectSourceUploadMethod.HeadlessBrowser,
            ProjectSourceUploadMethodResolver.Resolve(bundle, ProjectSourceUploadMethod.HeadlessBrowser));
    }

    [Fact]
    public void Resolve_uses_bundle_when_no_ui_selection()
    {
        WrapperSettingsStore.Save(new WrapperSettings
        {
            PublicationLabDomUploadMethod = ProjectSourceUploadMethod.HeadlessBrowser,
        });

        var bundle = AdventureStore.CreateNew("Resolver");
        bundle.Metadata.Settings.ProjectSourceUploadMethod = ProjectSourceUploadMethod.PureApi;
        AdventureStore.Save(bundle);

        Assert.Equal(
            ProjectSourceUploadMethod.PureApi,
            ProjectSourceUploadMethodResolver.Resolve(bundle));
    }

    [Fact]
    public void PersistSelection_writes_bundle_and_wrapper()
    {
        var bundle = AdventureStore.CreateNew("Persist");
        bundle.Metadata.Settings.ProjectSourceUploadMethod = ProjectSourceUploadMethod.HeadlessBrowser;
        AdventureStore.Save(bundle);

        ProjectSourceUploadMethodResolver.PersistSelection(bundle, ProjectSourceUploadMethod.PureApi);

        Assert.Equal(
            ProjectSourceUploadMethod.PureApi,
            WrapperSettingsStore.Current.PublicationLabDomUploadMethod);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(
            ProjectSourceUploadMethod.PureApi,
            reloaded.Metadata.Settings.ProjectSourceUploadMethod);
    }
}
