using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>Builds worker job context from local adventure state — no play WebView required.</summary>
internal static class UtilityWorkerStoryContextProvider
{
    public static Task ApplyAsync(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (UtilityJobContextAssembler.IsEnabled(bundle, entry.Channel))
        {
            var assembly = UtilityJobContextAssembler.AssembleWorkerSoloLocalSync(
                bundle,
                entry.JobId,
                context);
            assembly.ApplyTo(context);
            return Task.CompletedTask;
        }

        var local = UtilityJobContextAssembler.AssembleWorkerSoloLocalSync(
            bundle,
            entry.JobId,
            context);
        local.ApplyTo(context);
        return Task.CompletedTask;
    }
}
