using ChatGPTWrapper.ApiDiagnostics.Infrastructure;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public static class IsolatedAppRootCollection
{
    public const string Name = FileLockAwareCollectionNames.Name;
}

[Obsolete("Use FileLockAwareFixture.")]
public sealed class IsolatedAppRootFixture : IDisposable
{
    private readonly FileLockAwareFixture _inner = new();

    public string Root => _inner.Root;

    public void Dispose() => _inner.Dispose();
}
