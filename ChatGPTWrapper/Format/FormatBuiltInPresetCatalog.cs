namespace ChatGPTWrapper.Format;

public enum FormatPresetCategory
{
    Layout,
    Readability,
    Typography,
    Distinction,
    Weave,
    Ambient,
}

public sealed class FormatBuiltInPresetDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public FormatPresetCategory Category { get; init; }
}

public static class FormatBuiltInPresetCatalog
{
  private static readonly IReadOnlyList<FormatBuiltInPresetDefinition> Definitions = BuildDefinitions();

  public static IReadOnlyList<FormatBuiltInPresetDefinition> All => Definitions;

  public static FormatBuiltInPresetDefinition? Find(string? id) =>
      string.IsNullOrWhiteSpace(id)
          ? null
          : Definitions.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

  public static ContinuousViewFormatSettings CreateSnapshot(string id)
  {
      var format = ContinuousViewFormatSettings.CreateDefaults();
      Apply(id, format);
      return format;
  }

  public static void Apply(string id, ContinuousViewFormatSettings format)
  {
      ArgumentNullException.ThrowIfNull(format);

      if (!TryApply(id, format))
          throw new ArgumentException($"Unknown built-in format preset: {id}", nameof(id));
  }

  public static bool TryApply(string id, ContinuousViewFormatSettings format)
  {
      ArgumentNullException.ThrowIfNull(format);

      return id.ToLowerInvariant() switch
      {
          FormatProfileIds.Compact => ApplyCompact(format),
          FormatProfileIds.Default => ApplyDefault(format),
          FormatProfileIds.Relaxed => ApplyRelaxed(format),
          FormatProfileIds.WideCanvas => ApplyWideCanvas(format),
          FormatProfileIds.LongFormReading => ApplyLongFormReading(format),
          FormatProfileIds.LowGlare => ApplyLowGlare(format),
          FormatProfileIds.DyslexiaFriendly => ApplyDyslexiaFriendly(format),
          FormatProfileIds.HighContrastReading => ApplyHighContrastReading(format),
          FormatProfileIds.SepiaComfort => ApplySepiaComfort(format),
          FormatProfileIds.LiterarySerif => ApplyLiterarySerif(format),
          FormatProfileIds.TechnicalDocs => ApplyTechnicalDocs(format),
          FormatProfileIds.NarrativeProse => ApplyNarrativeProse(format),
          FormatProfileIds.AcademicJournal => ApplyAcademicJournal(format),
          FormatProfileIds.RoleForward => ApplyRoleForward(format),
          FormatProfileIds.MinimalDistraction => ApplyMinimalDistraction(format),
          FormatProfileIds.CinematicWeave => ApplyCinematicWeave(format),
          FormatProfileIds.MidnightFocus => ApplyMidnightFocus(format),
          _ => false,
      };
  }

