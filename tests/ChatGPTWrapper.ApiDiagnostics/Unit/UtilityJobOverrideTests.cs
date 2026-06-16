using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class UtilityJobOverrideTests
{
    [Fact]
    public void BuildJobPrompt_appends_length_override_for_process_turn()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.UtilityJobOverrides[GenerationJobId.ProcessTurn] = new UtilityJobOverrideSettings
        {
            ResponseLength = "brief",
            ResponseDetail = "entities-only",
        };

        var prompt = GenerationJobHandlers.BuildJobPrompt(
            bundle,
            GenerationJobId.ProcessTurn,
            new GenerationJobContext());

        Assert.Contains("Response length: brief", prompt);
        Assert.Contains("Response detail: entities-only", prompt);
        Assert.Contains("=== JOB OVERRIDES ===", prompt);
    }
}
