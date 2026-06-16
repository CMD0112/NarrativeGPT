namespace ChatGPTWrapper.ChatGptApi;

public sealed class TranscriptTurnPair
{
    public string PlayerText { get; init; } = "";

    public string NarratorText { get; init; } = "";

    public int? TurnIndex { get; init; }
}
