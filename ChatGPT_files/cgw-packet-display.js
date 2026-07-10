(function () {
  "use strict";

  if (globalThis.__cgwPacketDisplayBooted) {
    return;
  }
  globalThis.__cgwPacketDisplayBooted = true;

  var MARKER = "[[cgw:";
  var INVALIDATION_RE = /\[\[cgw:invalidation[^\]]*\]\]\s*/gi;
  var TRAILING_INJECTION_MARKERS = [
    "=== TURN OVERRIDES ===",
    "=== TURN DIRECTIVE ===",
    "=== CANON UPDATE (check sources) ===",
  ];
  var BLOCK_RE =
    /\[\[cgw:([a-z][a-z0-9_-]*)([^\]]*)\]\]([\s\S]*?)\[\[\/cgw:\1\]\]/gi;
  var STRIP_TAG_RE =
    /\[\[cgw:[^\]/\]]+[^\]]*\]\][\s\S]*?\[\[\/cgw:[^\]]+\]\]/g;
  var STRUCTURED_SECTION_RE = /^\[([a-z][a-z0-9_-]*)\]\s*(.*)$/i;
  var STRUCTURED_SECTION_NAMES = {
    meta: true,
    sources: true,
    instructions: true,
    summary: true,
    state: true,
    cards: true,
    memory: true,
    transcript: true,
    player: true,
    user: true,
  };
  var STRUCTURED_INJECTION_SECTIONS =
    /\[(meta|sources|instructions|summary|state|cards|memory|transcript)\]/i;
  var SECTION_ORDER = [
    "meta",
    "sources",
    "sources-always",
    "sources-turn",
    "sources-inline",
    "instructions",
    "summary",
    "state",
    "cards",
    "memory",
    "transcript",
  ];
  var parseCache = new Map();
  var MAX_CACHE = 64;
  var MAX_DISPLAY_CACHE = 128;
  var STAMP_STORAGE_PREFIX = "cgw-packet-stamp:";
  var DOM_READY_MAX_MS = 600;
  var DOM_READY_HOLD_MS = 32;
  var PENDING_FALLBACK_MS = 2000;

  var passScheduled = false;
  var mainObserver = null;
  var activeConversationKey = null;
  var turnRegistry = {};
  var turnDisplayCache = {};
  var sourceBackupByTurnId = {};
  var nextTurnId = 1;
  var domReadyTimer = null;
  var pendingFallbackTimer = null;
  var batchScheduled = false;

  function hideEnabled() {
    return globalThis.__cgwHideContextTags === true;
  }

  var UTILITY_TAG_MARKER = "[[cgw:utility";
  var UTILITY_RESPONSE_TAG_MARKER = "[[cgw:utility-response";

  function isUtilityTaggedText(text) {
    return !!text && String(text).indexOf(UTILITY_TAG_MARKER) >= 0;
  }

  function isUtilityResponseTaggedText(text) {
    return !!text && String(text).indexOf(UTILITY_RESPONSE_TAG_MARKER) >= 0;
  }

  function utilityTrafficVisible() {
    return (
      document.documentElement.getAttribute("data-cgw-show-utility-traffic") ===
      "1"
    );
  }

  function shouldHideUtilityDisplay(text) {
    if (!isUtilityTaggedText(text)) return false;
    if (utilityTrafficVisible()) return false;
    return globalThis.__cgwHideInlineUtilityDuringPlay !== false;
  }

  function expandEnabled() {
    return globalThis.__cgwExpandHiddenContext !== false;
  }

  var PACKET_CONTEXT_UI_LS_KEY = "cgw-show-packet-context-ui";

  function isPacketContextUiVisible() {
    return (
      document.documentElement.getAttribute("data-cgw-show-packet-context") ===
      "1"
    );
  }

  function refreshHiddenPacketIndicators() {
    function syncPacketHost(el, hiddenClass) {
      if (!el.querySelector("[data-cgw-packet-context]")) return;
      el.classList.add("cgw-has-packet-context");
      if (isPacketContextUiVisible()) {
        el.classList.remove(hiddenClass);
      } else {
        el.classList.add(hiddenClass);
      }
    }
    document
      .querySelectorAll(".cgw-continuous-segment--user")
      .forEach(function (seg) {
        syncPacketHost(seg, "cgw-continuous-segment--has-hidden-packet");
      });
    document.querySelectorAll(".cgw-weave-embed").forEach(function (embed) {
      syncPacketHost(embed, "cgw-weave-embed--has-hidden-packet");
    });
  }

  function setPacketContextUiVisible(visible) {
    var root = document.documentElement;
    if (visible) {
      root.setAttribute("data-cgw-show-packet-context", "1");
    } else {
      root.removeAttribute("data-cgw-show-packet-context");
    }
    try {
      localStorage.setItem(PACKET_CONTEXT_UI_LS_KEY, visible ? "1" : "0");
    } catch (e) {}
    refreshHiddenPacketIndicators();
    Object.keys(turnRegistry).forEach(function (turnId) {
      var entry = turnRegistry[turnId];
      if (!entry || !entry.wrap) return;
      entry.displayFp = null;
      wrapSetDisplayFp(entry.wrap, null);
    });
    processDeltaTurns();
  }

  function initPacketContextUiVisibility() {
    var visible = true;
    try {
      var stored = localStorage.getItem(PACKET_CONTEXT_UI_LS_KEY);
      if (stored === "0") visible = false;
      else if (stored === "1") visible = true;
    } catch (e) {}
    var root = document.documentElement;
    if (visible) {
      root.setAttribute("data-cgw-show-packet-context", "1");
    } else {
      root.removeAttribute("data-cgw-show-packet-context");
    }
    refreshHiddenPacketIndicators();
  }

  function togglePacketContextUiVisible() {
    setPacketContextUiVisible(!isPacketContextUiVisible());
    return isPacketContextUiVisible();
  }

  function overlayActive() {
    if (globalThis.__cgwTranscriptViewMode) {
      return globalThis.__cgwTranscriptViewMode !== "native";
    }
    return globalThis.__cgwContinuousViewEnabled === true;
  }

  function getConversationKey() {
    try {
      var u = new URL(location.href);
      var path = (u.pathname || "").replace(/\\/g, "/");
      var match = path.match(/\/c\/([^/]+)/i);
      if (match) return match[1];
      var frag = u.hash || "";
      if (frag.charAt(0) === "#") frag = frag.slice(1);
      match = frag.match(/\/c\/([^/]+)/i);
      if (match) return match[1];
    } catch (_e) {
      /* ignore */
    }
    return null;
  }

  function displayCacheKey(turnId) {
    return (getConversationKey() || activeConversationKey || "unknown") + ":" + turnId;
  }

  function escapeHtml(text) {
    return String(text || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function turnWrapper(turnRoot) {
    if (!turnRoot) return null;
    var testTurn = turnRoot.closest('[data-testid^="conversation-turn-"]');
    if (testTurn) return testTurn;
    if (turnRoot.matches && turnRoot.matches("[data-message-author-role]")) {
      return turnRoot;
    }
    var article = turnRoot.closest("article");
    if (article) return article;
    return turnRoot;
  }

  function findUserContentHostIn(root) {
    if (!root) return null;
    var leaf = findNativePlayerTextLeaf(root);
    if (leaf) return leaf;
    var actions = root.querySelector('[aria-label="Your message actions"]');
    if (
      actions &&
      actions.parentElement &&
      actions.parentElement.previousElementSibling
    ) {
      return actions.parentElement.previousElementSibling;
    }
    var markdown = root.querySelector(
      '.markdown, [class*="markdown"], .prose, [class*="prose"]'
    );
    if (markdown) return markdown;
    var dirs = root.querySelectorAll('[dir="auto"]');
    if (dirs.length === 1) return dirs[0];
    var group = root.querySelector('[class*="group/turn-messages"]');
    if (group && group.firstElementChild) return group.firstElementChild;
    return null;
  }

  function findNativePlayerTextLeaf(wrap) {
    if (!wrap) return null;
    var pres = wrap.querySelectorAll(
      '.whitespace-pre-wrap, [class*="whitespace-pre-wrap"]'
    );
    if (pres.length) return pres[0];
    return null;
  }

  function collectNativeUserMessageText(root) {
    if (!root) return "";
    var wrap = turnWrapper(root) || root;
    var pres = wrap.querySelectorAll(
      '.whitespace-pre-wrap, [class*="whitespace-pre-wrap"]'
    );
    if (pres.length) {
      var parts = [];
      for (var i = 0; i < pres.length; i++) {
        var chunk = (pres[i].textContent || "").trim();
        if (chunk) parts.push(chunk);
      }
      if (parts.length) {
        return sanitizeExtractedMessageText(parts.join("\n\n"));
      }
    }

    var host = findUserContentHostIn(wrap);
    if (host) {
      return sanitizeExtractedMessageText((host.textContent || "").trim());
    }

    return sanitizeExtractedMessageText((wrap.textContent || "").trim());
  }

  function looksLikePacketText(text) {
    if (!text) return false;
    if (isRevisionPromptDisplayText(text)) return false;
    if (text.indexOf(MARKER) >= 0) return true;
    return isStructuredPreviewPacket(text);
  }

  function isRevisionPromptDisplayText(text) {
    if (!text) return false;
    var stripped = String(text).replace(INVALIDATION_RE, "").trimStart();
    if (stripped.indexOf("For play turn ") === 0) return true;
    return stripped.indexOf("disregard your prior assistant reply for this turn") >= 0;
  }

  function findNativePacketTurns() {
    var byTestId = Array.prototype.slice.call(
      document.querySelectorAll('[data-testid^="conversation-turn-"]')
    );
    if (byTestId.length) {
      return byTestId.filter(function (wrap) {
        return wrap.querySelector('[data-message-author-role="user"]');
      });
    }
    var byRole = Array.prototype.slice.call(
      document.querySelectorAll('[data-message-author-role="user"]')
    );
    var seen = new Set();
    var out = [];
    byRole.forEach(function (node) {
      var wrap = turnWrapper(node);
      if (!wrap || seen.has(wrap)) return;
      seen.add(wrap);
      out.push(wrap);
    });
    return out;
  }

  function getOrAssignTurnId(wrap) {
    var existing = wrap.getAttribute("data-cgw-turn-id");
    if (existing) return existing;
    var id = String(nextTurnId++);
    wrap.setAttribute("data-cgw-turn-id", id);
    return id;
  }

  function computeSourceFingerprint(wrap) {
    if (!wrap) return "";
    var text = collectNativeUserMessageText(wrap);
    if (!text) {
      var turnId = wrap.getAttribute("data-cgw-turn-id");
      if (turnId && sourceBackupByTurnId[turnId]) {
        text = sourceBackupByTurnId[turnId].sourceText || "";
      }
    }
    var len = text.length;
    var tail = len > 96 ? text.slice(len - 96) : text;
    var streaming = 0;
    var turns = findNativePacketTurns();
    if (turns.length && turns[turns.length - 1] === wrap) streaming = 1;
    return len + "\x01" + tail + "\x01" + streaming;
  }

  function sanitizeExtractedMessageText(text) {
    if (typeof globalThis.__cgwSanitizeExtractedMessageText === "function") {
      return globalThis.__cgwSanitizeExtractedMessageText(text);
    }
    if (!text) return "";
    return String(text)
      .replace(/\r\n/g, "\n")
      .replace(/\r/g, "\n")
      .replace(/\s*Show more\s*Show less\s*/gi, " ")
      .replace(/\s*Show more\s*/gi, " ")
      .replace(/\s*Show less\s*/gi, " ")
      .replace(/[ \t\f\v]{2,}/g, " ")
      .trim();
  }

  function isStructuredPreviewPacket(text) {
    if (!text || text.indexOf(MARKER) >= 0) return false;
    var normalized = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    if (!/\[(user|player)\]/i.test(normalized)) return false;
    return STRUCTURED_INJECTION_SECTIONS.test(normalized);
  }

  function parseStructuredPreview(text) {
    if (!text || !isStructuredPreviewPacket(text)) return null;
    if (parseCache.has(text)) return parseCache.get(text);

    var lines = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
    var blocks = {};
    var userLine = "";
    var currentName = null;
    var currentHeaderExtra = "";
    var currentLines = [];

    function flushSection() {
      if (!currentName) return;
      var body = currentLines.join("\n").trim();
      if (currentName === "user") {
        if (!userLine) userLine = body;
      } else if (currentName === "player") {
        userLine = body;
      } else if (currentName === "meta") {
        var metaBody = currentHeaderExtra
          ? currentHeaderExtra + (body ? "\n" + body : "")
          : body;
        if (metaBody.trim()) blocks.meta = metaBody.trim();
      } else if (body) {
        blocks[currentName] = body;
      }
      currentName = null;
      currentHeaderExtra = "";
      currentLines = [];
    }

    for (var i = 0; i < lines.length; i++) {
      var line = lines[i];
      var match = line.match(STRUCTURED_SECTION_RE);
      var sectionName =
        match && STRUCTURED_SECTION_NAMES[match[1].toLowerCase()]
          ? match[1].toLowerCase()
          : null;
      if (sectionName) {
        flushSection();
        currentName = sectionName;
        currentHeaderExtra = (match[2] || "").trim();
        continue;
      }
      if (currentName) currentLines.push(line);
    }
    flushSection();

    expandSourcesV2Blocks(blocks);

    var sectionCount = 0;
    Object.keys(blocks).forEach(function (key) {
      if (blocks[key]) sectionCount++;
    });

    var result = {
      blocks: blocks,
      userLine: sanitizeExtractedMessageText(
        stripTrailingInjectionBlocks(userLine)
      ),
      sectionCount: sectionCount,
    };

    if (parseCache.size >= MAX_CACHE) {
      var firstKey = parseCache.keys().next().value;
      if (firstKey !== undefined) parseCache.delete(firstKey);
    }
    parseCache.set(text, result);
    return result;
  }

  function parsePacketText(text) {
    if (!text) return null;
    if (text.indexOf(MARKER) >= 0) return parsePacket(text);
    if (isStructuredPreviewPacket(text)) return parseStructuredPreview(text);
    return null;
  }

  function isPacketTurn(turn, rawText) {
    if (rawText && rawText.indexOf(MARKER) >= 0) return true;
    if (rawText && isStructuredPreviewPacket(rawText)) return true;
    var stamp = readStamp(turn);
    if (stamp && (stamp.userLine || stamp.hash)) return true;
    return !!loadPersistedStamp(turn);
  }

  function getPacketSourceText(turn, blocks) {
    var wrap = turnWrapper(turn) || turn;
    var joined = sanitizeExtractedMessageText(collectNativeUserMessageText(turn));
    if (looksLikePacketText(joined)) return joined;

    var fromBlocks = sanitizeExtractedMessageText(blocksPlainText(blocks));
    if (looksLikePacketText(fromBlocks)) return fromBlocks;

    var turnId = wrap.getAttribute && wrap.getAttribute("data-cgw-turn-id");
    if (turnId && sourceBackupByTurnId[turnId]) {
      var backup = sourceBackupByTurnId[turnId].sourceText || "";
      if (backup && looksLikePacketText(backup)) return backup;
    }

    return joined || fromBlocks;
  }

  function extractPacketUserLine(rawText) {
    if (!rawText) return "";
    var cleaned = sanitizeExtractedMessageText(rawText);
    var parsed = parsePacketText(cleaned);
    if (parsed && parsed.userLine) {
      return sanitizeExtractedMessageText(parsed.userLine);
    }
    if (cleaned.indexOf(MARKER) >= 0) {
      return sanitizeExtractedMessageText(
        stripTrailingInjectionBlocks(stripContextTags(cleaned))
      );
    }
    return "";
  }

  function stripTrailingInjectionBlocks(text) {
    if (!text) return "";
    var earliest = text.length;
    for (var i = 0; i < TRAILING_INJECTION_MARKERS.length; i++) {
      var idx = text.indexOf(TRAILING_INJECTION_MARKERS[i]);
      if (idx >= 0 && idx < earliest) earliest = idx;
    }
    return earliest < text.length ? text.slice(0, earliest).trim() : text.trim();
  }

  globalThis.__cgwStripTrailingInjectionBlocks = stripTrailingInjectionBlocks;

  function stripContextTags(text) {
    if (!text) return "";
    return String(text).replace(STRIP_TAG_RE, "").trim();
  }

  globalThis.__cgwStripContextTags = stripContextTags;

  function expandSourcesV2Blocks(blocks) {
    var sources = blocks.sources;
    if (!sources || sources.indexOf("ALWAYS RETRIEVE:") < 0) return blocks;

    var parts = sources.split(/\n(?=ALWAYS RETRIEVE:|THIS TURN:|INLINE EXCERPTS:)/);
    delete blocks.sources;
    parts.forEach(function (part) {
      var trimmed = part.trim();
      if (!trimmed) return;
      if (trimmed.indexOf("ALWAYS RETRIEVE:") === 0) {
        blocks["sources-always"] = trimmed;
      } else if (trimmed.indexOf("THIS TURN:") === 0) {
        blocks["sources-turn"] = trimmed;
      } else if (trimmed.indexOf("INLINE EXCERPTS:") === 0) {
        blocks["sources-inline"] = trimmed;
      } else if (!blocks.sources) {
        blocks.sources = trimmed;
      }
    });
    return blocks;
  }

  function parsePacket(text) {
    if (!text || text.indexOf(MARKER) < 0) return null;
    if (parseCache.has(text)) return parseCache.get(text);

    var blocks = {};
    var m;
    BLOCK_RE.lastIndex = 0;
    while ((m = BLOCK_RE.exec(text)) !== null) {
      var blockName = m[1].toLowerCase();
      if (blockName === "action") continue;
      blocks[blockName] = (m[3] || "").trim();
    }
    expandSourcesV2Blocks(blocks);

    var strippedBody = text.replace(BLOCK_RE, "").replace(INVALIDATION_RE, "");
    if (strippedBody.indexOf(MARKER) >= 0) {
      strippedBody = stripContextTags(text.replace(INVALIDATION_RE, ""));
    }
    var remainder = sanitizeExtractedMessageText(
      stripTrailingInjectionBlocks(strippedBody.trim())
    );
    var playerTagged = blocks.player;
    if (playerTagged) {
      remainder = playerTagged;
      delete blocks.player;
    }

    var sectionCount = 0;
    Object.keys(blocks).forEach(function (key) {
      if (blocks[key]) sectionCount++;
    });

    var result = {
      blocks: blocks,
      userLine: remainder,
      sectionCount: sectionCount,
    };

    if (parseCache.size >= MAX_CACHE) {
      var firstKey = parseCache.keys().next().value;
      if (firstKey !== undefined) parseCache.delete(firstKey);
    }
    parseCache.set(text, result);
    return result;
  }

  function stampStorageKey(wrap) {
    var conv = getConversationKey() || "unknown";
    var id =
      wrap.getAttribute("data-cgw-turn-id") ||
      wrap.getAttribute("data-testid") ||
      "";
    if (!id) {
      var turns = findNativePacketTurns();
      var idx = turns.indexOf(wrap);
      id = String(idx >= 0 ? idx : 0);
    }
    return STAMP_STORAGE_PREFIX + conv + ":" + id;
  }

  function persistStamp(wrap, display) {
    if (!wrap || !display) return;
    try {
      sessionStorage.setItem(
        stampStorageKey(wrap),
        JSON.stringify({
          userLine: display.userLine || "",
          hash: wrap.getAttribute("data-cgw-packet-hash") || "",
          sectionCount: display.sectionCount || 0,
          blocks: display.blocks || {},
        })
      );
    } catch (_e) {
      /* ignore */
    }
  }

  function loadPersistedStamp(wrap) {
    if (!wrap) return null;
    try {
      var raw = sessionStorage.getItem(stampStorageKey(wrap));
      if (!raw) return null;
      return JSON.parse(raw);
    } catch (_e) {
      return null;
    }
  }

  function readStamp(turn) {
    var wrap = turnWrapper(turn);
    if (!wrap) return null;
    var userLine = wrap.getAttribute("data-cgw-user-line");
    if (userLine) {
      return {
        userLine: userLine,
        hash: wrap.getAttribute("data-cgw-packet-hash") || "",
      };
    }
    var persisted = loadPersistedStamp(wrap);
    if (persisted && persisted.userLine) {
      return {
        userLine: persisted.userLine,
        hash: persisted.hash || "",
        blocks: persisted.blocks,
        sectionCount: persisted.sectionCount,
      };
    }
    return null;
  }

  function blocksPlainText(blocks) {
    var rf = globalThis.__cgwContinuousRichFormat;
    if (rf && typeof rf.blockPlainText === "function") {
      return blocks
        .map(function (b) {
          return rf.blockPlainText(b);
        })
        .join("\n")
        .trim();
    }
    return "";
  }

  function buildPlayerBlocks(userLine) {
    var line = (userLine || "").trim();
    if (!line) return [];
    var rf = globalThis.__cgwContinuousRichFormat;
    if (rf && typeof rf.plainTextToProseBlocks === "function") {
      return rf.plainTextToProseBlocks(line);
    }
    if (rf && typeof rf.splitParagraphFallback === "function") {
      return rf.splitParagraphFallback(line);
    }
    return [{ kind: "prose", html: "<p>" + escapeHtml(line) + "</p>" }];
  }

  function sectionSortIndex(name) {
    var idx = SECTION_ORDER.indexOf(String(name || "").toLowerCase());
    return idx >= 0 ? idx : SECTION_ORDER.length + 1;
  }

  function orderedSectionNames(blocks) {
    return Object.keys(blocks).sort(function (a, b) {
      var ai = sectionSortIndex(a);
      var bi = sectionSortIndex(b);
      if (ai !== bi) return ai - bi;
      return String(a).localeCompare(String(b));
    });
  }

  function buildContextSummaryBlock(blocks, sectionCount) {
    if (!expandEnabled() || sectionCount <= 0) return null;
    var sections = [];
    orderedSectionNames(blocks).forEach(function (name) {
      var body = blocks[name];
      if (!body) return;
      sections.push({ name: name, body: body });
    });
    if (!sections.length) return null;
    return {
      kind: "packetContext",
      sectionCount: sections.length,
      sections: sections,
    };
  }

  function resolvePacketDisplay(turn, rawText) {
    if (!hideEnabled()) return null;
    var cleaned = sanitizeExtractedMessageText(rawText || "");
    if (!looksLikePacketText(cleaned) && !isPacketTurn(turn, cleaned)) return null;

    var parsed = parsePacketText(cleaned);
    var stamp = readStamp(turn);

    if (!parsed && !stamp) return null;

    var userLine = "";
    var blocks = {};
    var sectionCount = 0;

    var livePacket =
      cleaned.indexOf(MARKER) >= 0 || isStructuredPreviewPacket(cleaned);

    if (stamp && stamp.userLine && !livePacket) {
      userLine = sanitizeExtractedMessageText(stamp.userLine);
      if (stamp.blocks) blocks = stamp.blocks;
      if (typeof stamp.sectionCount === "number") {
        sectionCount = stamp.sectionCount;
      }
    }
    if (parsed) {
      if (livePacket || !userLine) {
        userLine = sanitizeExtractedMessageText(parsed.userLine);
      }
      blocks = parsed.blocks;
      sectionCount = parsed.sectionCount;
    }
    if (!userLine) {
      userLine = extractPacketUserLine(cleaned);
    }

    return {
      userLine: userLine,
      blocks: blocks,
      sectionCount: sectionCount,
    };
  }

  function transformUserBlocks(turn, blocks, role) {
    blocks = blocks || [];
    if (!hideEnabled()) return blocks;
    if (role && role !== "user") return blocks;

    var rawText = getPacketSourceText(turn, blocks);
    if (!rawText) return blocks;
    if (isRevisionPromptDisplayText(rawText)) return [];
    if (shouldHideUtilityDisplay(rawText)) return [];
    if (!looksLikePacketText(rawText) && !isPacketTurn(turn, rawText)) return blocks;

    var display = resolvePacketDisplay(turn, rawText);
    var userLine =
      (display && display.userLine) || extractPacketUserLine(rawText);
    if (!userLine) {
      var stamp = readStamp(turn);
      if (stamp && stamp.userLine) {
        userLine = sanitizeExtractedMessageText(stamp.userLine);
      }
    }

    var next = buildPlayerBlocks(userLine);
    if (display) {
      var summary = buildContextSummaryBlock(
        display.blocks,
        display.sectionCount
      );
      if (summary) next.push(summary);
    } else {
      var parsed = parsePacketText(sanitizeExtractedMessageText(rawText));
      if (parsed) {
        var summaryFallback = buildContextSummaryBlock(
          parsed.blocks,
          parsed.sectionCount
        );
        if (summaryFallback) next.push(summaryFallback);
      }
    }

    return next;
  }

  function buildNativePlayerLine(display, wrap) {
    var userLine = display && display.userLine;
    if (!userLine && wrap) {
      var stamped = wrap.getAttribute("data-cgw-user-line");
      if (stamped) userLine = sanitizeExtractedMessageText(stamped);
    }
    if (!userLine && display && display.blocks && display.blocks.player) {
      userLine = sanitizeExtractedMessageText(display.blocks.player);
    }
    return sanitizeExtractedMessageText(userLine || "");
  }

  function buildNativeDisplayHtml(display, wrap) {
    return buildNativePlayerLine(display, wrap);
  }

  function displayFingerprint(display) {
    return (
      (display.userLine || "") +
      "\x01" +
      display.sectionCount +
      "\x01" +
      (expandEnabled() ? "1" : "0") +
      "\x01" +
      (isPacketContextUiVisible() ? "1" : "0")
    );
  }

  function wrapSetDisplayFp(wrap, fp) {
    if (fp) wrap.setAttribute("data-cgw-packet-fp", fp);
    else wrap.removeAttribute("data-cgw-packet-fp");
  }

  function decodeDisplayMeta(wrap) {
    if (!wrap.getAttribute("data-cgw-packet-fp")) return null;
    var sourceText = getPacketSourceText(wrap, []);
    return resolvePacketDisplay(wrap, sourceText);
  }

  function lookupTurnDisplayCache(turnId, sourceFp) {
    var cached = turnDisplayCache[displayCacheKey(turnId)];
    if (!cached || cached.sourceFp !== sourceFp) return null;
    return cached;
  }

  function storeTurnDisplayCache(turnId, sourceFp, displayFp, html, display) {
    var key = displayCacheKey(turnId);
    turnDisplayCache[key] = {
      sourceFp: sourceFp,
      displayFp: displayFp,
      html: html,
      displayMeta: display,
    };
    var keys = Object.keys(turnDisplayCache);
    if (keys.length <= MAX_DISPLAY_CACHE) return;
    for (var i = 0; i < keys.length - MAX_DISPLAY_CACHE; i++) {
      delete turnDisplayCache[keys[i]];
    }
  }

  function teardownTurn(wrap) {
    if (!wrap) return;
    var turnId = wrap.getAttribute("data-cgw-turn-id");
    var backup = turnId ? sourceBackupByTurnId[turnId] : null;
    if (turnId) {
      delete turnRegistry[turnId];
      delete sourceBackupByTurnId[turnId];
    }

    var host = findNativePlayerTextLeaf(wrap) || findUserContentHostIn(wrap);
    cleanupLegacyPacketMount(host, wrap);
    if (host && backup && backup.leafHtml != null) {
      var liveText = collectNativeUserMessageText(wrap);
      if (!looksLikePacketText(liveText)) {
        host.innerHTML = backup.leafHtml;
      }
    }

    wrap.removeAttribute("data-cgw-packet-managed");
    wrap.removeAttribute("data-cgw-packet-fp");
  }

  function teardownAllPacketDisplays() {
    document.querySelectorAll("[data-cgw-packet-managed]").forEach(function (wrap) {
      teardownTurn(wrap);
    });
    turnRegistry = {};
    sourceBackupByTurnId = {};
    clearPendingAttr();
  }

  function clearPendingAttr() {
    document.documentElement.removeAttribute("data-cgw-packet-pending");
    if (pendingFallbackTimer != null) {
      clearTimeout(pendingFallbackTimer);
      pendingFallbackTimer = null;
    }
  }

  function releasePendingFallback() {
    clearPendingAttr();
    reconcileOrphanedPacketSources();
  }

  function hostHasVisiblePlayerLine(wrap) {
    if (!wrap) return false;
    var text = collectNativeUserMessageText(wrap);
    if (!text) return false;
    return !looksLikePacketText(text);
  }

  function cleanupLegacyPacketMount(host, wrap) {
    if (host) {
      host.classList.remove("cgw-native-packet-source");
      host.removeAttribute("aria-hidden");
    }
    if (!wrap) return;
    wrap.querySelectorAll(".cgw-native-packet-display").forEach(function (el) {
      el.remove();
    });
  }

  function ensureSourceBackup(wrap, leaf, turnId, sourceText) {
    if (!turnId || !wrap) return;
    var liveText = collectNativeUserMessageText(wrap);
    if (!sourceText && looksLikePacketText(liveText)) sourceText = liveText;
    if (!looksLikePacketText(liveText) && !sourceText) return;
    if (!leaf) leaf = findNativePlayerTextLeaf(wrap) || findUserContentHostIn(wrap);
    if (!leaf) return;
    sourceBackupByTurnId[turnId] = {
      leafHtml: leaf.innerHTML,
      sourceText: sourceText || liveText,
    };
  }

  function rewriteNativePlayerMessage(wrap, userLine) {
    if (!wrap || userLine == null) return false;
    var line = String(userLine);
    var pres = wrap.querySelectorAll(
      '.whitespace-pre-wrap, [class*="whitespace-pre-wrap"]'
    );
    if (pres.length) {
      pres[0].textContent = line;
      pres[0].classList.remove("cgw-native-packet-source");
      pres[0].removeAttribute("aria-hidden");
      for (var i = 1; i < pres.length; i++) pres[i].remove();
      return !!line.trim();
    }
    var leaf = findNativePlayerTextLeaf(wrap) || findUserContentHostIn(wrap);
    if (!leaf) return false;
    leaf.textContent = line;
    leaf.classList.remove("cgw-native-packet-source");
    leaf.removeAttribute("aria-hidden");
    return !!line.trim();
  }

  function rewriteNativeHostContent(leaf, userLine) {
    var wrap =
      (leaf &&
        (leaf.closest('[data-testid^="conversation-turn-"]') ||
          leaf.closest("[data-message-author-role]"))) ||
      leaf;
    return rewriteNativePlayerMessage(wrap, userLine);
  }

  function reconcileOrphanedPacketSources() {
    document.querySelectorAll(".cgw-native-packet-source").forEach(function (host) {
      var wrap =
        host.closest('[data-testid^="conversation-turn-"]') ||
        host.closest("[data-message-author-role]");
      if (!wrap) return;
      var displayEl =
        host.nextElementSibling &&
        host.nextElementSibling.classList &&
        host.nextElementSibling.classList.contains("cgw-native-packet-display")
          ? host.nextElementSibling
          : wrap.querySelector(".cgw-native-packet-display");
      var playerLine = "";
      if (displayEl) {
        var player = displayEl.querySelector("[data-cgw-packet-player]");
        playerLine = sanitizeExtractedMessageText(
          (player && player.textContent) || displayEl.textContent || ""
        );
      }
      cleanupLegacyPacketMount(host, wrap);
      if (playerLine) {
        rewriteNativePlayerMessage(wrap, playerLine);
        wrap.setAttribute("data-cgw-packet-managed", "1");
      }
    });

    document.querySelectorAll("[data-cgw-packet-managed]").forEach(function (wrap) {
      if (!hostHasVisiblePlayerLine(wrap)) {
        wrap.removeAttribute("data-cgw-packet-managed");
        wrap.removeAttribute("data-cgw-packet-fp");
        var turnId = wrap.getAttribute("data-cgw-turn-id");
        if (turnId) delete turnRegistry[turnId];
      }
    });
  }

  function setPendingAttr() {
    if (hideEnabled() && !overlayActive()) {
      document.documentElement.setAttribute("data-cgw-packet-pending", "1");
      if (pendingFallbackTimer != null) clearTimeout(pendingFallbackTimer);
      pendingFallbackTimer = setTimeout(releasePendingFallback, PENDING_FALLBACK_MS);
    }
  }

  function mountPacketDisplay(wrap, html, display, displayFp) {
    var leaf = findNativePlayerTextLeaf(wrap) || findUserContentHostIn(wrap);
    if (!leaf) return false;

    var playerLine = (html || "").trim();
    if (!playerLine && display) {
      playerLine = buildNativePlayerLine(display, wrap).trim();
    }
    if (!playerLine) return false;

    var turnId = getOrAssignTurnId(wrap);
    var sourceText = getPacketSourceText(wrap, []);
    cleanupLegacyPacketMount(leaf, wrap);
    ensureSourceBackup(wrap, leaf, turnId, sourceText);

    if (wrap.getAttribute("data-cgw-packet-fp") !== displayFp) {
      if (!rewriteNativePlayerMessage(wrap, playerLine)) return false;
      wrapSetDisplayFp(wrap, displayFp);
    }

    wrap.setAttribute("data-cgw-packet-managed", "1");
    persistStamp(wrap, display);
    return true;
  }

  function resolveTurnDisplay(wrap, sourceText) {
    var display = resolvePacketDisplay(wrap, sourceText);
    if (!display) {
      var stampOnly = readStamp(wrap);
      if (stampOnly && stampOnly.userLine) {
        display = {
          userLine: sanitizeExtractedMessageText(stampOnly.userLine),
          blocks: stampOnly.blocks || {},
          sectionCount: stampOnly.sectionCount || 0,
        };
      }
    }
    if (!display) return null;
    var displayFp = displayFingerprint(display);
    var html = buildNativeDisplayHtml(display, wrap);
    return { display: display, displayFp: displayFp, html: html };
  }

  function processTurn(wrap, turnId, sourceFp, mounts) {
    var sourceText = getPacketSourceText(wrap, []);
    if (!isPacketTurn(wrap, sourceText)) {
      if (wrap.getAttribute("data-cgw-packet-managed")) teardownTurn(wrap);
      return;
    }

    var entry = turnRegistry[turnId];
    if (
      entry &&
      entry.wrap === wrap &&
      entry.sourceFp === sourceFp &&
      entry.displayFp &&
      entry.displayFp === wrap.getAttribute("data-cgw-packet-fp")
    ) {
      if (
        hostHasVisiblePlayerLine(wrap) &&
        !wrap.querySelector(".cgw-native-packet-display")
      ) {
        return;
      }
    }

    var cached = lookupTurnDisplayCache(turnId, sourceFp);
    var resolved;
    if (cached) {
      resolved = {
        display: cached.displayMeta,
        displayFp: cached.displayFp,
        html: cached.html,
      };
    } else {
      resolved = resolveTurnDisplay(wrap, sourceText);
      if (!resolved) {
        if (wrap.getAttribute("data-cgw-packet-managed")) teardownTurn(wrap);
        return;
      }
      if (!resolved.html || !resolved.html.trim()) {
        resolved.html = buildNativeDisplayHtml(resolved.display, wrap);
      }
      if (!resolved.html || !resolved.html.trim()) {
        if (wrap.getAttribute("data-cgw-packet-managed")) teardownTurn(wrap);
        return;
      }
      storeTurnDisplayCache(
        turnId,
        sourceFp,
        resolved.displayFp,
        resolved.html,
        resolved.display
      );
    }

    if (mounts) {
      mounts.push({
        wrap: wrap,
        html: resolved.html,
        display: resolved.display,
        displayFp: resolved.displayFp,
        turnId: turnId,
        sourceFp: sourceFp,
      });
      return;
    }

    if (
      mountPacketDisplay(
        wrap,
        resolved.html,
        resolved.display,
        resolved.displayFp
      )
    ) {
      turnRegistry[turnId] = {
        wrap: wrap,
        sourceFp: sourceFp,
        displayFp: resolved.displayFp,
      };
    } else if (wrap.getAttribute("data-cgw-packet-managed")) {
      teardownTurn(wrap);
    }
  }

  function commitMounts(mounts) {
    var mounted = 0;
    mounts.forEach(function (item) {
      if (
        mountPacketDisplay(
          item.wrap,
          item.html,
          item.display,
          item.displayFp
        )
      ) {
        turnRegistry[item.turnId] = {
          wrap: item.wrap,
          sourceFp: item.sourceFp,
          displayFp: item.displayFp,
        };
        mounted++;
      }
    });
    return mounted;
  }

  function batchApplyAllTurns() {
    if (overlayActive() || !hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }

    var turns = findNativePacketTurns();
    var mounts = [];
    turns.forEach(function (wrap) {
      var turnId = getOrAssignTurnId(wrap);
      var sourceFp = computeSourceFingerprint(wrap);
      processTurn(wrap, turnId, sourceFp, mounts);
    });

    requestAnimationFrame(function () {
      var mounted = 0;
      try {
        mounted = commitMounts(mounts);
        pruneTurnRegistry(turns);
        reconcileOrphanedPacketSources();
      } finally {
        if (mounted > 0) clearPendingAttr();
        else releasePendingFallback();
      }
    });
  }

  function pruneTurnRegistry(currentTurns) {
    var liveIds = {};
    currentTurns.forEach(function (wrap) {
      var id = wrap.getAttribute("data-cgw-turn-id");
      if (id) liveIds[id] = true;
    });
    Object.keys(turnRegistry).forEach(function (turnId) {
      if (!liveIds[turnId]) delete turnRegistry[turnId];
    });
  }

  function processDeltaTurns() {
    if (overlayActive()) {
      teardownAllPacketDisplays();
      return;
    }

    if (!hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }

    var turns = findNativePacketTurns();
    if (!turns.length) return;

    turns.forEach(function (wrap) {
      var turnId = getOrAssignTurnId(wrap);
      var sourceFp = computeSourceFingerprint(wrap);
      processTurn(wrap, turnId, sourceFp, null);
    });
    pruneTurnRegistry(turns);
    reconcileOrphanedPacketSources();
    if (typeof globalThis.__cgwApplyNativeUtilityTurnHide === "function") {
      globalThis.__cgwApplyNativeUtilityTurnHide();
    }
  }

  function waitForUserTurnsReady(callback) {
    var started = Date.now();
    var quietSince = 0;

    function tick() {
      if (overlayActive() || !hideEnabled()) {
        callback(false);
        return;
      }
      var turns = findNativePacketTurns();
      if (turns.length > 0) {
        if (!quietSince) quietSince = Date.now();
        if (Date.now() - quietSince >= DOM_READY_HOLD_MS) {
          callback(true);
          return;
        }
      } else {
        quietSince = 0;
      }
      if (Date.now() - started >= DOM_READY_MAX_MS) {
        callback(turns.length > 0);
        return;
      }
      domReadyTimer = setTimeout(tick, 16);
    }

    if (domReadyTimer != null) clearTimeout(domReadyTimer);
    tick();
  }

  function enterConversationPacketPass(key) {
    if (key !== activeConversationKey) {
      turnRegistry = {};
      sourceBackupByTurnId = {};
      turnDisplayCache = {};
    }
    activeConversationKey = key;

    if (overlayActive() || !hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }

    setPendingAttr();
  }

  function schedulePacketDisplayPass() {
    if (passScheduled) return;
    passScheduled = true;
    requestAnimationFrame(function () {
      passScheduled = false;
      if (overlayActive() || !hideEnabled()) return;
      processDeltaTurns();
    });
  }

  function bindMainObserver() {
    if (mainObserver || typeof MutationObserver === "undefined") return;
    var main = document.querySelector("main") || document.body;
    mainObserver = new MutationObserver(function () {
      if (overlayActive() || !hideEnabled()) return;
      schedulePacketDisplayPass();
    });
    mainObserver.observe(main, { childList: true, subtree: true });
  }

  function packetDisplayNavigate() {
    if (overlayActive()) return;
    if (!hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }

    var key = getConversationKey();
    enterConversationPacketPass(key);

    waitForUserTurnsReady(function (ready) {
      if (!ready || overlayActive() || !hideEnabled()) {
        releasePendingFallback();
        return;
      }
      if (getConversationKey() !== key && key !== null) {
        releasePendingFallback();
        return;
      }
      batchApplyAllTurns();
    });
  }

  function applyNativePacketDisplay() {
    if (overlayActive() || !hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }
    var key = getConversationKey();
    if (key !== activeConversationKey || !Object.keys(turnRegistry).length) {
      packetDisplayNavigate();
      return;
    }
    processDeltaTurns();
  }

  function stampUserTurnDisplay(userLine, packetHash) {
    var turns = findNativePacketTurns();
    if (!turns.length) return;
    var wrap = turns[turns.length - 1];

    if (userLine) wrap.setAttribute("data-cgw-user-line", userLine);
    if (packetHash) wrap.setAttribute("data-cgw-packet-hash", packetHash);

    var turnId = getOrAssignTurnId(wrap);
    var sourceFp = computeSourceFingerprint(wrap);
    if (turnRegistry[turnId]) turnRegistry[turnId].displayFp = null;
    wrapSetDisplayFp(wrap, null);

    var display = resolvePacketDisplay(wrap, userLine || "");
    if (display) persistStamp(wrap, display);

    processTurn(wrap, turnId, sourceFp, null);
    clearPendingAttr();

    if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
      globalThis.__cgwContinuousViewSchedule();
    }
  }

  function clearPacketTurnProcessingState(wrap) {
    if (!wrap) return;
    var turnId = wrap.getAttribute("data-cgw-turn-id");
    wrap.removeAttribute("data-cgw-packet-managed");
    wrap.removeAttribute("data-cgw-packet-fp");
    wrap.removeAttribute("data-cgw-user-line");
    wrap.removeAttribute("data-cgw-packet-hash");
    if (!turnId) return;
    delete turnRegistry[turnId];
    delete sourceBackupByTurnId[turnId];
    Object.keys(turnDisplayCache).forEach(function (key) {
      if (key.indexOf(":" + turnId) >= 0) delete turnDisplayCache[key];
    });
  }

  function scheduleOverlayPacketReprocess(immediate) {
    if (typeof globalThis.__cgwScheduleContinuousViewRebuild === "function") {
      globalThis.__cgwScheduleContinuousViewRebuild(
        immediate ? { immediate: true } : undefined
      );
      return;
    }
    if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
      globalThis.__cgwContinuousViewSchedule(
        immediate ? { immediate: true } : undefined
      );
    }
  }

  function reprocessNativePacketTurns() {
    parseCache.clear();
    turnDisplayCache = {};
    turnRegistry = {};
    sourceBackupByTurnId = {};
    findNativePacketTurns().forEach(function (wrap) {
      clearPacketTurnProcessingState(wrap);
    });
    batchApplyAllTurns();
  }

  function reprocessOverlayPacketTurns(immediate) {
    parseCache.clear();
    turnDisplayCache = {};
    turnRegistry = {};
    scheduleOverlayPacketReprocess(!!immediate);
  }

  function forceReprocessAllPacketTurns(opts) {
    opts = opts || {};
    bindMainObserver();

    if (!hideEnabled()) {
      if (!overlayActive()) teardownAllPacketDisplays();
      return;
    }

    if (overlayActive()) {
      reprocessOverlayPacketTurns(!!opts.immediate);
      return;
    }

    reprocessNativePacketTurns();
  }

  globalThis.__cgwPacketDisplay = {
    parsePacket: parsePacket,
    parseStructuredPreview: parseStructuredPreview,
    parsePacketText: parsePacketText,
    isStructuredPreviewPacket: isStructuredPreviewPacket,
    collectNativeUserMessageText: collectNativeUserMessageText,
    looksLikePacketText: looksLikePacketText,
    stripContextTags: stripContextTags,
    findNativePlayerTextLeaf: findNativePlayerTextLeaf,
    transformUserBlocks: transformUserBlocks,
    resolvePacketDisplay: resolvePacketDisplay,
    buildPlayerBlocks: buildPlayerBlocks,
    buildContextSummaryBlock: buildContextSummaryBlock,
    isPacketContextUiVisible: isPacketContextUiVisible,
    setPacketContextUiVisible: setPacketContextUiVisible,
    togglePacketContextUiVisible: togglePacketContextUiVisible,
    teardownAllPacketDisplays: teardownAllPacketDisplays,
    teardownAllPacketShells: teardownAllPacketDisplays,
    reconcileOrphanedPacketSources: reconcileOrphanedPacketSources,
    applyNativePacketDisplay: applyNativePacketDisplay,
    batchApplyAllTurns: batchApplyAllTurns,
    forceReprocessAllPacketTurns: forceReprocessAllPacketTurns,
    enterConversationPacketPass: enterConversationPacketPass,
    processDeltaTurns: processDeltaTurns,
  };

  globalThis.__cgwStampUserTurnDisplay = stampUserTurnDisplay;
  globalThis.__cgwPacketDisplayNavigate = packetDisplayNavigate;
  globalThis.__cgwForceReprocessAllPacketTurns = forceReprocessAllPacketTurns;
  globalThis.__cgwApplyContextTagDisplay = function () {
    bindMainObserver();
    if (overlayActive()) {
      reprocessOverlayPacketTurns(true);
      return;
    }
    if (!hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }
    reprocessNativePacketTurns();
  };

  bindMainObserver();
  initPacketContextUiVisibility();
  packetDisplayNavigate();
})();
