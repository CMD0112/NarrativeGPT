namespace ChatGPTWrapper;



public sealed class PhraseHighlightRule

{

    public string Phrase { get; set; } = "";



    public string Color { get; set; } = "#FFD166";



    public string? BackgroundColor { get; set; }



    /// <summary>Absolute CSS font-weight (100–900). When set, <see cref="Bold"/> is ignored for weight.</summary>

    public int? FontWeight { get; set; }



    /// <summary>When <see cref="FontWeight"/> is null, adds the format bold delta on top of role weight.</summary>

    public bool Bold { get; set; }



    public bool Italic { get; set; }



    public bool Underline { get; set; }



    public bool Strikethrough { get; set; }



    /// <summary>Multiplier on inherited font size (e.g. 1.15 = 115%).</summary>

    public double? FontSizeScale { get; set; }



    public double? LetterSpacingEm { get; set; }



    public string? FontFamily { get; set; }



    /// <summary>CSS text-transform: uppercase, lowercase, or capitalize.</summary>

    public string? TextTransform { get; set; }



    /// <summary>Highlight span opacity (0–1).</summary>

    public double? Opacity { get; set; }



    public string? BorderColor { get; set; }



    public int? BorderWidthPx { get; set; }



    public int? BorderRadiusPx { get; set; }



    public double? PaddingXEm { get; set; }



    public double? PaddingYEm { get; set; }



    /// <summary>Raw CSS text-shadow value.</summary>

    public string? TextShadow { get; set; }



    /// <summary>Raw CSS box-shadow value.</summary>

    public string? BoxShadow { get; set; }



    public bool Enabled { get; set; } = true;



    /// <summary>Optional link to adventure entity (characters, player, party, locations).</summary>

    public Guid? EntityId { get; set; }



    /// <summary>Entity category matching <see cref="EntityEditMapper"/> categories.</summary>

    public string? EntityCategory { get; set; }



    /// <summary>

    /// Primary phrase this alias mirrors for style sync (same entity).

    /// Null on primary rules and unlinked phrases.

    /// </summary>

    public string? SyncWithPhrase { get; set; }



    /// <summary>

    /// When true, style changes do not propagate to or from linked alias/primary rules.

    /// </summary>

    public bool SyncOverride { get; set; }



    /// <summary>

    /// When true, color group sharing does not propagate to or from peers in the same grouping profile bucket.

    /// </summary>

    public bool GroupOverride { get; set; }



    public PhraseHighlightRule Clone() =>

        new()

        {

            Phrase = Phrase,

            Color = Color,

            BackgroundColor = BackgroundColor,

            FontWeight = FontWeight,

            Bold = Bold,

            Italic = Italic,

            Underline = Underline,

            Strikethrough = Strikethrough,

            FontSizeScale = FontSizeScale,

            LetterSpacingEm = LetterSpacingEm,

            FontFamily = FontFamily,

            TextTransform = TextTransform,

            Opacity = Opacity,

            BorderColor = BorderColor,

            BorderWidthPx = BorderWidthPx,

            BorderRadiusPx = BorderRadiusPx,

            PaddingXEm = PaddingXEm,

            PaddingYEm = PaddingYEm,

            TextShadow = TextShadow,

            BoxShadow = BoxShadow,

            Enabled = Enabled,

            EntityId = EntityId,

            EntityCategory = EntityCategory,

            SyncWithPhrase = SyncWithPhrase,

            SyncOverride = SyncOverride,

            GroupOverride = GroupOverride,

        };

}

