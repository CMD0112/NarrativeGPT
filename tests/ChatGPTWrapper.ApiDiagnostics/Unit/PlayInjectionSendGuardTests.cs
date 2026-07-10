using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayInjectionSendGuardTests
{
    [Fact]
    public void Validate_rejects_empty_merged_packet()
    {
        var bundle = new AdventureBundle { Metadata = new AdventureMetadata() };
        var prepared = new PromptInjectionPrepareResult
        {
            MergedText = "",
            UserText = "look around",
            ContextText = "context",
            Hash = "abc",
        };

        var result = PlayInjectionSendGuard.Validate(bundle, prepared, usePrebuiltPacket: false);

        Assert.False(result.Ok);
        Assert.Equal("empty_packet", result.DiagnosticCode);
    }

    [Fact]
    public void Validate_rejects_missing_context()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { UseContextTags = true },
            },
        };
        var prepared = new PromptInjectionPrepareResult
        {
            MergedText = "look around",
            UserText = "look around",
            ContextText = "",
            Hash = "abc",
            Profile = PacketProfile.InlineFallback,
        };

        var result = PlayInjectionSendGuard.Validate(bundle, prepared, usePrebuiltPacket: false);

        Assert.False(result.Ok);
        Assert.Equal("missing_context", result.DiagnosticCode);
    }

    [Fact]
    public void Validate_accepts_prepared_linked_packet()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-guard", inSync: true, entryCount: 0);
        bundle.Metadata.Settings.UseContextTags = true;

        var prepared = PromptInjectionService.PrepareSend(bundle, "look around");

        var result = PlayInjectionSendGuard.Validate(bundle, prepared, usePrebuiltPacket: false);

        Assert.True(result.Ok);
    }

    [Fact]
    public void Validate_skips_prebuilt_start_packet()
    {
        var bundle = new AdventureBundle { Metadata = new AdventureMetadata() };
        var prepared = PromptInjectionService.PreparePrebuiltPacket("[[cgw:meta mode=\"inline\" turn=\"1\"]]\n\nBegin");

        var result = PlayInjectionSendGuard.Validate(bundle, prepared, usePrebuiltPacket: true);

        Assert.True(result.Ok);
    }
}
