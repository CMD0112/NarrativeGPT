namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure.Logging;

/// <summary>Assertions over diagnostic JSONL traces with log excerpts on failure.</summary>
public static class DiagnosticTraceAssert
{
    public static void ContainsEvent(
        this DiagnosticTraceReader reader,
        string eventName,
        string? channel = null,
        string? because = null)
    {
        if (reader.ContainsEvent(eventName, channel))
            return;

        var reason = string.IsNullOrWhiteSpace(because) ? "" : $" {because}";
        throw new Xunit.Sdk.XunitException(
            $"Expected event '{eventName}'"
            + (channel is null ? "" : $" on channel '{channel}'")
            + $" in {reader.Path}.{reason}"
            + Environment.NewLine + Environment.NewLine
            + reader.FormatExcerpt());
    }

    public static void DoesNotContainEvent(
        this DiagnosticTraceReader reader,
        string eventName,
        string? channel = null)
    {
        if (!reader.ContainsEvent(eventName, channel))
            return;

        throw new Xunit.Sdk.XunitException(
            $"Did not expect event '{eventName}'"
            + (channel is null ? "" : $" on channel '{channel}'")
            + $" in {reader.Path}."
            + Environment.NewLine + Environment.NewLine
            + reader.FormatExcerpt());
    }

    public static void Sequence(
        this DiagnosticTraceReader reader,
        params string[] eventNames)
    {
        if (reader.ContainsEventSequence(eventNames))
            return;

        throw new Xunit.Sdk.XunitException(
            $"Expected event sequence [{string.Join(" → ", eventNames)}] in {reader.Path}."
            + Environment.NewLine + Environment.NewLine
            + reader.FormatExcerpt());
    }

    public static void NoErrors(this DiagnosticTraceReader reader)
    {
        var errors = reader.Errors();
        if (errors.Count == 0)
            return;

        throw new Xunit.Sdk.XunitException(
            $"Expected no error-level events in {reader.Path}, found {errors.Count}."
            + Environment.NewLine + Environment.NewLine
            + reader.FormatExcerpt());
    }

    public static void NoWarningsOrErrors(this DiagnosticTraceReader reader)
    {
        var faults = reader.Errors().Concat(reader.Warnings()).ToList();
        if (faults.Count == 0)
            return;

        throw new Xunit.Sdk.XunitException(
            $"Expected no warn/error events in {reader.Path}, found {faults.Count}."
            + Environment.NewLine + Environment.NewLine
            + reader.FormatExcerpt());
    }
}
