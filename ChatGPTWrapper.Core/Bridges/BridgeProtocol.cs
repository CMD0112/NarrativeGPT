namespace ChatGPTWrapper.Bridges;

public static class BridgeProtocol
{
    public const int Version = 1;

    public const string ChannelApi = "cgw-api";

    public const string ChannelPlay = "cgw-play";

    public const string ChannelDisplay = "cgw-display";
}

public sealed class BridgeRequest
{
    public int ProtocolVersion { get; init; } = BridgeProtocol.Version;

    public string? Channel { get; init; }

    public string? Id { get; init; }

    public string? Action { get; init; }
}

public sealed class BridgeResponse
{
    public int ProtocolVersion { get; init; } = BridgeProtocol.Version;

    public string? Channel { get; init; }

    public string? Id { get; init; }

    public string? Type { get; init; }

    public bool Ok { get; init; }

    public string? Error { get; init; }
}
