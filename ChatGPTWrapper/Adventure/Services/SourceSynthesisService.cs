using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class SourceSynthesisService
{
    public static string BuildSynthesizeToFilePrompt(
        AdventureBundle bundle,
        string targetRelativePath,
        string utilityJobId,
        string parsedContent)
    {
        return $"""
            === SOURCE SYNTHESIS JOB ===
            Merge the following utility output into the existing project source file `{targetRelativePath}`.
            Preserve structure and headings where possible. Output the full merged file only.

            === UTILITY JOB ===
            {utilityJobId}

            === PARSED CONTENT ===
            {parsedContent}

            === CURRENT ADVENTURE SUMMARY ===
            {bundle.Summary.RollingSummary}
            """;
    }

    public static bool WriteSynthesizedFile(AdventureBundle bundle, string targetRelativePath, string content) =>
        AdventureSourceFileService.TryWrite(bundle, targetRelativePath, content, "synthesis");
}
