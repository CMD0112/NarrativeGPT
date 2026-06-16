using ChatGPTWrapper;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[CollectionDefinition(nameof(IsolatedAppRootCollection), DisableParallelization = true)]
public sealed class IsolatedAppRootCollection : ICollectionFixture<IsolatedAppRootFixture>;

public sealed class IsolatedAppRootFixture : IDisposable
{
    public IsolatedAppRootFixture()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "ChatGPTWrapper-History-" + Guid.NewGuid().ToString("N"));
        AppDirectories.TestRootOverride = Root;
        AppDirectories.EnsureCreated();
    }

    public string Root { get; }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
