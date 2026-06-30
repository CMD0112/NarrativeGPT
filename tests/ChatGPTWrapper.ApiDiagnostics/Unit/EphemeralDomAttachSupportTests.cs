using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EphemeralDomAttachSupportTests
{
    [Fact]
    public void CanAttachOnConversationPage_requires_matching_href()
    {
        const string gizmoId = "g-p-6a220fab2eb48191a75b9d88d85a3d91";
        const string conv = "6a42c73c-53f8-83ea-99c5-8cc220e70b9d";
        var href = ChatGptUrls.BuildProjectConversationUrl(conv, gizmoId);

        Assert.True(EphemeralDomAttachSupport.CanAttachOnConversationPage(href, conv, gizmoId));
        Assert.False(EphemeralDomAttachSupport.CanAttachOnConversationPage(
            $"https://chatgpt.com/g/{gizmoId}/project",
            conv,
            gizmoId));
    }

    [Fact]
    public void RequiresConversationProvision_on_project_home_without_submit()
    {
        const string gizmoId = "g-p-test";
        var probe = new EphemeralDomAttachSupport.AttachProbe(
            PageHref: $"https://chatgpt.com/g/{gizmoId}/project",
            ComposerFound: true,
            SubmitFound: false,
            ConversationId: null);

        Assert.True(EphemeralDomAttachSupport.RequiresConversationProvision(probe, null, gizmoId));
    }

    [Fact]
    public void RequiresConversationProvision_false_on_conversation_with_submit()
    {
        const string gizmoId = "g-p-test";
        const string conv = "conv-1";
        var href = ChatGptUrls.BuildProjectConversationUrl(conv, gizmoId);
        var probe = new EphemeralDomAttachSupport.AttachProbe(href, true, true, conv);

        Assert.False(EphemeralDomAttachSupport.RequiresConversationProvision(probe, conv, gizmoId));
    }

    [Fact]
    public void ResolveFallbackConversationId_prefers_first_non_empty()
    {
        Assert.Equal("a", EphemeralDomAttachSupport.ResolveFallbackConversationId("a", "b", "c"));
        Assert.Equal("b", EphemeralDomAttachSupport.ResolveFallbackConversationId(null, "b", "c"));
        Assert.Equal("c", EphemeralDomAttachSupport.ResolveFallbackConversationId(null, null, "c"));
        Assert.Null(EphemeralDomAttachSupport.ResolveFallbackConversationId(null, null, null));
    }
}