  private static IReadOnlyList<FormatBuiltInPresetDefinition> BuildDefinitions() =>
  [
      Def(FormatProfileIds.Compact, "Compact", "Tight transcript density with crisp sans-serif type and subtle role accents.", FormatPresetCategory.Layout),
      Def(FormatProfileIds.Default, "Default", "Balanced layout, typography, and accent colors for everyday reading.", FormatPresetCategory.Layout),
      Def(FormatProfileIds.Relaxed, "Relaxed", "Roomier spacing, literary assistant prose, and softer segment chrome.", FormatPresetCategory.Layout),
      Def(FormatProfileIds.WideCanvas, "Wide canvas", "Extra-wide column for ultrawide monitors and side-by-side workflows.", FormatPresetCategory.Layout),
      Def(FormatProfileIds.LongFormReading, "Long-form reading", "Comfortable line length, open line spacing, and calm dividers for long sessions.", FormatPresetCategory.Readability),
      Def(FormatProfileIds.LowGlare, "Low-glare", "Muted accents, gentle background tints, and softer dividers to reduce eye strain.", FormatPresetCategory.Readability),
      Def(FormatProfileIds.DyslexiaFriendly, "Dyslexia-friendly", "Larger type, open spacing, and hyperlegible sans-serif stacks for easier scanning.", FormatPresetCategory.Readability),
      Def(FormatProfileIds.HighContrastReading, "High contrast", "Strong text/background separation and readable link colors for low-vision comfort.", FormatPresetCategory.Readability),
      Def(FormatProfileIds.SepiaComfort, "Sepia comfort", "Warm parchment-like prose colors with soft brown accents for extended reading.", FormatPresetCategory.Readability),
      Def(FormatProfileIds.LiterarySerif, "Literary serif", "Garamond narrator prose, Charter headings, and classic book-like rhythm.", FormatPresetCategory.Typography),
      Def(FormatProfileIds.TechnicalDocs, "Technical docs", "Humanist sans body, monospace code blocks, and tighter block spacing.", FormatPresetCategory.Typography),
      Def(FormatProfileIds.NarrativeProse, "Narrative prose", "Literary assistant voice with generous paragraph rhythm and flowing margins.", FormatPresetCategory.Typography),
      Def(FormatProfileIds.AcademicJournal, "Academic journal", "Charter serif body, formal heading scale, and restrained table/code styling.", FormatPresetCategory.Typography),
      Def(FormatProfileIds.RoleForward, "Role-forward", "Bold role distinction: labels, accent borders, and contrasting user/assistant colors.", FormatPresetCategory.Distinction),
      Def(FormatProfileIds.MinimalDistraction, "Minimal distraction", "Low-chrome transcript: no dividers, muted accents, and inherit-first colors.", FormatPresetCategory.Distinction),
      Def(FormatProfileIds.CinematicWeave, "Cinematic Weave", "Flowing narrator serif with aside-style player embeds tuned for Weave mode.", FormatPresetCategory.Weave),
      Def(FormatProfileIds.MidnightFocus, "Midnight focus", "Cool dark overlay palette with restrained blues for late-night sessions.", FormatPresetCategory.Ambient),
  ];

  private static FormatBuiltInPresetDefinition Def(
      string id,
      string name,
      string description,
      FormatPresetCategory category) =>
      new()
      {
          Id = id,
          Name = name,
          Description = description,
          Category = category,
      };

  private static void Reset(ContinuousViewFormatSettings f) => f.CopyFrom(ContinuousViewFormatSettings.CreateDefaults());

  private static bool ApplyCompact(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 40;
      f.OverlayPaddingXRem = 1.25;
      f.OverlayPaddingYRem = 1;
      f.SegmentSpacingRem = 0.85;
      f.ShowSegmentDividers = true;
      f.SegmentDividerOpacity = 20;
      f.SegmentBorderRadiusPx = 4;
      f.UserFontSizeRem = 0.94;
      f.UserLineHeight = 1.48;
      f.AssistantFontSizeRem = 1;
      f.AssistantLineHeight = 1.55;
      f.UserFontFamily = FormatFontFamilies.Humanist;
      f.AssistantFontFamily = FormatFontFamilies.Sans;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.HeadingFontFamily = FormatFontFamilies.Sans;
      f.UserFontWeight = 500;
      f.AssistantFontWeight = 400;
      f.UserLetterSpacingEm = 0.008;
      f.AssistantLetterSpacingEm = 0.008;
      f.BlockLetterSpacingEm = 0.008;
      f.UserAccentBorderWidthPx = 2;
      f.AssistantAccentBorderWidthPx = 2;
      f.UserBackgroundOpacity = 5;
      f.AssistantBackgroundOpacity = 0;
      f.BlockMarginRem = 0.55;
      f.ProseParagraphMarginRem = 0.45;
      f.CodeFontSizeRem = 0.875;
      f.CodeLineHeight = 1.48;
      f.CodeBlockPaddingRem = 0.65;
      f.CodeBorderRadiusPx = 6;
      f.HeadingMarginRem = 0.55;
      f.HeadingH1ScaleRem = 1.35;
      f.HeadingH2ScaleRem = 1.22;
      f.HeadingH3ScaleRem = 1.1;
      f.UserAccentColor = "#5B9FD4";
      f.AssistantAccentColor = "#6E7F96";
      f.LinkColor = "#7CB8E8";
      f.InlineCodeBackgroundColor = "#2A2A30";
      f.CodeBackgroundColor = "#1E1E24";
      f.CodeBorderColor = "#3A3A44";
      f.WeaveEmbedKind = WeaveEmbedKind.Blockquote;
      f.WeaveEmbedMarginBlockRem = 0.65;
      return true;
  }

