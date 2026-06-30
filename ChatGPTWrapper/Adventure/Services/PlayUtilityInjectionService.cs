using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Queues and builds injection-first utility sections for play packets (CMD-328).</summary>
internal static class PlayUtilityInjectionService
{
    public const string UtilityOnlyPlayerMarker = "[CGW utility request]";

    public static bool UsesInjectionFirst(AdventureBundle bundle) =>
        bundle.Metadata.Settings.PlayUtilityInjectionMode == PlayUtilityInjectionMode.InjectionFirst
        && UtilityDeliveryModeService.UsesInlineDelivery(bundle);

    public static void EnqueueAfterTurn(AdventureBundle bundle, TurnRecord turn, IEnumerable<string> jobIds)
    {
        if (!UsesInjectionFirst(bundle))
            return;

        bundle.Metadata.PlayUtilityInjectionQueue ??= [];
        var max = ResolveMaxSections(bundle);
        var remaining = Math.Max(0, max - bundle.Metadata.PlayUtilityInjectionQueue.Count);
        var spill = UtilityJobRouter.ShouldSpillAutoToWorker(bundle);
        var spillList = new List<string>();
        var dualRun = LocalUtilityInferencePolicy.IsDualRun(bundle);

        foreach (var jobId in jobIds)
        {
            if (dualRun && LocalUtilityInferencePolicy.SupportsJob(jobId))
            {
                UtilityOutboxService.Enqueue(
                    bundle,
                    jobId,
                    UtilityExecutionChannel.AutoBackground,
                    new GenerationJobContext { Turn = turn });
                continue;
            }

            if (remaining > 0)
            {
                if (IsAlreadyQueued(bundle, jobId))
                    continue;

                bundle.Metadata.PlayUtilityInjectionQueue.Add(new PendingUtilityInjection
                {
                    RunId = Guid.NewGuid(),
                    JobId = jobId,
                    Channel = UtilityExecutionChannel.AutoBackground,
                    LinkedTurnId = turn.Id,
                    TurnIndex = turn.Index,
                    QueuedAt = DateTimeOffset.UtcNow,
                });
                remaining--;
                continue;
            }

            if (spill)
                spillList.Add(jobId);
        }

        foreach (var jobId in spillList)
        {
            UtilityOutboxService.Enqueue(
                bundle,
                jobId,
                UtilityExecutionChannel.WorkerBackground,
                new GenerationJobContext { Turn = turn });
        }
    }

    public static PendingUtilityInjection CreateManualPending(string jobId, GenerationJobContext context) =>
        new()
        {
            JobId = jobId,
            Channel = UtilityExecutionChannel.ManualBackground,
            LinkedTurnId = context.Turn?.Id,
            TurnIndex = context.Turn?.Index,
            EntityId = context.EntityId,
            EntityKind = context.EntityKind,
            CardId = context.CardId,
            QueuedAt = DateTimeOffset.UtcNow,
        };

    public static IReadOnlyList<string> BuildAndDrainUtilitySections(
        AdventureBundle bundle,
        GenerationJobContext? turnContext = null,
        PlayPacketContextSnapshot? playSnapshot = null)
    {
        var queue = bundle.Metadata.PlayUtilityInjectionQueue ?? [];
        if (queue.Count == 0)
            return [];

        var max = ResolveMaxSections(bundle);
        var take = queue.Take(max).ToList();
        bundle.Metadata.LastDispatchedUtilityJobs = take;
        bundle.Metadata.PlayUtilityInjectionQueue = queue.Skip(take.Count).ToList();

        return take
            .Select(pending => BuildUtilitySection(bundle, pending, turnContext, playSnapshot))
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .ToList();
    }

    public static string BuildUtilitySection(
        AdventureBundle bundle,
        PendingUtilityInjection pending,
        GenerationJobContext? extraContext = null,
        PlayPacketContextSnapshot? playSnapshot = null)
    {
        var context = BuildJobContext(bundle, pending, extraContext);
        ApplyContextAssembly(bundle, pending, context, playSnapshot);
        if (context.UtilityContextManifest is { } manifest)
            pending.ContextManifest = manifest.ToRecord();

        var jobBody = GenerationJobHandlers.BuildJobPrompt(bundle, pending.JobId, context);
        jobBody = UtilityResponseSchemaRegistry.AppendResponseContract(jobBody, pending.JobId);
        context.SuppressInlineGuide = true;

        return ContextTagFormat.WrapUtilityJob(
            pending.JobId,
            jobBody,
            ChannelToAttr(pending.Channel),
            pending.RunId);
    }

    public static string BuildUtilityOnlyPacket(
        AdventureBundle bundle,
        PendingUtilityInjection pending,
        PlayPacketContextSnapshot? playSnapshot = null)
    {
        var section = BuildUtilitySection(bundle, pending, playSnapshot: playSnapshot);
        return string.IsNullOrWhiteSpace(section) ? "" : section;
    }

