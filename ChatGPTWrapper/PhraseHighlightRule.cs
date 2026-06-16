namespace ChatGPTWrapper;

public sealed class PhraseHighlightRule
{
    public string Phrase { get; set; } = "";

    public string Color { get; set; } = "#FFD166";

    public string? BackgroundColor { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public PhraseHighlightRule Clone() =>
        new()
        {
            Phrase = Phrase,
            Color = Color,
            BackgroundColor = BackgroundColor,
            Bold = Bold,
            Italic = Italic,
        };
}
