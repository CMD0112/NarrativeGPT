using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure.Logging;

/// <summary>Turns on production-equivalent extended diagnostics for a test scope.</summary>
public static class DiagnosticTestBootstrap
{
    public static bool ExtendedDiagnosticsEnabled { get; private set; }

    public static void Start(string? testLabel = null, bool? forceExtended = null)
    {
        var extended = forceExtended
                       ?? ParseTruthy(Environment.GetEnvironmentVariable("CGW_TEST_EXTENDED_DIAGNOSTICS"))
                       ?? true;

        DiagnosticsOptions.ResetForTests();
        DiagnosticsLog.ResetSessionCountsForTests();

        if (extended)
        {
            DiagnosticsOptions.Initialize(["--extended-diagnostics"]);
            DiagnosticsSession.WriteExtendedHeader(
            [
                "--extended-diagnostics",
                testLabel is null ? "--test-run" : $"--test-label={testLabel}",
            ]);
        }
        else
        {
            DiagnosticsOptions.Initialize([]);
        }

        ExtendedDiagnosticsEnabled = extended;
    }

    public static void End()
    {
        if (ExtendedDiagnosticsEnabled)
            DiagnosticsSession.WriteExtendedShutdown();

        DiagnosticsOptions.ResetForTests();
        ExtendedDiagnosticsEnabled = false;
    }

    private static bool? ParseTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
