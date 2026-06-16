using System.Diagnostics;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Reporting;

public static class SourceSyncPerfRunnerBase
{
    public static async Task RunStep(
        SourceSyncPerfReport report,
        string phase,
        string id,
        Func<Task<string>> work)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = await work();
            report.AddStep(new SourceSyncPerfStep
            {
                Id = id,
                Phase = phase,
                DurationMs = sw.ElapsedMilliseconds,
                Detail = detail,
            });
        }
        catch (Exception ex)
        {
            string? detail = ex.GetType().Name;
            string error = ex.Message;
            if (ex is ChatGptApiException apiEx)
            {
                detail = $"status={apiEx.StatusCode?.ToString() ?? "n/a"} endpoint={apiEx.Endpoint}";
                if (!string.IsNullOrWhiteSpace(apiEx.Endpoint)
                    && apiEx.Endpoint.StartsWith("/backend-api/", StringComparison.Ordinal))
                {
                    detail += $" lastPath={apiEx.Endpoint}";
                }

                var attemptsDetail = FormatDownloadAttemptsDetail(apiEx);
                if (!string.IsNullOrWhiteSpace(attemptsDetail))
                    detail += $" {attemptsDetail}";

                if (!string.IsNullOrWhiteSpace(apiEx.RawBody))
                    detail += $" body={apiEx.RawBody[..Math.Min(120, apiEx.RawBody.Length)]}";
            }

            report.AddStep(new SourceSyncPerfStep
            {
                Id = id,
                Phase = phase,
                DurationMs = sw.ElapsedMilliseconds,
                Error = error,
                Detail = detail,
            });
        }
    }

    public static Task RunStep(
        SourceSyncPerfReport report,
        string phase,
        string id,
        Func<Task> work) =>
        RunStep(report, phase, id, async () =>
        {
            await work();
            return "";
        });

    public static Task RunStep(
        SourceSyncPerfReport report,
        string phase,
        string id,
        Action work) =>
        RunStep(report, phase, id, () =>
        {
            work();
            return Task.FromResult("");
        });

    private static string? FormatDownloadAttemptsDetail(ChatGptApiException apiEx)
    {
        if (apiEx.Message.Contains("attempted=", StringComparison.Ordinal))
        {
            var attemptedIndex = apiEx.Message.IndexOf("attempted=", StringComparison.Ordinal);
            var slice = apiEx.Message[attemptedIndex..];
            var semicolon = slice.IndexOf(';', StringComparison.Ordinal);
            var attempted = semicolon >= 0 ? slice[..semicolon] : slice;
            var paths = attempted["attempted=".Length..];
            var count = string.IsNullOrWhiteSpace(paths) ? 0 : paths.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
            var truncated = paths.Length > 160 ? paths[..160] + "..." : paths;
            return $"attempts={count} paths=[{truncated}]";
        }

        if (string.IsNullOrWhiteSpace(apiEx.RawBody)
            || !apiEx.RawBody.Contains("\"attempts\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(apiEx.RawBody);
            if (!doc.RootElement.TryGetProperty("attempts", out var attempts)
                || attempts.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            var paths = new List<string>();
            foreach (var attempt in attempts.EnumerateArray())
            {
                if (attempt.TryGetProperty("path", out var path)
                    && path.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var value = path.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        paths.Add(value);
                }
            }

            if (paths.Count == 0)
                return null;

            var joined = string.Join(";", paths);
            var truncated = joined.Length > 160 ? joined[..160] + "..." : joined;
            return $"attempts={paths.Count} paths=[{truncated}]";
        }
        catch
        {
            return null;
        }
    }
}
