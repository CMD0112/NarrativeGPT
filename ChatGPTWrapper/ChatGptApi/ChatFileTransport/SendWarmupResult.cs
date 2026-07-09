namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class SendWarmupResult
{
    public bool ParentReady { get; init; }

    public bool ConduitReady { get; init; }

    public bool BridgeWarm { get; init; }

    public SentinelPrefetchResult? Sentinel { get; init; }

    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                $"parent_ready={ParentReady}",
                $"conduit_ready={ConduitReady}",
                $"bridge_warm={BridgeWarm}",
            };
            if (Sentinel is not null)
                parts.Add(Sentinel.Summary);
            return string.Join(" ", parts);
        }
    }
}
