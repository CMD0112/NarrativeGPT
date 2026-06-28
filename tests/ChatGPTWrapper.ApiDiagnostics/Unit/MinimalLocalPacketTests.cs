using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class MinimalLocalPacketTests
{
    [Fact]
    public void Unlinked_adventure_uses_minimal_local_profile()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null, inSync: false, entryCount: 0);
        bundle.SourceManifest.Entries.Clear();
        bundle.Metadata.Settings.UseContextTags = true;

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around the room.");

        Assert.Equal(PacketProfile.MinimalLocal, prepared.Profile);
        Assert.Equal(PacketMode.Thin, prepared.Mode);
        Assert.Contains("mode=\"minimal\"", prepared.MergedText);
        Assert.DoesNotContain("=== WORLD RULES ===", prepared.MergedText);
        Assert.DoesNotContain("Content boundaries:", prepared.MergedText);
        Assert.Contains("Rain lashes the drawbridge", prepared.MergedText);
    }

    [Fact]
    public void Published_linked_adventure_uses_source_delegated_profile()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(inSync: true, entryCount: 4);
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.LocalSha256 = "hash";
            SourceManifestHelper.MarkManuallyPublished(entry);
        }

        PopulateSectionManifest(bundle);

        var prepared = PromptInjectionService.PrepareSend(bundle, "Enter the hall.");

        Assert.Equal(PacketProfile.SourceDelegated, prepared.Profile);
        Assert.Contains("[[cgw:sources", prepared.MergedText);
        Assert.Contains("mode=\"delegated\"", prepared.MergedText);
        Assert.DoesNotContain("Content boundaries:", prepared.MergedText);
    }

    [Fact]
    public void Inline_fallback_when_user_proceeds_unpublished()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(inSync: true, entryCount: 4);
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.ContentBoundaries = ["No explicit gore."];

        var prepared = PromptInjectionService.PrepareSend(
            bundle, "Search the library.", userChoseInlineFallback: true);

        Assert.Equal(PacketProfile.InlineFallback, prepared.Profile);
        Assert.Equal(PacketMode.Fat, prepared.Mode);
        Assert.Contains("Content boundaries:", prepared.MergedText);
    }

    private static void PopulateSectionManifest(AdventureBundle bundle)
    {
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.Sections = entry.RelativePath switch
            {
                "scenario.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "opening",
                        Kind = "scenario",
                        Title = "Opening",
                        BodyCache = bundle.Scenario.OpeningSituation,
                    },
                ],
                "world.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "rules",
                        Kind = "rule",
                        Title = "Rules",
                        BodyCache = bundle.Scenario.WorldRules,
                    },
                ],
                "cast.md" =>
                [
                    new SectionManifestEntry
                    {
                        Id = "player",
                        Kind = "person",
                        Title = "Player",
                        BodyCache = bundle.Scenario.PlayerRole,
                    },
                ],
                _ => [],
            };
        }
    }
}
