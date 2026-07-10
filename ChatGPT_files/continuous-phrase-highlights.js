/**
 * ChatGPT Wrapper — custom phrase highlighting for continuous view.
 */
(function () {
  var STYLE_ID = "cgw-phrase-highlight-styles";
  var HIGHLIGHT_CLASS = "cgw-phrase-highlight";
  var DECORATE_DEBOUNCE_MS = 50;
  var LARGE_RULE_SET = 100;
  var decorateTimer = null;
  var CSS_SCOPE =
    "html[data-cgw-continuous-view=\"1\"] .cgw-phrase-highlight.cgw-phrase-h-";
  var FIRST_NAME_ALIAS_STOPWORDS = {
    the: 1,
    a: 1,
    an: 1,
    mother: 1,
    father: 1,
    captain: 1,
    king: 1,
    queen: 1,
    lord: 1,
    lady: 1,
    sir: 1,
    dame: 1,
    examiner: 1,
    priest: 1,
    sister: 1,
    brother: 1,
    uncle: 1,
    aunt: 1,
    old: 1,
    young: 1,
    lost: 1,
    true: 1,
    red: 1,
    black: 1,
  };
  var DEFAULT_CANVAS = "#161618";
  var MIN_BODY_RATIO = 4.5;
  var RATIO_TOLERANCE = 0.02;
  var DEFAULT_BOLD_WEIGHT_DELTA = 300;
  var USER_SEGMENT_SELECTOR =
    'html[data-cgw-continuous-view="1"] .cgw-continuous-segment--user .cgw-phrase-highlight.cgw-phrase-h-';
  var ASSISTANT_SEGMENT_SELECTOR =
    'html[data-cgw-continuous-view="1"] .cgw-continuous-segment--assistant .cgw-phrase-highlight.cgw-phrase-h-';
  var WEAVE_USER_SELECTOR =
    'html[data-cgw-transcript-mode="weave"] .cgw-weave-embed .cgw-phrase-highlight.cgw-phrase-h-';
  var WEAVE_ASSISTANT_SELECTOR =
    'html[data-cgw-transcript-mode="weave"] .cgw-weave-body .cgw-phrase-highlight.cgw-phrase-h-';

  var enabled = !!globalThis.__cgwPhraseHighlightsEnabled;
  var rules = normalizeRules(globalThis.__cgwPhraseHighlightRules);
  var compiledRules = compileRules(rules);

  function readRuleField(rule, camel, pascal) {
    if (!rule) return undefined;
    if (rule[camel] !== undefined && rule[camel] !== null) return rule[camel];
    if (rule[pascal] !== undefined && rule[pascal] !== null) return rule[pascal];
    return undefined;
  }

  function readOptionalNumber(raw) {
    if (raw === undefined || raw === null || raw === "") return null;
    var n = typeof raw === "number" ? raw : parseFloat(raw);
    return Number.isFinite(n) ? n : null;
  }

  function readOptionalInt(raw) {
    if (raw === undefined || raw === null || raw === "") return null;
    var n = typeof raw === "number" ? raw : parseInt(raw, 10);
    return Number.isFinite(n) ? n : null;
  }

  function readOptionalString(raw) {
    if (raw === undefined || raw === null) return "";
    var s = String(raw).trim();
    return s;
  }

  function normalizeRules(raw) {
    if (!Array.isArray(raw)) return [];
    return raw
      .map(function (r) {
        var phrase = readRuleField(r, "phrase", "Phrase");
        if (typeof phrase !== "string" || !phrase.trim()) return null;
        var ruleEnabled = readRuleField(r, "enabled", "Enabled");
        if (ruleEnabled === false) return null;
        return {
          phrase: phrase.trim(),
          color: readRuleField(r, "color", "Color"),
          backgroundColor: readRuleField(r, "backgroundColor", "BackgroundColor"),
          fontWeight: readOptionalInt(readRuleField(r, "fontWeight", "FontWeight")),
          bold: !!readRuleField(r, "bold", "Bold"),
          italic: !!readRuleField(r, "italic", "Italic"),
          underline: !!readRuleField(r, "underline", "Underline"),
          strikethrough: !!readRuleField(r, "strikethrough", "Strikethrough"),
          fontSizeScale: readOptionalNumber(readRuleField(r, "fontSizeScale", "FontSizeScale")),
          letterSpacingEm: readOptionalNumber(readRuleField(r, "letterSpacingEm", "LetterSpacingEm")),
          fontFamily: readOptionalString(readRuleField(r, "fontFamily", "FontFamily")),
          textTransform: readOptionalString(readRuleField(r, "textTransform", "TextTransform")),
          opacity: readOptionalNumber(readRuleField(r, "opacity", "Opacity")),
          borderColor: readOptionalString(readRuleField(r, "borderColor", "BorderColor")),
          borderWidthPx: readOptionalInt(readRuleField(r, "borderWidthPx", "BorderWidthPx")),
          borderRadiusPx: readOptionalInt(readRuleField(r, "borderRadiusPx", "BorderRadiusPx")),
          paddingXEm: readOptionalNumber(readRuleField(r, "paddingXEm", "PaddingXEm")),
          paddingYEm: readOptionalNumber(readRuleField(r, "paddingYEm", "PaddingYEm")),
          textShadow: readOptionalString(readRuleField(r, "textShadow", "TextShadow")),
          boxShadow: readOptionalString(readRuleField(r, "boxShadow", "BoxShadow")),
          entityId: readRuleField(r, "entityId", "EntityId"),
          entityCategory: readRuleField(r, "entityCategory", "EntityCategory"),
        };
      })
      .filter(Boolean)
      .map(function (r, index) {
        return {
          index: index,
          phrase: r.phrase,
          color: sanitizeHex(r.color, "#FFD166"),
          backgroundColor: r.backgroundColor
            ? sanitizeHex(r.backgroundColor, "")
            : "",
          fontWeight: r.fontWeight,
          bold: r.bold,
          italic: r.italic,
          underline: r.underline,
          strikethrough: r.strikethrough,
          fontSizeScale: r.fontSizeScale,
          letterSpacingEm: r.letterSpacingEm,
          fontFamily: r.fontFamily,
          textTransform: r.textTransform,
          opacity: r.opacity,
          borderColor: r.borderColor ? sanitizeHex(r.borderColor, "") : "",
          borderWidthPx: r.borderWidthPx,
          borderRadiusPx: r.borderRadiusPx,
          paddingXEm: r.paddingXEm,
          paddingYEm: r.paddingYEm,
          textShadow: r.textShadow,
          boxShadow: r.boxShadow,
          entityId: r.entityId,
          entityCategory: r.entityCategory,
        };
      });
  }

  function ruleStyleFingerprintPart(r) {
    return [
      r.index,
      r.phrase,
      r.color,
      r.backgroundColor,
      r.fontWeight == null ? "" : r.fontWeight,
      r.bold ? 1 : 0,
      r.italic ? 1 : 0,
      r.underline ? 1 : 0,
      r.strikethrough ? 1 : 0,
      r.fontSizeScale == null ? "" : r.fontSizeScale,
      r.letterSpacingEm == null ? "" : r.letterSpacingEm,
      r.fontFamily || "",
      r.textTransform || "",
      r.opacity == null ? "" : r.opacity,
      r.borderColor || "",
      r.borderWidthPx == null ? "" : r.borderWidthPx,
      r.borderRadiusPx == null ? "" : r.borderRadiusPx,
      r.paddingXEm == null ? "" : r.paddingXEm,
      r.paddingYEm == null ? "" : r.paddingYEm,
      r.textShadow || "",
      r.boxShadow || "",
    ].join("\x01");
  }

  function rulesStyleFingerprint(list) {
    return (enabled ? "1" : "0") + "\x05" +
      list.map(ruleStyleFingerprintPart).join("\x02");
  }

  function sanitizeHex(value, fallback) {
    var v = String(value || "").trim();
    if (!v) return fallback;
    if (v.charAt(0) !== "#") v = "#" + v;
    return /^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$/.test(v) ? v : fallback;
  }

  function parseHexColor(hex) {
    var v = sanitizeHex(hex, "");
    if (!v) return null;
    if (v.length === 4) {
      return {
        r: parseInt(v.charAt(1) + v.charAt(1), 16),
        g: parseInt(v.charAt(2) + v.charAt(2), 16),
        b: parseInt(v.charAt(3) + v.charAt(3), 16),
      };
    }
    return {
      r: parseInt(v.slice(1, 3), 16),
      g: parseInt(v.slice(3, 5), 16),
      b: parseInt(v.slice(5, 7), 16),
    };
  }

  function relativeLuminance(hex) {
    var rgb = parseHexColor(hex);
    if (!rgb) return -1;
    function channel(value) {
      var c = value / 255;
      return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    }
    var r = channel(rgb.r);
    var g = channel(rgb.g);
    var b = channel(rgb.b);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }

  function contrastRatio(fg, bg) {
    var fgL = relativeLuminance(fg);
    var bgL = relativeLuminance(bg);
    if (fgL < 0 || bgL < 0) return 1;
    var lighter = Math.max(fgL, bgL);
    var darker = Math.min(fgL, bgL);
    return (lighter + 0.05) / (darker + 0.05);
  }

  function isReadable(fg, bg) {
    return contrastRatio(fg, bg) + RATIO_TOLERANCE >= MIN_BODY_RATIO;
  }

  function nudgeTowardReadable(fg, bg) {
    var bgLum = relativeLuminance(bg);
    if (bgLum < 0) return fg;
    return bgLum < 0.5 ? lighten(fg, 0.08) : darken(fg, 0.08);
  }

  function lighten(hex, amount) {
    var rgb = parseHexColor(hex);
    if (!rgb) return hex;
    function clamp(v) {
      return Math.max(0, Math.min(255, Math.round(v)));
    }
    return (
      "#" +
      [rgb.r, rgb.g, rgb.b]
        .map(function (c) {
          return clamp(c + amount * 255).toString(16).padStart(2, "0");
        })
        .join("")
    );
  }

  function darken(hex, amount) {
    return lighten(hex, -amount);
  }

  function pickExtremeReadable(bg) {
    var white = "#FFFFFF";
    var black = "#000000";
    return contrastRatio(white, bg) >= contrastRatio(black, bg) ? white : black;
  }

  function ensureReadable(fg, bg) {
    var normalized = sanitizeHex(fg, fg);
    var canvas = sanitizeHex(bg, DEFAULT_CANVAS) || DEFAULT_CANVAS;
    if (isReadable(normalized, canvas)) return normalized;
    var adjusted = normalized;
    for (var pass = 0; pass < 32; pass++) {
      if (isReadable(adjusted, canvas)) return adjusted;
      adjusted = nudgeTowardReadable(adjusted, canvas);
    }
    return pickExtremeReadable(canvas);
  }

  function effectiveBackgroundForRule(rule) {
    return rule && rule.backgroundColor
      ? rule.backgroundColor
      : DEFAULT_CANVAS;
  }

  function resolveEffectiveBackground(el, rule) {
    if (rule && rule.backgroundColor) return rule.backgroundColor;
    var node = el;
    while (node && node.nodeType === 1) {
      try {
        var style = globalThis.getComputedStyle(node);
        var bg = style.backgroundColor;
        if (bg && bg !== "transparent" && bg !== "rgba(0, 0, 0, 0)") {
          var match = bg.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
          if (match) {
            return (
              "#" +
              [match[1], match[2], match[3]]
                .map(function (v) {
                  return parseInt(v, 10).toString(16).padStart(2, "0");
                })
                .join("")
            );
          }
        }
      } catch (_e) {
        /* ignore */
      }
      node = node.parentElement;
    }
    return DEFAULT_CANVAS;
  }

  function displayColorForRule(rule, background) {
    if (!rule || !rule.color) return rule ? rule.color : "";
    return ensureReadable(rule.color, background || effectiveBackgroundForRule(rule));
  }

  function getRuleByIndex(index) {
    for (var i = 0; i < rules.length; i++) {
      if (rules[i].index === index) return rules[i];
    }
    return rules[index] || null;
  }

  function typographyHandledByStylesheet() {
    return enabled && rules.length > 0 && rules.length <= LARGE_RULE_SET;
  }

  function readBoldWeightDelta() {
    var container = document.getElementById("cgw-continuous-view");
    if (!container) return DEFAULT_BOLD_WEIGHT_DELTA;
    var raw = getComputedStyle(container)
      .getPropertyValue("--cgw-hl-bold-weight-delta")
      .trim();
    var n = parseInt(raw, 10);
    return Number.isFinite(n) ? n : DEFAULT_BOLD_WEIGHT_DELTA;
  }

  function clampFontWeight(weight) {
    return Math.min(900, Math.max(100, weight));
  }

  function readRoleBaseFontWeight(el) {
    if (!el || !el.closest) return 400;
    var segment = el.closest(
      ".cgw-continuous-segment--user, .cgw-continuous-segment--assistant, .cgw-weave-embed, .cgw-weave-body"
    );
    if (!segment) return 400;
    var isUser =
      segment.classList.contains("cgw-continuous-segment--user") ||
      segment.classList.contains("cgw-weave-embed");
    var cssVar = isUser
      ? "--cgw-cv-user-font-weight"
      : "--cgw-cv-assistant-font-weight";
    var weaveVar = isUser
      ? "--cgw-weave-embed-font-weight"
      : "--cgw-weave-body-font-weight";
    var container = document.getElementById("cgw-continuous-view");
    if (container) {
      var style = getComputedStyle(container);
      var fromWeave = parseInt(style.getPropertyValue(weaveVar).trim(), 10);
      if (Number.isFinite(fromWeave)) return fromWeave;
      var fromVar = parseInt(style.getPropertyValue(cssVar).trim(), 10);
      if (Number.isFinite(fromVar)) return fromVar;
    }
    var block = segment.querySelector(
      ".cgw-continuous-block:not(.cgw-continuous-block--code)"
    );
    if (block) {
      var computed = parseInt(getComputedStyle(block).fontWeight, 10);
      if (Number.isFinite(computed)) return computed;
    }
    return 400;
  }

  function composeHighlightFontWeight(el, highlightBold) {
    if (!highlightBold) return null;
    return clampFontWeight(readRoleBaseFontWeight(el) + readBoldWeightDelta());
  }

  function resolveHighlightFontWeight(el, rule) {
    if (!rule) return null;
    if (rule.fontWeight !== null && rule.fontWeight !== undefined) {
      return clampFontWeight(rule.fontWeight);
    }
    if (rule.bold) {
      return composeHighlightFontWeight(el, true);
    }
    return null;
  }

  function buildTextDecoration(rule) {
    if (!rule) return "";
    var parts = [];
    if (rule.underline) parts.push("underline");
    if (rule.strikethrough) parts.push("line-through");
    return parts.join(" ");
  }

  function buildRuleVisualCss(rule, el) {
    if (!rule) return "";
    var parts = [];
    var bg = el ? resolveEffectiveBackground(el, rule) : effectiveBackgroundForRule(rule);
    var color = displayColorForRule(rule, bg);
    if (color) parts.push("color:" + color + " !important");
    if (rule.backgroundColor) {
      parts.push("background-color:" + rule.backgroundColor + " !important");
    }
    var weight = el ? resolveHighlightFontWeight(el, rule) : null;
    if (weight === null && rule.fontWeight !== null && rule.fontWeight !== undefined) {
      weight = clampFontWeight(rule.fontWeight);
    }
    if (weight !== null) {
      parts.push("font-weight:" + weight + " !important");
    }
    if (rule.italic) parts.push("font-style:italic !important");
    var decoration = buildTextDecoration(rule);
    if (decoration) parts.push("text-decoration:" + decoration + " !important");
    if (rule.fontSizeScale !== null && rule.fontSizeScale !== undefined) {
      parts.push("font-size:calc(1em * " + rule.fontSizeScale + ") !important");
    }
    if (rule.letterSpacingEm !== null && rule.letterSpacingEm !== undefined) {
      parts.push("letter-spacing:" + rule.letterSpacingEm + "em !important");
    }
    if (rule.fontFamily) {
      parts.push("font-family:" + rule.fontFamily + " !important");
    }
    if (rule.textTransform) {
      parts.push("text-transform:" + rule.textTransform + " !important");
    }
    if (rule.opacity !== null && rule.opacity !== undefined) {
      parts.push("opacity:" + rule.opacity + " !important");
    }
    if (rule.borderWidthPx) {
      var borderColor = rule.borderColor || "currentColor";
      parts.push(
        "border:" + rule.borderWidthPx + "px solid " + borderColor + " !important"
      );
    }
    if (rule.borderRadiusPx) {
      parts.push("border-radius:" + rule.borderRadiusPx + "px !important");
    }
    if (rule.paddingXEm !== null || rule.paddingYEm !== null) {
      var py = rule.paddingYEm != null ? rule.paddingYEm : 0;
      var px = rule.paddingXEm != null ? rule.paddingXEm : 0;
      parts.push("padding:" + py + "em " + px + "em !important");
    }
    if (rule.textShadow) parts.push("text-shadow:" + rule.textShadow + " !important");
    if (rule.boxShadow) parts.push("box-shadow:" + rule.boxShadow + " !important");
    return parts.join(";");
  }

  function applyTypographyToElement(el, rule) {
    if (!el || !rule) return;
    var weight = resolveHighlightFontWeight(el, rule);
    if (weight !== null) {
      el.style.setProperty("font-weight", String(weight), "important");
    } else {
      el.style.removeProperty("font-weight");
    }
    if (rule.italic) {
      el.style.setProperty("font-style", "italic", "important");
    } else {
      el.style.removeProperty("font-style");
    }
    var decoration = buildTextDecoration(rule);
    if (decoration) {
      el.style.setProperty("text-decoration", decoration, "important");
    } else {
      el.style.removeProperty("text-decoration");
    }
    if (rule.fontSizeScale !== null && rule.fontSizeScale !== undefined) {
      el.style.setProperty(
        "font-size",
        "calc(1em * " + rule.fontSizeScale + ")",
        "important"
      );
    } else {
      el.style.removeProperty("font-size");
    }
    if (rule.letterSpacingEm !== null && rule.letterSpacingEm !== undefined) {
      el.style.setProperty(
        "letter-spacing",
        rule.letterSpacingEm + "em",
        "important"
      );
    } else {
      el.style.removeProperty("letter-spacing");
    }
    if (rule.fontFamily) {
      el.style.setProperty("font-family", rule.fontFamily, "important");
    } else {
      el.style.removeProperty("font-family");
    }
    if (rule.textTransform) {
      el.style.setProperty("text-transform", rule.textTransform, "important");
    } else {
      el.style.removeProperty("text-transform");
    }
    if (rule.opacity !== null && rule.opacity !== undefined) {
      el.style.setProperty("opacity", String(rule.opacity), "important");
    } else {
      el.style.removeProperty("opacity");
    }
    if (rule.borderWidthPx) {
      var borderColor = rule.borderColor || "currentColor";
      el.style.setProperty(
        "border",
        rule.borderWidthPx + "px solid " + borderColor,
        "important"
      );
    } else {
      el.style.removeProperty("border");
    }
    if (rule.borderRadiusPx) {
      el.style.setProperty(
        "border-radius",
        rule.borderRadiusPx + "px",
        "important"
      );
    } else {
      el.style.removeProperty("border-radius");
    }
    if (rule.paddingXEm !== null || rule.paddingYEm !== null) {
      var py = rule.paddingYEm != null ? rule.paddingYEm : 0;
      var px = rule.paddingXEm != null ? rule.paddingXEm : 0;
      el.style.setProperty("padding", py + "em " + px + "em", "important");
    } else {
      el.style.removeProperty("padding");
    }
    if (rule.textShadow) {
      el.style.setProperty("text-shadow", rule.textShadow, "important");
    } else {
      el.style.removeProperty("text-shadow");
    }
    if (rule.boxShadow) {
      el.style.setProperty("box-shadow", rule.boxShadow, "important");
    } else {
      el.style.removeProperty("box-shadow");
    }
  }

  function boldWeightCssDeclaration(roleWeightVar, weaveWeightVar) {
    var baseWeight =
      "var(" +
      weaveWeightVar +
      ",var(" +
      roleWeightVar +
      ",400))";
    return (
      "font-weight:min(900,calc(" +
      baseWeight +
      " + var(--cgw-hl-bold-weight-delta," +
      DEFAULT_BOLD_WEIGHT_DELTA +
      ")))!important;"
    );
  }

  function applyRuleStyleToElement(el, rule) {
    if (!el || !rule) return;
    var bg = resolveEffectiveBackground(el, rule);
    var color = displayColorForRule(rule, bg);
    if (color) el.style.setProperty("color", color, "important");
    if (rule.backgroundColor) {
      el.style.setProperty("background-color", rule.backgroundColor, "important");
    }
    if (!typographyHandledByStylesheet()) {
      applyTypographyToElement(el, rule);
    }
  }

  function buildInlineStyleAttr(rule, background) {
    return buildRuleVisualCss(rule, null);
  }

  function phraseEndsWithPossessive(phrase) {
    return /['\u2019]s?$/i.test(String(phrase).trim());
  }

  function getPhraseWords(phrase) {
    return String(phrase).trim().split(/\s+/).filter(Boolean);
  }

  function startsWithArticle(phrase) {
    return /^(the|a|an)(\s+|$)/i.test(String(phrase).trim());
  }

  function isCapitalizedWord(word) {
    if (!word) return false;
    var ch = word.charAt(0);
    return ch === ch.toUpperCase() && ch !== ch.toLowerCase();
  }

  function isAllLowerWords(words) {
    return words.every(function (word) {
      var stripped = word.replace(/['\u2019]s?$/i, "");
      return stripped === stripped.toLocaleLowerCase();
    });
  }

  function expandSlashVariants(phrase) {
    if (phrase.indexOf("/") === -1) return [phrase];
    return phrase
      .split(/\s*\/\s*/)
      .map(function (part) {
        return part.trim();
      })
      .filter(Boolean);
  }

  function classifyPhraseProfile(phrase, rule) {
    var trimmed = String(phrase).trim();
    if (trimmed.indexOf("/") >= 0) return "slashVariants";

    var words = getPhraseWords(trimmed);
    if (words.length <= 1) {
      if (words.length === 1 && words[0].indexOf("-") >= 0) return "descriptive";
      return "single";
    }

    if (rule.entityId && !startsWithArticle(trimmed)) return "properName";

    if (startsWithArticle(trimmed) || isAllLowerWords(words)) {
      return "descriptive";
    }

    if (words.length >= 2 && isCapitalizedWord(words[0])) return "properName";

    if (isCapitalizedWord(words[words.length - 1])) return "titledName";

    return "descriptive";
  }

  function getFirstNameAlias(phrase, profile, rule) {
    if (!rule.entityId || profile !== "properName") return null;
    var parts = getPhraseWords(phrase);
    if (parts.length < 2) return null;
    var first = parts[0];
    if (!first || !isCapitalizedWord(first)) return null;
    if (FIRST_NAME_ALIAS_STOPWORDS[first.toLocaleLowerCase()]) return null;
    return first;
  }

  function compileRuleNeedles(rule) {
    var phrase = rule.phrase;
    var profile = classifyPhraseProfile(phrase, rule);
    var variants = profile === "slashVariants" ? expandSlashVariants(phrase) : [phrase];
    var needles = [];
    var seen = {};

    function addNeedle(text, tier) {
      if (!text) return;
      var key = text.toLocaleLowerCase();
      if (seen[key]) return;
      seen[key] = 1;
      needles.push({
        text: text,
        lower: key,
        tier: tier || 1,
      });
    }

    variants.forEach(function (variant) {
      addNeedle(variant, 1);
      var alias = getFirstNameAlias(variant, profile, rule);
      if (alias) addNeedle(alias, 2);
    });

    needles.sort(function (a, b) {
      return a.tier - b.tier || b.text.length - a.text.length;
    });

    return { profile: profile, needles: needles };
  }

  function extendMatchForPossessive(text, end) {
    if (end >= text.length) return end;
    var ch = text.charAt(end);
    if (ch !== "'" && ch !== "\u2019") return end;
    if (end + 1 < text.length && text.charAt(end + 1).toLowerCase() === "s") {
      return end + 2;
    }
    return end + 1;
  }

  function compileRules(list) {
    return list
      .slice()
      .sort(function (a, b) {
        return b.phrase.length - a.phrase.length;
      })
      .map(function (rule) {
        var compiled = compileRuleNeedles(rule);
        return {
          index: rule.index,
          phrase: rule.phrase,
          profile: compiled.profile,
          needles: compiled.needles,
        };
      });
  }

  function rebuildStylesheet() {
    var head = document.head || document.getElementsByTagName("head")[0];
    if (!head) return;

    var el = document.getElementById(STYLE_ID);
    if (!el) {
      el = document.createElement("style");
      el.id = STYLE_ID;
      head.appendChild(el);
    }

    if (!enabled || !rules.length) {
      el.textContent = "";
      return;
    }

    if (rules.length > LARGE_RULE_SET) {
      el.textContent = "";
      return;
    }

    var css = rules
      .map(function (rule) {
        var parts = [];
        var bg = effectiveBackgroundForRule(rule);
        var color = displayColorForRule(rule, bg);
        if (color) {
          parts.push(CSS_SCOPE + rule.index + "{color:" + color + "!important;}");
        }
        if (rule.backgroundColor) {
          parts.push(
            CSS_SCOPE +
              rule.index +
              "{background-color:" +
              rule.backgroundColor +
              "!important;}"
          );
        }

        var visual = buildRuleVisualCss(rule, null);
        if (visual) {
          parts.push(CSS_SCOPE + rule.index + "{" + visual + "}");
        }

        if (rule.bold && (rule.fontWeight === null || rule.fontWeight === undefined)) {
          parts.push(
            USER_SEGMENT_SELECTOR +
              rule.index +
              "{" +
              boldWeightCssDeclaration(
                "--cgw-cv-user-font-weight",
                "--cgw-weave-embed-font-weight"
              ) +
              "}"
          );
          parts.push(
            ASSISTANT_SEGMENT_SELECTOR +
              rule.index +
              "{" +
              boldWeightCssDeclaration(
                "--cgw-cv-assistant-font-weight",
                "--cgw-weave-body-font-weight"
              ) +
              "}"
          );
          parts.push(
            WEAVE_USER_SELECTOR +
              rule.index +
              "{" +
              boldWeightCssDeclaration(
                "--cgw-cv-user-font-weight",
                "--cgw-weave-embed-font-weight"
              ) +
              "}"
          );
          parts.push(
            WEAVE_ASSISTANT_SELECTOR +
              rule.index +
              "{" +
              boldWeightCssDeclaration(
                "--cgw-cv-assistant-font-weight",
                "--cgw-weave-body-font-weight"
              ) +
              "}"
          );
        }

        return parts.join("\n");
      })
      .join("\n");

    el.textContent = css;
  }

  function clearRenderFingerprints() {
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
  }

  function shouldHighlightBlock(block) {
    if (!block || !block.kind) return false;
    if (
      block.kind === "code" ||
      block.kind === "table" ||
      block.kind === "image" ||
      block.kind === "hr" ||
      block.kind === "math"
    ) {
      return false;
    }
    if (block.html) return true;
    if (block.kind === "fallback" && block.text) return true;
    return false;
  }

  function escapeHtml(text) {
    return String(text)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function wrapMatch(index, text) {
    var rule = getRuleByIndex(index);
    var style = buildInlineStyleAttr(rule);
    return (
      '<span class="' +
      HIGHLIGHT_CLASS +
      " cgw-phrase-h-" +
      index +
      '"' +
      (style ? ' style="' + style + '"' : "") +
      ">" +
      escapeHtml(text) +
      "</span>"
    );
  }

  function isWordBoundary(text, start, end) {
    var before = start > 0 ? text.charAt(start - 1) : "";
    var after = end < text.length ? text.charAt(end) : "";
    if (before && /[\p{L}\p{N}_]/u.test(before)) return false;
    if (before === "-") return false;
    if (after && /[\p{L}\p{N}_]/u.test(after)) return false;
    return true;
  }

  function findMatchesInText(text, rule) {
    var needles = rule.needles || [];
    if (!needles.length) return [];

    var textLower = text.toLocaleLowerCase();
    var matches = [];

    needles.forEach(function (needle) {
      var idx = 0;
      while (idx < text.length) {
        var found = textLower.indexOf(needle.lower, idx);
        if (found === -1) break;
        var end = found + needle.lower.length;
        if (!isWordBoundary(text, found, end)) {
          idx = found + 1;
          continue;
        }
        if (!phraseEndsWithPossessive(rule.phrase)) {
          end = extendMatchForPossessive(text, end);
        }
        matches.push({ start: found, end: end });
        idx = found + 1;
      }
    });

    matches.sort(function (a, b) {
      return a.start - b.start || b.end - a.end;
    });

    var filtered = [];
    var cursor = 0;
    matches.forEach(function (match) {
      if (match.start < cursor) return;
      filtered.push(match);
      cursor = match.end;
    });
    return filtered;
  }

  function applyScannedRuleToSegments(segments, rule) {
    var out = [];
    segments.forEach(function (seg) {
      if (seg.type === "markup") {
        out.push(seg);
        return;
      }

      var text = seg.value;
      if (!text) return;

      var matches = findMatchesInText(text, rule);
      if (!matches.length) {
        out.push(seg);
        return;
      }

      var last = 0;
      matches.forEach(function (match) {
        if (match.start > last) {
          out.push({ type: "text", value: text.slice(last, match.start) });
        }
        out.push({
          type: "markup",
          value: wrapMatch(rule.index, text.slice(match.start, match.end)),
        });
        last = match.end;
      });

      if (last < text.length) {
        out.push({ type: "text", value: text.slice(last) });
      }
    });

    return out.length ? out : segments;
  }

  function applyRuleToSegments(segments, rule) {
    return applyScannedRuleToSegments(segments, rule);
  }

  function highlightTextValue(text) {
    if (!text || !compiledRules.length) return text;

    var segments = [{ type: "text", value: String(text) }];

    compiledRules.forEach(function (rule) {
      segments = applyRuleToSegments(segments, rule);
    });

    return segments
      .map(function (seg) {
        return seg.type === "markup" ? seg.value : escapeHtml(seg.value);
      })
      .join("");
  }

  function shouldSkipHighlightNode(node) {
    if (!node || node.nodeType !== 1) return false;
    var tag = node.tagName ? node.tagName.toLowerCase() : "";
    if (tag === "code" || tag === "pre") return true;
    if (node.classList && node.classList.contains(HIGHLIGHT_CLASS)) return true;
    return false;
  }

  function replaceTextNodeWithHighlights(textNode) {
    var raw = textNode.nodeValue;
    if (!raw) return false;

    var highlighted = highlightTextValue(raw);
    if (!highlighted || highlighted === escapeHtml(raw)) return false;

    var parent = textNode.parentNode;
    if (!parent) return false;

    var spanWrap = document.createElement("span");
    spanWrap.innerHTML = highlighted;
    Array.prototype.forEach.call(spanWrap.querySelectorAll("." + HIGHLIGHT_CLASS), function (el) {
      var cls = el.className || "";
      var match = cls.match(/cgw-phrase-h-(\d+)/);
      if (match) applyRuleStyleToElement(el, getRuleByIndex(parseInt(match[1], 10)));
    });

    while (spanWrap.firstChild) {
      parent.insertBefore(spanWrap.firstChild, textNode);
    }
    parent.removeChild(textNode);
    return true;
  }

  function highlightDomTree(root) {
    if (!root || !compiledRules.length) return;

    var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode: function (node) {
        if (!node.nodeValue || !/\S/.test(node.nodeValue)) {
          return NodeFilter.FILTER_REJECT;
        }
        var parent = node.parentElement;
        while (parent && parent !== root) {
          if (shouldSkipHighlightNode(parent)) {
            return NodeFilter.FILTER_REJECT;
          }
          parent = parent.parentElement;
        }
        return NodeFilter.FILTER_ACCEPT;
      },
    });

    var textNodes = [];
    while (walker.nextNode()) {
      textNodes.push(walker.currentNode);
    }

    textNodes.forEach(replaceTextNodeWithHighlights);
  }

  function highlightHtmlString(html) {
    if (!html || !compiledRules.length) return html;

    var wrap = document.createElement("div");
    wrap.innerHTML = html;
    highlightDomTree(wrap);
    return wrap.innerHTML;
  }

  function applyHighlightsToBlock(block) {
    if (!enabled || !compiledRules.length || !shouldHighlightBlock(block)) {
      return block;
    }

    if (block.html) {
      return Object.assign({}, block, { html: highlightHtmlString(block.html) });
    }

    if (block.kind === "fallback" && block.text) {
      return {
        kind: "prose",
        html: "<p>" + highlightTextValue(block.text) + "</p>",
      };
    }

    return block;
  }

  function applyHighlightsToBlocks(blocks) {
    if (!enabled || !compiledRules.length || !Array.isArray(blocks)) {
      return blocks;
    }
    return blocks.map(applyHighlightsToBlock);
  }

  function stripPhraseHighlightsInElement(root) {
    if (!root) return;
    var highlights = root.querySelectorAll("." + HIGHLIGHT_CLASS);
    Array.prototype.forEach.call(highlights, function (span) {
      var parent = span.parentNode;
      if (!parent) return;
      while (span.firstChild) {
        parent.insertBefore(span.firstChild, span);
      }
      parent.removeChild(span);
    });
    root.normalize();
  }

  function refreshExistingHighlightStylesInRoot(root) {
    if (!enabled || !rules.length || !root) return;
    Array.prototype.forEach.call(
      root.querySelectorAll("." + HIGHLIGHT_CLASS),
      function (el) {
        var match = (el.className || "").match(/cgw-phrase-h-(\d+)/);
        if (match) {
          applyRuleStyleToElement(el, getRuleByIndex(parseInt(match[1], 10)));
        }
      }
    );
  }

  function decoratePhraseHighlightsInElementCore(root) {
    if (!root) return;
    if (!enabled || !compiledRules.length) {
      stripPhraseHighlightsInElement(root);
      return;
    }
    rebuildStylesheet();
    stripPhraseHighlightsInElement(root);
    highlightDomTree(root);
    refreshExistingHighlightStylesInRoot(root);
  }

  function decoratePhraseHighlightsInElement(root) {
    if (!root) return;
    if (decorateTimer) {
      clearTimeout(decorateTimer);
      decorateTimer = null;
    }
    decorateTimer = setTimeout(function () {
      decorateTimer = null;
      var t0 = globalThis.__cgwPhraseHighlightBench ? performance.now() : 0;
      decoratePhraseHighlightsInElementCore(root);
      if (globalThis.__cgwPhraseHighlightBench) {
        var ms = performance.now() - t0;
        console.log(
          "[cgw] phrase highlight decorate: " +
            ms.toFixed(1) +
            "ms (" +
            compiledRules.length +
            " rules)"
        );
      }
    }, DECORATE_DEBOUNCE_MS);
  }

  function refreshExistingHighlightStyles() {
    var container = document.getElementById("cgw-continuous-view");
    if (!container) return;
    refreshExistingHighlightStylesInRoot(container);
  }

  function patchRichFormat() {
    var rf = globalThis.__cgwContinuousRichFormat;
    if (!rf || rf.__cgwPhraseHighlightsPatched) return;

    var origAppendRichBlock = rf.appendRichBlock;

    if (typeof origAppendRichBlock === "function") {
      rf.appendRichBlock = function (parent, block) {
        origAppendRichBlock(parent, applyHighlightsToBlock(block));
      };
    }

    rf.__cgwPhraseHighlightsPatched = true;
  }

  function scheduleRefresh() {
    if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
      globalThis.__cgwContinuousViewSchedule();
    }
  }

  function refreshPhraseHighlights(nextEnabled, nextRules, opts) {
    opts = opts || {};
    var nextEnabledBool = !!nextEnabled;
    var nextRulesNormalized = normalizeRules(nextRules);
    var nextFp =
      (nextEnabledBool ? "1" : "0") +
      "\x05" +
      nextRulesNormalized.map(ruleStyleFingerprintPart).join("\x02");

    if (
      !opts.force &&
      nextEnabledBool === enabled &&
      nextFp === globalThis.__cgwPhraseHighlightStyleFp
    ) {
      return;
    }

    enabled = nextEnabledBool;
    rules = nextRulesNormalized;
    compiledRules = compileRules(rules);
    globalThis.__cgwPhraseHighlightsEnabled = enabled;
    globalThis.__cgwPhraseHighlightRules = rules;
    globalThis.__cgwPhraseHighlightStyleFp = nextFp;
    globalThis.__cgwApplyPhraseHighlightsToBlocks = applyHighlightsToBlocks;
    globalThis.__cgwDecoratePhraseHighlightsInElement = decoratePhraseHighlightsInElement;
    rebuildStylesheet();
    patchRichFormat();
    clearRenderFingerprints();
    if (opts.schedule === false) {
      refreshExistingHighlightStyles();
    }
    if (opts.schedule !== false) {
      scheduleRefresh();
    }
  }

  globalThis.__cgwStripPhraseHighlightsInElement = stripPhraseHighlightsInElement;

  globalThis.__cgwSetPhraseHighlights = refreshPhraseHighlights;
  globalThis.__cgwApplyPhraseHighlightsToBlocks = applyHighlightsToBlocks;
  globalThis.__cgwDecoratePhraseHighlightsInElement = decoratePhraseHighlightsInElement;
  globalThis.__cgwPhraseHighlightStyleFp = rulesStyleFingerprint(rules);
  globalThis.__cgwPhraseHighlightBench = false;
  globalThis.__cgwEnablePhraseHighlightBench = function (on) {
    globalThis.__cgwPhraseHighlightBench = !!on;
  };

  if (!globalThis.__cgwPhraseHighlightsBooted) {
    globalThis.__cgwPhraseHighlightsBooted = true;
    rebuildStylesheet();
    patchRichFormat();

    if (typeof globalThis.__cgwContinuousRichFormat === "undefined") {
      var patchTimer = setInterval(function () {
        if (globalThis.__cgwContinuousRichFormat) {
          patchRichFormat();
          clearInterval(patchTimer);
        }
      }, 0);
      setTimeout(function () {
        clearInterval(patchTimer);
      }, 5000);
    }
  } else {
    refreshPhraseHighlights(enabled, rules);
  }
})();
