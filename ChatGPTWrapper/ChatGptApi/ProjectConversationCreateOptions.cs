using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ProjectConversationCreateOptions
{
    public Func<CoreWebView2, CancellationToken, Task<string?>>? TryUiCreate { get; init; }

    /// <summary>Skip API create paths and use UI new-chat only (retry after client-bootstrap 403).</summary>
    public bool UiCreateOnly { get; init; }
}
