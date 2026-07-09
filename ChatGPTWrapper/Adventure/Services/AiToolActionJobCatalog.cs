using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Maps play utility job action keys to generation job ids and default contexts.</summary>
internal static class AiToolActionJobCatalog
{
    public static bool TryResolve(
        AdventureBundle bundle,
        string actionKey,
        out string jobId,
        out GenerationJobContext context)
    {
        jobId = string.Empty;
        context = new GenerationJobContext();

        switch (actionKey)
        {
            case "ProcessLastExchange":
                jobId = GenerationJobId.ProcessTurn;
                context = new GenerationJobContext
                {
                    ProcessTurnIncludeMemories = true,
                    ProcessTurnIncludeEntities = true,
                    SuppressInlineGuide = true,
                };
                return true;
            case "Memories":
                jobId = GenerationJobId.ProposeMemories;
                context = new GenerationJobContext { SuppressInlineGuide = true };
                return true;
            case "ExtractEntities":
                jobId = GenerationJobId.ExtractEntities;
                context = new GenerationJobContext { SuppressInlineGuide = true };
                return true;
            case "State":
                jobId = GenerationJobId.UpdateState;
                context = new GenerationJobContext { SuppressInlineGuide = true };
                return true;
            case "Digest":
                jobId = GenerationJobId.UpdateSummary;
                return true;
            case "Continuity":
                jobId = GenerationJobId.ContinuityCheck;
                return true;
            case "EntityState":
                jobId = GenerationJobId.ProposeEntityState;
                context = new GenerationJobContext { SuppressInlineGuide = true };
                return true;
            case "CanonEvolution":
                jobId = GenerationJobId.ProposeCanonEvolution;
                context = new GenerationJobContext { SuppressInlineGuide = true };
                return true;
            default:
                return false;
        }
    }

    public static IReadOnlyList<string> ListDisplayNames(AdventureBundle bundle)
    {
        _ = bundle;
        return
        [
            "Process last exchange",
            "Memories",
            "Extract entities",
            "State update",
            "Digest",
            "Continuity",
            "Entity state",
            "Canon evolution",
        ];
    }
}