    public static string PrependUtilitySections(string mergedPacket, IReadOnlyList<string> utilitySections)
    {
        if (utilitySections.Count == 0)
            return mergedPacket;

        var utilityBlock = string.Join(Environment.NewLine + Environment.NewLine, utilitySections);
        return string.IsNullOrWhiteSpace(mergedPacket)
            ? utilityBlock
            : utilityBlock + Environment.NewLine + Environment.NewLine + mergedPacket;
    }

    public static bool IsUtilityOnlyPacket(PromptInjectionPrepareResult prepared) =>
        prepared.HasUtilityInjection
        && string.Equals(prepared.UserText, UtilityOnlyPlayerMarker, StringComparison.Ordinal);

    private static GenerationJobContext BuildJobContext(
        AdventureBundle bundle,
        PendingUtilityInjection pending,
        GenerationJobContext? extraContext)
    {
        TurnRecord? turn = null;
        if (pending.LinkedTurnId is { } turnId)
            turn = bundle.Log.Turns.FirstOrDefault(t => t.Id == turnId);
        turn ??= extraContext?.Turn;

        UtilityTranscriptScope? scope = null;
        if (pending.JobId is GenerationJobId.ExtractEntities
            or GenerationJobId.ProposeMemories
            or GenerationJobId.ProcessTurn)
        {
            scope = UtilityTranscriptScopeService.ResolveFromLocalLog(bundle)
                    ?? UtilityTranscriptScopeService.ResolveFallbackTurn(bundle);
        }

        return new GenerationJobContext
        {
            Turn = turn,
            Scope = scope ?? extraContext?.Scope,
            EntityId = pending.EntityId ?? extraContext?.EntityId,
            EntityKind = pending.EntityKind ?? extraContext?.EntityKind,
            CardId = pending.CardId ?? extraContext?.CardId,
            UserPrompt = extraContext?.UserPrompt,
            ProcessTurnIncludeMemories = extraContext?.ProcessTurnIncludeMemories ?? true,
            ProcessTurnIncludeEntities = extraContext?.ProcessTurnIncludeEntities ?? true,
            ProcessTurnIncludeSummary = extraContext?.ProcessTurnIncludeSummary ?? false,
            SuppressInlineGuide = true,
            DesignStep = extraContext?.DesignStep,
        };
    }

    private static void ApplyContextAssembly(
        AdventureBundle bundle,
        PendingUtilityInjection pending,
        GenerationJobContext context,
        PlayPacketContextSnapshot? playSnapshot)
    {
        if (!UtilityJobContextAssembler.IsEnabled(bundle, pending.Channel))
        {
            ApplyReferenceFirstDefaults(bundle, context);
            return;
        }

        var assembly = pending.Channel == UtilityExecutionChannel.AutoBackground && playSnapshot is not null
            ? UtilityJobContextAssembler.AssemblePlayBundledSync(
                bundle,
                pending.JobId,
                pending.Channel,
                playSnapshot)
            : UtilityJobContextAssembler.AssemblePlayUtilityOnlySync(
                bundle,
                pending.JobId,
                pending.Channel);

        assembly.ApplyTo(context);
    }

    private static void ApplyReferenceFirstDefaults(AdventureBundle bundle, GenerationJobContext context)
    {
        var hasPlayThreadTurns = !string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(bundle))
                                 && bundle.Log.Turns.Any(t => t.Status == TurnStatus.Accepted);
        context.OmitRedundantJobTurnSlices = hasPlayThreadTurns;
        context.StoryContextHasTranscript = hasPlayThreadTurns;
        context.StoryContextIncludesSummary =
            !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary);
        context.StoryContextIncludesState =
            EntityExtractionService.BuildWorldSnapshot(bundle, includeSummary: false) != "(none)";
    }

    private static int ResolveMaxSections(AdventureBundle bundle)
    {
        var max = bundle.Metadata.Settings.MaxUtilitySectionsPerSend;
        return max <= 0 ? 2 : max;
    }

    private static bool IsAlreadyQueued(AdventureBundle bundle, string jobId) =>
        bundle.Metadata.PlayUtilityInjectionQueue?.Any(p =>
            string.Equals(p.JobId, jobId, StringComparison.OrdinalIgnoreCase)) == true;

    private static string ChannelToAttr(UtilityExecutionChannel channel) =>
        channel switch
        {
            UtilityExecutionChannel.AutoBackground => "auto",
            UtilityExecutionChannel.ManualBackground => "manual",
            UtilityExecutionChannel.WorkerBackground => "worker",
            _ => "manual",
        };
}
