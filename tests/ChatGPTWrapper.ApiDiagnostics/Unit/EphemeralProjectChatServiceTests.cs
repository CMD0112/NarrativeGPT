using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EphemeralProjectChatServiceTests
{
    [Fact]
    public async Task ProvisionComposerAsync_returns_missing_core_when_core_null()
    {
        var service = new EphemeralProjectChatService(null!, null!);
        var result = await service.ProvisionComposerAsync(
            new EphemeralProjectChatRequest
            {
                Core = null!,
                GizmoId = "g-p-test",
                MessageText = "hi",
            });

        Assert.False(result.Success);
        Assert.Equal(EphemeralProjectChatPhase.Create, result.FailedPhase);
        Assert.Equal("missing_core", result.Error);
    }

    [Fact]
    public async Task ProvisionComposerAsync_returns_missing_core_before_other_validation()
    {
        var service = new EphemeralProjectChatService(null!, null!);
        var result = await service.ProvisionComposerAsync(
            new EphemeralProjectChatRequest
            {
                Core = null!,
                GizmoId = "  ",
                MessageText = "hi",
            });

        Assert.False(result.Success);
        Assert.Equal("missing_core", result.Error);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("conv-1", true)]
    public void IsAcceptableCreateResult_rejects_client_bootstrapped_and_empty(string? conversationId, bool acceptable)
    {
        var result = new CreateProjectConversationResult
        {
            ConversationId = conversationId,
            ClientBootstrapped = false,
        };

        Assert.Equal(acceptable, EphemeralProjectChatService.IsAcceptableCreateResult(result));
    }

    [Fact]
    public void IsAcceptableCreateResult_rejects_client_bootstrapped_ids()
    {
        var result = new CreateProjectConversationResult
        {
            ConversationId = Guid.NewGuid().ToString(),
            ClientBootstrapped = true,
        };

        Assert.False(EphemeralProjectChatService.IsAcceptableCreateResult(result));
    }

    [Fact]
    public void IsAcceptableCreateResult_accepts_init_registered_client_ids()
    {
        var result = new CreateProjectConversationResult
        {
            ConversationId = Guid.NewGuid().ToString(),
            ClientBootstrapped = true,
            InitRegistered = true,
        };

        Assert.True(EphemeralProjectChatService.IsAcceptableCreateResult(result));
    }

    [Theory]
    [InlineData("hello", true, true)]
    [InlineData("hello", false, false)]
    [InlineData(null, true, false)]
    [InlineData("  ", true, false)]
    public void IsSettledResponse_requires_text_and_stream_complete(
        string? text,
        bool streamComplete,
        bool expected) =>
        Assert.Equal(expected, EphemeralProjectChatService.IsSettledResponse(text, streamComplete));

    [Fact]
    public void CanSendFromProjectHome_requires_project_landing_for_gizmo()
    {
        const string gizmoId = "g-p-6a220fab2eb48191a75b9d88d85a3d91";
        var home = $"https://chatgpt.com/g/{gizmoId}/project";

        Assert.True(EphemeralProjectChatService.CanSendFromProjectHome(home, gizmoId));
        Assert.False(EphemeralProjectChatService.CanSendFromProjectHome(
            ChatGptUrls.BuildProjectConversationUrl("conv-1", gizmoId),
            gizmoId));
        Assert.False(EphemeralProjectChatService.CanSendFromProjectHome(home, "g-p-other"));
    }

    [Fact]
    public void IsAcceptableCreateResult_accepts_dom_composer_ready_without_id()
    {
        var result = new CreateProjectConversationResult { DomComposerReady = true };

        Assert.True(EphemeralProjectChatService.IsAcceptableCreateResult(result));
    }

    [Fact]
    public void ShouldUseDomSend_when_dom_composer_ready_or_init_registered()
    {
        var dom = new CreateProjectConversationResult { DomComposerReady = true };
        var api = new CreateProjectConversationResult { ConversationId = "conv-1" };
        var registered = new CreateProjectConversationResult
        {
            ConversationId = Guid.NewGuid().ToString(),
            InitRegistered = true,
        };

        Assert.True(EphemeralProjectChatService.ShouldUseDomSend(dom));
        Assert.False(EphemeralProjectChatService.ShouldUseDomSend(api));
        Assert.True(EphemeralProjectChatService.ShouldUseDomSend(registered));
    }

    [Theory]
    [InlineData(100, null, 90_000)]
    [InlineData(100, 60_000, 60_000)]
    [InlineData(5000, null, 130_000)]
    public void ComputeEphemeralDomTimeoutMs_scales_short_messages(int length, int? overrideMs, int expected) =>
        Assert.Equal(expected, EphemeralProjectChatService.ComputeEphemeralDomTimeoutMs(length, overrideMs));

    [Theory]
    [InlineData("http_403", true)]
    [InlineData("missing_conduit_token", true)]
    [InlineData("http_401", false)]
    public void IsDomFallbackSendError_matches_api_registration_failures(string error, bool expected) =>
        Assert.Equal(expected, EphemeralProjectChatService.IsDomFallbackSendError(error));

    [Fact]
    public void ShouldSkipConversationNavigation_when_init_registered()
    {
        var registered = new CreateProjectConversationResult
        {
            ConversationId = "conv-1",
            InitRegistered = true,
        };
        var fromUrl = new CreateProjectConversationResult { ConversationId = "conv-2" };

        Assert.True(EphemeralProjectChatService.ShouldSkipConversationNavigation(registered));
        Assert.False(EphemeralProjectChatService.ShouldSkipConversationNavigation(fromUrl));

        var dom = new CreateProjectConversationResult { DomComposerReady = true };
        Assert.True(EphemeralProjectChatService.ShouldSkipConversationNavigation(dom));
    }

    [Theory]
    [InlineData("conversation_mismatch", true)]
    [InlineData("capture_premature", true)]
    [InlineData("http_403", false)]
    public void IsEphemeralDomRecoverableError_matches_dom_provision_outcomes(string error, bool expected) =>
        Assert.Equal(expected, EphemeralProjectChatService.IsEphemeralDomRecoverableError(error));

    [Fact]
    public void NormalizeEphemeralDomSendResult_accepts_short_assistant_text_after_dom_provision()
    {
        var raw = new ConversationSendResult
        {
            Success = false,
            Error = "capture_premature",
            ConversationId = "conv-new",
            AssistantText = "EPHEMERAL_OK",
            StreamComplete = true,
        };

        var normalized = EphemeralProjectChatService.NormalizeEphemeralDomSendResult(raw);

        Assert.True(normalized.Success);
        Assert.Equal("conv-new", normalized.ConversationId);
        Assert.Equal("EPHEMERAL_OK", normalized.AssistantText);
    }

    [Fact]
    public void BuildHideConversationBody_sets_is_visible_false()
    {
        var body = ChatGptConversationSendService.BuildHideConversationBody();
        var dict = Assert.IsType<Dictionary<string, object?>>(body);
        Assert.False(Assert.IsType<bool>(dict["is_visible"]));
    }

    [Fact]
    public void BuildRenameConversationBody_sets_title()
    {
        var body = ChatGptConversationSendService.BuildRenameConversationBody("Utility Worker Thread");
        var dict = Assert.IsType<Dictionary<string, object?>>(body);
        Assert.Equal("Utility Worker Thread", dict["title"]);
    }

    [Fact]
    public void BuildProjectSettingsPatchBody_includes_observed_fields()
    {
        var body = ChatGptProjectApiService.BuildProjectSettingsPatchBody(
            "The King in Red & Black",
            "# Instructions",
            "book",
            "#fa423e");
        var dict = Assert.IsType<Dictionary<string, object?>>(body);
        Assert.Equal("The King in Red & Black", dict["name"]);
        Assert.Equal("# Instructions", dict["instructions"]);
        Assert.Equal("book", dict["emoji"]);
        Assert.Equal("#fa423e", dict["theme"]);
    }
}
