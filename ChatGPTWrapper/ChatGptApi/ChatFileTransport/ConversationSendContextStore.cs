using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class ConversationSendContextStore
{
    private readonly ConcurrentDictionary<string, ConversationSendContext> _entries = new();

    private static string Key(CoreWebView2 core, string conversationId) =>
        $"{core.GetHashCode()}:{conversationId.Trim()}";

    public ConversationSendContext GetOrCreate(CoreWebView2 core, string conversationId)
    {
        var key = Key(core, conversationId);
        return _entries.GetOrAdd(
            key,
            _ => new ConversationSendContext { ConversationId = conversationId.Trim() });
    }

    public bool TryGet(CoreWebView2 core, string conversationId, out ConversationSendContext? context)
    {
        context = null;
        if (string.IsNullOrWhiteSpace(conversationId))
            return false;

        return _entries.TryGetValue(Key(core, conversationId.Trim()), out context);
    }

    public void Invalidate(CoreWebView2 core, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        _entries.TryRemove(Key(core, conversationId.Trim()), out _);
        ConversationParentCache.Invalidate(conversationId.Trim());
        ConversationConduitCache.Invalidate(conversationId.Trim());
    }

    internal void ClearForTests() => _entries.Clear();
}
