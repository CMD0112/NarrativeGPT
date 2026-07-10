namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure.Logging;

/// <summary>
/// Base for tests that run with extended diagnostics and file-lock isolation.
/// </summary>
[Collection(FileLockAwareCollectionNames.Name)]
public abstract class LoggedTestBase : IDisposable
{
    protected DiagnosticTestSession Session { get; }

    protected LoggedTestBase()
    {
        Session = DiagnosticTestSession.Enter(GetType());
    }

    protected DiagnosticTraceBundle Traces => Session.Traces;

    public void Dispose() => Session.Dispose();
}
