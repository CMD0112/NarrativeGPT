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
    segmentDividerOpacity: 22,
    segmentBorderRadiusPx: 6,
    userFontSizeRem: 0.98,
    userLineHeight: 1.55,
    assistantFontSizeRem: 1.0625,
    assistantLineHeight: 1.65,
    blockMarginRem: 0.75,
    proseParagraphMarginRem: 0.6,
    blockLetterSpacingEm: 0.01,
    userLetterSpacingEm: 0.01,
    assistantLetterSpacingEm: 0.01,
    userFontWeight: 400,
    assistantFontWeight: 400,
    userFontFamily: null,
    assistantFontFamily: null,
    codeFontFamily: null,
    headingFontFamily: null,
    userAccentBorderWidthPx: 3,
    assistantAccentBorderWidthPx: 3,
    userBackgroundOpacity: 0,
    assistantBackgroundOpacity: 0,
    userIndentRem: 0,
    assistantIndentRem: 0,
    showRoleLabels: false,
    enhancedProseLineHeight: 1.68,
    enhancedProseLetterSpacingEm: 0.012,
    codeFontSizeRem: 0.9375,
    codeLineHeight: 1.55,
    codeBlockPaddingRem: 0.85,
    codeBorderRadiusPx: 8,
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

  var COLOR_FIELDS = [
    ["segmentDividerColor", "SegmentDividerColor", "--cgw-cv-segment-divider-color"],
    ["overlayBackgroundColor", "OverlayBackgroundColor", "--cgw-cv-overlay-background"],
    ["userTextColor", "UserTextColor", "--cgw-cv-user-text"],
    ["userBackgroundColor", "UserBackgroundColor", "--cgw-cv-user-bg"],
    ["userAccentColor", "UserAccentColor", "--cgw-cv-user-accent"],
    ["userBorderColor", "UserBorderColor", "--cgw-cv-user-border"],
    ["assistantTextColor", "AssistantTextColor", "--cgw-cv-assistant-text"],
    ["assistantBackgroundColor", "AssistantBackgroundColor", "--cgw-cv-assistant-bg"],
    ["assistantAccentColor", "AssistantAccentColor", "--cgw-cv-assistant-accent"],
    ["assistantBorderColor", "AssistantBorderColor", "--cgw-cv-assistant-border"],
    ["linkColor", "LinkColor", "--cgw-cv-link"],
    ["linkHoverColor", "LinkHoverColor", "--cgw-cv-link-hover"],
    ["inlineCodeBackgroundColor", "InlineCodeBackgroundColor", "--cgw-cv-inline-code-bg"],
    ["codeBackgroundColor", "CodeBackgroundColor", "--cgw-cv-code-bg"],
    ["codeBorderColor", "CodeBorderColor", "--cgw-cv-code-border"],
    ["codeLangLabelColor", "CodeLangLabelColor", "--cgw-cv-code-lang-label"],
    ["tableBorderColor", "TableBorderColor", "--cgw-cv-table-border"],
    ["tableHeaderBackgroundColor", "TableHeaderBackgroundColor", "--cgw-cv-table-header-bg"],
  ];

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

  function toColor(value) {
    if (typeof value !== "string") return null;
    var trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  function normalizeSettings(raw) {
    var src = raw && typeof raw === "object" ? raw : {};
    var blockLetter = toNumber(
      readField(src, "blockLetterSpacingEm", "BlockLetterSpacingEm"),
      DEFAULTS.blockLetterSpacingEm
    );
    var userLetter = readField(src, "userLetterSpacingEm", "UserLetterSpacingEm");
    var assistantLetter = readField(src, "assistantLetterSpacingEm", "AssistantLetterSpacingEm");
    var settings = {
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
      segmentDividerOpacity: toNumber(
        readField(src, "segmentDividerOpacity", "SegmentDividerOpacity"),
        DEFAULTS.segmentDividerOpacity
      ),
      segmentBorderRadiusPx: toNumber(
        readField(src, "segmentBorderRadiusPx", "SegmentBorderRadiusPx"),
        DEFAULTS.segmentBorderRadiusPx
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
      blockLetterSpacingEm: blockLetter,
      userLetterSpacingEm: toNumber(userLetter, blockLetter),
      assistantLetterSpacingEm: toNumber(assistantLetter, blockLetter),
      userFontWeight: toNumber(
        readField(src, "userFontWeight", "UserFontWeight"),
        DEFAULTS.userFontWeight
      ),
      assistantFontWeight: toNumber(
        readField(src, "assistantFontWeight", "AssistantFontWeight"),
        DEFAULTS.assistantFontWeight
      ),
      userFontFamily: readField(src, "userFontFamily", "UserFontFamily") || null,
      assistantFontFamily: readField(src, "assistantFontFamily", "AssistantFontFamily") || null,
      codeFontFamily: readField(src, "codeFontFamily", "CodeFontFamily") || null,
      headingFontFamily: readField(src, "headingFontFamily", "HeadingFontFamily") || null,
      userAccentBorderWidthPx: toNumber(
        readField(src, "userAccentBorderWidthPx", "UserAccentBorderWidthPx"),
        DEFAULTS.userAccentBorderWidthPx
      ),
      assistantAccentBorderWidthPx: toNumber(
        readField(src, "assistantAccentBorderWidthPx", "AssistantAccentBorderWidthPx"),
        DEFAULTS.assistantAccentBorderWidthPx
      ),
      userBackgroundOpacity: toNumber(
        readField(src, "userBackgroundOpacity", "UserBackgroundOpacity"),
        DEFAULTS.userBackgroundOpacity
      ),
      assistantBackgroundOpacity: toNumber(
        readField(src, "assistantBackgroundOpacity", "AssistantBackgroundOpacity"),
        DEFAULTS.assistantBackgroundOpacity
      ),
      userIndentRem: toNumber(
        readField(src, "userIndentRem", "UserIndentRem"),
        DEFAULTS.userIndentRem
      ),
      assistantIndentRem: toNumber(
        readField(src, "assistantIndentRem", "AssistantIndentRem"),
        DEFAULTS.assistantIndentRem
      ),
      showRoleLabels: toBool(
        readField(src, "showRoleLabels", "ShowRoleLabels"),
        DEFAULTS.showRoleLabels
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
      codeBorderRadiusPx: toNumber(
        readField(src, "codeBorderRadiusPx", "CodeBorderRadiusPx"),
        DEFAULTS.codeBorderRadiusPx
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
      weaveEmbedMarginBlockRem: toNumber(
        readField(src, "weaveEmbedMarginBlockRem", "WeaveEmbedMarginBlockRem"),
        1
      ),
      weaveEmbedKind: readField(src, "weaveEmbedKind", "WeaveEmbedKind") || "blockquote",
    };

    for (var i = 0; i < COLOR_FIELDS.length; i++) {
      var field = COLOR_FIELDS[i];
      settings[field[0]] = toColor(readField(src, field[0], field[1]));
    }

    return settings;
  }

  function rem(value) {
    return value + "rem";
  }

  function em(value) {
    return value + "em";
  }

  var FONT_PRESET_STACKS = {
    sans: 'system-ui, "Segoe UI", sans-serif',
    serif: 'Georgia, "Times New Roman", serif',
    mono: 'ui-monospace, "Cascadia Code", "Segoe UI Mono", Consolas, monospace',
    humanist: '"Segoe UI", system-ui, -apple-system, BlinkMacSystemFont, sans-serif',
    literary: '"Literata", "Palatino Linotype", Palatino, Georgia, serif',
    typewriter: '"Courier New", Courier, monospace',
    charter: '"Charter", "Bitstream Charter", Georgia, serif',
    garamond: 'Garamond, "EB Garamond", "Times New Roman", serif',
  };

  function accentCenterAdjustPx(widthPx) {
    return (widthPx - 3) / 2 + "px";
  }

  function resolveFontFamilyStack(stored) {
    if (typeof stored !== "string") return null;
    var trimmed = stored.trim();
    if (!trimmed || trimmed.toLowerCase() === "inherit") return null;
    if (FONT_PRESET_STACKS[trimmed]) return FONT_PRESET_STACKS[trimmed];
    return trimmed;
  }

  function appendFontFamily(lines, cssVariable, stored) {
    var stack = resolveFontFamilyStack(stored);
    if (stack) lines.push("  " + cssVariable + ": " + stack);
  }

  function buildCssBlock(selector, settings) {
    var borderWidth = settings.showSegmentDividers ? "1px" : "0";
    var lines = [
      "  --cgw-cv-overlay-px: " + rem(settings.overlayPaddingXRem),
      "  --cgw-cv-overlay-py: " + rem(settings.overlayPaddingYRem),
      "  --cgw-cv-content-max-width: " + rem(settings.contentMaxWidthRem),
      "  --cgw-cv-segment-spacing: " + rem(settings.segmentSpacingRem),
      "  --cgw-cv-segment-border-width: " + borderWidth,
      "  --cgw-cv-segment-divider-opacity: " + settings.segmentDividerOpacity,
      "  --cgw-cv-segment-border-radius: " + settings.segmentBorderRadiusPx + "px",
      "  --cgw-cv-block-margin: " + rem(settings.blockMarginRem),
      "  --cgw-cv-prose-p-margin: " + rem(settings.proseParagraphMarginRem),
      "  --cgw-cv-user-font-size: " + rem(settings.userFontSizeRem),
      "  --cgw-cv-user-line-height: " + settings.userLineHeight,
      "  --cgw-cv-user-letter-spacing: " + em(settings.userLetterSpacingEm),
      "  --cgw-cv-user-font-weight: " + settings.userFontWeight,
      "  --cgw-cv-user-accent-border-width: " + settings.userAccentBorderWidthPx + "px",
      "  --cgw-cv-user-accent-center-adjust: " + accentCenterAdjustPx(settings.userAccentBorderWidthPx),
      "  --cgw-cv-user-indent: " + rem(settings.userIndentRem),
      "  --cgw-cv-user-bg-opacity: " + settings.userBackgroundOpacity,
      "  --cgw-cv-assistant-font-size: " + rem(settings.assistantFontSizeRem),
      "  --cgw-cv-assistant-line-height: " + settings.assistantLineHeight,
      "  --cgw-cv-assistant-letter-spacing: " + em(settings.assistantLetterSpacingEm),
      "  --cgw-cv-assistant-font-weight: " + settings.assistantFontWeight,
      "  --cgw-cv-assistant-accent-border-width: " + settings.assistantAccentBorderWidthPx + "px",
      "  --cgw-cv-assistant-accent-center-adjust: " +
        accentCenterAdjustPx(settings.assistantAccentBorderWidthPx),
      "  --cgw-cv-assistant-indent: " + rem(settings.assistantIndentRem),
      "  --cgw-cv-assistant-bg-opacity: " + settings.assistantBackgroundOpacity,
      "  --cgw-cv-enhanced-prose-line-height: " + settings.enhancedProseLineHeight,
      "  --cgw-cv-enhanced-prose-letter-spacing: " + em(settings.enhancedProseLetterSpacingEm),
      "  --cgw-cv-code-font-size: " + rem(settings.codeFontSizeRem),
      "  --cgw-cv-code-line-height: " + settings.codeLineHeight,
      "  --cgw-cv-code-block-padding: " + rem(settings.codeBlockPaddingRem),
      "  --cgw-cv-code-border-radius: " + settings.codeBorderRadiusPx + "px",
      "  --cgw-cv-heading-margin: " + rem(settings.headingMarginRem),
      "  --cgw-cv-heading-h1: " + rem(settings.headingH1ScaleRem),
      "  --cgw-cv-heading-h2: " + rem(settings.headingH2ScaleRem),
      "  --cgw-cv-heading-h3: " + rem(settings.headingH3ScaleRem),
      "  --cgw-cv-heading-h4: " + rem(settings.headingH4ScaleRem),
      "  --cgw-cv-heading-h5: " + rem(settings.headingH5ScaleRem),
      "  --cgw-cv-heading-h6: " + rem(settings.headingH6ScaleRem),
    ];

    for (var j = 0; j < COLOR_FIELDS.length; j++) {
      var colorField = COLOR_FIELDS[j];
      var colorValue = settings[colorField[0]];
      if (colorValue) {
        lines.push("  " + colorField[2] + ": " + colorValue);
      }
    }

    appendFontFamily(lines, "--cgw-cv-user-font-family", settings.userFontFamily);
    appendFontFamily(lines, "--cgw-cv-assistant-font-family", settings.assistantFontFamily);
    appendFontFamily(lines, "--cgw-cv-code-font-family", settings.codeFontFamily);
    appendFontFamily(lines, "--cgw-cv-heading-font-family", settings.headingFontFamily);

    return selector + " {\n" + lines.join(";\n") + ";\n}\n";
  }

  function buildWeaveCssBlock(settings) {
    var embedKind = settings.weaveEmbedKind || settings.WeaveEmbedKind || "blockquote";
    embedKind = String(embedKind).toLowerCase();
    var lines = [
      "  --cgw-weave-content-max-width: " + rem(settings.contentMaxWidthRem),
      "  --cgw-weave-paragraph-gap: " + rem(settings.proseParagraphMarginRem),
      "  --cgw-weave-embed-margin-block: " + rem(settings.weaveEmbedMarginBlockRem || 0),
      "  --cgw-weave-body-font-size: " + rem(settings.assistantFontSizeRem),
      "  --cgw-weave-body-line-height: " + settings.assistantLineHeight,
      "  --cgw-weave-body-letter-spacing: " + em(settings.assistantLetterSpacingEm),
      "  --cgw-weave-body-font-weight: " + settings.assistantFontWeight,
      "  --cgw-weave-embed-font-size: " + rem(settings.userFontSizeRem),
      "  --cgw-weave-embed-line-height: " + settings.userLineHeight,
      "  --cgw-weave-embed-letter-spacing: " + em(settings.userLetterSpacingEm),
      "  --cgw-weave-embed-font-weight: " + settings.userFontWeight,
      "  --cgw-weave-embed-accent-width: " + settings.userAccentBorderWidthPx + "px",
      "  --cgw-weave-embed-accent-center-adjust: " + accentCenterAdjustPx(settings.userAccentBorderWidthPx),
      "  --cgw-weave-embed-kind-preset: " + embedKind,
    ];
    appendFontFamily(lines, "--cgw-weave-body-font-family", settings.assistantFontFamily);
    appendFontFamily(lines, "--cgw-weave-embed-font-family", settings.userFontFamily);
    if (settings.assistantTextColor)
      lines.push("  --cgw-weave-body-text: " + settings.assistantTextColor);
    if (settings.userTextColor)
      lines.push("  --cgw-weave-embed-text: " + settings.userTextColor);
    if (settings.userBackgroundColor) {
      lines.push("  --cgw-weave-embed-bg: " + settings.userBackgroundColor);
      lines.push("  --cgw-weave-embed-aside-bg: " + settings.userBackgroundColor);
    }
    if (settings.userAccentColor)
      lines.push("  --cgw-weave-embed-accent: " + settings.userAccentColor);
    return (
      'html[data-cgw-transcript-mode="weave"] #cgw-continuous-view.cgw-weave-view {\n' +
      lines.join(";\n") +
      ";\n}\n"
    );
  }

  function buildCssText(settings) {
    var active =
      'html[data-cgw-continuous-view="1"] #cgw-continuous-view';
    var pending =
      'html[data-cgw-continuous-view="1"][data-cgw-cv-pending="1"] #cgw-continuous-view';
    var css =
      buildCssBlock(active, settings) + buildCssBlock(pending, settings);
    if (globalThis.__cgwTranscriptViewMode === "weave") {
      css += buildWeaveCssBlock(settings);
    }
    return css;
  }

  function applyRoleLabelAttribute(settings) {
    var root = document.documentElement;
    if (!root) return;
    if (settings.showRoleLabels) {
      root.setAttribute("data-cgw-cv-show-role-labels", "1");
    } else {
      root.removeAttribute("data-cgw-cv-show-role-labels");
    }
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
    applyRoleLabelAttribute(current);
    if (schedule !== false) {
      globalThis.__cgwFormatSettingsRevision =
        (typeof globalThis.__cgwFormatSettingsRevision === "number"
          ? globalThis.__cgwFormatSettingsRevision
          : 0) + 1;
    }
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
