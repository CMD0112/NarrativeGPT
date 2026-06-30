using ChatGPTWrapper;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure;

[CollectionDefinition(FileLockAwareCollectionNames.Name, DisableParallelization = true)]
public sealed class FileLockAwareCollection;

public static class FileLockAwareCollectionNames
{
    public const string Name = "FileLockAware";
}

/// <summary>
/// Per-class isolated appdata root with extended diagnostics enabled.
/// </summary>
public sealed class FileLockAwareFixture : IDisposable
{
    private readonly IDisposable _appDataLock;
    private readonly string _testLabel;

    public FileLockAwareFixture()
    {
        _testLabel = nameof(FileLockAwareFixture);
        _appDataLock = FileLockGate.AcquireAppData(_testLabel);
        Root = AssemblyTestEnvironment.CreateClassRoot(_testLabel);
        Directory.CreateDirectory(Root);
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = Root;
        AppDirectories.EnsureCreated();
        Logging.DiagnosticTestBootstrap.Start(_testLabel);
        Traces = new Logging.DiagnosticTraceBundle(Root);
    }

    public string Root { get; }

    public Logging.DiagnosticTraceBundle Traces { get; }

    public void Dispose()
    {
        Logging.DiagnosticTestBootstrap.End();
        AppDirectories.TestRootOverride = null;
        AppDirectories.ResetStoresForTests();

        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            /* best effort */
        }

        _appDataLock.Dispose();
    }
}
