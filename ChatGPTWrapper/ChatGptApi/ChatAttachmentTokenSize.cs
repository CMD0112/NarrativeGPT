namespace ChatGPTWrapper.ChatGptApi;

internal static class ChatAttachmentTokenSize
{
    /// <summary>Heuristic when upload finalize omits <c>file_token_size</c> (browser ratio ~4.3 bytes/token for text).</summary>
    public static int Estimate(byte[] content, string mimeType)
    {
        if (content is not { Length: > 0 })
            return 0;

        if (IsTextLike(mimeType))
            return Math.Max(1, (int)Math.Ceiling(content.Length / 4.3));

        return Math.Max(1, content.Length / 16);
    }

    public static int Resolve(byte[] content, string mimeType, int? fromUpload) =>
        fromUpload is > 0 ? fromUpload.Value : Estimate(content, mimeType);

    private static bool IsTextLike(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mimeType.Contains("markdown", StringComparison.OrdinalIgnoreCase)
        || mimeType.Contains("json", StringComparison.OrdinalIgnoreCase);
}
