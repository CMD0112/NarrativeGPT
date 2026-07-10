using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProjectPublicationLaneRegistryTests
{
    [Fact]
    public void Lab_Snorlax_order_is_dom_first()
    {
        var api = new object();
        // Registry only needs gizmo id check — use a known Snorlax prefix
        var lanes = ProjectPublicationLaneRegistry.ForGizmo(
            "g-p-test123",
            ProjectPublicationProfile.Lab,
            null!);

        Assert.Equal(3, lanes.Count);
        Assert.Equal(ProjectPublicationLaneId.BrowserNative, lanes[0].LaneId);
        Assert.Equal(ProjectPublicationLaneId.Library, lanes[1].LaneId);
        Assert.Equal(ProjectPublicationLaneId.RegisterProjectFiles, lanes[2].LaneId);
    }

    [Fact]
    public void BatchSync_order_is_api_first()
    {
        var lanes = ProjectPublicationLaneRegistry.ForGizmo(
            "g-p-test123",
            ProjectPublicationProfile.BatchSync,
            null!);

        Assert.Equal(ProjectPublicationLaneId.RegisterProjectFiles, lanes[0].LaneId);
        Assert.Equal(ProjectPublicationLaneId.Library, lanes[1].LaneId);
        Assert.Equal(ProjectPublicationLaneId.BrowserNative, lanes[2].LaneId);
    }
}
