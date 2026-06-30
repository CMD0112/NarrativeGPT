using System.Text;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Inlines UTF-8 reference file bodies into utility job packets (API text lane).
/// </summary>
internal static class UtilityAttachmentTextInlining
{
    private const int MaxDisplayCharsPerFile = 120_000;

    public static string AppendInlineContents(string jobBody, IReadOnlyList<DomAttachmentPayload> attachments)
    {
        if (attachments is not { Count: > 0 })
            return jobBody;

        var sections = new List<string> { jobBody.TrimEnd() };

        foreach (var attachment in attachments)
        {
            var name = string.IsNullOrWhiteSpace(attachment.Name) ? "attachment" : attachment.Name;
            var text = DecodeText(attachment.Content);
            if (text.Length > MaxDisplayCharsPerFile)
            {
                text = text[..MaxDisplayCharsPerFile]
                       + $"\n\n[... truncated at {MaxDisplayCharsPerFile} characters ...]";
            }

            var fence = text.Contains("```", StringComparison.Ordinal) ? "````" : "```";
            sections.Add($"=== FILE: {name} ===\n{fence}\n{text}\n{fence}");
        }

        return string.Join("\n\n", sections);
    }

    public static bool IsMostlyText(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            return true;

        var nullCount = 0;
        foreach (var b in content)
        {
            if (b == 0)
                nullCount++;
        }

        if (nullCount > 0)
            return false;

        try
        {
            var decoded = Encoding.UTF8.GetString(content);
            var replacementCount = decoded.Count(c => c == '\uFFFD');
            return replacementCount <= Math.Max(4, content.Length / 500);
        }
        catch
        {
            return false;
        }
    }

    private static string DecodeText(byte[] content) =>
        Encoding.UTF8.GetString(content);
}