  private static bool ApplyDefault(ContinuousViewFormatSettings f)
  {
      Reset(f);
      return true;
  }

  private static bool ApplyRelaxed(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 44;
      f.OverlayPaddingXRem = 2;
      f.OverlayPaddingYRem = 1.85;
      f.SegmentSpacingRem = 1.6;
      f.ShowSegmentDividers = true;
      f.SegmentDividerOpacity = 24;
      f.SegmentBorderRadiusPx = 8;
      f.UserFontSizeRem = 1.02;
      f.UserLineHeight = 1.62;
      f.AssistantFontSizeRem = 1.125;
      f.AssistantLineHeight = 1.75;
      f.UserFontFamily = FormatFontFamilies.Humanist;
      f.AssistantFontFamily = FormatFontFamilies.Literary;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.HeadingFontFamily = FormatFontFamilies.Charter;
      f.UserLetterSpacingEm = 0.012;
      f.AssistantLetterSpacingEm = 0.012;
      f.BlockLetterSpacingEm = 0.012;
      f.UserAccentBorderWidthPx = 3;
      f.AssistantAccentBorderWidthPx = 3;
      f.UserBackgroundOpacity = 6;
      f.AssistantBackgroundOpacity = 2;
      f.BlockMarginRem = 0.95;
      f.ProseParagraphMarginRem = 0.75;
      f.CodeFontSizeRem = 0.975;
      f.CodeLineHeight = 1.62;
      f.CodeBlockPaddingRem = 1;
      f.CodeBorderRadiusPx = 10;
      f.HeadingMarginRem = 0.95;
      f.HeadingH1ScaleRem = 1.5;
      f.HeadingH2ScaleRem = 1.32;
      f.HeadingH3ScaleRem = 1.15;
      f.UserAccentColor = "#5B9FD4";
      f.AssistantAccentColor = "#7A9BB8";
      f.LinkColor = "#8CC4FF";
      f.AssistantTextColor = "#E8E4DC";
      f.CodeBackgroundColor = "#232328";
      f.WeaveEmbedMarginBlockRem = 0.95;
      return true;
  }

  private static bool ApplyWideCanvas(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 52;
      f.OverlayPaddingXRem = 2.25;
      f.SegmentSpacingRem = 1.15;
      f.UserFontSizeRem = 1;
      f.AssistantFontSizeRem = 1.08;
      f.UserLineHeight = 1.58;
      f.AssistantLineHeight = 1.68;
      f.UserFontFamily = FormatFontFamilies.Sans;
      f.AssistantFontFamily = FormatFontFamilies.Sans;
      f.ShowSegmentDividers = true;
      f.SegmentDividerOpacity = 16;
      f.BlockMarginRem = 0.7;
      f.ComposerClearanceMinPx = 0;
      return true;
  }

