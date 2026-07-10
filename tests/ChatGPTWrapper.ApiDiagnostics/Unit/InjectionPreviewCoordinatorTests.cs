using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InjectionPreviewCoordinatorTests
{
    [Fact]
    public void Refresh_detects_override_delta_when_tone_changes()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-preview-delta");
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.Settings.UseContextTags = true;

        var first = InjectionPreviewCoordinator.Refresh(
            bundle, "Hello.", null, null, "", previous: null);

        bundle.Metadata.Settings.PlayTurnOverrides.Tone = "tense";
        var second = InjectionPreviewCoordinator.Refresh(
            bundle, "Hello.", null, null, "", previous: first);

        Assert.Contains(second.DeltaMessages, m => m.Contains("override", StringComparison.OrdinalIgnoreCase)
                                                   || m.Contains("Tone", StringComparison.OrdinalIgnoreCase)
                                                   || second.Deltas.Any(d => d.SectionId == "overrides"));
    }

    [Fact]
    public void Refresh_returns_section_rows_with_display_names()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-preview-rows");
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.Settings.UseContextTags = true;

        var snapshot = InjectionPreviewCoordinator.Refresh(
            bundle, "Explore.", null, null, "", previous: null);

        Assert.True(snapshot.HasPlayerLine);
        Assert.Contains(snapshot.SectionRows, r => r.DisplayName == "Your message");
        Assert.True(snapshot.CharCount > 0);
    }

    [Fact]
    public void Refresh_marks_transcript_omitted_when_policy_disabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-preview-omit");
        bundle.Metadata.Settings.InjectionPolicy.IncludeTranscript = false;

        var snapshot = InjectionPreviewCoordinator.Refresh(
            bundle, "Wait.", null, null, "", previous: null);

        var transcript = snapshot.SectionRows.First(r => r.Id == "transcript");
        Assert.Equal("Omitted", transcript.StatusBadge);
    }
}
