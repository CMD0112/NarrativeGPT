using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>
/// End-to-end evaluation of the native compose intercept → host merge pipeline.
/// Proves short native composer text is the input to <see cref="PlayPacketPrepareSession"/>,
/// not the final packet delivered to ChatGPT when injection is enabled.
/// </summary>
[Collection("PlayComposeWebView")]
[Trait("Category", "Integration")]
public sealed class PlayNativeSendInjectionPipelineTests(PlayComposeTestHost host) : IAsyncLifetime
{
    public Task InitializeAsync() => host.ResetPageAsync(enableWrapper: false);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Native_send_posts_compose_send_for_host_merge()
    {
        const string playerLine = "examine the iron gate";

        await host.SetNativeInputValueAsync(playerLine);
        await host.TriggerNativeSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(100));

        var messages = await host.DrainMessagesAsync();
        var send = messages.LastOrDefault(m =>
            m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.NotNull(send);
        Assert.Equal(playerLine, send!.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void Host_merge_expands_short_compose_text_when_injection_enabled()
    {
        const string playerLine = "examine the iron gate";
        var bundle = CreateInjectionEnabledBundle();

        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                ApplySurfaceActions = false,
                PriorThreadUserMessageCount = 2,
            },
            (_, _, text) => text ?? "");

        Assert.NotEqual(playerLine, session.Prepared.MergedText);
        Assert.Contains(playerLine, session.Prepared.MergedText);
        Assert.Contains("[[cgw:", session.Prepared.MergedText);
    }

    [Fact]
    public async Task Full_pipeline_native_intercept_plus_prepare_differs_from_plain_send()
    {
        const string playerLine = "open the chest carefully";
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle(
            "pipeline-pin-tab",
            conversationId: "conv-pipeline",
            projectId: "g-p-pipeline");

        await host.SetNativeInputValueAsync(playerLine);
        await host.TriggerNativeSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(100));

        var messages = await host.DrainMessagesAsync();
        var sendJson = messages.Last(m =>
            m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        var interceptedText = sendJson.RootElement.GetProperty("text").GetString();
        Assert.Equal(playerLine, interceptedText);

        bundle.Metadata.Settings.UseContextTags = true;
        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = interceptedText!,
                ApplySurfaceActions = false,
                PriorThreadUserMessageCount = 1,
            },
            (_, _, text) => text ?? "");

        Assert.True(session.Prepared.MergedText.Length > interceptedText!.Length * 2);
        Assert.Contains("[[cgw:meta", session.Prepared.MergedText);

        Assert.True(PlayComposeInjectionPolicy.ShouldRegisterIntercept(
            new PlayComposeRegistrationContext(
                IsPlayMode: true,
                Bundle: bundle,
                CandidateTabKey: "pipeline-pin-tab",
                PlayWebViewTabKey: "stale-tab",
                ActiveWebViewTabKey: "stale-tab",
                SuppressPlayAutomation: false)));

        var deliveryPayload = JsonSerializer.Serialize(new
        {
            type = "bridge_submit",
            text = session.Prepared.MergedText,
            composeLength = interceptedText.Length,
            mergedLength = session.Prepared.MergedText.Length,
        });
        Assert.Contains("mergedLength", deliveryPayload);
        Assert.DoesNotContain("\"composeLength\":\"" + session.Prepared.MergedText.Length, deliveryPayload);
    }

    [Fact]
    public async Task Native_send_blocked_while_busy_prevents_unmerged_bypass()
    {
        await host.SetNativeInputValueAsync("should not intercept");
        await host.ApplyStateAsync(new PlayComposeUiState { Busy = true, Status = "Sending…" });
        await host.TriggerNativeSendClickAsync();
        await host.PumpUiAsync(TimeSpan.FromMilliseconds(100));

        var messages = await host.DrainMessagesAsync();
        Assert.DoesNotContain(
            messages,
            m => m.RootElement.GetProperty("type").GetString() == "cgwComposeSend");
        Assert.Equal("should not intercept", await host.GetNativeInputValueAsync());
    }

    private static AdventureBundle CreateInjectionEnabledBundle()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-merge", inSync: true);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Summary = new SummaryDocument { RollingSummary = "The party stands at the gate." };
        bundle.State = new StateDocument { CurrentLocation = "Courtyard" };
        return bundle;
    }
}
