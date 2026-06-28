using ChatGPTWrapper;

namespace ChatGPTWrapper.Format;

public static class FormatSettingsSanity
{
    public static IReadOnlyList<string> GetWarnings(ContinuousViewFormatSettings format) =>
        FormatReadabilityAnalyzer.GetWarningMessages(format);
}
