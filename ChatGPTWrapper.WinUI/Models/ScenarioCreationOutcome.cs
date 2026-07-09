using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.WinUI.Models;

internal sealed class ScenarioCreationOutcome
{
    public bool Confirmed { get; init; }

    public bool RequestDesignWithAi { get; init; }

    public ScenarioDocument? Scenario { get; init; }

    public string AdventureTitle { get; init; } = string.Empty;

    public bool StartWithOpeningNarration { get; init; } = true;
}
