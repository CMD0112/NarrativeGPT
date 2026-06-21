namespace ChatGPTWrapper.Adventure.Services.Canon;

internal enum CanonValidationSeverity
{
    Warning,
    Error,
}

internal sealed class CanonValidationIssue
{
    public required CanonValidationSeverity Severity { get; init; }

    public required string File { get; init; }

    public int? Line { get; init; }

    public required string Message { get; init; }

    public string? SectionId { get; init; }

    public override string ToString()
    {
        var location = Line is int line ? $"{File}:{line}" : File;
        return $"[{Severity}] {location}: {Message}";
    }
}
