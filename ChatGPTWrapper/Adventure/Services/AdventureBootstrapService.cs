using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureBootstrapService
{
    public static int AcceptedTurnCount(AdventureBundle bundle) =>
        PlayTurnScopeService.GetPacketAcceptedTurns(bundle).Count;

    public static bool IsFreshAdventure(AdventureBundle bundle) =>
        PlayTurnScopeService.IsFreshPlayThread(bundle);

    public static string GetOpeningPlayerLine(ScenarioDocument scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.OpeningSituation))
            return scenario.OpeningSituation.Trim();

        if (!string.IsNullOrWhiteSpace(scenario.Setting))
            return $"The story begins. {scenario.Setting.Trim()}";

        return "Begin the adventure.";
    }

    public static string BuildStartPacket(AdventureBundle bundle)
    {
        var opening = GetOpeningPlayerLine(bundle.Scenario);
        var prompt = PromptPacketBuilder.UseThinPackets(bundle)
            ? "Begin the adventure using the Project scenario source. Open with vivid narration. Do not ask the player questions yet — set the stage.\n\n" +
              $"Opening hook: {opening}"
            : "Begin the adventure. Open with vivid narration that establishes the scene and situation described below. Do not ask the player questions yet — set the stage.\n\n" +
              $"Opening hook: {opening}";

        return PromptPacketBuilder.Build(bundle, prompt).Text;
    }
}
