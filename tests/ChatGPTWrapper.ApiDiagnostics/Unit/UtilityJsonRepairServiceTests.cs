using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityJsonRepairServiceTests
{
    [Fact]
    public void TryEnsureValidJson_repairs_unescaped_dialogue_in_memory_text()
    {
        const string broken = """
            {"memories":[{"text":"Garran answered, "A child, clearly."","tags":["gate"],"pinned":false}]}
            """;

        var repaired = UtilityJsonRepairService.TryEnsureValidJson(broken);

        Assert.NotNull(repaired);
        using var doc = JsonDocument.Parse(repaired!);
        var text = doc.RootElement.GetProperty("memories")[0].GetProperty("text").GetString();
        Assert.Contains("A child, clearly.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryEnsureValidJson_repairs_unescaped_word_quotes()
    {
        const string broken = """
            {"memories":[{"text":"The word "separating" made Garran's Crownward pulse.","tags":[],"pinned":false}]}
            """;

        var repaired = UtilityJsonRepairService.TryEnsureValidJson(broken);

        Assert.NotNull(repaired);
        using var doc = JsonDocument.Parse(repaired!);
        var text = doc.RootElement.GetProperty("memories")[0].GetProperty("text").GetString();
        Assert.Contains("separating", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryEnsureValidJson_leaves_valid_json_unchanged()
    {
        const string valid = """{"memories":[{"text":"Plain text","tags":[],"pinned":false}]}""";

        var result = UtilityJsonRepairService.TryEnsureValidJson(valid);

        Assert.Equal(valid, result);
    }

    [Fact]
    public void TryNormalizeJsonObjectResponse_repairs_process_turn_bundle()
    {
        const string broken = """
            {"memories":[{"text":"Garran answered, "A child, clearly."","tags":["gate"],"pinned":false,"anchor":{"pairOffset":0,"playerHint":"A child, clearly."},"outcome":"Deflection."},{"text":"The word "separating" pulsed.","tags":["crownward"],"pinned":false}],"entities":[{"name":"Greyford Gate","entityType":"place","description":"The gate.","aliases":["gate"]}]}
            """;

        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(broken);

        Assert.NotNull(normalized);
        using var doc = JsonDocument.Parse(normalized!);
        Assert.Equal(2, doc.RootElement.GetProperty("memories").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("entities").GetArrayLength());
    }

    [Fact]
    public void ApplyProcessTurn_parses_repaired_chatgpt_style_bundle()
    {
        const string broken = """
            {"memories":[{"text":"Garran answered, "A child, clearly."","tags":["gate"],"pinned":false}],"entities":[{"name":"The Crown Clerk","entityType":"person","description":"Gate official.","aliases":["clerk"]}],"summary":"Gate tension."}
            """;

        var bundle = AdventureTestData.CreateLinkedBundle();
        var result = GenerationJobHandlers.ApplyResponse(
            bundle,
            GenerationJobId.ProcessTurn,
            broken,
            context: new GenerationJobContext());

        Assert.Null(result.Error);
        Assert.Equal(3, result.ProposalCount);
        Assert.Single(bundle.Memory.ReviewQueue);
        Assert.Single(bundle.Entities.ReviewQueue);
        Assert.True(bundle.Summary.PendingReview);
        Assert.Equal("Gate tension.", bundle.Summary.ProposedSummary);
    }

    [Fact]
    public void TryNormalizeJsonArrayResponse_repairs_bare_memory_array()
    {
        const string broken = """
            [{"text":"He said, "wait here."","tags":[],"pinned":false}]
            """;

        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(broken);

        Assert.NotNull(normalized);
        Assert.True(EntityExtractionService.IsValidJsonArray(normalized));
    }
}
