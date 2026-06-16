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
    }

    [Fact]
    public void ApplyInjectedOnly_leaves_text_when_no_injected_actions()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var merged = PlaySurfaceActionSendHelper.ApplyInjectedOnly(bundle, "look around");
        Assert.Equal("look around", merged);
    }
}
