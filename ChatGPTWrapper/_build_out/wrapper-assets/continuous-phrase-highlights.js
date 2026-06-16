/**
 * ChatGPT Wrapper — custom phrase highlighting for continuous view.
 */
(function () {
  var STYLE_ID = "cgw-phrase-highlight-styles";
  var HIGHLIGHT_CLASS = "cgw-phrase-highlight";
  var MAX_RULES = 50;
  var WORD_CHAR = "[\\p{L}\\p{N}_]";
  var BOUNDARY_BEFORE = "(?<!" + WORD_CHAR + ")";
  var BOUNDARY_AFTER = "(?!" + WORD_CHAR + ")";
  var CSS_SCOPE =
    "html[data-cgw-continuous-view=\"1\"] .cgw-phrase-highlight.cgw-phrase-h-";

  var enabled = !!globalThis.__cgwPhraseHighlightsEnabled;
  var rules = normalizeRules(globalThis.__cgwPhraseHighlightRules);
  var compiledRules = compileRules(rules);

  function readRuleField(rule, camel, pascal) {
    if (!rule) return undefined;
    if (rule[camel] !== undefined && rule[camel] !== null) return rule[camel];
    if (rule[pascal] !== undefined && rule[pascal] !== null) return rule[pascal];
    return undefined;
  }

  function normalizeRules(raw) {
    if (!Array.isArray(raw)) return [];
    return raw
      .map(function (r) {
        var phrase = readRuleField(r, "phrase", "Phrase");
        if (typeof phrase !== "string" || !phrase.trim()) return null;
        return {
          phrase: phrase.trim(),
          color: readRuleField(r, "color", "Color"),
          backgroundColor: readRuleField(r, "backgroundColor", "BackgroundColor"),
          bold: !!readRuleField(r, "bold", "Bold"),
          italic: !!readRuleField(r, "italic", "Italic"),
        };
      })
      .filter(Boolean)
      .slice(0, MAX_RULES)
      .map(function (r, index) {
        return {
          index: index,
          phrase: r.phrase,
          color: sanitizeHex(r.color, "#FFD166"),
          backgroundColor: r.backgroundColor
            ? sanitizeHex(r.backgroundColor, "")
            : "",
          bold: r.bold,
          italic: r.italic,
        };
      });
  }

  function rulesStyleFingerprint(list) {
    return (enabled ? "1" : "0") + "\x05" +
      list
        .map(function (r) {
          return [
            r.index,
            r.phrase,
            r.color,
            r.backgroundColor,
            r.bold ? 1 : 0,
            r.italic ? 1 : 0,
          ].join("\x01");
        })
        .join("\x02");
  }

  function sanitizeHex(value, fallback) {
    var v = String(value || "").trim();
    if (!v) return fallback;
    if (v.charAt(0) !== "#") v = "#" + v;
    return /^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$/.test(v) ? v : fallback;
  }

  function escapeRegex(text) {
    return String(text).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  }

  function getRuleByIndex(index) {
    for (var i = 0; i < rules.length; i++) {
      if (rules[i].index === index) return rules[i];
    }
    return rules[index] || null;
  }

  function applyRuleStyleToElement(el, rule) {
    if (!el || !rule) return;
    if (rule.color) el.style.setProperty("color", rule.color, "important");
    if (rule.backgroundColor) {
      el.style.setProperty("background-color", rule.backgroundColor, "important");
    }
    if (rule.bold) el.style.setProperty("font-weight", "700", "important");
    if (rule.italic) el.style.setProperty("font-style", "italic", "important");
  }

  function buildInlineStyleAttr(rule) {
    if (!rule) return "";
    var parts = [];
    if (rule.color) parts.push("color:" + rule.color + " !important");
    if (rule.backgroundColor) {
      parts.push("background-color:" + rule.backgroundColor + " !important");
    }
    if (rule.bold) parts.push("font-weight:700 !important");
    if (rule.italic) parts.push("font-style:italic !important");
    return parts.join(";");
  }

  function compileRuleRegex(phrase) {
    var parts = phrase.split(/\s+/).filter(Boolean);
    var body =
      parts.length <= 1
        ? escapeRegex(phrase)
        : parts.map(escapeRegex).join("\\s+");

    var attempts = [
      { pattern: BOUNDARY_BEFORE + "(" + body + ")" + BOUNDARY_AFTER, flags: "giu" },
      { pattern: "\\b(" + body + ")\\b", flags: "gi" },
      { pattern: "(" + body + ")", flags: "gi" },
    ];

    for (var i = 0; i < attempts.length; i++) {
      try {
        return {
          mode: "regex",
          regex: new RegExp(attempts[i].pattern, attempts[i].flags),
        };
      } catch (_e) {
        /* try next */
      }
    }

    return {
      mode: "literal",
      phraseLower: phrase.toLocaleLowerCase(),
    };
  }

  function compileRules(list) {
    return list
      .slice()
      .sort(function (a, b) {
        return b.phrase.length - a.phrase.length;
      })
      .map(function (rule) {
        var compiled = compileRuleRegex(rule.phrase);
        return {
          index: rule.index,
          phrase: rule.phrase,
          mode: compiled.mode,
          regex: compiled.regex || null,
          phraseLower: compiled.phraseLower || rule.phrase.toLocaleLowerCase(),
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

    var css = rules
      .map(function (rule) {
        var parts = [CSS_SCOPE + rule.index + "{"];
        if (rule.color) parts.push("color:" + rule.color + " !important;");
        if (rule.backgroundColor) {
          parts.push("background-color:" + rule.backgroundColor + " !important;");
        }
        if (rule.bold) parts.push("font-weight:700 !important;");
        if (rule.italic) parts.push("font-style:italic !important;");
        parts.push("}");
        return parts.join("");
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
    if (after && /[\p{L}\p{N}_]/u.test(after)) return false;
    return true;
  }

  function applyLiteralRuleToSegments(segments, rule) {
    var phraseLower = rule.phraseLower;
    if (!phraseLower) return segments;

    var out = [];
    segments.forEach(function (seg) {
      if (seg.type === "markup") {
        out.push(seg);
        return;
      }

      var text = seg.value;
      if (!text) return;

      var textLower = text.toLocaleLowerCase();
      var last = 0;
      var idx = textLower.indexOf(phraseLower, last);
      var matched = false;

      while (idx !== -1) {
        var end = idx + phraseLower.length;
        if (!isWordBoundary(text, idx, end)) {
          idx = textLower.indexOf(phraseLower, idx + 1);
          continue;
        }

        matched = true;
        if (idx > last) {
          out.push({ type: "text", value: text.slice(last, idx) });
        }
        out.push({
          type: "markup",
          value: wrapMatch(rule.index, text.slice(idx, end)),
        });
        last = end;
        idx = textLower.indexOf(phraseLower, last);
      }

      if (last < text.length) {
        out.push({ type: "text", value: text.slice(last) });
      } else if (!matched) {
        out.push({ type: "text", value: text });
      }
    });

    return out.length ? out : segments;
  }

  function applyRegexRuleToSegments(segments, rule) {
    var out = [];
    segments.forEach(function (seg) {
      if (seg.type === "markup") {
        out.push(seg);
        return;
      }

      var text = seg.value;
      if (!text) return;

      if (!rule.regex) {
        out.push(seg);
        return;
      }

      rule.regex.lastIndex = 0;
      var last = 0;
      var match;
      var matched = false;
      while ((match = rule.regex.exec(text)) !== null) {
        matched = true;
        if (match.index > last) {
          out.push({ type: "text", value: text.slice(last, match.index) });
        }
        out.push({
          type: "markup",
          value: wrapMatch(rule.index, match[1] || match[0]),
        });
        last = match.index + match[0].length;
        if (match[0].length === 0) {
          rule.regex.lastIndex++;
        }
      }
      if (last < text.length) {
        out.push({ type: "text", value: text.slice(last) });
      } else if (!matched) {
        out.push({ type: "text", value: text });
      }
    });
    return out.length ? out : segments;
  }

  function applyRuleToSegments(segments, rule) {
    if (rule.mode === "literal") {
      return applyLiteralRuleToSegments(segments, rule);
    }
    return applyRegexRuleToSegments(segments, rule);
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

  function decoratePhraseHighlightsInElement(root) {
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

  function refreshPhraseHighlights(nextEnabled, nextRules) {
    enabled = !!nextEnabled;
    rules = normalizeRules(nextRules);
    compiledRules = compileRules(rules);
    globalThis.__cgwPhraseHighlightsEnabled = enabled;
    globalThis.__cgwPhraseHighlightRules = rules;
    globalThis.__cgwPhraseHighlightStyleFp = rulesStyleFingerprint(rules);
    globalThis.__cgwApplyPhraseHighlightsToBlocks = applyHighlightsToBlocks;
    globalThis.__cgwDecoratePhraseHighlightsInElement = decoratePhraseHighlightsInElement;
    rebuildStylesheet();
    patchRichFormat();
    clearRenderFingerprints();
    scheduleRefresh();
  }

  globalThis.__cgwStripPhraseHighlightsInElement = stripPhraseHighlightsInElement;

  globalThis.__cgwSetPhraseHighlights = refreshPhraseHighlights;
  globalThis.__cgwApplyPhraseHighlightsToBlocks = applyHighlightsToBlocks;
  globalThis.__cgwDecoratePhraseHighlightsInElement = decoratePhraseHighlightsInElement;
  globalThis.__cgwPhraseHighlightStyleFp = rulesStyleFingerprint(rules);

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
