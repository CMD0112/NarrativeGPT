using System.Reflection;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InlineUtilityDomPipelineTests
{
    [Theory]
    [InlineData(UtilityDeliveryMode.InlinePlayThread, true)]
    [InlineData(UtilityDeliveryMode.SeparateThread, false)]
    public void InlineUtilityPipeline_uses_dom_only_for_inline_delivery(
        UtilityDeliveryMode mode,
        bool expected)
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { UtilityDeliveryMode = mode },
            },
        };

        Assert.Equal(expected, InlineUtilityPipeline.UsesDomOnlyPipeline(bundle));
    }

    [Fact]
    public void InlineUtilityPipeline_send_phase_is_send_dom_inline()
    {
        Assert.Equal("send_dom_inline", InlineUtilityPipeline.SendPhase);
        Assert.Equal("inlinePlayThread", InlineUtilityPipeline.DeliveryMode);
    }

    [Fact]
    public void GenerationJobService_has_inline_dom_send_method()
    {
        var method = typeof(GenerationJobService).GetMethod(
            "SendInlineUtilityPacketDomAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public async Task CaptureAsync_domOnly_local_source_does_not_require_play_core()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings
                {
                    UtilityStoryContext = new UtilityStoryContextSettings
                    {
                        Source = UtilityStorySource.LocalLog,
                    },
                },
            },
            Log = new LogDocument
            {
                Turns =
                [
                    new TurnRecord
                    {
                        Index = 1,
                        Status = TurnStatus.Accepted,
                        PlayerText = "look around",
                        NarratorText = "Dust swirls.",
                    },
                ],
            },
        };

        var settings = UtilityStoryContextSettingsService.Resolve(bundle, GenerationJobId.ProposeMemories);
        var service = new PlayThreadTranscriptService(null!, turnService: null);

        var result = await service.CaptureAsync(
            bundle,
            settings,
            playCore: null,
            domOnlyCapture: true);

        Assert.Equal(StoryContextSourceUsed.LocalLog, result.SourceUsed);
        Assert.Single(result.TurnPairs);
        Assert.Equal("look around", result.TurnPairs[0].PlayerText);
    }

    [Fact]
    public async Task CaptureAsync_domOnly_live_without_play_core_returns_unavailable()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedConversationId = "conv-abc",
                Settings = new AdventureSettings
                {
                    UtilityStoryContext = new UtilityStoryContextSettings
                    {
                        Source = UtilityStorySource.LivePlayThread,
                    },
                },
            },
        };

        var settings = UtilityStoryContextSettingsService.Resolve(bundle, GenerationJobId.ProposeMemories);
        var service = new PlayThreadTranscriptService(null!, turnService: null);

        var result = await service.CaptureAsync(
            bundle,
            settings,
            playCore: null,
            domOnlyCapture: true);

        Assert.Equal(StoryContextSourceUsed.None, result.SourceUsed);
        Assert.Empty(result.TurnPairs);
        Assert.Equal("play_thread_unavailable", result.Error);
    }
}