  private static bool ApplyLongFormReading(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 46;
      f.OverlayPaddingXRem = 1.9;
      f.OverlayPaddingYRem = 1.65;
      f.SegmentSpacingRem = 1.5;
      f.ShowSegmentDividers = true;
      f.SegmentDividerOpacity = 18;
      f.UserFontSizeRem = 1;
      f.UserLineHeight = 1.62;
      f.AssistantFontSizeRem = 1.1;
      f.AssistantLineHeight = 1.72;
      f.UserFontFamily = FormatFontFamilies.Humanist;
      f.AssistantFontFamily = FormatFontFamilies.Literary;
      f.HeadingFontFamily = FormatFontFamilies.Charter;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.BlockMarginRem = 0.85;
      f.ProseParagraphMarginRem = 0.7;
      f.UserLetterSpacingEm = 0.01;
      f.AssistantLetterSpacingEm = 0.012;
      f.BlockLetterSpacingEm = 0.01;
      f.HeadingH2ScaleRem = 1.3;
      f.HeadingMarginRem = 0.85;
      f.UserAccentColor = "#5B9FD4";
      f.AssistantAccentColor = "#6F8FA8";
      f.LinkColor = "#84B8E6";
      f.AssistantTextColor = "#ECE8E2";
      f.WeaveEmbedMarginBlockRem = 0.9;
      return true;
  }

  private static bool ApplyLowGlare(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 44;
      f.SegmentSpacingRem = 1.35;
      f.UserFontSizeRem = 0.98;
      f.UserLineHeight = 1.58;
      f.AssistantFontSizeRem = 1.05;
      f.AssistantLineHeight = 1.65;
      f.UserFontFamily = FormatFontFamilies.Humanist;
      f.AssistantFontFamily = FormatFontFamilies.Serif;
      f.UserBackgroundOpacity = 5;
      f.AssistantBackgroundOpacity = 4;
      f.SegmentDividerOpacity = 12;
      f.ShowSegmentDividers = true;
      f.UserAccentBorderWidthPx = 2;
      f.AssistantAccentBorderWidthPx = 2;
      f.UserAccentColor = "#6A8FA8";
      f.AssistantAccentColor = "#6A8FA8";
      f.UserTextColor = "#C8CDD4";
      f.AssistantTextColor = "#B8BDC6";
      f.LinkColor = "#7A9BB5";
      f.SegmentDividerColor = "#3A3F48";
      f.OverlayBackgroundColor = "#121316";
      f.InlineCodeBackgroundColor = "#252830";
      f.CodeBackgroundColor = "#1A1D22";
      return true;
  }

  private static bool ApplyDyslexiaFriendly(ContinuousViewFormatSettings f)
  {
      Reset(f);
      const string hyperlegible = "\"Atkinson Hyperlegible\", \"OpenDyslexic\", \"Segoe UI\", sans-serif";
      f.ContentMaxWidthRem = 42;
      f.SegmentSpacingRem = 1.55;
      f.ShowSegmentDividers = false;
      f.UserFontSizeRem = 1.08;
      f.UserLineHeight = 1.75;
      f.AssistantFontSizeRem = 1.12;
      f.AssistantLineHeight = 1.78;
      f.UserFontFamily = hyperlegible;
      f.AssistantFontFamily = hyperlegible;
      f.HeadingFontFamily = hyperlegible;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.UserLetterSpacingEm = 0.04;
      f.AssistantLetterSpacingEm = 0.045;
      f.BlockLetterSpacingEm = 0.035;
      f.UserFontWeight = 500;
      f.ProseParagraphMarginRem = 0.85;
      f.BlockMarginRem = 0.9;
      f.HeadingH2ScaleRem = 1.2;
      f.UserTextColor = "#ECEFF4";
      f.AssistantTextColor = "#E2E6ED";
      f.UserAccentColor = "#6FA3C8";
      f.AssistantAccentColor = "#6FA3C8";
      return true;
  }

