using System.Windows;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper;

internal static class ClipboardCopy
{
    public static bool TrySetText(string text, string logContext = "ClipboardCopy")
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log($"{logContext} attempt {attempt + 1} failed: {ex.Message}");
                Thread.Sleep(100);
            }
        }

        return false;
    }
}
