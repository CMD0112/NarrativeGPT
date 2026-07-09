using ChatGPTWrapper;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure;

/// <summary>
/// Per test-class isolated appdata root under the assembly run folder, serialized via <see cref="FileLockGate"/>.
/// </summary>
public sealed class FileLockAwareTestScope : IDisposable
{
    private readonly IDisposable _appDataLock;
    private readonly string _root;
    private bool _disposed;

    private FileLockAwareTestScope(Type testClass)
    {
        _appDataLock = FileLockGate.AcquireAppData(testClass.Name);
        _root = AssemblyTestEnvironment.CreateClassRoot(testClass.Name);
        Directory.CreateDirectory(_root);

        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _root;
        DiagnosticsPaths.TestRootOverride = _root;
        AppDirectories.EnsureCreated();
        WpfDiagnosticsHost.Register();
    }

    public string Root => _root;

    public static FileLockAwareTestScope Enter(Type testClass) => new(testClass);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        AppDirectories.TestRootOverride = null;
        DiagnosticsPaths.TestRootOverride = null;
        AppDirectories.ResetStoresForTests();

        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* best effort */
        }

        _appDataLock.Dispose();
    }
}
