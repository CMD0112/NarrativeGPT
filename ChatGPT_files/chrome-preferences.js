/**
 * ChatGPT Wrapper — unified chrome preference apply (format, CV flags, highlights).
 */
(function () {
  var lastSnapshot = null;

  function readField(obj, camel, pascal) {
    if (!obj) return undefined;
    if (obj[camel] !== undefined && obj[camel] !== null) return obj[camel];
    if (obj[pascal] !== undefined && obj[pascal] !== null) return obj[pascal];
    return undefined;
  }

  function normalizeTranscriptViewMode(raw) {
    var mode = String(raw || "native").toLowerCase();
    if (mode === "continuous" || mode === "weave") return mode;
    return "native";
  }

  function normalizePayload(raw) {
    var src = raw && typeof raw === "object" ? raw : {};
    var transcriptViewMode = normalizeTranscriptViewMode(
      readField(src, "transcriptViewMode", "TranscriptViewMode")
    );
    var legacyContinuous = !!readField(
      src,
      "continuousViewEnabled",
      "ContinuousViewEnabled"
    );
    if (
      transcriptViewMode === "native" &&
      legacyContinuous &&
      !readField(src, "transcriptViewMode", "TranscriptViewMode")
    ) {
      transcriptViewMode = "continuous";
    }
    return {
      revision:
        typeof readField(src, "revision", "Revision") === "number"
          ? readField(src, "revision", "Revision")
          : 0,
      transcriptViewMode: transcriptViewMode,
      continuousViewEnabled: transcriptViewMode !== "native",
      proseEnhancementsEnabled: !!readField(
        src,
        "proseEnhancementsEnabled",
        "ProseEnhancementsEnabled"
      ),
      hideAssistantEditArtifacts: !!readField(
        src,
        "hideAssistantEditArtifacts",
        "HideAssistantEditArtifacts"
      ),
      hideContextTagsInThread:
        readField(src, "hideContextTagsInThread", "HideContextTagsInThread") !==
        false,
      expandHiddenContextInThread:
        readField(
          src,
          "expandHiddenContextInThread",
          "ExpandHiddenContextInThread"
        ) !== false,
      phraseHighlightsEnabled: !!readField(
        src,
        "phraseHighlightsEnabled",
        "PhraseHighlightsEnabled"
      ),
      phraseHighlightRules:
        readField(src, "phraseHighlightRules", "PhraseHighlightRules") || [],
      continuousViewFormat:
        readField(src, "continuousViewFormat", "ContinuousViewFormat") || {},
    };
  }

  function snapshotPayload(payload) {
    return JSON.stringify({
      revision: payload.revision,
      transcriptViewMode: payload.transcriptViewMode,
      continuousViewEnabled: payload.continuousViewEnabled,
      proseEnhancementsEnabled: payload.proseEnhancementsEnabled,
      hideAssistantEditArtifacts: payload.hideAssistantEditArtifacts,
      hideContextTagsInThread: payload.hideContextTagsInThread,
      expandHiddenContextInThread: payload.expandHiddenContextInThread,
      phraseHighlightsEnabled: payload.phraseHighlightsEnabled,
      phraseHighlightRules: payload.phraseHighlightRules,
      continuousViewFormat: payload.continuousViewFormat,
    });
  }

  function formatFingerprint(format) {
    if (!format || typeof format !== "object") return "";
    return JSON.stringify(format);
  }

  function rulesFingerprint(rules, enabled) {
    return (enabled ? "1" : "0") + "\x05" + JSON.stringify(rules || []);
  }

  function classifyImpact(prev, next) {
    var impact = { cssOnly: false, decorate: false, rebuild: false };
    if (!prev) {
      impact.cssOnly = true;
      impact.decorate = true;
      impact.rebuild = true;
      return impact;
    }

    if (
      formatFingerprint(prev.continuousViewFormat) !==
      formatFingerprint(next.continuousViewFormat)
    ) {
      impact.cssOnly = true;
      var prevFmt = prev.continuousViewFormat || {};
      var nextFmt = next.continuousViewFormat || {};
      if (
        !!readField(prevFmt, "showImages", "ShowImages") !==
        !!readField(nextFmt, "showImages", "ShowImages")
      ) {
        impact.rebuild = true;
      }
    }

    if (prev.proseEnhancementsEnabled !== next.proseEnhancementsEnabled) {
      impact.cssOnly = true;
      impact.decorate = true;
    }

    if (
      prev.hideAssistantEditArtifacts !== next.hideAssistantEditArtifacts ||
      prev.hideContextTagsInThread !== next.hideContextTagsInThread ||
      prev.expandHiddenContextInThread !== next.expandHiddenContextInThread
    ) {
      impact.rebuild = true;
    }

    if (prev.transcriptViewMode !== next.transcriptViewMode) {
      impact.rebuild = true;
    }

    if (prev.continuousViewEnabled !== next.continuousViewEnabled) {
      impact.rebuild = true;
    }

    if (
      rulesFingerprint(prev.phraseHighlightRules, prev.phraseHighlightsEnabled) !==
      rulesFingerprint(next.phraseHighlightRules, next.phraseHighlightsEnabled)
    ) {
      impact.decorate = true;
    }

    if (prev.revision !== next.revision && !impact.rebuild && !impact.decorate) {
      impact.cssOnly = true;
    }

    return impact;
  }

  function setProseAttribute(enabled) {
    if (enabled) {
      document.documentElement.setAttribute("data-cgw-prose-enhanced", "1");
    } else {
      document.documentElement.removeAttribute("data-cgw-prose-enhanced");
    }
  }

  function applyGlobals(payload) {
    globalThis.__cgwTranscriptViewMode = payload.transcriptViewMode;
    globalThis.__cgwContinuousViewEnabled = payload.continuousViewEnabled;
    globalThis.__cgwProseEnhancementsEnabled = payload.proseEnhancementsEnabled;
    globalThis.__cgwHideAssistantEditArtifacts = payload.hideAssistantEditArtifacts;
    globalThis.__cgwHideContextTags = payload.hideContextTagsInThread;
    globalThis.__cgwExpandHiddenContext = payload.expandHiddenContextInThread;
    globalThis.__cgwPhraseHighlightsEnabled = payload.phraseHighlightsEnabled;
    globalThis.__cgwPhraseHighlightRules = payload.phraseHighlightRules;
    globalThis.__cgwFormatSettingsRevision = payload.revision;
    setProseAttribute(payload.proseEnhancementsEnabled);
  }

  function syncComposerClearance() {
    if (typeof globalThis.__cgwUpdateComposerClearance === "function") {
      globalThis.__cgwUpdateComposerClearance();
    }
    var container = document.getElementById("cgw-continuous-view");
    if (!container) return;
    var host =
      container.parentElement &&
      container.parentElement.classList &&
      container.parentElement.classList.contains("cgw-transcript-scroll-host")
        ? container.parentElement
        : container.parentElement;
    if (
      host &&
      typeof globalThis.__cgwSyncOverlayGeometry === "function"
    ) {
      globalThis.__cgwSyncOverlayGeometry(host, container, { preserveScroll: true });
    }
  }

  function applyChromePreferences(rawPayload, opts) {
    opts = opts || {};
    var payload = normalizePayload(rawPayload);
    var prev = lastSnapshot ? JSON.parse(lastSnapshot) : null;
    var impact = classifyImpact(prev, payload);

    applyGlobals(payload);

    if (typeof globalThis.__cgwSetContinuousViewFormat === "function") {
      globalThis.__cgwSetContinuousViewFormat(payload.continuousViewFormat, false);
    }

    syncComposerClearance();

    if (typeof globalThis.__cgwSetHideAssistantEditArtifacts === "function") {
      globalThis.__cgwSetHideAssistantEditArtifacts(
        payload.hideAssistantEditArtifacts
      );
    }

    if (typeof globalThis.__cgwSetPhraseHighlights === "function") {
      globalThis.__cgwSetPhraseHighlights(
        payload.phraseHighlightsEnabled,
        payload.phraseHighlightRules,
        { schedule: false }
      );
    }

    if (typeof globalThis.__cgwApplyContextTagDisplay === "function") {
      globalThis.__cgwApplyContextTagDisplay();
    }

    if (impact.rebuild) {
      if (typeof globalThis.__cgwScheduleContinuousViewRebuild === "function") {
        globalThis.__cgwScheduleContinuousViewRebuild();
      } else if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
        globalThis.__cgwContinuousViewSchedule({ immediate: true });
      }
    } else if (impact.decorate) {
      if (
        typeof globalThis.__cgwScheduleContinuousViewDecorationOnly === "function"
      ) {
        globalThis.__cgwScheduleContinuousViewDecorationOnly();
      } else if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
        globalThis.__cgwContinuousViewSchedule();
      }
    } else if (impact.cssOnly) {
      if (
        typeof globalThis.__cgwScheduleContinuousViewDecorationOnly === "function"
      ) {
        globalThis.__cgwScheduleContinuousViewDecorationOnly();
      }
      syncComposerClearance();
    }

    if (typeof globalThis.__cgwSetTranscriptViewMode === "function") {
      globalThis.__cgwSetTranscriptViewMode(payload.transcriptViewMode);
    } else if (typeof globalThis.__cgwSetContinuousView === "function") {
      globalThis.__cgwSetContinuousView(payload.continuousViewEnabled);
    } else if (impact.rebuild || impact.decorate || impact.cssOnly) {
      if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
        globalThis.__cgwContinuousViewSchedule(
          opts.immediate ? { immediate: true } : undefined
        );
      }
    }

    if (
      opts.navigate !== false &&
      typeof globalThis.__cgwContinuousViewNavigate === "function"
    ) {
      globalThis.__cgwContinuousViewNavigate();
    }

    lastSnapshot = snapshotPayload(payload);
    return impact;
  }

  globalThis.__cgwApplyChromePreferences = applyChromePreferences;
})();
