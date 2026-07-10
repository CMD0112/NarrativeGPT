(function () {
  "use strict";

  var renderers = {};

  function registerRenderer(spec) {
    if (!spec || !spec.id) return;
    renderers[spec.id] = spec;
  }

  function mergeLogTurnLinkIntoRegistry(registry) {
    if (!registry) return registry;
    var map = globalThis.__cgwLogTurnLinkMap || {};
    Object.keys(registry).forEach(function (domTurnId) {
      var entry = registry[domTurnId];
      if (!entry || entry.logTurnIndex == null) return;
      var link = map[entry.logTurnIndex];
      if (!link) return;
      entry.linkedTurnId = link.turnId || link.TurnId;
      entry.turnIndex = link.turnIndex != null ? link.turnIndex : link.TurnIndex;
      entry.playerSnippet = link.playerSnippet || link.PlayerSnippet || "";
      entry.displayTurnNumber =
        link.displayTurnNumber != null
          ? link.displayTurnNumber
          : link.DisplayTurnNumber;
    });
    return registry;
  }

  function assignPlayPairIndices(registry, segments) {
    if (!registry || !segments) return;
    var playPairIndex = 0;
    var awaitingAssistant = false;
    segments.forEach(function (seg) {
      var entry = registry[seg.turnId];
      if (!entry) return;
      if (seg.role === "user") {
        entry.logTurnIndex = playPairIndex;
        entry.editRole = "user";
        awaitingAssistant = true;
      } else if (seg.role === "assistant") {
        entry.logTurnIndex = awaitingAssistant ? playPairIndex : Math.max(0, playPairIndex - 1);
        entry.editRole = "assistant";
        if (awaitingAssistant) {
          playPairIndex++;
          awaitingAssistant = false;
        }
      }
    });
    mergeLogTurnLinkIntoRegistry(registry);
  }

  function resolveInvalidationMarkerTurn(entry) {
    if (!entry) return null;
    if (entry.displayTurnNumber != null) return entry.displayTurnNumber;
    if (entry.turnIndex != null) return entry.turnIndex + 1;
    if (entry.logTurnIndex != null) return entry.logTurnIndex + 1;
    return null;
  }

  function postTurnInvalidated(turnId, reason, revisedText, opts) {
    opts = opts || {};
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = turnId != null ? registry[turnId] : null;
    var editRole =
      opts.editRole ||
      (entry && entry.editRole) ||
      (entry && entry.role === "user" ? "user" : "assistant");

    var msg = {
      type: "turnInvalidated",
      turnId: turnId != null ? String(turnId) : null,
      logTurnIndex:
        opts.logTurnIndex != null
          ? opts.logTurnIndex
          : entry && entry.logTurnIndex != null
            ? entry.logTurnIndex
            : null,
      editRole: editRole,
      reason: reason || "surrogate_edit",
      text: revisedText || "",
      usedFallback: !!opts.usedFallback,
      revisionGroupId: opts.revisionGroupId || null,
      revisionPrompt: opts.revisionPrompt || null,
      assistantDomTurnId: opts.assistantDomTurnId || null,
      ok: true,
    };

    var kernel = globalThis.__cgwPageKernel;
    if (kernel && kernel.bus && typeof kernel.bus.post === "function") {
      kernel.bus.post(msg);
      return;
    }
    try {
      if (
        window.chrome &&
        window.chrome.webview &&
        window.chrome.webview.postMessage
      ) {
        window.chrome.webview.postMessage(JSON.stringify(msg));
      }
    } catch (_e) {
      /* ignore */
    }
  }

  function confirmComposerFallback(onConfirm, onCancel) {
    var ok = window.confirm(
      "ChatGPT's native edit is unavailable. Send a revision message in the composer instead?"
    );
    if (ok) {
      if (typeof onConfirm === "function") onConfirm();
    } else if (typeof onCancel === "function") {
      onCancel();
    }
    return ok;
  }

  function buildSupersedeWarning(entry) {
    if (!entry || entry.logTurnIndex == null) return "";
    var map = globalThis.__cgwLogTurnLinkMap || {};
    var keys = Object.keys(map);
    if (!keys.length) return "";
    var lastIndex = -1;
    keys.forEach(function (k) {
      var idx = parseInt(k, 10);
      if (!isNaN(idx) && idx > lastIndex) lastIndex = idx;
    });
    if (lastIndex < 0 || entry.logTurnIndex >= lastIndex) return "";
    return (
      "Editing turn " +
      (entry.displayTurnNumber || entry.logTurnIndex + 1) +
      " will discard later responses in this adventure log."
    );
  }

  function buildTurnContextLabel(entry, role) {
    if (!entry) return role === "user" ? "Edit message" : "Edit response";
    var num = entry.displayTurnNumber || (entry.logTurnIndex != null ? entry.logTurnIndex + 1 : null);
    if (num == null) return role === "user" ? "Edit message" : "Edit response";
    if (role === "user") return "Edit message (turn " + num + ")";
    var snippet = entry.playerSnippet ? ' — "' + entry.playerSnippet + '"' : "";
    return "Edit response (turn " + num + ")" + snippet;
  }

  globalThis.__cgwTranscriptInteractions = {
    registerRenderer: registerRenderer,
    mergeLogTurnLinkIntoRegistry: mergeLogTurnLinkIntoRegistry,
    assignPlayPairIndices: assignPlayPairIndices,
    resolveInvalidationMarkerTurn: resolveInvalidationMarkerTurn,
    postTurnInvalidated: postTurnInvalidated,
    confirmComposerFallback: confirmComposerFallback,
    buildSupersedeWarning: buildSupersedeWarning,
    buildTurnContextLabel: buildTurnContextLabel,
  };
})();
