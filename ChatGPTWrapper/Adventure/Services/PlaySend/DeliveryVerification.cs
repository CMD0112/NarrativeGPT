namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal sealed record DeliveryVerification(
    bool Verified,
    string? FailureCode,
    int TurnCountDelta,
    string Channel)
{
    public static DeliveryVerification Ok(int turnCountDelta, string channel) =>
        new(true, null, turnCountDelta, channel);

    public static DeliveryVerification Failed(string code, string channel) =>
        new(false, code, 0, channel);
}
