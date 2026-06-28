using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class NarrativeStartPacketTests : IDisposable
{
    private readonly string _root;

    public NarrativeStartPacketTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-narrative-start-" + Guid.NewGuid().ToString("N"));
        AppDirectories.TestRootOverride = _root;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public void BuildStartPlayerDirective_lists_core_source_files()
    {
        var bundle = AdventureStore.CreateNew("Start directive");
        bundle.Metadata.Settings.ForceInlineLore = true;
        AdventureStore.Save(bundle);

        var directive = AdventureBootstrapService.BuildStartPlayerDirective(bundle);

        Assert.Contains("every adventure source file", directive, StringComparison.Ordinal);
        Assert.Contains("Your reply is the opening scene", directive, StringComparison.Ordinal);
        Assert.Contains("scenario.md", directive, StringComparison.Ordinal);
        Assert.Contains("cast.md", directive, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStartPacket_omits_canon_notify_on_fresh_narrative_start()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        CanonReconciliationService.SetNotifyFlag(bundle,
        [
            new CanonChangeHint
            {
                FileName = SectionSchema.CastFile,
                SectionIds = ["party/anwen"],
                ChangeKind = "update",
            },
        ]);

        var packet = AdventureBootstrapService.BuildStartPacket(bundle);

        Assert.Contains("Your reply is the opening scene", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("CANON UPDATE", packet, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStartPacket_player_directive_does_not_use_opening_hook_line()
    {
        var bundle = AdventureStore.CreateNew("Opening scenario");
        bundle.Metadata.LinkedProjectId = "g-p-hook";
        bundle.Metadata.Settings.ForceInlineLore = true;
        bundle.Metadata.Settings.UseSectionInjection = false;
        bundle.Scenario.OpeningSituation = "The storm breaks over the red keep.";
        AdventureStore.Save(bundle);

        var packet = PlayThreadPacketService.BuildStartPacket(bundle.Metadata.Id);

        Assert.Contains("Your reply is the opening scene", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("Opening hook:", packet, StringComparison.Ordinal);
    }
}
