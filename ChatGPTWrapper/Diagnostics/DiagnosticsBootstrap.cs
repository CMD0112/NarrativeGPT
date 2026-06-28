using System.Text.Json;

namespace ChatGPTWrapper.Diagnostics;

internal static class DiagnosticsBootstrap
{
    public static string GetScript()
    {
        var extended = DiagnosticsOptions.Extended ? "true" : "false";
        var ui = DiagnosticsOptions.LogUiEvents ? "true" : "false";
        var sessionId = JsonSerializer.Serialize(DiagnosticsLog.SessionId);
        return $"""
            globalThis.__cgwExtendedDiagnostics={extended};
            globalThis.__cgwLogUiEvents={ui};
            globalThis.__cgwDiagnosticsSessionId={sessionId};
            """;
    }
}