  private static bool ApplyHighContrastReading(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 42;
      f.UserFontSizeRem = 1.05;
      f.AssistantFontSizeRem = 1.12;
      f.UserLineHeight = 1.65;
      f.AssistantLineHeight = 1.72;
      f.UserFontFamily = FormatFontFamilies.Sans;
      f.AssistantFontFamily = FormatFontFamilies.Sans;
      f.UserFontWeight = 600;
      f.AssistantFontWeight = 500;
      f.UserTextColor = "#FFFFFF";
      f.AssistantTextColor = "#F4F4F5";
      f.UserBackgroundColor = "#0A0A0C";
      f.AssistantBackgroundColor = "#101014";
      f.UserBackgroundOpacity = 100;
      f.AssistantBackgroundOpacity = 100;
      f.UserAccentColor = "#FFD166";
      f.AssistantAccentColor = "#5B9FD4";
      f.UserAccentBorderWidthPx = 4;
      f.AssistantAccentBorderWidthPx = 4;
      f.LinkColor = "#9ED0FF";
      f.LinkHoverColor = "#C8E4FF";
      f.SegmentDividerColor = "#5A5A66";
      f.SegmentDividerOpacity = 40;
      f.InlineCodeBackgroundColor = "#2C2C34";
      f.CodeBackgroundColor = "#18181E";
      f.CodeBorderColor = "#666674";
      f.ShowRoleLabels = true;
      return true;
  }

  private static bool ApplySepiaComfort(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 45;
      f.SegmentSpacingRem = 1.4;
      f.UserFontSizeRem = 1.02;
      f.AssistantFontSizeRem = 1.1;
      f.UserLineHeight = 1.64;
      f.AssistantLineHeight = 1.74;
      f.UserFontFamily = FormatFontFamilies.Garamond;
      f.AssistantFontFamily = FormatFontFamilies.Garamond;
      f.HeadingFontFamily = FormatFontFamilies.Charter;
      f.CodeFontFamily = FormatFontFamilies.Typewriter;
      f.OverlayBackgroundColor = "#1A1612";
      f.UserTextColor = "#E8DFD0";
      f.AssistantTextColor = "#DDD2C0";
      f.UserBackgroundColor = "#2A231C";
      f.AssistantBackgroundColor = "#241F19";
      f.UserBackgroundOpacity = 35;
      f.AssistantBackgroundOpacity = 28;
      f.UserAccentColor = "#C9A66B";
      f.AssistantAccentColor = "#B8925A";
      f.LinkColor = "#D4B483";
      f.SegmentDividerColor = "#4A3F32";
      f.SegmentDividerOpacity = 28;
      f.InlineCodeBackgroundColor = "#342C24";
      f.CodeBackgroundColor = "#2A231C";
      f.CodeBorderColor = "#5C4E3E";
      f.TableBorderColor = "#5C4E3E";
      f.TableHeaderBackgroundColor = "#342C24";
      return true;
  }

  private static bool ApplyLiterarySerif(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 44;
      f.SegmentSpacingRem = 1.45;
      f.UserFontFamily = FormatFontFamilies.Humanist;
      f.AssistantFontFamily = FormatFontFamilies.Garamond;
      f.HeadingFontFamily = FormatFontFamilies.Charter;
      f.CodeFontFamily = FormatFontFamilies.Typewriter;
      f.AssistantFontSizeRem = 1.14;
      f.AssistantLineHeight = 1.76;
      f.UserFontSizeRem = 0.98;
      f.UserLineHeight = 1.58;
      f.ProseParagraphMarginRem = 0.8;
      f.BlockMarginRem = 0.85;
      f.HeadingH1ScaleRem = 1.55;
      f.HeadingH2ScaleRem = 1.34;
      f.HeadingH3ScaleRem = 1.16;
      f.HeadingMarginRem = 0.9;
      f.AssistantTextColor = "#E9E2D8";
      f.LinkColor = "#A8C4E0";
      f.CodeBlockPaddingRem = 0.9;
      f.WeaveEmbedKind = WeaveEmbedKind.Blockquote;
      f.WeaveEmbedMarginBlockRem = 1;
      return true;
  }

