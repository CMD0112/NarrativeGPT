using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Pre-send checks that injected play packets include narrator context and the player line.
/// </summary>
internal static class PlayInjectionSendGuard
{
    internal sealed class ValidationResult
    {
        public bool Ok { get; init; }

        public string? UserMessage { get; init; }

        public string? DiagnosticCode { get; init; }
    }

    public static ValidationResult Validate(
        AdventureBundle bundle,
        PromptInjectionPrepareResult prepared,
        bool usePrebuiltPacket)
    {
        if (usePrebuiltPacket)
            return Ok();

        if (string.IsNullOrWhiteSpace(prepared.MergedText))
        {
            return Fail(
                "empty_packet",
                "Nothing to send — the merged packet is empty. Check Play injection settings and try again.");
        }

        if (string.IsNullOrWhiteSpace(prepared.UserText))
        {
            if (prepared.HasUtilityInjection)
                return Ok();

            return Fail(
                "empty_player_line",
                "Nothing to send — enter a player line or attach a file.");
        }

        if (!ContainsPlayerLine(prepared))
        {
            return Fail(
                "user_line_not_merged",
                "The player line is missing from the merged packet. Copy the packet preview and report this if it persists.");
        }

        if (!HasInjectedContext(bundle, prepared))
        {
            if (prepared.HasUtilityInjection)
                return Ok();

            return Fail(
                "missing_context",
                "Narrator context is missing from this packet. Open Play → Injection settings, verify sources are published, "
                + "then try again. If you rotated play threads, use Start new play thread before sending.");
        }

        return Ok();
    }

    private static bool ContainsPlayerLine(PromptInjectionPrepareResult prepared)
    {
        if (string.IsNullOrWhiteSpace(prepared.UserText))
            return true;

        if (prepared.MergedText.Contains(prepared.UserText, StringComparison.Ordinal))
            return true;

        if (ConversationStreamParser.IsInjectedContextUserMessage(prepared.MergedText))
            return ConversationStreamParser.ExtractTranscriptPlayerText(prepared.MergedText) is not null;

        return false;
    }

    private static bool HasInjectedContext(AdventureBundle bundle, PromptInjectionPrepareResult prepared)
    {
        if (!string.IsNullOrWhiteSpace(prepared.ContextText))
            return true;

        if (prepared.Profile == PacketProfile.MinimalLocal)
            return prepared.MergedText.Contains("narrator", StringComparison.OrdinalIgnoreCase);

        if (bundle.Metadata.Settings.UseContextTags)
        {
            return prepared.MergedText.Contains(ContextTagFormat.TagPrefix + "meta", StringComparison.Ordinal)
                   || prepared.MergedText.Contains(ContextTagFormat.TagPrefix + "instructions", StringComparison.Ordinal)
                   || prepared.MergedText.Contains("=== PLAYER TURN ===", StringComparison.Ordinal);
        }

        return prepared.MergedText.Contains("=== ", StringComparison.Ordinal)
               || prepared.MergedText.Contains("narrator", StringComparison.OrdinalIgnoreCase);
    }

    private static ValidationResult Ok() => new() { Ok = true };

    private static ValidationResult Fail(string code, string message) =>
        new()
        {
            Ok = false,
            DiagnosticCode = code,
            UserMessage = message,
        };
}
