using System.Text.Json;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure.Logging;

/// <summary>Reads and queries JSONL diagnostic traces written during a test session.</summary>
public sealed class DiagnosticTraceReader
{
    private readonly string _path;
    private IReadOnlyList<DiagnosticJsonLineRecord>? _lines;

    public DiagnosticTraceReader(string path) => _path = path;

    public string Path => _path;

    public bool Exists => File.Exists(_path);

    public IReadOnlyList<DiagnosticJsonLineRecord> Lines => _lines ??= Load();

    public DiagnosticTraceReader Reload()
    {
        _lines = null;
        return this;
    }

    public IEnumerable<DiagnosticJsonLineRecord> Events(
        string? eventName = null,
        string? channel = null,
        string? level = null)
    {
        foreach (var line in Lines)
        {
            if (eventName is not null
                && !string.Equals(line.Event, eventName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (channel is not null
                && !string.Equals(line.Channel, channel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (level is not null
                && !string.Equals(line.Level, level, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return line;
        }
    }

    public bool ContainsEvent(
        string eventName,
        string? channel = null,
        Func<DiagnosticJsonLineRecord, bool>? predicate = null)
    {
        foreach (var line in Events(eventName, channel))
        {
            if (predicate is null || predicate(line))
                return true;
        }

        return false;
    }

    public DiagnosticJsonLineRecord? LastEvent(string eventName, string? channel = null)
    {
        DiagnosticJsonLineRecord? last = null;
        foreach (var line in Events(eventName, channel))
            last = line;

        return last;
    }

    public bool ContainsEventSequence(
        IReadOnlyList<string> eventNames,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (eventNames.Count == 0)
            return true;

        var index = 0;
        foreach (var line in Lines)
        {
            if (!string.Equals(line.Event, eventNames[index], comparison))
                continue;

            index++;
            if (index >= eventNames.Count)
                return true;
        }

        return false;
    }

    public IReadOnlyList<DiagnosticJsonLineRecord> Errors() =>
        Lines.Where(l => l.IsError).ToList();

    public IReadOnlyList<DiagnosticJsonLineRecord> Warnings() =>
        Lines.Where(l => l.IsWarning).ToList();

    public string FormatExcerpt(int maxLines = 24, string? title = null)
    {
        if (!Exists)
            return $"{title ?? _path}: (file missing)";

        var lines = Lines;
        if (lines.Count == 0)
            return $"{title ?? _path}: (empty)";

        var take = Math.Min(maxLines, lines.Count);
        var body = string.Join(
            Environment.NewLine,
            lines.TakeLast(take).Select(l => l.ToString()));

        var header = title ?? _path;
        if (lines.Count > take)
            header += $" (last {take} of {lines.Count} lines)";

        return header + Environment.NewLine + body;
    }

    private IReadOnlyList<DiagnosticJsonLineRecord> Load()
    {
        if (!File.Exists(_path))
            return [];

        var results = new List<DiagnosticJsonLineRecord>();
        foreach (var raw in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                results.Add(new DiagnosticJsonLineRecord
                {
                    At = root.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var dto)
                        ? dto
                        : default,
                    SessionId = root.TryGetProperty("sessionId", out var sid)
                        ? sid.GetString() ?? ""
                        : "",
                    RunIdShort = root.TryGetProperty("runIdShort", out var runShort)
                        ? runShort.GetString()
                        : null,
                    Channel = root.TryGetProperty("channel", out var ch)
                        ? ch.GetString() ?? ""
                        : "",
                    Category = root.TryGetProperty("category", out var cat)
                        ? cat.GetString()
                        : null,
                    Level = root.TryGetProperty("level", out var lvl)
                        ? lvl.GetString()
                        : null,
                    Event = root.TryGetProperty("event", out var ev)
                        ? ev.GetString() ?? ""
                        : "",
                    Message = root.TryGetProperty("message", out var msg)
                        ? msg.GetString() ?? ""
                        : "",
                    Outcome = root.TryGetProperty("outcome", out var outcome)
                        ? outcome.GetString()
                        : null,
                    Data = root.TryGetProperty("data", out var data)
                        ? data.Clone()
                        : null,
                    RawLine = raw,
                });
            }
            catch
            {
                /* skip malformed lines */
            }
        }

        return results;
    }
}
