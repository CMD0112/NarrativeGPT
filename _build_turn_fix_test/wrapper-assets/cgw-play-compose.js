(function () {
  "use strict";

  var kernel = globalThis.__cgwPageKernel;
  var composerDom = globalThis.__cgwComposerDom;
  var COMPOSE_VERSION = 22;
  var MAX_ATTACHMENT_BYTES = 20 * 1024 * 1024;
  var MAX_ATTACHMENTS = 10;
  var DOM_FALLBACK_STASH_KEY = "__cgwDomFallbackAttachmentStash";
  var pendingAttachments = [];
  var sendLockTimer = null;
  var hostUploadInFlight = false;
  var uploadDebounceTimer = null;
  var uploadJobCounter = 0;

  function sendLog(level, eventName, message, data) {
    if (kernel && kernel.bus && typeof kernel.bus.playSendLog === "function") {
      kernel.bus.playSendLog(level, eventName, message, data, "play-compose");
    }
  }
  var sendInFlight = false;
  var inputDebounce = null;
  var mountPollTimer = null;
  var focusRetryTimer = null;
  var focusWanted = false;
  var focusStartedAt = 0;
  var nativeFocusGuardBound = false;
  var enterGuardBound = false;
  var nativeSendInterceptBound = false;
  var nativeInputSyncBound = false;
  var mountedRoot = null;
  var domUnsubscribe = null;

  var FOCUS_MAX_MS = 3000;

  function postToHost(msg) {
    if (kernel && kernel.bus) kernel.bus.post(msg);
    else {
      try {
        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
          window.chrome.webview.postMessage(JSON.stringify(msg));
        }
      } catch (_e) {
        /* ignore */
      }
    }
  }

  function composer() {
    return composerDom || null;
  }

  function findComposerAnchor() {
    var cd = composer();
    if (cd) return cd.findComposerAnchor(getMounted());
    return { node: document.body, mode: "fixed" };
  }

  function relocateNativeComposerChrome(anchor, root) {
    var cd = composer();
    if (cd) cd.relocateNativeComposerChrome(anchor, root);
  }

  function restoreNativeFromOffscreen() {
    var cd = composer();
    if (cd) cd.restoreNativeFromOffscreen();
  }

  function localSetWrapperComposer(enabled) {
    globalThis.__cgwWrapperComposer = !!enabled;
    var root = document.documentElement;
    if (!root) return;
    if (enabled) root.setAttribute("data-cgw-wrapper-composer", "1");
    else root.removeAttribute("data-cgw-wrapper-composer");
    ensurePlayComposeHooks();
    if (enabled) {
      ensureDomSubscription();
      scheduleMount();
      if (kernel && kernel.features) kernel.features.activate("play-compose");
    } else {
      stopMountPoll();
      cancelFocusRetries();
      focusWanted = false;
      teardownDomSubscription();
      unmountWrapperComposer();
    }
  }

  function ensurePlayComposeHooks() {
    ensureNativeSendIntercept();
    ensureNativeInputSync();
    ensureEnterGuard();
    if (globalThis.__cgwWrapperComposer) ensureNativeFocusGuard();
  }

  function canInterceptNativeSend() {
    if (globalThis.__cgwWrapperComposer) return false;
    if (globalThis.__cgwBridgeAutomationActive) return false;
    var state = globalThis.__cgwPlayComposeState || {};
    if (state.busy || sendInFlight || globalThis.__cgwComposeSendInFlight) return false;
    return true;
  }

  function findNativeComposerInput() {
    var cd = composer();
    if (cd) {
      return cd.findComposerInput({
        preferOffscreen: !!globalThis.__cgwWrapperComposer,
        skipWrapper: true,
      });
    }
    var el = document.querySelector("#prompt-textarea");
    if (el && !el.closest("#cgw-play-composer-root")) return el;
    return (
      document.querySelector('[data-testid="composer-text-input"] [contenteditable="true"]') ||
      document.querySelector('div.ProseMirror[contenteditable="true"]')
    );
  }

  function readNativeComposerText() {
    if (typeof globalThis.__cgwNativeComposerReadText === "function") {
      return globalThis.__cgwNativeComposerReadText();
    }
    var el = findNativeComposerInput();
    if (!el) return "";
    if (el.tagName === "TEXTAREA") return (el.value || "").trim();
    return (el.textContent || el.innerText || "").trim();
  }

  function nativeComposerHasAttachments() {
    if (typeof globalThis.__cgwNativeComposerHasAttachments === "function") {
      return globalThis.__cgwNativeComposerHasAttachments();
    }
    return false;
  }

  function clearNativeComposerText() {
    var el = findNativeComposerInput();
    if (!el) return;
    if (el.tagName === "TEXTAREA") {
      el.value = "";
      el.dispatchEvent(new Event("input", { bubbles: true }));
      return;
    }
    try {
      el.focus();
      document.execCommand("selectAll", false, null);
      document.execCommand("delete", false, null);
    } catch (_e) {
      el.textContent = "";
    }
    el.dispatchEvent(
      new InputEvent("input", { bubbles: true, inputType: "deleteContentBackward" })
    );
  }

  function triggerNativeSend(reason) {
    if (!canInterceptNativeSend()) return false;
    var text = readNativeComposerText();
    var hasAttachments = nativeComposerHasAttachments();
    if (!text && !hasAttachments) return false;

    sendInFlight = true;
    globalThis.__cgwComposeSendInFlight = true;
    armSendLockTimeout();

    sendLog("info", "compose_send_start", "Native composer send intercepted", {
      textLength: text.length,
      attachmentCount: hasAttachments ? 1 : 0,
      reason: reason || null,
    });

    var attachmentMeta =
      composerDom && typeof composerDom.listNativeComposerAttachments === "function"
        ? composerDom.listNativeComposerAttachments()
        : [];

    postToHost({
      type: "cgwComposeSend",
      text: text,
      attachments: [],
      attachmentsPreStaged: hasAttachments,
      attachmentMeta: attachmentMeta,
    });

    clearNativeComposerText();
    postToHost({ type: "cgwComposeInput", text: "" });
    return true;
  }

  function ensureNativeSendIntercept() {
    if (nativeSendInterceptBound) return;
    nativeSendInterceptBound = true;
    document.addEventListener(
      "click",
      function (ev) {
        if (globalThis.__cgwWrapperComposer) return;
        if (!canInterceptNativeSend()) return;
        var btn =
          ev.target && ev.target.closest
            ? ev.target.closest(
                '[data-testid="composer-submit-button"], button[data-testid*="composer-submit"], form button[type="submit"]'
              )
            : null;
        if (!btn) return;
        if (
          !btn.closest('[data-testid="composer"]') &&
          !btn.closest("form:has(#prompt-textarea)")
        ) {
          return;
        }
        ev.preventDefault();
        ev.stopPropagation();
        ev.stopImmediatePropagation();
        triggerNativeSend("submit-click");
      },
      true
    );
  }

  function ensureNativeInputSync() {
    if (nativeInputSyncBound) return;
    nativeInputSyncBound = true;
    document.addEventListener(
      "input",
      function (ev) {
        if (globalThis.__cgwWrapperComposer) return;
        if (!isNativeComposerElement(ev.target)) return;
        if (inputDebounce) clearTimeout(inputDebounce);
        inputDebounce = setTimeout(function () {
          inputDebounce = null;
          postToHost({ type: "cgwComposeInput", text: readNativeComposerText() });
        }, 120);
      },
      true
    );
  }

  function ensureDomSubscription() {
    if (domUnsubscribe || !kernel || !kernel.dom) return;
    domUnsubscribe = kernel.dom.subscribe(
      "play-compose-mount",
      { debounceMs: 150 },
      function () {
        if (!globalThis.__cgwWrapperComposer) return;
        var mounted = getMounted();
        var current = findComposerAnchor();
        if (needsRemount(mounted, current)) {
          scheduleMount();
          return;
        }
        if (current && current.node && mounted) {
          relocateNativeComposerChrome(current.node, mounted);
        }
      }
    );
  }

  function teardownDomSubscription() {
    if (domUnsubscribe) {
      domUnsubscribe();
      domUnsubscribe = null;
    } else if (kernel && kernel.dom) {
      kernel.dom.unsubscribe("play-compose-mount");
    }
  }

  function startMountPoll() {
    stopMountPoll();
    if (getMounted()) return;
    var attempts = 0;
    mountPollTimer = setInterval(function () {
      if (!globalThis.__cgwWrapperComposer) {
        stopMountPoll();
        return;
      }
      mountWrapperComposer();
      attempts++;
      if (attempts >= 20 || getMounted()) stopMountPoll();
    }, 150);
  }

  function stopMountPoll() {
    if (mountPollTimer) {
      clearInterval(mountPollTimer);
      mountPollTimer = null;
    }
  }

  function sendIconSvg() {
    return (
      '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">' +
      '<path d="M12 3.5l7 7.5h-4.5V20h-5v-8.5H5l7-8z" fill="currentColor"/>' +
      "</svg>"
    );
  }

  function attachIconSvg() {
    return (
      '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">' +
      '<path d="M16.5 6.5v9.25a4.25 4.25 0 1 1-8.5 0V7a2.75 2.75 0 1 1 5.5 0v8.5a1.25 1.25 0 1 1-2.5 0V7.5" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>' +
      "</svg>"
    );
  }

  function removeIconSvg() {
    return (
      '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">' +
      '<path d="M6 6l12 12M18 6L6 18" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>' +
      "</svg>"
    );
  }

  function formatBytes(bytes) {
    if (!bytes || bytes < 1024) return bytes + " B";
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
  }

  function newAttachmentId() {
    uploadJobCounter += 1;
    return "att-" + Date.now().toString(36) + "-" + uploadJobCounter;
  }

  function attachmentIsUploading(att) {
    return att.uploadStatus === "reading" || att.uploadStatus === "queued" || att.uploadStatus === "uploading";
  }

  function allAttachmentsReady() {
    if (!pendingAttachments.length) return true;
    return pendingAttachments.every(function (att) {
      return att.uploadStatus === "ready";
    });
  }

  function anyAttachmentUploading() {
    return pendingAttachments.some(attachmentIsUploading) || hostUploadInFlight;
  }

  function canSendNow(input) {
    var text = input ? (input.value || "").trim() : readInputText();
    if (pendingAttachments.length) {
      return allAttachmentsReady() && (!!text || pendingAttachments.length > 0);
    }
    return !!text;
  }

  function updateUploadFooter(root) {
    root = root || getMounted();
    if (!root) return;
    var footer = root.querySelector(".cgw-compose-footer");
    if (!footer) return;
    var state = globalThis.__cgwPlayComposeState || {};
    if (state.busy) return;

    var uploading = pendingAttachments.filter(function (att) {
      return att.uploadStatus === "uploading";
    });
    var preparing = pendingAttachments.filter(function (att) {
      return att.uploadStatus === "reading" || att.uploadStatus === "queued";
    });
    var failed = pendingAttachments.filter(function (att) {
      return att.uploadStatus === "error";
    });

    if (uploading.length) {
      var label = uploading[0].name || "file";
      footer.textContent =
        uploading.length > 1
          ? "Uploading " + uploading.length + " files…"
          : "Uploading " + label + "…";
      return;
    }
    if (preparing.length || hostUploadInFlight) {
      footer.textContent = "Preparing upload…";
      return;
    }
    if (failed.length) {
      footer.textContent = failed[0].uploadError || "Upload failed — remove and try again";
      return;
    }
    if (!state.status) footer.textContent = "";
    var shell = root.querySelector(".cgw-compose-shell");
    if (shell) shell.classList.toggle("cgw-compose-uploading", anyAttachmentUploading());
  }

  function updateSendEnabled(input, sendBtn) {
    if (!sendBtn) return;
    var state = globalThis.__cgwPlayComposeState || {};
    var root = getMounted();
    if (state.busy || sendInFlight || anyAttachmentUploading()) {
      sendBtn.disabled = true;
      updateUploadFooter(root);
      return;
    }
    sendBtn.disabled = !canSendNow(input);
    updateUploadFooter(root);
  }

  function spinnerIconSvg() {
    return (
      '<svg viewBox="0 0 20 20" fill="none" aria-hidden="true" class="cgw-compose-upload-spinner">' +
      '<circle cx="10" cy="10" r="7" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-dasharray="28 12" />' +
      "</svg>"
    );
  }

  function renderAttachmentChips(root) {
    if (!root) return;
    var row = root.querySelector(".cgw-compose-attachments");
    if (!row) return;
    row.innerHTML = "";
    if (!pendingAttachments.length) {
      row.hidden = true;
      return;
    }
    row.hidden = false;
    pendingAttachments.forEach(function (att, index) {
      var chip = document.createElement("div");
      var status = att.uploadStatus || "queued";
      chip.className = "cgw-compose-attachment cgw-compose-attachment--" + status;
      var statusLabel = "";
      if (status === "reading" || status === "queued") statusLabel = "Preparing";
      else if (status === "uploading") statusLabel = "Uploading";
      else if (status === "ready") statusLabel = "Ready";
      else if (status === "error") statusLabel = "Failed";
      var statusHtml =
        status === "uploading" || status === "reading" || status === "queued"
          ? '<span class="cgw-compose-attachment-status" aria-live="polite">' +
            spinnerIconSvg() +
            "<span>" +
            statusLabel +
            "</span></span>"
          : status === "ready"
            ? '<span class="cgw-compose-attachment-status cgw-compose-attachment-status--ready" aria-label="Upload complete">✓</span>'
            : status === "error"
              ? '<span class="cgw-compose-attachment-status cgw-compose-attachment-status--error" title="' +
                (att.uploadError || "Upload failed") +
                '">!</span>'
              : "";
      chip.innerHTML =
        statusHtml +
        '<span class="cgw-compose-attachment-name" title="' +
        (att.name || "file") +
        '">' +
        (att.name || "file") +
        "</span>" +
        '<span class="cgw-compose-attachment-size">' +
        formatBytes(att.sizeBytes || 0) +
        "</span>" +
        '<button type="button" class="cgw-compose-attachment-remove" aria-label="Remove attachment">' +
        removeIconSvg() +
        "</button>";
      var removeBtn = chip.querySelector(".cgw-compose-attachment-remove");
      if (removeBtn) {
        removeBtn.disabled = status === "uploading" || status === "reading";
        removeBtn.addEventListener("click", function (ev) {
          ev.preventDefault();
          if (att.uploadStatus === "uploading" || att.uploadStatus === "reading") return;
          pendingAttachments.splice(index, 1);
          renderAttachmentChips(root);
          scheduleAttachmentUpload();
          updateSendEnabled(getComposeInput(), root.querySelector(".cgw-compose-send"));
        });
      }
      row.appendChild(chip);
    });
    updateUploadFooter(root);
  }

  function serializeAttachmentForHost(att) {
    var payload = {
      id: att.id,
      name: att.name,
      mimeType: att.mimeType,
      base64: att.base64,
      sizeBytes: att.sizeBytes,
    };
    if (att.width > 0) payload.width = att.width;
    if (att.height > 0) payload.height = att.height;
    return payload;
  }

  function startAttachmentUploadBatch() {
    if (!globalThis.__cgwWrapperComposer) return;
    if (hostUploadInFlight) return;
    var batch = pendingAttachments.filter(function (att) {
      return att.uploadStatus === "queued";
    });
    if (!batch.length) {
      updateUploadFooter(getMounted());
      updateSendEnabled(getComposeInput(), getMounted()?.querySelector(".cgw-compose-send"));
      return;
    }

    var jobId = "upload-" + Date.now().toString(36);
    hostUploadInFlight = true;
    batch.forEach(function (att) {
      att.uploadStatus = "uploading";
      att.uploadJobId = jobId;
    });
    renderAttachmentChips(getMounted());
    updateSendEnabled(getComposeInput(), getMounted()?.querySelector(".cgw-compose-send"));

    postToHost({
      type: "cgwComposeUploadRequest",
      jobId: jobId,
      attachmentIds: batch.map(function (att) {
        return att.id;
      }),
      attachments: batch.map(serializeAttachmentForHost),
    });
    sendLog("info", "compose_upload_start", "Requested host upload for attachments", {
      jobId: jobId,
      attachmentCount: batch.length,
    });
  }

  function scheduleAttachmentUpload() {
    if (!globalThis.__cgwWrapperComposer) return;
    if (uploadDebounceTimer) clearTimeout(uploadDebounceTimer);
    uploadDebounceTimer = setTimeout(function () {
      uploadDebounceTimer = null;
      startAttachmentUploadBatch();
    }, 350);
  }

  function applyUploadStatus(jobId, attachmentIds, status, error) {
    var ids = attachmentIds && attachmentIds.length ? attachmentIds : null;
    var touched = false;
    pendingAttachments.forEach(function (att) {
      var matchesJob = jobId && att.uploadJobId === jobId;
      var matchesId = ids && ids.indexOf(att.id) >= 0;
      if (!matchesJob && !matchesId) return;
      if (status === "uploading" && att.uploadStatus !== "queued") return;
      att.uploadStatus = status;
      att.uploadError = error || "";
      if (status !== "uploading") att.uploadJobId = "";
      touched = true;
    });
    if (!touched) return;
    hostUploadInFlight = status === "uploading";
    if (status === "ready" || status === "error") {
      hostUploadInFlight = false;
      scheduleAttachmentUpload();
    }
    renderAttachmentChips(getMounted());
    updateSendEnabled(getComposeInput(), getMounted()?.querySelector(".cgw-compose-send"));
    sendLog(status === "error" ? "warn" : "info", "compose_upload_status", "Attachment upload status updated", {
      jobId: jobId,
      status: status,
      error: error || null,
      attachmentCount: ids ? ids.length : null,
    });
  }

  function clearPendingAttachments(root) {
    pendingAttachments = [];
    renderAttachmentChips(root || getMounted());
  }

  function copyAttachmentForStash(att) {
    return {
      name: att.name,
      mimeType: att.mimeType,
      base64: att.base64,
      sizeBytes: att.sizeBytes,
      width: att.width,
      height: att.height,
    };
  }

  function readDomFallbackStash() {
    var stash = globalThis[DOM_FALLBACK_STASH_KEY];
    return stash && stash.length ? stash : null;
  }

  function stashAttachmentsForDomFallback() {
    if (!pendingAttachments.length) return;
    var copy = pendingAttachments.map(copyAttachmentForStash);
    globalThis[DOM_FALLBACK_STASH_KEY] = copy;
    sendLog("info", "compose_dom_stash", "Stashed attachments for DOM fallback", {
      attachmentCount: copy.length,
      totalBytes: copy.reduce(function (sum, att) {
        return sum + (att.sizeBytes || 0);
      }, 0),
    });
  }

  function peekDomFallbackAttachments() {
    var stash = readDomFallbackStash();
    if (!stash) return [];
    return stash.map(copyAttachmentForStash);
  }

  function clearDomFallbackAttachmentStash() {
    delete globalThis[DOM_FALLBACK_STASH_KEY];
  }

  function readImageDimensions(file, done) {
    if (!file || !file.type || file.type.indexOf("image/") !== 0) {
      done(null, null);
      return;
    }
    var url = URL.createObjectURL(file);
    var img = new Image();
    img.onload = function () {
      URL.revokeObjectURL(url);
      done(img.naturalWidth || img.width || null, img.naturalHeight || img.height || null);
    };
    img.onerror = function () {
      URL.revokeObjectURL(url);
      done(null, null);
    };
    img.src = url;
  }

  function readFileAsBase64(file, done) {
    try {
      var reader = new FileReader();
      reader.onload = function () {
        var result = reader.result || "";
        var comma = String(result).indexOf(",");
        done(null, comma >= 0 ? String(result).slice(comma + 1) : String(result));
      };
      reader.onerror = function () {
        done(reader.error || new Error("read_failed"));
      };
      reader.readAsDataURL(file);
    } catch (err) {
      done(err);
    }
  }

  function addFilesFromList(fileList, root, footer) {
    if (!fileList || !fileList.length) return;
    var input = root ? root.querySelector(".cgw-compose-input") : getComposeInput();
    var sendBtn = root ? root.querySelector(".cgw-compose-send") : null;
    var rejected = [];
    var files = Array.prototype.slice.call(fileList);
    var remaining = 0;

    function finishBatch() {
      renderAttachmentChips(root || getMounted());
      updateSendEnabled(input, sendBtn);
      if (rejected.length && footer) {
        footer.textContent = rejected.join("; ");
      } else {
        scheduleAttachmentUpload();
      }
    }

    files.forEach(function (file) {
      if (pendingAttachments.length >= MAX_ATTACHMENTS) {
        rejected.push("Maximum " + MAX_ATTACHMENTS + " attachments");
        return;
      }
      if (!file || !file.size) {
        rejected.push((file && file.name) || "Empty file skipped");
        return;
      }
      if (file.size > MAX_ATTACHMENT_BYTES) {
        rejected.push((file.name || "File") + " exceeds " + formatBytes(MAX_ATTACHMENT_BYTES));
        return;
      }
      remaining++;
      var slotIndex = pendingAttachments.length;
      pendingAttachments.push({
        id: newAttachmentId(),
        name: file.name || "attachment",
        mimeType: file.type || "application/octet-stream",
        base64: "",
        sizeBytes: file.size,
        width: null,
        height: null,
        uploadStatus: "reading",
        uploadError: "",
        uploadJobId: "",
      });
      renderAttachmentChips(root || getMounted());
      updateSendEnabled(input, sendBtn);
      readImageDimensions(file, function (width, height) {
        readFileAsBase64(file, function (err, base64) {
          var slot = pendingAttachments[slotIndex];
          if (!slot || slot.uploadStatus !== "reading") {
            remaining--;
            if (remaining <= 0) finishBatch();
            return;
          }
          if (err || !base64) {
            if (pendingAttachments[slotIndex] === slot) pendingAttachments.splice(slotIndex, 1);
            rejected.push((file.name || "File") + " could not be read");
          } else {
            slot.base64 = base64;
            slot.width = width;
            slot.height = height;
            slot.uploadStatus = "queued";
          }
          remaining--;
          if (remaining <= 0) finishBatch();
        });
      });
    });

    if (remaining === 0) finishBatch();
  }

  function resizeInput(input) {
    if (!input) return;
    input.style.height = "auto";
    var next = Math.min(input.scrollHeight, 200);
    input.style.height = Math.max(24, next) + "px";
  }

  function getMounted() {
    if (mountedRoot && mountedRoot.isConnected) return mountedRoot;
    var byId = document.getElementById("cgw-play-composer-root");
    if (byId) {
      mountedRoot = byId;
      return byId;
    }
    return mountedRoot;
  }

  function getComposeInput() {
    var root = getMounted();
    return root ? root.querySelector(".cgw-compose-input") : null;
  }

  function readInputText() {
    if (!globalThis.__cgwWrapperComposer) return readNativeComposerText();
    var input = getComposeInput();
    return input ? (input.value || "").trim() : "";
  }

  function isNativeComposerElement(node) {
    var cd = composer();
    if (cd && cd.isInsideWrapper(node)) return false;
    if (!node || !node.closest) return false;
    if (node.closest("#cgw-play-composer-root")) return false;
    if (
      node.closest('[data-testid="composer"]') ||
      node.closest("form:has(#prompt-textarea)") ||
      node.closest("#cgw-native-composer-offscreen")
    ) {
      if (node.id === "prompt-textarea") return true;
      if (node.closest('[data-testid="composer-text-input"]')) return true;
      if (node.closest('div.ProseMirror[contenteditable="true"]')) return true;
      return node.isContentEditable || node.tagName === "TEXTAREA";
    }
    return false;
  }

  function isolateNativeComposerFocus() {
    var scopedSelectors = [
      '[data-testid="composer"] #prompt-textarea',
      '[data-testid="composer"] [data-testid="composer-text-input"]',
      'form:has(#prompt-textarea) #prompt-textarea',
      '#cgw-native-composer-offscreen #prompt-textarea',
      '[data-testid="composer"] div.ProseMirror[contenteditable="true"]',
      '#cgw-native-composer-offscreen div.ProseMirror[contenteditable="true"]',
    ];
    scopedSelectors.forEach(function (sel) {
      document.querySelectorAll(sel).forEach(function (el) {
        if (el.closest("#cgw-play-composer-root")) return;
        try {
          el.tabIndex = -1;
          el.setAttribute("aria-hidden", "true");
        } catch (_e) {
          /* ignore */
        }
      });
    });
  }

  function ensureEnterGuard() {
    if (enterGuardBound) return;
    enterGuardBound = true;
    document.addEventListener(
      "keydown",
      function (ev) {
        if (globalThis.__cgwBridgeAutomationActive) return;
        if (ev.key !== "Enter" || ev.shiftKey || ev.isComposing) return;

        var state = globalThis.__cgwPlayComposeState || {};
        var fromWrapper =
          ev.target && ev.target.closest && ev.target.closest("#cgw-play-composer-root");
        var fromNative = isNativeComposerElement(ev.target);

        if (!fromWrapper && !fromNative) return;

        if (!globalThis.__cgwWrapperComposer) {
          if (!fromNative) return;
          ev.preventDefault();
          ev.stopPropagation();
          ev.stopImmediatePropagation();
          triggerNativeSend("native-enter");
          return;
        }

        if (fromNative) {
          ev.preventDefault();
          ev.stopPropagation();
          ev.stopImmediatePropagation();
          if (!state.busy && !sendInFlight) requestComposeFocus("native-enter");
          return;
        }

        if (state.busy || sendInFlight || globalThis.__cgwComposeSendInFlight) {
          ev.preventDefault();
          ev.stopPropagation();
          ev.stopImmediatePropagation();
          return;
        }

        var root = getMounted();
        var sendBtn = root && root.querySelector(".cgw-compose-send");
        if (sendBtn && sendBtn.disabled) {
          ev.preventDefault();
          ev.stopPropagation();
          ev.stopImmediatePropagation();
        }
      },
      true
    );
  }

  function ensureNativeFocusGuard() {
    if (nativeFocusGuardBound) return;
    nativeFocusGuardBound = true;
    document.addEventListener(
      "focusin",
      function (ev) {
        if (!globalThis.__cgwWrapperComposer) return;
        if (globalThis.__cgwBridgeAutomationActive) return;
        var state = globalThis.__cgwPlayComposeState || {};
        if (state.busy) return;
        if (!isNativeComposerElement(ev.target)) return;
        requestComposeFocus("native-steal");
      },
      true
    );
  }

  function unmountWrapperComposer() {
    stopMountPoll();
    var root = getMounted();
    if (root) root.remove();
    mountedRoot = null;
    restoreNativeFromOffscreen();
  }

  function releaseSendLock() {
    sendInFlight = false;
    globalThis.__cgwComposeSendInFlight = false;
    if (sendLockTimer) {
      clearTimeout(sendLockTimer);
      sendLockTimer = null;
    }
    var sendBtn = getMounted()?.querySelector(".cgw-compose-send");
    var state = globalThis.__cgwPlayComposeState || {};
    if (sendBtn) sendBtn.disabled = !!state.busy || sendInFlight || !canSendNow(getComposeInput());
  }

  function armSendLockTimeout() {
    if (sendLockTimer) clearTimeout(sendLockTimer);
    sendLockTimer = setTimeout(function () {
      sendLockTimer = null;
      if (!sendInFlight && !globalThis.__cgwComposeSendInFlight) return;
      var state = globalThis.__cgwPlayComposeState || {};
      if (state.busy) return;
      releaseSendLock();
    }, 12000);
  }

  function cancelFocusRetries() {
    if (focusRetryTimer) {
      clearTimeout(focusRetryTimer);
      focusRetryTimer = null;
    }
  }

  function blurNativeComposerFocus() {
    var active = document.activeElement;
    if (!active || !active.closest) return;
    if (active.closest("#cgw-play-composer-root")) return;
    if (!isNativeComposerElement(active)) return;
    try {
      active.blur();
    } catch (_e) {
      /* ignore */
    }
  }

  function placeCaretAtEnd(input) {
    if (!input) return;
    var len = (input.value || "").length;
    try {
      input.setSelectionRange(len, len);
    } catch (_e) {
      /* ignore */
    }
  }

  function focusComposeInput() {
    var root = getMounted();
    if (!root) return false;
    var input = root.querySelector(".cgw-compose-input");
    if (!input) return false;

    var state = globalThis.__cgwPlayComposeState || {};
    if (state.busy) return false;

    var sendBtn = root.querySelector(".cgw-compose-send");
    if (sendBtn) sendBtn.disabled = !!state.busy || sendInFlight || !canSendNow(input);

    blurNativeComposerFocus();

    try {
      input.focus({ preventScroll: true });
    } catch (_focus) {
      try {
        input.focus();
      } catch (_e2) {
        return false;
      }
    }

    if (document.activeElement === input) {
      placeCaretAtEnd(input);
      return true;
    }
    return false;
  }

  function scheduleFocusAttempt(delayMs) {
    cancelFocusRetries();
    focusRetryTimer = setTimeout(function () {
      focusRetryTimer = null;
      ensureFocused();
    }, delayMs);
  }

  function ensureFocused() {
    if (!focusWanted) return;
    if (!globalThis.__cgwWrapperComposer) {
      focusWanted = false;
      return;
    }

    var state = globalThis.__cgwPlayComposeState || {};
    if (state.busy) {
      scheduleFocusAttempt(80);
      return;
    }

    if (focusComposeInput()) {
      focusWanted = false;
      cancelFocusRetries();
      return;
    }

    if (Date.now() - focusStartedAt >= FOCUS_MAX_MS) {
      focusWanted = false;
      cancelFocusRetries();
      return;
    }

    scheduleFocusAttempt(60);
  }

  function requestComposeFocus(_reason) {
    focusWanted = true;
    focusStartedAt = Date.now();
    cancelFocusRetries();
    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        ensureFocused();
      });
    });
  }

  function triggerSend(input, sendBtn) {
    if (sendInFlight || globalThis.__cgwComposeSendInFlight) {
      sendLog("warn", "compose_send_blocked", "Send blocked while in flight", {
        sendInFlight: sendInFlight,
        globalInFlight: !!globalThis.__cgwComposeSendInFlight,
      });
      return;
    }
    if (!input || !sendBtn || sendBtn.disabled) {
      sendLog("warn", "compose_send_blocked", "Send blocked by disabled controls", {
        hasInput: !!input,
        hasSendBtn: !!sendBtn,
        sendDisabled: sendBtn ? sendBtn.disabled : null,
      });
      return;
    }
    var text = (input.value || "").trim();
    if (!text && !pendingAttachments.length) {
      sendLog("debug", "compose_send_empty", "Send ignored for empty text and no attachments");
      return;
    }

    if (!allAttachmentsReady()) {
      sendLog("warn", "compose_send_blocked", "Send blocked until attachment uploads finish", {
        pending: pendingAttachments.length,
      });
      return;
    }

    var attachmentsPayload = pendingAttachments
      .filter(function (att) {
        return att.uploadStatus === "ready";
      })
      .map(function (att) {
        var payload = {
          name: att.name,
          mimeType: att.mimeType,
          base64: att.base64,
          sizeBytes: att.sizeBytes,
        };
        if (att.width > 0) payload.width = att.width;
        if (att.height > 0) payload.height = att.height;
        return payload;
      });
    var attachmentsPreStaged =
      attachmentsPayload.length > 0 &&
      pendingAttachments.every(function (att) {
        return att.uploadStatus === "ready";
      });

    sendLog("info", "compose_send_start", "Wrapper composer send triggered", {
      textLength: text.length,
      attachmentCount: attachmentsPayload.length,
      preview: text.length > 120 ? text.slice(0, 120) + "…" : text,
    });

    sendInFlight = true;
    globalThis.__cgwComposeSendInFlight = true;
    sendBtn.disabled = true;
    armSendLockTimeout();

    postToHost({ type: "cgwComposeInput", text: text });
    postToHost({
      type: "cgwComposeSend",
      text: text,
      attachments: attachmentsPayload,
      attachmentsPreStaged: attachmentsPreStaged,
    });

    sendLog("info", "compose_send_posted", "Posted compose send messages to host", {
      textLength: text.length,
      attachmentCount: attachmentsPayload.length,
    });

    if (attachmentsPayload.length && !attachmentsPreStaged) {
      stashAttachmentsForDomFallback();
    }

    input.value = "";
    clearPendingAttachments(getMounted());
    resizeInput(input);
    if (inputDebounce) {
      clearTimeout(inputDebounce);
      inputDebounce = null;
    }

    requestComposeFocus("after-send");
  }

  function buildComposerDom() {
    var root = document.createElement("div");
    root.id = "cgw-play-composer-root";

    root.innerHTML =
      '<div class="cgw-compose-wrap">' +
      '<div class="cgw-compose-shell">' +
      '<div class="cgw-compose-attachments" hidden></div>' +
      '<div class="cgw-compose-main">' +
      '<button type="button" class="cgw-compose-attach" aria-label="Attach files" title="Attach files">' +
      attachIconSvg() +
      "</button>" +
      '<input type="file" class="cgw-compose-file-input" multiple tabindex="-1" aria-hidden="true" />' +
      '<textarea class="cgw-compose-input" rows="1" aria-label="Message ChatGPT" placeholder="Message ChatGPT"></textarea>' +
      '<button type="button" class="cgw-compose-send" aria-label="Send message" title="Send (Enter)" disabled>' +
      sendIconSvg() +
      "</button>" +
      "</div>" +
      '<div class="cgw-compose-footer" aria-live="polite"></div>' +
      "</div>" +
      "</div>";

    var input = root.querySelector(".cgw-compose-input");
    var sendBtn = root.querySelector(".cgw-compose-send");
    var attachBtn = root.querySelector(".cgw-compose-attach");
    var fileInput = root.querySelector(".cgw-compose-file-input");
    var shell = root.querySelector(".cgw-compose-shell");
    var footer = root.querySelector(".cgw-compose-footer");

    function notifyInput() {
      if (inputDebounce) clearTimeout(inputDebounce);
      inputDebounce = setTimeout(function () {
        postToHost({ type: "cgwComposeInput", text: input.value || "" });
      }, 120);
    }

    input.addEventListener("input", function () {
      resizeInput(input);
      notifyInput();
      updateSendEnabled(input, sendBtn);
    });

    input.addEventListener("keydown", function (ev) {
      if (ev.key !== "Enter" || ev.shiftKey || ev.isComposing) return;
      ev.preventDefault();
      ev.stopPropagation();
      var state = globalThis.__cgwPlayComposeState || {};
      if (state.busy || sendInFlight || sendBtn.disabled) return;
      if (!canSendNow(input)) return;
      triggerSend(input, sendBtn);
    });

    sendBtn.addEventListener("click", function (ev) {
      ev.preventDefault();
      triggerSend(input, sendBtn);
    });

    attachBtn.addEventListener("click", function (ev) {
      ev.preventDefault();
      var state = globalThis.__cgwPlayComposeState || {};
      if (state.busy || sendInFlight) return;
      try {
        fileInput.value = "";
        fileInput.click();
      } catch (_e) {
        /* ignore */
      }
    });

    fileInput.addEventListener("change", function () {
      if (!fileInput.files || !fileInput.files.length) return;
      addFilesFromList(fileInput.files, root, footer);
      fileInput.value = "";
    });

    shell.addEventListener("dragover", function (ev) {
      var state = globalThis.__cgwPlayComposeState || {};
      if (state.busy || sendInFlight) return;
      if (!ev.dataTransfer || !ev.dataTransfer.types) return;
      if (ev.dataTransfer.types.indexOf("Files") < 0) return;
      ev.preventDefault();
      shell.classList.add("cgw-compose-dragover");
    });

    shell.addEventListener("dragleave", function (ev) {
      if (ev.target === shell || !shell.contains(ev.relatedTarget)) {
        shell.classList.remove("cgw-compose-dragover");
      }
    });

    shell.addEventListener("drop", function (ev) {
      shell.classList.remove("cgw-compose-dragover");
      var state = globalThis.__cgwPlayComposeState || {};
      if (state.busy || sendInFlight) return;
      if (!ev.dataTransfer || !ev.dataTransfer.files || !ev.dataTransfer.files.length) return;
      ev.preventDefault();
      addFilesFromList(ev.dataTransfer.files, root, footer);
    });

    mountedRoot = root;
    return root;
  }

  function needsRemount(mounted, anchorInfo) {
    if (!mounted) return true;
    if (!mounted.isConnected) return true;
    if (!anchorInfo || !anchorInfo.node) return false;
    if (!anchorInfo.node.contains(mounted)) return true;
    var host = mounted.closest('[data-testid="composer"]');
    if (host && host !== anchorInfo.node) return true;
    return false;
  }

  function mountWrapperComposer() {
    if (!globalThis.__cgwWrapperComposer) return;

    var anchorInfo = findComposerAnchor();
    if (!anchorInfo || !anchorInfo.node) return;

    var anchor = anchorInfo.node;
    var existing = getMounted();

    if (existing && anchor.contains(existing)) {
      if (anchorInfo.mode === "fixed") {
        existing.classList.add("cgw-compose-fixed");
      } else {
        existing.classList.remove("cgw-compose-fixed");
      }
      relocateNativeComposerChrome(anchor, existing);
      isolateNativeComposerFocus();
      return;
    }

    var root = existing;
    if (!root) {
      root = buildComposerDom();
    }

    if (anchorInfo.mode === "fixed") {
      root.classList.add("cgw-compose-fixed");
    } else {
      root.classList.remove("cgw-compose-fixed");
    }

    if (root.parentElement !== anchor) {
      anchor.insertBefore(root, anchor.firstChild);
    }
    mountedRoot = root;

    paintComposeDomFromState(globalThis.__cgwPlayComposeState || {}, {});
    relocateNativeComposerChrome(anchor, root);
    isolateNativeComposerFocus();
    if (typeof applyPlaySurfaceActions === "function") applyPlaySurfaceActions();
    bindComposerInsetObserver();
    if (focusWanted) requestComposeFocus("remount");
    ensureDomSubscription();
  }

  function scheduleMount() {
    if (!globalThis.__cgwWrapperComposer) return;
    mountWrapperComposer();
    if (getMounted()) return;
    setTimeout(mountWrapperComposer, 0);
    setTimeout(mountWrapperComposer, 80);
    setTimeout(mountWrapperComposer, 250);
    startMountPoll();
  }

  function paintComposeDomFromState(state, patch) {
    if (!state || typeof state !== "object") return;
    var root = getMounted();
    if (!root) return;

    var input = root.querySelector(".cgw-compose-input");
    var sendBtn = root.querySelector(".cgw-compose-send");
    var footer = root.querySelector(".cgw-compose-footer");

    patch = patch || {};
    if (
      input &&
      (Object.prototype.hasOwnProperty.call(patch, "text") || patch.clear)
    ) {
      input.value = typeof state.text === "string" ? state.text : "";
      resizeInput(input);
    }

    if (typeof state.placeholder === "string" && input) {
      input.placeholder = state.placeholder;
    }

    var busy = !!state.busy;
    root.classList.toggle("cgw-compose-busy", busy);
    var attachBtn = root.querySelector(".cgw-compose-attach");
    if (attachBtn) attachBtn.disabled = busy || sendInFlight;
    if (sendBtn) updateSendEnabled(input, sendBtn);

    if (typeof state.status === "string" && footer) {
      footer.textContent = state.status;
    }
  }

  function applyComposeState(patch) {
    if (!patch || typeof patch !== "object") return;

    var prev = globalThis.__cgwPlayComposeState || {};
    var next = Object.assign({}, prev, patch);
    if (patch.clear) {
      next.text = "";
      delete next.clear;
    }
    globalThis.__cgwPlayComposeState = next;

    if (!getMounted()) {
      if (patch.busy === false) releaseSendLock();
      if (patch.clear && !globalThis.__cgwWrapperComposer) clearNativeComposerText();
      return;
    }
    if (patch.busy === false) {
      sendLog("info", "compose_busy_false", "Compose busy released by host", {
        status: next.status || null,
      });
      releaseSendLock();
    }
    if (patch.busy === true) {
      sendLog("debug", "compose_busy_true", "Compose busy set by host", {
        status: next.status || null,
      });
      sendInFlight = false;
      globalThis.__cgwComposeSendInFlight = false;
      if (sendLockTimer) {
        clearTimeout(sendLockTimer);
        sendLockTimer = null;
      }
      cancelFocusRetries();
    }
    globalThis.__cgwPlayComposeState = next;

    paintComposeDomFromState(next, patch);

    if (patch.clear) {
      if (inputDebounce) {
        clearTimeout(inputDebounce);
        inputDebounce = null;
      }
      clearPendingAttachments(getMounted());
      postToHost({ type: "cgwComposeInput", text: "" });
    }

    if (patch.clearAttachments) {
      clearPendingAttachments(getMounted());
      delete next.clearAttachments;
    }

    if (patch.busy === true) {
      return;
    }

    if (patch.focus || patch.clear || patch.busy === false) {
      requestComposeFocus("state");
    }
  }

  if (kernel && kernel.features) {
    kernel.features.register("play-compose", {
      onActivate: function () {
        ensurePlayComposeHooks();
        if (globalThis.__cgwWrapperComposer) ensureDomSubscription();
      },
      onDeactivate: function () {
        if (globalThis.__cgwWrapperComposer) unmountWrapperComposer();
      },
    });
  }

  globalThis.__cgwPlayComposeEnsureHooks = ensurePlayComposeHooks;
  globalThis.__cgwPlayComposeApplyState = applyComposeState;
  globalThis.__cgwPlayComposeSetUploadStatus = function (jobId, attachmentIds, status, error) {
    applyUploadStatus(jobId, attachmentIds || [], status || "error", error || "");
  };
  globalThis.__cgwPlayComposeGetText = readInputText;
  globalThis.__cgwPlayComposeGetAttachmentCount = function () {
    return pendingAttachments.length;
  };
  globalThis.__cgwPlayComposePeekDomFallbackAttachments = peekDomFallbackAttachments;
  globalThis.__cgwPlayComposeClearDomFallbackAttachments = clearDomFallbackAttachmentStash;
  globalThis.__cgwPlayComposeRequestFocus = requestComposeFocus;
  globalThis.__cgwPlayComposeScheduleMount = scheduleMount;
  globalThis.__cgwPlayComposeUnmount = unmountWrapperComposer;
  globalThis.__cgwPlayComposeVersion = COMPOSE_VERSION;
  globalThis.__cgwSetWrapperComposer = localSetWrapperComposer;

  function applyPlaySurfaceActions() {
    var settings = globalThis.__cgwPlaySurfaceActions;
    if (!settings || typeof settings !== "object") return;
    var root =
      document.getElementById("cgw-play-composer-root") ||
      document.querySelector('[data-testid="composer"]');
    if (!root) return;
    Object.keys(settings).forEach(function (actionKey) {
      var mode = String(settings[actionKey] || "").toLowerCase();
      if (mode !== "hidden" && mode !== "injectedonly") return;
      var needle = actionKey.toLowerCase();
      root.querySelectorAll("button, [role='button']").forEach(function (btn) {
        if (btn.classList && btn.classList.contains("cgw-compose-send")) return;
        var label = (
          (btn.getAttribute("aria-label") || "") +
          " " +
          (btn.textContent || "")
        )
          .trim()
          .toLowerCase();
        if (label.indexOf(needle) >= 0) {
          btn.style.display = "none";
          btn.setAttribute("data-cgw-play-action-hidden", mode);
        }
      });
    });
    root.querySelectorAll("[data-cgw-play-action-hidden]").forEach(function (btn) {
      var label = (
        (btn.getAttribute("aria-label") || "") +
        " " +
        (btn.textContent || "")
      )
        .trim()
        .toLowerCase();
      var stillHidden = Object.keys(settings).some(function (actionKey) {
        var mode = String(settings[actionKey] || "").toLowerCase();
        if (mode !== "hidden" && mode !== "injectedonly") return false;
        return label.indexOf(actionKey.toLowerCase()) >= 0;
      });
      if (!stillHidden) {
        btn.style.display = "";
        btn.removeAttribute("data-cgw-play-action-hidden");
      }
    });
  }

  function updateComposerScrollInset() {
    var root = document.getElementById("cgw-play-composer-root");
    var height = root ? root.getBoundingClientRect().height : 0;
    var inset = Math.max(0, Math.ceil(height + 12));
    document.documentElement.style.setProperty("--cgw-composer-inset", inset + "px");
    document.querySelectorAll(".cgw-transcript-scroll-host").forEach(function (host) {
      host.style.paddingBottom = inset + "px";
    });
  }

  function bindComposerInsetObserver() {
    if (globalThis.__cgwComposerInsetObserver) return;
    var root = document.getElementById("cgw-play-composer-root");
    if (!root || typeof ResizeObserver === "undefined") {
      updateComposerScrollInset();
      return;
    }
    globalThis.__cgwComposerInsetObserver = new ResizeObserver(function () {
      updateComposerScrollInset();
    });
    globalThis.__cgwComposerInsetObserver.observe(root);
    updateComposerScrollInset();
  }

  globalThis.__cgwUpdateComposerScrollInset = updateComposerScrollInset;

  globalThis.__cgwApplyPlaySurfaceActions = applyPlaySurfaceActions;

  if (globalThis.__cgwWrapperComposer) {
    localSetWrapperComposer(true);
  } else {
    ensurePlayComposeHooks();
  }
})();
