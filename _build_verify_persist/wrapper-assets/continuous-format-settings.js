/**
 * ChatGPT Wrapper — runtime continuous view format settings (CSS custom properties).
 */
(function () {
  var STYLE_ID = "cgw-continuous-view-format-css";
  var DEFAULTS = {
    contentMaxWidthRem: 42,
    overlayPaddingXRem: 1.75,
    overlayPaddingYRem: 1.5,
    segmentSpacingRem: 1.25,
    showSegmentDividers: true,
    userFontSizeRem: 0.98,
    userLineHeight: 1.55,
    assistantFontSizeRem: 1.0625,
    assistantLineHeight: 1.65,
    blockMarginRem: 0.75,
    proseParagraphMarginRem: 0.6,
    blockLetterSpacingEm: 0.01,
    enhancedProseLineHeight: 1.68,
    enhancedProseLetterSpacingEm: 0.012,
    codeFontSizeRem: 0.9375,
    codeLineHeight: 1.55,
    codeBlockPaddingRem: 0.85,
    headingMarginRem: 0.75,
    headingH1ScaleRem: 1.45,
    headingH2ScaleRem: 1.28,
    headingH3ScaleRem: 1.12,
    headingH4ScaleRem: 1.02,
    headingH5ScaleRem: 0.96,
    headingH6ScaleRem: 0.9,
    showImages: true,
    composerClearanceMinPx: 0,
    composerClearanceMaxPx: 0,
  };

  var current = normalizeSettings(globalThis.__cgwContinuousViewFormat);

  function readField(obj, camel, pascal) {
    if (!obj) return undefined;
    if (obj[camel] !== undefined && obj[camel] !== null) return obj[camel];
    if (obj[pascal] !== undefined && obj[pascal] !== null) return obj[pascal];
    return undefined;
  }

  function toNumber(value, fallback) {
    var n = Number(value);
    return Number.isFinite(n) ? n : fallback;
  }

  function toBool(value, fallback) {
    if (value === true || value === false) return value;
    if (value === "true" || value === 1) return true;
    if (value === "false" || value === 0) return false;
    return fallback;
  }

  function normalizeSettings(raw) {
    var src = raw && typeof raw === "object" ? raw : {};
    return {
      contentMaxWidthRem: toNumber(
        readField(src, "contentMaxWidthRem", "ContentMaxWidthRem"),
        DEFAULTS.contentMaxWidthRem
      ),
      overlayPaddingXRem: toNumber(
        readField(src, "overlayPaddingXRem", "OverlayPaddingXRem"),
        DEFAULTS.overlayPaddingXRem
      ),
      overlayPaddingYRem: toNumber(
        readField(src, "overlayPaddingYRem", "OverlayPaddingYRem"),
        DEFAULTS.overlayPaddingYRem
      ),
      segmentSpacingRem: toNumber(
        readField(src, "segmentSpacingRem", "SegmentSpacingRem"),
        DEFAULTS.segmentSpacingRem
      ),
      showSegmentDividers: toBool(
        readField(src, "showSegmentDividers", "ShowSegmentDividers"),
        DEFAULTS.showSegmentDividers
      ),
      userFontSizeRem: toNumber(
        readField(src, "userFontSizeRem", "UserFontSizeRem"),
        DEFAULTS.userFontSizeRem
      ),
      userLineHeight: toNumber(
        readField(src, "userLineHeight", "UserLineHeight"),
        DEFAULTS.userLineHeight
      ),
      assistantFontSizeRem: toNumber(
        readField(src, "assistantFontSizeRem", "AssistantFontSizeRem"),
        DEFAULTS.assistantFontSizeRem
      ),
      assistantLineHeight: toNumber(
        readField(src, "assistantLineHeight", "AssistantLineHeight"),
        DEFAULTS.assistantLineHeight
      ),
      blockMarginRem: toNumber(
        readField(src, "blockMarginRem", "BlockMarginRem"),
        DEFAULTS.blockMarginRem
      ),
      proseParagraphMarginRem: toNumber(
        readField(src, "proseParagraphMarginRem", "ProseParagraphMarginRem"),
        DEFAULTS.proseParagraphMarginRem
      ),
      blockLetterSpacingEm: toNumber(
        readField(src, "blockLetterSpacingEm", "BlockLetterSpacingEm"),
        DEFAULTS.blockLetterSpacingEm
      ),
      enhancedProseLineHeight: toNumber(
        readField(src, "enhancedProseLineHeight", "EnhancedProseLineHeight"),
        DEFAULTS.enhancedProseLineHeight
      ),
      enhancedProseLetterSpacingEm: toNumber(
        readField(src, "enhancedProseLetterSpacingEm", "EnhancedProseLetterSpacingEm"),
        DEFAULTS.enhancedProseLetterSpacingEm
      ),
      codeFontSizeRem: toNumber(
        readField(src, "codeFontSizeRem", "CodeFontSizeRem"),
        DEFAULTS.codeFontSizeRem
      ),
      codeLineHeight: toNumber(
        readField(src, "codeLineHeight", "CodeLineHeight"),
        DEFAULTS.codeLineHeight
      ),
      codeBlockPaddingRem: toNumber(
        readField(src, "codeBlockPaddingRem", "CodeBlockPaddingRem"),
        DEFAULTS.codeBlockPaddingRem
      ),
      headingMarginRem: toNumber(
        readField(src, "headingMarginRem", "HeadingMarginRem"),
        DEFAULTS.headingMarginRem
      ),
      headingH1ScaleRem: toNumber(
        readField(src, "headingH1ScaleRem", "HeadingH1ScaleRem"),
        DEFAULTS.headingH1ScaleRem
      ),
      headingH2ScaleRem: toNumber(
        readField(src, "headingH2ScaleRem", "HeadingH2ScaleRem"),
        DEFAULTS.headingH2ScaleRem
      ),
      headingH3ScaleRem: toNumber(
        readField(src, "headingH3ScaleRem", "HeadingH3ScaleRem"),
        DEFAULTS.headingH3ScaleRem
      ),
      headingH4ScaleRem: toNumber(
        readField(src, "headingH4ScaleRem", "HeadingH4ScaleRem"),
        DEFAULTS.headingH4ScaleRem
      ),
      headingH5ScaleRem: toNumber(
        readField(src, "headingH5ScaleRem", "HeadingH5ScaleRem"),
        DEFAULTS.headingH5ScaleRem
      ),
      headingH6ScaleRem: toNumber(
        readField(src, "headingH6ScaleRem", "HeadingH6ScaleRem"),
        DEFAULTS.headingH6ScaleRem
      ),
      showImages: toBool(
        readField(src, "showImages", "ShowImages"),
        DEFAULTS.showImages
      ),
      composerClearanceMinPx: toNumber(
        readField(src, "composerClearanceMinPx", "ComposerClearanceMinPx"),
        DEFAULTS.composerClearanceMinPx
      ),
      composerClearanceMaxPx: toNumber(
        readField(src, "composerClearanceMaxPx", "ComposerClearanceMaxPx"),
        DEFAULTS.composerClearanceMaxPx
      ),
    };
  }

  function rem(value) {
    return value + "rem";
  }

  function em(value) {
    return value + "em";
  }

  function buildCssBlock(selector, settings) {
    var borderWidth = settings.showSegmentDividers ? "1px" : "0";
    return (
      selector + " {\n" +
      "  --cgw-cv-overlay-px: " + rem(settings.overlayPaddingXRem) + ";\n" +
      "  --cgw-cv-overlay-py: " + rem(settings.overlayPaddingYRem) + ";\n" +
      "  --cgw-cv-content-max-width: " + rem(settings.contentMaxWidthRem) + ";\n" +
      "  --cgw-cv-segment-spacing: " + rem(settings.segmentSpacingRem) + ";\n" +
      "  --cgw-cv-segment-border-width: " + borderWidth + ";\n" +
      "  --cgw-cv-block-margin: " + rem(settings.blockMarginRem) + ";\n" +
      "  --cgw-cv-prose-p-margin: " + rem(settings.proseParagraphMarginRem) + ";\n" +
      "  --cgw-cv-user-font-size: " + rem(settings.userFontSizeRem) + ";\n" +
      "  --cgw-cv-user-line-height: " + settings.userLineHeight + ";\n" +
      "  --cgw-cv-assistant-font-size: " + rem(settings.assistantFontSizeRem) + ";\n" +
      "  --cgw-cv-assistant-line-height: " + settings.assistantLineHeight + ";\n" +
      "  --cgw-cv-block-font-size: " + rem(settings.assistantFontSizeRem) + ";\n" +
      "  --cgw-cv-block-line-height: " + settings.assistantLineHeight + ";\n" +
      "  --cgw-cv-block-letter-spacing: " + em(settings.blockLetterSpacingEm) + ";\n" +
      "  --cgw-cv-enhanced-prose-line-height: " + settings.enhancedProseLineHeight + ";\n" +
      "  --cgw-cv-enhanced-prose-letter-spacing: " + em(settings.enhancedProseLetterSpacingEm) + ";\n" +
      "  --cgw-cv-code-font-size: " + rem(settings.codeFontSizeRem) + ";\n" +
      "  --cgw-cv-code-line-height: " + settings.codeLineHeight + ";\n" +
      "  --cgw-cv-code-block-padding: " + rem(settings.codeBlockPaddingRem) + ";\n" +
      "  --cgw-cv-heading-margin: " + rem(settings.headingMarginRem) + ";\n" +
      "  --cgw-cv-heading-h1: " + rem(settings.headingH1ScaleRem) + ";\n" +
      "  --cgw-cv-heading-h2: " + rem(settings.headingH2ScaleRem) + ";\n" +
      "  --cgw-cv-heading-h3: " + rem(settings.headingH3ScaleRem) + ";\n" +
      "  --cgw-cv-heading-h4: " + rem(settings.headingH4ScaleRem) + ";\n" +
      "  --cgw-cv-heading-h5: " + rem(settings.headingH5ScaleRem) + ";\n" +
      "  --cgw-cv-heading-h6: " + rem(settings.headingH6ScaleRem) + ";\n" +
      "}\n"
    );
  }

  function buildCssText(settings) {
    var active =
      'html[data-cgw-continuous-view="1"] #cgw-continuous-view';
    var pending =
      'html[data-cgw-continuous-view="1"][data-cgw-cv-pending="1"] #cgw-continuous-view';
    return buildCssBlock(active, settings) + buildCssBlock(pending, settings);
  }

  function applyComposerClearanceGlobals(settings) {
    if (settings.composerClearanceMinPx > 0) {
      globalThis.__cgwComposerClearanceMinPx = settings.composerClearanceMinPx;
    } else {
      delete globalThis.__cgwComposerClearanceMinPx;
    }
    if (settings.composerClearanceMaxPx > 0) {
      globalThis.__cgwComposerClearanceMaxPx = settings.composerClearanceMaxPx;
    } else {
      delete globalThis.__cgwComposerClearanceMaxPx;
    }
  }

  function ensureStyleElement() {
    var parent = document.head || document.documentElement;
    var el = document.getElementById(STYLE_ID);
    if (!el) {
      el = document.createElement("style");
      el.id = STYLE_ID;
      parent.appendChild(el);
      return el;
    }
    if (el.parentNode) {
      el.parentNode.appendChild(el);
    } else {
      parent.appendChild(el);
    }
    return el;
  }

  function applyFormatSettings(settings, schedule) {
    current = normalizeSettings(settings);
    globalThis.__cgwContinuousViewFormat = current;
    globalThis.__cgwShowContinuousImages = current.showImages !== false;
    applyComposerClearanceGlobals(current);
    var styleEl = ensureStyleElement();
    styleEl.textContent = buildCssText(current);
    if (schedule !== false) {
      if (typeof globalThis.__cgwScheduleContinuousViewDecorationOnly === "function") {
        globalThis.__cgwScheduleContinuousViewDecorationOnly();
      } else if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
        globalThis.__cgwContinuousViewSchedule();
      }
    }
  }

  globalThis.__cgwNormalizeContinuousViewFormat = normalizeSettings;
  globalThis.__cgwBuildContinuousViewFormatCss = buildCssText;
  globalThis.__cgwSetContinuousViewFormat = applyFormatSettings;
  applyFormatSettings(current, false);
})();

