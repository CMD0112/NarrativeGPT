using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class CanonNotifyInjectionTests
{
    [Fact]
    public void PrepareSend_appends_canon_notify_block_when_flag_active()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        CanonReconciliationService.SetNotifyFlag(bundle,
        [
            new CanonChangeHint
            {
                FileName = SectionSchema.CastFile,
                SectionIds = ["npcs/test"],
                ChangeKind = "update",
            },
        ]);

        var prepared = PromptInjectionService.PrepareSend(bundle, "Hello narrator.");

        Assert.Contains("CANON UPDATE", prepared.MergedText, StringComparison.Ordinal);
        Assert.Contains("npcs/test", prepared.MergedText, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearNotify_preserves_unresolved_drift_after_defer()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        CanonReconciliationService.MarkUnresolvedDrift(bundle);

        CanonReconciliationService.ClearNotify(bundle);

        Assert.False(CanonReconciliationService.HasPendingNotify(bundle));
        Assert.True(CanonReconciliationService.HasUnresolvedDrift(bundle));
        Assert.DoesNotContain("CANON UPDATE",
            PromptInjectionService.PrepareSend(bundle, "test").MergedText,
            StringComparison.Ordinal);
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
                SectionIds = ["npcs/test"],
                ChangeKind = "update",
            },
        ]);

        var packet = AdventureBootstrapService.BuildStartPacket(bundle);

        Assert.DoesNotContain("CANON UPDATE", packet, StringComparison.Ordinal);
    }
}
