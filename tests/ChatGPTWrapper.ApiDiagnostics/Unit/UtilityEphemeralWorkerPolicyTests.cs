using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityEphemeralWorkerPolicyTests
{
    [Fact]
    public void IsEnabled_defaults_false()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        Assert.False(UtilityEphemeralWorkerPolicy.IsEnabled(bundle));
    }

    [Fact]
    public void IsWorkerLaneAvailable_ephemeral_requires_linked_project_only()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.UtilityWorkerCapabilities = null;

        Assert.True(UtilityEphemeralWorkerPolicy.IsWorkerLaneAvailable(bundle));
    }

    [Fact]
    public void IsWorkerLaneAvailable_ephemeral_false_without_linked_project()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;

        Assert.False(UtilityEphemeralWorkerPolicy.IsWorkerLaneAvailable(bundle));
    }

    [Fact]
    public void IsWorkerLaneAvailable_legacy_requires_green_capabilities()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = false;

        Assert.False(UtilityEphemeralWorkerPolicy.IsWorkerLaneAvailable(bundle));

        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
            HostReady = true,
            SseReliable = true,
        };

        Assert.True(UtilityEphemeralWorkerPolicy.IsWorkerLaneAvailable(bundle));
    }

    [Fact]
    public void RequiresWorkerPin_false_when_ephemeral_enabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;

        Assert.False(UtilityEphemeralWorkerPolicy.RequiresWorkerPin(bundle));
    }

    [Fact]
    public void RequiresWorkerPin_true_when_legacy_and_unpinned()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = false;

        Assert.True(UtilityEphemeralWorkerPolicy.RequiresWorkerPin(bundle));
    }
}
