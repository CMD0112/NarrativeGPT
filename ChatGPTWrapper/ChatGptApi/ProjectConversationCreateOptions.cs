using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ProjectConversationCreateOptions
{
    public Func<CoreWebView2, CancellationToken, Task<string?>>? TryUiCreate { get; init; }

    /// <summary>Skip API create paths and use UI new-chat only (retry after client-bootstrap 403).</summary>
    public bool UiCreateOnly { get; init; }

    /// <summary>Skip legacy POST /backend-api/conversations (often 405).</summary>
    public bool SkipLegacyApiCreate { get; init; }

    /// <summary>Do not return client-generated conversation ids.</summary>
    public bool SkipClientBootstrap { get; init; }

    public static ProjectConversationCreateOptions ForPlayProvision => new()
    {
        SkipLegacyApiCreate = true,
        SkipClientBootstrap = true,
    };
}
