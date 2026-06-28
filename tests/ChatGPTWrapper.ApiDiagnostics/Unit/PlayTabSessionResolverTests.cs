using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlayTabSessionResolverTests
{
    [Fact]
    public void ResolveCapabilities_delegates_to_resolver()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-resolver",
                LinkedConversationId = "conv-1",
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "tab-pin";

        var source = ChatGptUrls.BuildProjectConversationUrl("conv-1", "g-p-resolver");
        var caps = PlayTabSessionResolver.ResolveCapabilities(
            bundle,
            webView: null,
            tabs: null,
            source);

        Assert.Equal(PlayDeliveryChannel.Api, caps.DeliveryChannel);
    }
}
