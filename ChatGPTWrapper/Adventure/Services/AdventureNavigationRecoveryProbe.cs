using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureNavigationRecoveryProbe
{
    private const string AccessDeniedScript = """
        (() => {
          const text = (document.body?.innerText || '').toLowerCase();
          if (!text) return false;
          return text.includes("don't have access")
              || text.includes('do not have access')
              || text.includes("don't have access to this")
              || text.includes('do not have access to this')
              || text.includes("you don't have permission")
              || text.includes('content is not available')
              || text.includes('page not found')
              || text.includes('unable to load');
        })()
        """;

    public static async Task<bool> ShowsAccessDeniedAsync(CoreWebView2 core)
    {
        try
        {
            var raw = await core.ExecuteScriptAsync(AccessDeniedScript);
            return raw.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> RequiresRecoveryAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        AdventureNavigationIntent intent)
    {
        if (!AdventureNavigationService.HasLinkedProject(bundle))
            return false;

        var source = core.Source;
        if (AdventureNavigationService.RequiresNavigationRecovery(source, bundle))
            return true;

        if (AdventureNavigationService.IsOnValidAdventureWebTarget(source, bundle, intent)
            && !await ShowsAccessDeniedAsync(core))
        {
            return false;
        }

        return await ShowsAccessDeniedAsync(core);
    }
}
