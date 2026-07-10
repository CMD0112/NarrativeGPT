namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

[Flags]
public enum ChatSendTransportCapabilities
{
    None = 0,
    ApiUpload = 1,
    ApiSend = 2,
    DomStaging = 4,
    DomSend = 8,
    Sentinel = 16,
}

public enum ChatFileTransportPlan
{
    DomOnly,
    ApiOnly,
    ApiWithDomFallback,
}