  private static bool ApplyTechnicalDocs(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 48;
      f.SegmentSpacingRem = 1.1;
      f.UserFontFamily = FormatFontFamilies.Sans;
      f.AssistantFontFamily = FormatFontFamilies.Humanist;
      f.HeadingFontFamily = FormatFontFamilies.Sans;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.UserFontSizeRem = 0.96;
      f.AssistantFontSizeRem = 1.02;
      f.UserLineHeight = 1.52;
      f.AssistantLineHeight = 1.58;
      f.CodeFontSizeRem = 0.9;
      f.CodeLineHeight = 1.5;
      f.CodeBlockPaddingRem = 0.75;
      f.CodeBorderRadiusPx = 6;
      f.BlockMarginRem = 0.6;
      f.ProseParagraphMarginRem = 0.5;
      f.HeadingH2ScaleRem = 1.18;
      f.HeadingH3ScaleRem = 1.06;
      f.InlineCodeBackgroundColor = "#25252C";
      f.CodeBackgroundColor = "#1A1A20";
      f.CodeBorderColor = "#3D3D48";
      f.CodeLangLabelColor = "#8A8A98";
      f.LinkColor = "#7CB8E8";
      f.ShowSegmentDividers = true;
      f.SegmentDividerOpacity = 14;
      return true;
  }

  private static bool ApplyNarrativeProse(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 46;
      f.SegmentSpacingRem = 1.55;
      f.UserFontFamily = FormatFontFamilies.Sans;
      f.AssistantFontFamily = FormatFontFamilies.Literary;
      f.HeadingFontFamily = FormatFontFamilies.Literary;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.AssistantFontSizeRem = 1.12;
      f.AssistantLineHeight = 1.78;
      f.ProseParagraphMarginRem = 0.82;
      f.BlockMarginRem = 0.88;
      f.UserIndentRem = 0.15;
      f.AssistantIndentRem = 0;
      f.HeadingH1ScaleRem = 1.48;
      f.HeadingH2ScaleRem = 1.3;
      f.AssistantTextColor = "#EBE6DE";
      f.UserAccentColor = "#6B8FAE";
      f.AssistantAccentColor = "#8A7A62";
      f.WeaveEmbedMarginBlockRem = 1.1;
      return true;
  }

  private static bool ApplyAcademicJournal(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ContentMaxWidthRem = 43;
      f.SegmentSpacingRem = 1.25;
      f.UserFontFamily = FormatFontFamilies.Serif;
      f.AssistantFontFamily = FormatFontFamilies.Charter;
      f.HeadingFontFamily = FormatFontFamilies.Charter;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.AssistantFontSizeRem = 1.06;
      f.AssistantLineHeight = 1.68;
      f.UserFontSizeRem = 0.98;
      f.UserLineHeight = 1.6;
      f.UserFontWeight = 500;
      f.HeadingH1ScaleRem = 1.42;
      f.HeadingH2ScaleRem = 1.26;
      f.HeadingH3ScaleRem = 1.1;
      f.HeadingH4ScaleRem = 1;
      f.HeadingMarginRem = 0.8;
      f.BlockMarginRem = 0.72;
      f.ProseParagraphMarginRem = 0.62;
      f.TableBorderColor = "#4A4A54";
      f.TableHeaderBackgroundColor = "#2A2A32";
      f.CodeBorderRadiusPx = 4;
      f.ShowRoleLabels = true;
      f.AssistantTextColor = "#E4E0D8";
      return true;
  }

  private static bool ApplyRoleForward(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ShowRoleLabels = true;
      f.UserAccentBorderWidthPx = 5;
      f.AssistantAccentBorderWidthPx = 5;
      f.UserIndentRem = 0.25;
      f.AssistantIndentRem = 0.1;
      f.UserFontWeight = 600;
      f.AssistantFontWeight = 500;
      f.UserFontFamily = FormatFontFamilies.Humanist;
      f.AssistantFontFamily = FormatFontFamilies.Serif;
      f.UserTextColor = "#A8D4FF";
      f.AssistantTextColor = "#F0ECE6";
      f.UserBackgroundColor = "#1A2A3A";
      f.AssistantBackgroundColor = "#1E1A24";
      f.UserBackgroundOpacity = 55;
      f.AssistantBackgroundOpacity = 40;
      f.UserAccentColor = "#5B9FD4";
      f.AssistantAccentColor = "#C9A0DC";
      f.UserBorderColor = "#5B9FD4";
      f.AssistantBorderColor = "#C9A0DC";
      f.SegmentDividerOpacity = 30;
      f.SegmentBorderRadiusPx = 8;
      return true;
  }

