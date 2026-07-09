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
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        PlayThreadBindingService.MarkVerified(bundle, "conv-1");
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "tab-pin";

        var source = ChatGptUrls.BuildProjectConversationUrl("conv-1", "g-p-resolver");
        var ctx = PlayTabCapabilityContext.FromUrl(bundle, source, candidateTabKey: "tab-pin");
        var session = PlayTabSessionFactory.FromBundle(bundle);
        var caps = PlayTabCapabilityResolver.Resolve(ctx, session);

        Assert.Equal(PlayDeliveryChannel.Api, caps.DeliveryChannel);
    }
}
