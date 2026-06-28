using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayPacketPrepareSessionTests
{
    [Fact]
    public void Prepare_matches_direct_PrepareSend_for_same_inputs()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-session", inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;

        const string playerLine = "look around the hall";
        var direct = PromptInjectionService.PrepareSend(bundle, playerLine, priorThreadUserMessageCount: 3);
        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                ConsumeContinuationQueue = false,
                ApplySurfaceActions = false,
                PriorThreadUserMessageCount = 3,
            },
            (_, _, _) => playerLine);

        Assert.Equal(direct.MergedText, session.Prepared.MergedText);
        Assert.Equal(direct.Hash, session.Prepared.Hash);
        Assert.Equal(playerLine, session.PlayerLine);
    }

    [Fact]
    public void Prepare_applies_surface_actions_when_requested()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-actions", inSync: true);
        bundle.Metadata.Settings.PlaySurfaceActions["continue"] = "InjectedOnly";

        var without = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = "hello",
                ApplySurfaceActions = false,
            },
            (_, _, text) => text ?? "");

        var withActions = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = "",
                ApplySurfaceActions = true,
            },
            (_, _, text) => text ?? "");

        Assert.Contains("[[cgw:action", withActions.Prepared.MergedText);
        Assert.DoesNotContain("[[cgw:action", without.Prepared.MergedText);
    }
}
