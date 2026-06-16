(function () {
  "use strict";

  if (globalThis.__cgwPacketDisplayBooted) {
    if (typeof globalThis.__cgwPacketDisplayNavigate === "function") {
      globalThis.__cgwPacketDisplayNavigate();
    } else if (typeof globalThis.__cgwApplyContextTagDisplay === "function") {
      globalThis.__cgwApplyContextTagDisplay();
    }
    return;
  }
  globalThis.__cgwPacketDisplayBooted = true;

  var MARKER = "[[cgw:";
  var BLOCK_RE =
    /\[\[cgw:([^\]/\]]+)([^\]]*)\]\]([\s\S]*?)\[\[\/cgw:\1\]\]/g;
  var SECTION_ORDER = [
    "meta",
    "sources",
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

  var passScheduled = false;
  var mainObserver = null;
  var activeConversationKey = null;
  var turnRegistry = {};
  var turnDisplayCache = {};
  var nextTurnId = 1;
  var domReadyTimer = null;
  var batchScheduled = false;

  function hideEnabled() {
    return globalThis.__cgwHideContextTags === true;
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
    document
      .querySelectorAll(".cgw-continuous-segment--user")
      .forEach(function (seg) {
        if (!seg.querySelector("[data-cgw-packet-context]")) return;
        if (isPacketContextUiVisible()) {
          seg.classList.remove("cgw-continuous-segment--has-hidden-packet");
        } else {
          seg.classList.add("cgw-continuous-segment--has-hidden-packet");
        }
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
    var visible = false;
    try {
      visible = localStorage.getItem(PACKET_CONTEXT_UI_LS_KEY) === "1";
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
    var actions = root.querySelector('[aria-label="Your message actions"]');
    if (
      actions &&
      actions.parentElement &&
      actions.parentElement.previousElementSibling
    ) {
      return actions.parentElement.previousElementSibling;
    }
    var pre = root.querySelector(
      '.whitespace-pre-wrap, [class*="whitespace-pre-wrap"]'
    );
    if (pre) return pre;
    var dirs = root.querySelectorAll('[dir="auto"]');
    if (dirs.length === 1) return dirs[0];
    var group = root.querySelector('[class*="group/turn-messages"]');
    if (group && group.firstElementChild) return group.firstElementChild;
    return null;
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
    var host = findUserContentHostIn(wrap) || wrap;
    var text = (host && (host.textContent || "")) || "";
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

  function isPacketTurn(turn, rawText) {
    if (rawText && rawText.indexOf(MARKER) >= 0) return true;
    var stamp = readStamp(turn);
    if (stamp && (stamp.userLine || stamp.hash)) return true;
    return !!loadPersistedStamp(turn);
  }

  function getPacketSourceText(turn, blocks) {
    var fromBlocks = sanitizeExtractedMessageText(blocksPlainText(blocks));
    if (fromBlocks.indexOf(MARKER) >= 0) return fromBlocks;

    var host =
      findUserContentHostIn(turn) ||
      turn.querySelector(".cgw-native-packet-source") ||
      turn;
    if (host) {
      var clone = host.cloneNode(true);
      clone.querySelectorAll("button, [role='button'], a").forEach(function (el) {
        var label = (
          (el.getAttribute("aria-label") || "") +
          " " +
          (el.textContent || "")
        )
          .trim()
          .toLowerCase();
        if (label === "show more" || label === "show less") el.remove();
      });
      var hostText = sanitizeExtractedMessageText((clone.textContent || "").trim());
      if (hostText.indexOf(MARKER) >= 0) return hostText;
    }

    return fromBlocks;
  }

  function parsePacket(text) {
    if (!text || text.indexOf(MARKER) < 0) return null;
    if (parseCache.has(text)) return parseCache.get(text);

    var blocks = {};
    var m;
    BLOCK_RE.lastIndex = 0;
    while ((m = BLOCK_RE.exec(text)) !== null) {
      blocks[m[1].toLowerCase()] = (m[3] || "").trim();
    }

    var remainder = sanitizeExtractedMessageText(text.replace(BLOCK_RE, "").trim());
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
    if (!isPacketTurn(turn, rawText)) return null;

    var cleaned = sanitizeExtractedMessageText(rawText || "");
    var parsed =
      cleaned.indexOf(MARKER) >= 0 ? parsePacket(cleaned) : null;
    var stamp = readStamp(turn);

    if (!parsed && !stamp) return null;

    var userLine = "";
    var blocks = {};
    var sectionCount = 0;

    if (stamp && stamp.userLine) {
      userLine = sanitizeExtractedMessageText(stamp.userLine);
      if (stamp.blocks) blocks = stamp.blocks;
      if (typeof stamp.sectionCount === "number") {
        sectionCount = stamp.sectionCount;
      }
    }
    if (parsed) {
      if (!userLine) userLine = sanitizeExtractedMessageText(parsed.userLine);
      blocks = parsed.blocks;
      sectionCount = parsed.sectionCount;
    }

    return {
      userLine: userLine,
      blocks: blocks,
      sectionCount: sectionCount,
    };
  }

  function transformUserBlocks(turn, blocks) {
    if (!blocks || !blocks.length) return blocks;
    var rawText = getPacketSourceText(turn, blocks);
    var display = resolvePacketDisplay(turn, rawText);
    if (!display) return blocks;

    var next = buildPlayerBlocks(display.userLine);
    var summary = buildContextSummaryBlock(
      display.blocks,
      display.sectionCount
    );
    if (summary) next.push(summary);
    return next.length ? next : blocks;
  }

  function renderNativeSectionBody(text) {
    var body = String(text || "").trim();
    if (!body) return "";
    return (
      '<pre class="cgw-packet-context__pre">' + escapeHtml(body) + "</pre>"
    );
  }

  function buildNativeDisplayHtml(display) {
    var parts = [];
    if (display.userLine) {
      parts.push(
        '<div class="cgw-packet-player" data-cgw-packet-player="1">' +
          escapeHtml(display.userLine).replace(/\n/g, "<br>") +
          "</div>"
      );
    }
    if (expandEnabled() && display.sectionCount > 0) {
      var panel =
        '<div class="cgw-packet-context" data-cgw-packet-context="1">' +
        '<details class="cgw-packet-context__details">' +
        '<summary class="cgw-packet-context__header">Adventure context · ' +
        display.sectionCount +
        " section" +
        (display.sectionCount === 1 ? "" : "s") +
        "</summary>" +
        '<div class="cgw-packet-context__sections">';
      orderedSectionNames(display.blocks).forEach(function (name) {
          var body = display.blocks[name];
          if (!body) return;
          panel +=
            '<section class="cgw-packet-context__section">' +
            '<div class="cgw-packet-context__heading">[' +
            escapeHtml(name) +
            "]</div>" +
            '<div class="cgw-packet-context__body">' +
            renderNativeSectionBody(body) +
            "</div></section>";
        });
      panel += "</div></details></div>";
      parts.push(panel);
    }
    return parts.join("");
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
    if (turnId) delete turnRegistry[turnId];

    var host = wrap.querySelector(".cgw-native-packet-source");
    if (!host) host = findUserContentHostIn(wrap);
    if (host) {
      host.classList.remove("cgw-native-packet-source");
      host.removeAttribute("aria-hidden");
    }

    var displayEl = wrap.querySelector(".cgw-native-packet-display");
    if (displayEl) displayEl.remove();

    wrap.removeAttribute("data-cgw-packet-managed");
    wrap.removeAttribute("data-cgw-packet-fp");
  }

  function teardownAllPacketDisplays() {
    document.querySelectorAll("[data-cgw-packet-managed]").forEach(function (wrap) {
      teardownTurn(wrap);
    });
    turnRegistry = {};
    clearPendingAttr();
  }

  function setPendingAttr() {
    if (hideEnabled() && !overlayActive()) {
      document.documentElement.setAttribute("data-cgw-packet-pending", "1");
    }
  }

  function clearPendingAttr() {
    document.documentElement.removeAttribute("data-cgw-packet-pending");
  }

  function findDisplaySibling(host) {
    if (!host) return null;
    var next = host.nextElementSibling;
    if (
      next &&
      next.classList &&
      next.classList.contains("cgw-native-packet-display")
    ) {
      return next;
    }
    return host.parentElement
      ? host.parentElement.querySelector(".cgw-native-packet-display")
      : null;
  }

  function mountPacketDisplay(wrap, html, display, displayFp) {
    var host = findUserContentHostIn(wrap);
    if (!host) return false;

    host.classList.add("cgw-native-packet-source");
    host.setAttribute("aria-hidden", "true");

    var displayEl = findDisplaySibling(host);
    if (!displayEl) {
      displayEl = document.createElement("div");
      displayEl.className = "cgw-native-packet-display";
      displayEl.setAttribute("data-cgw-packet-display", "1");
      if (host.nextSibling) {
        host.parentElement.insertBefore(displayEl, host.nextSibling);
      } else {
        host.parentElement.appendChild(displayEl);
      }
    }

    if (wrap.getAttribute("data-cgw-packet-fp") !== displayFp) {
      displayEl.innerHTML = html;
      wrapSetDisplayFp(wrap, displayFp);
    }

    wrap.setAttribute("data-cgw-packet-managed", "1");
    persistStamp(wrap, display);
    return true;
  }

  function resolveTurnDisplay(wrap, sourceText) {
    var display = resolvePacketDisplay(wrap, sourceText);
    if (!display) return null;
    var displayFp = displayFingerprint(display);
    var html = buildNativeDisplayHtml(display);
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
      entry.displayFp === wrap.getAttribute("data-cgw-packet-fp") &&
      wrap.querySelector(".cgw-native-packet-display")
    ) {
      return;
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
      if (!resolved) return;
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

    mountPacketDisplay(
      wrap,
      resolved.html,
      resolved.display,
      resolved.displayFp
    );
    turnRegistry[turnId] = {
      wrap: wrap,
      sourceFp: sourceFp,
      displayFp: resolved.displayFp,
    };
  }

  function commitMounts(mounts) {
    mounts.forEach(function (item) {
      mountPacketDisplay(
        item.wrap,
        item.html,
        item.display,
        item.displayFp
      );
      turnRegistry[item.turnId] = {
        wrap: item.wrap,
        sourceFp: item.sourceFp,
        displayFp: item.displayFp,
      };
    });
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
      commitMounts(mounts);
      clearPendingAttr();
      pruneTurnRegistry(turns);
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
    if (overlayActive() || !hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }

    var key = getConversationKey();
    enterConversationPacketPass(key);

    waitForUserTurnsReady(function (ready) {
      if (!ready || overlayActive() || !hideEnabled()) {
        clearPendingAttr();
        return;
      }
      if (getConversationKey() !== key && key !== null) {
        clearPendingAttr();
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

  globalThis.__cgwPacketDisplay = {
    parsePacket: parsePacket,
    transformUserBlocks: transformUserBlocks,
    resolvePacketDisplay: resolvePacketDisplay,
    buildPlayerBlocks: buildPlayerBlocks,
    buildContextSummaryBlock: buildContextSummaryBlock,
    isPacketContextUiVisible: isPacketContextUiVisible,
    setPacketContextUiVisible: setPacketContextUiVisible,
    togglePacketContextUiVisible: togglePacketContextUiVisible,
    teardownAllPacketShells: teardownAllPacketDisplays,
    applyNativePacketDisplay: applyNativePacketDisplay,
    batchApplyAllTurns: batchApplyAllTurns,
    enterConversationPacketPass: enterConversationPacketPass,
    processDeltaTurns: processDeltaTurns,
  };

  globalThis.__cgwStampUserTurnDisplay = stampUserTurnDisplay;
  globalThis.__cgwPacketDisplayNavigate = packetDisplayNavigate;
  globalThis.__cgwApplyContextTagDisplay = function () {
    bindMainObserver();
    if (overlayActive() || !hideEnabled()) {
      teardownAllPacketDisplays();
      return;
    }
    packetDisplayNavigate();
  };

  bindMainObserver();
  initPacketContextUiVisibility();
  packetDisplayNavigate();
})();
