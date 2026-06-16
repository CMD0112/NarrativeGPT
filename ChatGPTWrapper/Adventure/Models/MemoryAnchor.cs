namespace ChatGPTWrapper.Adventure.Models;

public sealed class MemoryAnchor
{
    public string Kind { get; set; } = "transcript";

    public int PairOffset { get; set; }

    public string? PlayerHint { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? TurnIndex { get; set; }

    public string? ContentHash { get; set; }
}
