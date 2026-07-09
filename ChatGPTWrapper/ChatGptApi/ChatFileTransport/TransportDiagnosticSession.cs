namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class TransportDiagnosticSession
{
    private readonly List<TransportDiagnosticStep> _steps = [];

    public IReadOnlyList<TransportDiagnosticStep> Steps => _steps;

    public void Record(string id, string lane, string transport, bool pass, string detail, string? classification = null)
    {
        _steps.Add(new TransportDiagnosticStep
        {
            Id = id,
            Lane = lane,
            Transport = transport,
            Pass = pass,
            Detail = detail,
            Classification = classification ?? (pass ? "pass" : "fail"),
            At = DateTimeOffset.UtcNow,
        });
    }

    public void RecordGap(string lane, string conversationId, string summary) =>
        Record("attach_gap", lane, "api", pass: true, detail: summary, classification: "gap-diagnosis");

    public string ToPlaySendTraceDetail() =>
        string.Join("; ", _steps.Select(s => $"{s.Id}={s.Classification}:{s.Detail}"));
}

public sealed class TransportDiagnosticStep
{
    public required string Id { get; init; }

    public required string Lane { get; init; }

    public required string Transport { get; init; }

    public bool Pass { get; init; }

    public required string Detail { get; init; }

    public required string Classification { get; init; }

    public DateTimeOffset At { get; init; }
}
