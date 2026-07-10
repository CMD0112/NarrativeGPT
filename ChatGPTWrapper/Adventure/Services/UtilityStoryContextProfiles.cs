using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class UtilityStoryContextProfiles
{
    public static UtilityStoryContextSettings ApplyJobProfile(UtilityStoryContextSettings baseSettings, string jobId)
    {
        var s = baseSettings.Clone();

        switch (jobId)
        {
            case GenerationJobId.ProcessTurn:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 2, fallback: 1);
                s.IncludeRollingSummary = false;
                s.IncludeEntityIndex = false;
                s.IncludePinnedMemory = false;
                s.Format = UtilityTranscriptFormat.CompactArrow;
                s.OmitRedundantJobTurnSlices = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 8_000);
                s.LookbackAnchor = UtilityLookbackAnchor.FromEnd;
                break;

            case GenerationJobId.ProposeMemories:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 1, fallback: 1);
                s.IncludeRollingSummary = false;
                s.IncludeEntityIndex = false;
                s.IncludePinnedMemory = false;
                s.Format = UtilityTranscriptFormat.CompactArrow;
                s.OmitRedundantJobTurnSlices = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 8_000);
                s.LookbackAnchor = UtilityLookbackAnchor.FromEnd;
                break;

            case GenerationJobId.ExtractEntities:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 1, fallback: 1);
                s.IncludeRollingSummary = false;
                s.IncludeEntityIndex = true;
                s.Format = UtilityTranscriptFormat.CompactArrow;
                s.OmitRedundantJobTurnSlices = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 8_000);
                s.LookbackAnchor = UtilityLookbackAnchor.FromEnd;
                break;

            case GenerationJobId.ExpandEntity:
                s.MaxTurnPairs = 0;
                s.IncludeRollingSummary = false;
                s.IncludeEntityIndex = false;
                s.Format = UtilityTranscriptFormat.CompactArrow;
                s.OmitRedundantJobTurnSlices = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 4_000);
                s.LookbackAnchor = UtilityLookbackAnchor.FromEnd;
                break;

            case GenerationJobId.UpdateState:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 2, fallback: 1);
                s.IncludeRollingSummary = false;
                s.IncludeEntityIndex = true;
                s.IncludeState = false;
                s.Format = UtilityTranscriptFormat.CompactArrow;
                s.OmitRedundantJobTurnSlices = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 8_000);
                s.LookbackAnchor = UtilityLookbackAnchor.FromEnd;
                break;

            case GenerationJobId.UpdateSummary:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 8, fallback: 6);
                s.IncludeRollingSummary = true;
                s.IncludeEntityIndex = false;
                s.MaxContextChars = Cap(s.MaxContextChars, 12_000);
                break;

            case GenerationJobId.BootstrapLore:
                s.MaxTurnPairs = 0;
                s.IncludeScenarioExcerpt = true;
                s.IncludeRollingSummary = false;
                s.MaxContextChars = Cap(s.MaxContextChars, 8_000);
                break;

            case GenerationJobId.ContinuityCheck:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 8, fallback: 8);
                s.IncludeRollingSummary = true;
                s.IncludeEntityIndex = true;
                s.IncludeState = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 16_000);
                break;

            case GenerationJobId.ProposeEntityState:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 2, fallback: 1);
                s.IncludeRollingSummary = false;
                s.IncludeEntityIndex = true;
                s.IncludeState = false;
                s.Format = UtilityTranscriptFormat.CompactArrow;
                s.OmitRedundantJobTurnSlices = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 10_000);
                s.LookbackAnchor = UtilityLookbackAnchor.FromEnd;
                break;

            case GenerationJobId.ProposeCanonEvolution:
                s.MaxTurnPairs = ClampMax(s.MaxTurnPairs, defaultMax: 3, fallback: 2);
                s.IncludeRollingSummary = true;
                s.IncludeEntityIndex = true;
                s.IncludeState = false;
                s.Format = UtilityTranscriptFormat.CompactArrow;
                s.OmitRedundantJobTurnSlices = true;
                s.MaxContextChars = Cap(s.MaxContextChars, 12_000);
                s.LookbackAnchor = UtilityLookbackAnchor.FromEnd;
                break;
        }

        return UtilityStoryContextSettingsNormalizer.Normalize(s);
    }

    private static int ClampMax(int current, int defaultMax, int fallback)
    {
        if (current <= 0)
            return fallback;

        return Math.Min(current, defaultMax);
    }

    private static int Cap(int current, int cap) =>
        current <= 0 ? cap : Math.Min(current, cap);
}
