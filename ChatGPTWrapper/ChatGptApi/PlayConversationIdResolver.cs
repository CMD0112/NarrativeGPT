using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

internal static class PlayConversationIdResolver
{
    public static string? Resolve(AdventureBundle bundle, CoreWebView2 core)
    {
        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                core.Source,
                bundle.Metadata.LinkedProjectId,
                out var projectConversationId))
        {
            return projectConversationId;
        }

        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.TryParseConversationId(uri, out var parsed)
            && !string.IsNullOrWhiteSpace(parsed))
        {
            return parsed;
        }

        return PlayThreadBindingService.GetActiveConversationId(bundle);
    }
}
