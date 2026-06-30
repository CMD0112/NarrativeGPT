using System.Text.Json;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure.Logging;

/// <summary>Parsed JSONL line from wrapper diagnostics or legacy channel logs.</summary>
public sealed class DiagnosticJsonLineRecord
{
    public DateTimeOffset At { get; init; }

    public string SessionId { get; init; } = "";

    public string? RunIdShort { get; init; }

    public string Channel { get; init; } = "";

    public string? Category { get; init; }

    public string? Level { get; init; }

    public string Event { get; init; } = "";

    public string Message { get; init; } = "";

    public string? Outcome { get; init; }

    public JsonElement? Data { get; init; }

    public string RawLine { get; init; } = "";

    public bool IsError =>
        string.Equals(Level, "error", StringComparison.OrdinalIgnoreCase);

    public bool IsWarning =>
        string.Equals(Level, "warn", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Level, "warning", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"[{Channel}/{Level}] {Event}: {Message}";
}
