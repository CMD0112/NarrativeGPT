namespace ChatGPTWrapper.Adventure.Services.NarratorScales;

internal enum NarratorScaleCategory
{
    Narration,
    Combat,
}

internal static class NarratorScaleLabels
{
    public const string ResponseLength = "Response length";
    public const string DetailLevel = "Detail level";
    public const string Tone = "Tone";
    public const string CombatDifficulty = "Combat difficulty";
    public const string ViolenceLevel = "Violence level";
    public const string NarrativePacing = "Narrative pacing";
    public const string ConsequenceWeight = "Consequence weight";

    public const string NarrationGroup = "Narration (delivery)";
    public const string CombatGroup = "Combat & stakes";
}
