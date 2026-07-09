namespace ChatGPTWrapper.ChatGptApi;

public sealed class SentinelPrefetchResult
{
    public bool Ok { get; init; }

    public string? Source { get; init; }

    public string? Error { get; init; }

    public string? Stage { get; init; }

    public string? Detail { get; init; }

    public int? FinalizeStatus { get; init; }

    public string Summary =>
        Ok
            ? $"sentinel_ok=True sentinel_source={Source ?? "none"}"
            : $"sentinel_ok=False sentinel_source={Source ?? "none"}"
              + (Stage is not null ? $" sentinel_stage={Stage}" : "")
              + (Detail is not null ? $" sentinel_detail={Detail}" : "")
              + (Error is not null ? $" sentinel_error={Error}" : "");
}
