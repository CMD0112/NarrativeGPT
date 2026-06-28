using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal static class PreparedSendSettingsFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Hash of adventure settings and turn overrides that affect <see cref="PromptInjectionService.PrepareSend"/>.
    /// </summary>
    public static string Compute(AdventureBundle bundle)
    {
        PlayInjectionPolicyService.EnsureDefaults(bundle.Metadata);

        var settings = bundle.Metadata.Settings;
        var payload = new FingerprintPayload
        {
            MaxPacketChars = settings.MaxPacketChars,
            UseContextTags = settings.UseContextTags,
            UseSectionInjection = settings.UseSectionInjection,
            PreferDomPlaySend = settings.PreferDomPlaySend,
            PlayTurnOverridesJson = JsonSerializer.Serialize(settings.PlayTurnOverrides, JsonOptions),
            InjectionPolicyJson = JsonSerializer.Serialize(settings.InjectionPolicy, JsonOptions),
            LinkedProjectId = bundle.Metadata.LinkedProjectId,
            LinkedConversationId = PlayThreadBindingService.GetActiveConversationId(bundle),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return PromptPacketBuilder.ComputeHash(json);
    }

    private sealed class FingerprintPayload
    {
        public int MaxPacketChars { get; init; }

        public bool UseContextTags { get; init; }

        public bool UseSectionInjection { get; init; }

        public bool PreferDomPlaySend { get; init; }

        public string PlayTurnOverridesJson { get; init; } = "";

        public string InjectionPolicyJson { get; init; } = "";

        public string? LinkedProjectId { get; init; }

        public string? LinkedConversationId { get; init; }
    }
}
