using ChatGPTWrapper.ChatGptApi.ProjectSource;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProjectPublicationTriageTests
{
    [Fact]
    public void BuildExhaustedSummary_includes_lane_attempts_and_manual_qa()
    {
        var run = new ProjectFilePublicationRun
        {
            RunId = Guid.Parse("27dd6cb1-b8ee-4b34-a645-83692ff031c2"),
            GizmoId = "g-p-test",
            RemoteFileName = "test.md",
            LocalSha256 = "abc",
            BaselineRemoteIds = [],
            Outcome = ProjectPublicationOutcome.Exhausted,
            Profile = ProjectPublicationProfile.Lab,
            Attempts =
            [
                new ProjectPublicationAttempt
                {
                    Lane = ProjectPublicationLaneId.BrowserNative,
                    Phase = ProjectSourcePublicationPhase.DomEscalation,
                    Outcome = ProjectPublicationAttemptOutcome.Failed,
                    Error = "dom_upload_timeout: file=test.md",
                    LatencyMs = 120_000,
                },
                new ProjectPublicationAttempt
                {
                    Lane = ProjectPublicationLaneId.RegisterProjectFiles,
                    Phase = ProjectSourcePublicationPhase.BindToProject,
                    Outcome = ProjectPublicationAttemptOutcome.Failed,
                    Error = "upload_not_downloadable",
                    FileId = "file_ghost",
                    ListConfirmObserved = true,
                    LatencyMs = 20_000,
                },
            ],
            DeferredGhostFileIds = ["file_ghost"],
        };

        var summary = ProjectPublicationTriage.BuildExhaustedSummary(run, "test.md");

        Assert.Contains("lane=BrowserNative", summary);
        Assert.Contains("dom_upload_timeout", summary);
        Assert.Contains("listConfirm=yes", summary);
        Assert.Contains("ghost_ids_cleaned", summary);
        Assert.Contains("Manual QA", summary);
    }
}

[Trait("Category", "Unit")]
public sealed class ChatGptUrlsCanonicalHomeTests
{
    [Theory]
    [InlineData("https://chatgpt.com/g/g-p-abc/project", "g-p-abc", true)]
    [InlineData("https://chatgpt.com/g/g-p-abc/c/conv-id", "g-p-abc", false)]
    [InlineData("https://chatgpt.com/g/g-p-abc/project", "g-p-other", false)]
    public void IsCanonicalProjectHome_distinguishes_project_home_from_thread(
        string url,
        string gizmoId,
        bool expected)
    {
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri));
        Assert.Equal(expected, ChatGptUrls.IsCanonicalProjectHome(uri, gizmoId));
    }

    [Fact]
    public void BuildProjectUrl_matches_canonical_home_pattern()
    {
        var url = ChatGptUrls.BuildProjectUrl("g-p-test");
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri));
        Assert.True(ChatGptUrls.IsCanonicalProjectHome(uri, "g-p-test"));
    }
}
