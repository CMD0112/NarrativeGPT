using System.IO;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Reads wrapper-hosted adventure files verbatim for local inference lab diagnostics.
/// Does not load <see cref="AdventureBundle"/> or apply migrations — raw disk only.
/// </summary>
internal static class LocalInferenceLabAdventureContextService
{
    private const int MaxTurnChoices = 60;

    private const int MaxUtilityRuns = 40;

    private static readonly LocalInferenceLabAttachableFileSpec[] AttachableFiles =
    [
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.Entities,
            RelativePath = "entities.json",
            Label = "entities.json",
            DefaultForDiagnostic = true,
        },
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.Memory,
            RelativePath = "memory.json",
            Label = "memory.json",
            DefaultForDiagnostic = true,
        },
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.Summary,
            RelativePath = "summary.json",
            Label = "summary.json",
            DefaultForDiagnostic = true,
        },
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.State,
            RelativePath = "state.json",
            Label = "state.json",
            DefaultForDiagnostic = true,
        },
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.Cards,
            RelativePath = "cards.json",
            Label = "cards.json",
            DefaultForDiagnostic = true,
        },
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.Continuity,
            RelativePath = "continuity.json",
            Label = "continuity.json",
            DefaultForDiagnostic = false,
        },
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.LogFull,
            RelativePath = "log.json",
            Label = "log.json (full)",
            DefaultForDiagnostic = false,
        },
        new()
        {
            Id = LocalInferenceLabAdventureFileIds.AdventureMeta,
            RelativePath = "adventure.json",
            Label = "adventure.json",
            DefaultForDiagnostic = false,
        },
    ];

    public static IReadOnlyList<LocalInferenceLabAttachableFileSpec> ListAttachableFileSpecs() => AttachableFiles;

    public static IReadOnlyList<LocalInferenceLabAdventureRef> ListAdventures() =>
        AdventureStore.ListIndex()
            .Select(meta => new LocalInferenceLabAdventureRef
            {
                Id = meta.Id,
                Title = meta.Title,
                LastPlayedAt = meta.LastPlayedAt,
            })
            .ToList();

    public static IReadOnlyList<LocalInferenceLabTurnRef> ListAcceptedTurns(Guid adventureId)
    {
        var logPath = Path.Combine(AppDirectories.AdventureDirectory(adventureId), "log.json");
        if (!File.Exists(logPath))
            return [];

        try
        {
            var log = JsonSerializer.Deserialize<LogDocument>(File.ReadAllText(logPath), AdventureJson.Options);
            if (log is null)
                return [];

            return log.Turns
                .Where(t => t.Status == TurnStatus.Accepted)
                .OrderByDescending(t => t.Index)
                .Take(MaxTurnChoices)
                .Select(t => new LocalInferenceLabTurnRef
                {
                    Index = t.Index,
                    Preview = TruncatePreview(t.PlayerText),
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<LocalInferenceLabUtilityRunRef> ListUtilityRuns(
        Guid adventureId,
        string? jobIdFilter = null,
        int? turnIndexFilter = null)
    {
        var index = UtilityJobResultStore.LoadIndex(adventureId);
        var runs = new List<LocalInferenceLabUtilityRunRef>();

        foreach (var (jobId, runIds) in index.RunsByJobId)
        {
            if (!string.IsNullOrWhiteSpace(jobIdFilter)
                && !string.Equals(jobId, jobIdFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var runId in runIds.AsEnumerable().Reverse())
            {
                var record = UtilityJobResultStore.LoadRun(adventureId, runId);
                if (record is null)
                    continue;

                if (turnIndexFilter is int turnIndex
                    && record.LinkedTurnIndex != turnIndex)
                {
                    continue;
                }

                runs.Add(new LocalInferenceLabUtilityRunRef
                {
                    RunId = record.RunId,
                    JobId = record.JobId,
                    TurnIndex = record.LinkedTurnIndex,
                    Lane = string.IsNullOrWhiteSpace(record.Lane) ? "unknown" : record.Lane,
                    ProposalCount = record.ProposalCount,
                    Error = record.Error ?? record.PullError ?? record.PushError,
                    CapturedAt = record.CapturedAt,
                });
            }
        }

        return runs
            .OrderByDescending(r => r.CapturedAt)
            .Take(MaxUtilityRuns)
            .ToList();
    }

    public static string? ResolveJobFilterForScenario(string diagnosticScenarioId) =>
        diagnosticScenarioId switch
        {
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagEntityProposalsId, StringComparison.OrdinalIgnoreCase)
                => GenerationJobId.ExtractEntities,
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagMemoryProposalsId, StringComparison.OrdinalIgnoreCase)
                => GenerationJobId.ProposeMemories,
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagSummaryProposalId, StringComparison.OrdinalIgnoreCase)
                => GenerationJobId.UpdateSummary,
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagProcessTurnBundleId, StringComparison.OrdinalIgnoreCase)
                => GenerationJobId.ProcessTurn,
            _ => null,
        };

    public static LocalInferenceLabAdventureAttachments? TryLoadAttachments(
        Guid adventureId,
        IReadOnlySet<string> selectedFileIds,
        Guid? utilityRunId,
        int? turnIndexForSlice)
    {
        var directory = AppDirectories.AdventureDirectory(adventureId);
        if (!Directory.Exists(directory))
            return null;

        var title = ResolveAdventureTitle(adventureId, directory);
        var attachments = new List<LocalInferenceLabFileAttachment>();

        foreach (var spec in AttachableFiles)
        {
            if (!selectedFileIds.Contains(spec.Id))
                continue;

            var content = TryReadFileText(directory, spec.RelativePath);
            if (content is null)
                continue;

            attachments.Add(ToAttachment(spec.RelativePath, content));
        }

        if (turnIndexForSlice is int turnIndex)
        {
            var slice = TryReadLogTurnSlice(directory, turnIndex);
            if (slice is not null)
                attachments.Add(slice);
        }

        string? jobId = null;
        if (utilityRunId is Guid runId)
        {
            var relativePath = $"utility-results/{runId}.json";
            var runPath = Path.Combine(directory, relativePath);
            if (File.Exists(runPath))
            {
                var content = File.ReadAllText(runPath);
                attachments.Add(ToAttachment(relativePath.Replace('\\', '/'), content));
                jobId = TryReadUtilityRunJobId(content);
            }
        }

        if (attachments.Count == 0)
            return null;

        return new LocalInferenceLabAdventureAttachments
        {
            AdventureId = adventureId,
            AdventureTitle = title,
            DirectoryPath = directory,
            JobId = jobId,
            UtilityRunId = utilityRunId,
            TurnIndex = turnIndexForSlice,
            Files = attachments,
        };
    }

    private static string ResolveAdventureTitle(Guid adventureId, string directory)
    {
        var fromIndex = ListAdventures().FirstOrDefault(a => a.Id == adventureId)?.Title;
        if (!string.IsNullOrWhiteSpace(fromIndex))
            return fromIndex!;

        var metaPath = Path.Combine(directory, "adventure.json");
        if (!File.Exists(metaPath))
            return adventureId.ToString("N");

        try
        {
            var meta = JsonSerializer.Deserialize<AdventureMetadata>(File.ReadAllText(metaPath), AdventureJson.Options);
            return string.IsNullOrWhiteSpace(meta?.Title) ? adventureId.ToString("N") : meta.Title;
        }
        catch (JsonException)
        {
            return adventureId.ToString("N");
        }
    }

    private static string? TryReadFileText(string adventureDirectory, string relativePath)
    {
        var path = Path.Combine(adventureDirectory, relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static LocalInferenceLabFileAttachment? TryReadLogTurnSlice(string adventureDirectory, int turnIndex)
    {
        var logPath = Path.Combine(adventureDirectory, "log.json");
        if (!File.Exists(logPath))
            return null;

        try
        {
            var log = JsonSerializer.Deserialize<LogDocument>(File.ReadAllText(logPath), AdventureJson.Options);
            var turn = log?.Turns.FirstOrDefault(t => t.Index == turnIndex);
            if (turn is null)
                return null;

            var json = JsonSerializer.Serialize(turn, AdventureJson.Options);
            return ToAttachment($"log.json#turn/{turnIndex}", json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LocalInferenceLabFileAttachment ToAttachment(string relativePath, string content)
    {
        var bytes = Encoding.UTF8.GetByteCount(content);
        return new LocalInferenceLabFileAttachment
        {
            RelativePath = relativePath,
            Content = content,
            ByteLength = bytes,
        };
    }

    private static string? TryReadUtilityRunJobId(string utilityRunJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(utilityRunJson);
            return doc.RootElement.TryGetProperty("jobId", out var jobId) && jobId.ValueKind == JsonValueKind.String
                ? jobId.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string TruncatePreview(string text)
    {
        var trimmed = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length <= 72 ? trimmed : trimmed[..72].TrimEnd() + "…";
    }
}
