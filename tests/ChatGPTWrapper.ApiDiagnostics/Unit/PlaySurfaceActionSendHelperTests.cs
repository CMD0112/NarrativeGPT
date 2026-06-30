using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlaySurfaceActionSendHelperTests
{
    [Fact]
    public void ApplyInjectedOnly_prepends_continue_packet_when_compose_empty()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.PlaySurfaceActions["continue"] = "InjectedOnly";

        var merged = PlaySurfaceActionSendHelper.ApplyInjectedOnly(bundle, "");
        Assert.Contains("[[cgw:action name=\"CONTINUE\"]]", merged, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("continue narrating", merged, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyInjectedOnly_leaves_text_when_no_injected_actions()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var merged = PlaySurfaceActionSendHelper.ApplyInjectedOnly(bundle, "look around");
        Assert.Equal("look around", merged);
    }

    [Fact]
    public void AllowsEmptyComposerSend_true_when_any_injected_only_action()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.PlaySurfaceActions["continue"] = "InjectedOnly";

        Assert.True(PlaySurfaceActionSendHelper.AllowsEmptyComposerSend(bundle));
    }

    [Fact]
    public void AllowsEmptyComposerSend_false_when_only_visible_actions()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        Assert.False(PlaySurfaceActionSendHelper.AllowsEmptyComposerSend(bundle));
    }

    [Fact]
    public void ShouldShowWrapperQuickAction_continue_hidden_or_injected_only()
    {
        Assert.True(PlaySurfaceActionSendHelper.ShouldShowWrapperQuickAction("continue", "Hidden"));
        Assert.True(PlaySurfaceActionSendHelper.ShouldShowWrapperQuickAction("continue", "InjectedOnly"));
        Assert.False(PlaySurfaceActionSendHelper.ShouldShowWrapperQuickAction("continue", "Visible"));
        Assert.False(PlaySurfaceActionSendHelper.ShouldShowWrapperQuickAction("regenerate", "Hidden"));
    }
}
