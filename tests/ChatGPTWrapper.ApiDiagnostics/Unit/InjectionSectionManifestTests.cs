using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InjectionSectionManifestTests
{
    [Fact]
    public void PrepareSend_populates_section_manifest_for_thin_packet()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-manifest");
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.LocalSha256 = "hash";
            SourceManifestHelper.MarkManuallyPublished(entry);
        }
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.Settings.UseContextTags = true;
        PopulateSectionManifest(bundle);
        bundle.SourceManifest.RefreshSyncedFlag();

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around.");

        Assert.Equal(PacketDelegationMode.SourceDelegated, prepared.DelegationMode);
        Assert.Contains(prepared.Sections, s => s.Id == "sources" && s.Kind == InjectionSectionKind.Reference && s.Included);
        Assert.Contains(prepared.Sections, s => s.Id == "player" && s.Kind == InjectionSectionKind.Delta && s.Included);
        Assert.DoesNotContain(prepared.Sections, s => s.Id == "instructions" && s.Kind == InjectionSectionKind.ConditionalInline && s.Included);
    }

    [Fact]
    public void PrepareSend_records_packet_trim_in_manifest()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-trim");
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.LocalSha256 = "hash";
            SourceManifestHelper.MarkManuallyPublished(entry);
        }
        bundle.Metadata.Settings.MaxPacketChars = 200;
        bundle.Metadata.Settings.UseSectionInjection = true;
        PopulateSectionManifest(bundle);
        bundle.SourceManifest.RefreshSyncedFlag();

        var prepared = PromptInjectionService.PrepareSend(bundle, "Explore the ruins in detail.");

        if (prepared.WasTrimmed)
            Assert.Contains(prepared.Trimmed, t => t.Id == "packet");
    }

    [Fact]
    public void FormatSectionSummary_lists_reference_and_delta_badges()
    {
        var sections = new List<InjectionSection>
        {
            new("sources", InjectionSectionKind.Reference, Mandatory: true, Included: true),
            new("state", InjectionSectionKind.Delta, Mandatory: true, Included: true),
        };

        var summary = InjectionSectionManifestBuilder.FormatSectionSummary(sections);

        Assert.Contains("[reference] sources", summary);
        Assert.Contains("[delta] state", summary);
    }

    private static void PopulateSectionManifest(AdventureBundle bundle)
    {
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.Sections =
            [
                new SectionManifestEntry
                {
                    Id = entry.RelativePath.Replace(".md", "", StringComparison.Ordinal),
                    Kind = "section",
                    Title = entry.RelativePath,
                    BodyCache = "body",
                },
            ];
        }
    }
}
