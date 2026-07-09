namespace ChatGPTWrapper.Shell;

public sealed class ShellStatusSnapshot
{
    public int ReviewCount { get; init; }

    public bool NeedsLink { get; init; }

    public bool JobActive { get; init; }

    public string BridgeSummary { get; init; } = string.Empty;

    public bool BridgeHealthy { get; init; } = true;
}
