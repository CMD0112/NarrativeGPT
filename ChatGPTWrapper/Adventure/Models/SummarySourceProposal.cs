namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Per-source rolling summary proposal (dual-run compare).</summary>
public sealed class SummarySourceProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text { get; set; } = "";

    public string InferenceSource { get; set; } = "";

    public Guid? UtilityRunId { get; set; }

    public bool Resolved { get; set; }
}
