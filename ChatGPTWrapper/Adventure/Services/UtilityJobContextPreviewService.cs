using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>AI Actions / prompt-history preview via <see cref="UtilityJobContextAssembler"/> (CMD-397).</summary>
internal static class UtilityJobContextPreviewService
{
    public static UtilityExecutionChannel ResolvePreviewChannel(AdventureBundle bundle)
    {
        var policy = bundle.Metadata.Settings.UtilityExecutionPolicy;
        if (policy == UtilityExecutionPolicy.WorkerOnly)
            return UtilityExecutionChannel.WorkerBackground;

        if (PlayUtilityInjectionService.UsesInjectionFirst(bundle))
            return UtilityExecutionChannel.AutoBackground;

        return UtilityExecutionChannel.ManualBackground;
    }

    public static UtilityStoryContextBuildResult BuildLocal(AdventureBundle bundle, string jobId)
    {
        var channel = ResolvePreviewChannel(bundle);
        if (!UtilityJobContextAssembler.IsEnabled(bundle, channel))
            return UtilityStoryContextBuilder.BuildPreviewFromLocal(bundle, jobId);

        var assembly = channel switch
        {
            UtilityExecutionChannel.WorkerBackground =>
                UtilityJobContextAssembler.AssembleWorkerSoloLocalSync(
                    bundle,
                    jobId,
                    BuildPreviewJobContext(bundle, jobId)),
            UtilityExecutionChannel.AutoBackground =>
                BuildBundledLocalAssembly(bundle, jobId),
            _ =>
                UtilityJobContextAssembler.AssemblePlayUtilityOnlySync(bundle, jobId, channel),
        };

        return ToPreviewResult(bundle, jobId, assembly);
    }

    public static async Task<UtilityStoryContextBuildResult> BuildLiveAsync(
        AdventureBundle bundle,
        string jobId,
        CoreWebView2? playCore,
        AdventureTurnService? playTurnService,
        ChatGptConversationSendService conversationSend,
        bool domOnlyCapture,
        CancellationToken cancellationToken = default)
    {
        var channel = ResolvePreviewChannel(bundle);
        if (!UtilityJobContextAssembler.IsEnabled(bundle, channel))
        {
            var transcriptService = new PlayThreadTranscriptService(conversationSend, playTurnService);
            var builder = new UtilityStoryContextBuilder(transcriptService);
            return await builder.BuildAsync(bundle, jobId, playCore, cancellationToken, domOnlyCapture: domOnlyCapture);
        }

        if (channel == UtilityExecutionChannel.AutoBackground)
        {
            var assembly = BuildBundledLocalAssembly(bundle, jobId);
            return ToPreviewResult(bundle, jobId, assembly);
        }

        UtilityStoryContextBuilder? storyBuilder = null;
        if (playCore is not null && playTurnService is not null)
        {
            var transcriptService = new PlayThreadTranscriptService(conversationSend, playTurnService);
            storyBuilder = new UtilityStoryContextBuilder(transcriptService);
        }

        var assembler = new UtilityJobContextAssembler(storyBuilder);
        var liveAssembly = await assembler.AssembleAsync(
            bundle,
            jobId,
            new UtilityContextAssemblyRequest
            {
                Channel = channel,
                PlayCore = playCore,
            },
            cancellationToken);

        return ToPreviewResult(bundle, jobId, liveAssembly);
    }

    private static UtilityJobContextAssemblyResult BuildBundledLocalAssembly(
        AdventureBundle bundle,
        string jobId)
    {
        var context = PromptPacketBuilder.BuildContext(bundle);
        var playPacket = PromptPacketBuilder.Preview(bundle, "[preview]");
        var snapshot = PlayPacketContextSnapshotBuilder.Build(context.ContextText, playPacket);
        return UtilityJobContextAssembler.AssemblePlayBundledSync(
            bundle,
            jobId,
            UtilityExecutionChannel.AutoBackground,
            snapshot);
    }

    private static UtilityStoryContextBuildResult ToPreviewResult(
        AdventureBundle bundle,
        string jobId,
        UtilityJobContextAssemblyResult assembly)
    {
        var context = BuildPreviewJobContext(bundle, jobId);
        assembly.ApplyTo(context);
        var jobCore = GenerationJobHandlers.BuildJobPrompt(bundle, jobId, context);

        return new UtilityStoryContextBuildResult
        {
            Text = assembly.StoryContextBlock,
            TranscriptSource = assembly.TranscriptSource,
            TurnPairCount = assembly.TurnPairCount,
            Manifest = assembly.Manifest.ToRecord(),
            JobCorePreview = jobCore,
        };
    }

    private static GenerationJobContext BuildPreviewJobContext(AdventureBundle bundle, string jobId)
    {
        UtilityTranscriptScope? scope = null;
        if (jobId is GenerationJobId.ExtractEntities
            or GenerationJobId.ProposeMemories
            or GenerationJobId.ProcessTurn)
        {
            scope = UtilityTranscriptScopeService.ResolveFromLocalLog(bundle)
                    ?? UtilityTranscriptScopeService.ResolveFallbackTurn(bundle);
        }

        return new GenerationJobContext
        {
            Scope = scope,
            SuppressInlineGuide = true,
        };
    }
}
