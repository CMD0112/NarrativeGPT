using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure.Logging;

/// <summary>
/// File-lock isolated appdata root with extended diagnostics logging — primary entry for logged tests.
/// </summary>
public sealed class DiagnosticTestSession : IDisposable
{
    private static readonly AsyncLocal<DiagnosticTestSession?> CurrentScope = new();

    private readonly FileLockAwareTestScope _fileScope;
    private bool _disposed;
    private bool _preserveArtifacts;

    private DiagnosticTestSession(Type testClass, bool extendedDiagnostics)
    {
        TestClass = testClass;
        _fileScope = FileLockAwareTestScope.Enter(testClass);
        DiagnosticTestBootstrap.Start(testClass.Name, extendedDiagnostics);
        Traces = new DiagnosticTraceBundle(_fileScope.Root);
        CurrentScope.Value = this;
    }

    public Type TestClass { get; }

    public string Root => _fileScope.Root;

    public DiagnosticTraceBundle Traces { get; }

    public static DiagnosticTestSession? Current => CurrentScope.Value;

    public static DiagnosticTestSession Enter(
        Type testClass,
        bool extendedDiagnostics = true) =>
        new(testClass, extendedDiagnostics);

    public void MarkPreserveArtifactsOnDispose() => _preserveArtifacts = true;

    public void ReloadTraces() => Traces.ReloadAll();

    public void AssertCleanTraces(bool warningsToo = false)
    {
        ReloadTraces();
        if (warningsToo)
        {
            Traces.Unified.NoWarningsOrErrors();
            Traces.PlaySend.NoWarningsOrErrors();
        }
        else
        {
            Traces.Unified.NoErrors();
            Traces.PlaySend.NoErrors();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_preserveArtifacts || ParsePreserveEnv())
                PreserveArtifacts();
        }
        finally
        {
            DiagnosticTestBootstrap.End();
            _fileScope.Dispose();
            if (ReferenceEquals(CurrentScope.Value, this))
                CurrentScope.Value = null;
        }
    }

    private void PreserveArtifacts()
    {
        try
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var dest = Path.Combine(
                Path.GetTempPath(),
                "cgw-test-artifacts",
                TestClass.Name,
                stamp);
            Directory.CreateDirectory(dest);

            CopyIfExists(DiagnosticsLog.UnifiedTracePath, dest);
            CopyIfExists(Traces.PlaySend.Path, dest);
            CopyIfExists(Traces.Sync.Path, dest);

            var marker = Path.Combine(dest, "_manifest.txt");
            File.WriteAllText(
                marker,
                $"testClass={TestClass.FullName}{Environment.NewLine}"
                + $"sessionId={DiagnosticsLog.SessionId}{Environment.NewLine}"
                + $"root={Root}{Environment.NewLine}"
                + $"preservedAt={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        }
        catch
        {
            /* best effort */
        }
    }

    private static void CopyIfExists(string source, string destDir)
    {
        if (!File.Exists(source))
            return;

        File.Copy(source, Path.Combine(destDir, Path.GetFileName(source)), overwrite: true);
    }

    private static bool ParsePreserveEnv()
    {
        var value = Environment.GetEnvironmentVariable("CGW_TEST_PRESERVE_LOGS");
        return !string.IsNullOrWhiteSpace(value)
               && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
