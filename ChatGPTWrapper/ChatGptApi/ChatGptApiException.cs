namespace ChatGPTWrapper.ChatGptApi;

public sealed class ChatGptApiException : Exception
{
    public ChatGptApiException(string message, string endpoint, int? statusCode = null, string? rawBody = null)
        : base(message)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        RawBody = rawBody;
    }

    public string Endpoint { get; }

    public int? StatusCode { get; }

    public string? RawBody { get; }
}
