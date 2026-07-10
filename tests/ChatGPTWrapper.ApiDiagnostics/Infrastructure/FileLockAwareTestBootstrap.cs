using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure;

/// <summary>
/// Shared setup for tests that manage their own isolated root via <see cref="FileLockAwareTestScope"/>.
/// </summary>
public static class FileLockAwareTestBootstrap
{
    public static string Enter(Type testClass)
    {
        using var _ = FileLockGate.AcquireAppData(testClass.Name + ".bootstrap");
        AppDirectories.ResetStoresForTests();
        var root = AssemblyTestEnvironment.CreateClassRoot(testClass.Name);
        Directory.CreateDirectory(root);
        AppDirectories.TestRootOverride = root;
        AppDirectories.EnsureCreated();
        return root;
    }

    public static void Exit(string root)
    {
        using var _ = FileLockGate.AcquireAppData("bootstrap-exit");
        AppDirectories.TestRootOverride = null;
        AppDirectories.ResetStoresForTests();
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