  private static bool ApplyMinimalDistraction(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.ShowSegmentDividers = false;
      f.ShowRoleLabels = false;
      f.UserAccentBorderWidthPx = 1;
      f.AssistantAccentBorderWidthPx = 1;
      f.SegmentBorderRadiusPx = 4;
      f.UserBackgroundOpacity = 0;
      f.AssistantBackgroundOpacity = 0;
      f.UserAccentColor = "#4A5564";
      f.AssistantAccentColor = "#4A5564";
      f.UserTextColor = null;
      f.AssistantTextColor = null;
      f.UserBackgroundColor = null;
      f.AssistantBackgroundColor = null;
      f.SegmentDividerColor = null;
      f.LinkColor = null;
      f.ContentMaxWidthRem = 44;
      f.UserFontFamily = FormatFontFamilies.Sans;
      f.AssistantFontFamily = FormatFontFamilies.Sans;
      f.SegmentSpacingRem = 1.2;
      return true;
  }

  private static bool ApplyCinematicWeave(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.WeaveEmbedKind = WeaveEmbedKind.Aside;
      f.WeaveEmbedMarginBlockRem = 1.25;
      f.ContentMaxWidthRem = 48;
      f.SegmentSpacingRem = 1.35;
      f.UserFontFamily = FormatFontFamilies.Sans;
      f.AssistantFontFamily = FormatFontFamilies.Literary;
      f.HeadingFontFamily = FormatFontFamilies.Garamond;
      f.AssistantFontSizeRem = 1.14;
      f.AssistantLineHeight = 1.8;
      f.UserFontSizeRem = 0.96;
      f.UserLineHeight = 1.55;
      f.ProseParagraphMarginRem = 0.88;
      f.BlockMarginRem = 0.9;
      f.UserBackgroundOpacity = 8;
      f.AssistantBackgroundOpacity = 0;
      f.UserAccentColor = "#8AAFC8";
      f.AssistantAccentColor = "#7A6E5E";
      f.AssistantTextColor = "#E8E2D8";
      f.UserTextColor = "#D0D8E0";
      f.ShowSegmentDividers = false;
      return true;
  }

  private static bool ApplyMidnightFocus(ContinuousViewFormatSettings f)
  {
      Reset(f);
      f.OverlayBackgroundColor = "#0A0B10";
      f.ContentMaxWidthRem = 44;
      f.SegmentSpacingRem = 1.3;
      f.UserFontSizeRem = 1;
      f.AssistantFontSizeRem = 1.08;
      f.UserLineHeight = 1.6;
      f.AssistantLineHeight = 1.7;
      f.UserFontFamily = FormatFontFamilies.Humanist;
      f.AssistantFontFamily = FormatFontFamilies.Serif;
      f.CodeFontFamily = FormatFontFamilies.Mono;
      f.UserTextColor = "#C5D0E0";
      f.AssistantTextColor = "#B8C4D4";
      f.UserBackgroundColor = "#121820";
      f.AssistantBackgroundColor = "#10141C";
      f.UserBackgroundOpacity = 45;
      f.AssistantBackgroundOpacity = 35;
      f.UserAccentColor = "#4A7CA8";
      f.AssistantAccentColor = "#3E6A94";
      f.LinkColor = "#6FA8D8";
      f.SegmentDividerColor = "#2A3040";
      f.SegmentDividerOpacity = 18;
      f.InlineCodeBackgroundColor = "#1A2030";
      f.CodeBackgroundColor = "#121820";
      f.CodeBorderColor = "#2E3848";
      f.ShowSegmentDividers = true;
      return true;
  }
}
