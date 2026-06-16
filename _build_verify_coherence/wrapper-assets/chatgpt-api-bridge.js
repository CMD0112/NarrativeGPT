(function () {
  "use strict";

  var BASE = "https://chatgpt.com";

  function post(msg) {
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(msg);
      }
    } catch (_e) {
      /* ignore */
    }
  }

  var BRIDGE_CHANNEL = "cgw-api";
  var PROTOCOL_VERSION = 1;

  function reply(id, payload) {
    if (globalThis.__cgwBridgeKernel) {
      globalThis.__cgwBridgeKernel.reply(BRIDGE_CHANNEL, id, payload);
      return;
    }
    post(Object.assign({ channel: BRIDGE_CHANNEL, protocolVersion: PROTOCOL_VERSION, id: id }, payload));
  }

  function parseJsonSafe(text) {
    if (!text) return null;
    try {
      return JSON.parse(text);
    } catch (_e) {
      return null;
    }
  }

  function fetchWithTimeout(url, init, timeoutMs) {
    var ms = timeoutMs || 8000;
    if (typeof AbortController !== "undefined") {
      var ac = new AbortController();
      var timer = setTimeout(function () {
        ac.abort();
      }, ms);
      var merged = Object.assign({}, init || {}, { signal: ac.signal });
      return fetch(url, merged).finally(function () {
        clearTimeout(timer);
      });
    }
    return Promise.race([
      fetch(url, init),
      new Promise(function (_, reject) {
        setTimeout(function () {
          reject(new Error("fetch_timeout"));
        }, ms);
      }),
    ]);
  }

  async function getAccessToken() {
    var res;
    try {
      res = await fetchWithTimeout(
        BASE + "/api/auth/session",
        {
          method: "GET",
          credentials: "include",
          cache: "no-store",
        },
        12000
      );
    } catch (err) {
      return {
        ok: false,
        status: 0,
        error: err && err.message === "fetch_timeout" ? "session_timeout" : "session_fetch_failed",
      };
    }
    if (!res.ok) {
      return { ok: false, status: res.status, error: "session_fetch_failed" };
    }
    var j = await res.json();
    var token = j && j.accessToken;
    if (!token || !String(token).split(".")[2]) {
      return { ok: false, status: 200, error: "no_access_token" };
    }
    cachedAccessToken = token;
    resolveAccountIdFromSession({
      ok: true,
      token: token,
      userId: j.user && j.user.id ? String(j.user.id) : null,
      email: j.user && j.user.email ? String(j.user.email) : null,
    });

    return {
      ok: true,
      token: token,
      userId: j.user && j.user.id ? String(j.user.id) : null,
      email: j.user && j.user.email ? String(j.user.email) : null,
      accountId: getChatGptAccountId(),
    };
  }

  function buildUrl(path, query) {
    var url = new URL(path.startsWith("http") ? path : BASE + path);
    if (query && typeof query === "object") {
      Object.keys(query).forEach(function (k) {
        var v = query[k];
        if (v !== undefined && v !== null) url.searchParams.set(k, String(v));
      });
    }
    return url.toString();
  }

  function getOaiDeviceId() {
    var patterns = [/oai-did=([^;]+)/i, /Oai-Device-Id=([^;]+)/i];
    for (var i = 0; i < patterns.length; i++) {
      var m = document.cookie.match(patterns[i]);
      if (m && m[1]) return decodeURIComponent(m[1]);
    }
    try {
      var stored = localStorage.getItem("oai-did") || localStorage.getItem("deviceId");
      if (stored) return String(stored);
    } catch (_e) {
      /* ignore */
    }
    return null;
  }

  var cachedAccountId = null;
  var cachedAccessToken = null;

  function extractAccountIdFromJwt(token) {
    if (!token) return null;
    try {
      var parts = String(token).split(".");
      if (parts.length < 2) return null;
      var payload = parts[1].replace(/-/g, "+").replace(/_/g, "/");
      while (payload.length % 4) payload += "=";
      var json = JSON.parse(atob(payload));
      var auth = json["https://api.openai.com/auth"];
      if (auth) {
        if (auth.chatgpt_account_id) return String(auth.chatgpt_account_id);
        if (auth.accounts && auth.accounts.default && auth.accounts.default.id) {
          return String(auth.accounts.default.id);
        }
        if (Array.isArray(auth.organizations)) {
          for (var i = 0; i < auth.organizations.length; i++) {
            var org = auth.organizations[i];
            if (org && org.is_default && org.id) return String(org.id);
          }
          if (auth.organizations[0] && auth.organizations[0].id) {
            return String(auth.organizations[0].id);
          }
        }
      }
    } catch (_e) {
      /* ignore */
    }
    return null;
  }

  function rememberAccountId(id) {
    if (!id) return;
    cachedAccountId = String(id);
    try {
      if (!globalThis.__CHATGPT_CLIENT_HEADERS__) {
        globalThis.__CHATGPT_CLIENT_HEADERS__ = {};
      }
      globalThis.__CHATGPT_CLIENT_HEADERS__["ChatGPT-Account-Id"] = cachedAccountId;
    } catch (_e) {
      /* ignore */
    }
  }

  function resolveAccountIdFromSession(session) {
    if (!session || !session.ok) return null;
    if (session.token) cachedAccessToken = session.token;
    var fromJwt = extractAccountIdFromJwt(session.token);
    if (fromJwt) {
      rememberAccountId(fromJwt);
      return fromJwt;
    }
    return getChatGptAccountId();
  }

  function getChatGptAccountId() {
    if (cachedAccountId) return cachedAccountId;

    var patterns = [
      /(?:^|; )_account=([^;]+)/i,
      /(?:^|; )_acct=([^;]+)/i,
      /(?:^|; )chatgpt-account-id=([^;]+)/i,
    ];
    for (var i = 0; i < patterns.length; i++) {
      var m = document.cookie.match(patterns[i]);
      if (m && m[1]) {
        rememberAccountId(decodeURIComponent(m[1]));
        return cachedAccountId;
      }
    }

    try {
      var headers = globalThis.__CHATGPT_CLIENT_HEADERS__;
      if (headers && headers["ChatGPT-Account-Id"]) {
        rememberAccountId(String(headers["ChatGPT-Account-Id"]));
        return cachedAccountId;
      }
    } catch (_e) {
      /* ignore */
    }

    if (cachedAccessToken) {
      var fromJwt = extractAccountIdFromJwt(cachedAccessToken);
      if (fromJwt) {
        rememberAccountId(fromJwt);
        return cachedAccountId;
      }
    }

    return null;
  }

  function sleep(ms) {
    return new Promise(function (resolve) {
      setTimeout(resolve, ms);
    });
  }

  function buildContextHeaders(extra) {
    var h = {};
    var did = getOaiDeviceId();
    if (did) h["oai-device-id"] = did;
    var acct = getChatGptAccountId();
    if (acct) h["ChatGPT-Account-Id"] = acct;
    try {
      if (navigator.language) h["oai-language"] = navigator.language;
    } catch (_e) {
      /* ignore */
    }
    if (extra && typeof extra === "object") {
      Object.keys(extra).forEach(function (k) {
        h[k] = extra[k];
      });
    }
    return h;
  }

  function mergeHeaders(token, extra) {
    var h = {
      accept: "*/*",
      authorization: "Bearer " + token,
      "oai-client-version": "prod-unknown",
    };
    var ctx = buildContextHeaders(extra);
    Object.keys(ctx).forEach(function (k) {
      h[k] = ctx[k];
    });
    try {
      var profile = globalThis.__CHATGPT_CLIENT_HEADERS__;
      if (profile && typeof profile === "object") {
        Object.keys(profile).forEach(function (k) {
          if (profile[k] !== undefined && profile[k] !== null) {
            h[k] = String(profile[k]);
          }
        });
      }
    } catch (_e) {
      /* ignore */
    }
    return h;
  }

  async function apiRequest(cmd) {
    var session = await getAccessToken();
    if (!session.ok) {
      return {
        type: "apiError",
        ok: false,
        error: session.error || "session_expired",
        status: session.status || 401,
      };
    }

    var method = (cmd.method || "GET").toUpperCase();
    var path = cmd.path || "/";
    if (
      path.indexOf("snorlax/sidebar") >= 0 &&
      !getOaiDeviceId()
    ) {
      return {
        type: "apiError",
        ok: false,
        error: "missing_oai_device_id",
        status: 0,
        message:
          "Sign in to ChatGPT in this tab and refresh the page (oai-did cookie missing).",
      };
    }

    var url = buildUrl(path, cmd.query);
    var init = {
      method: method,
      credentials: "include",
      cache: "no-store",
      headers: mergeHeaders(session.token, cmd.headers),
    };

    if (cmd.body !== undefined && cmd.body !== null && method !== "GET" && method !== "HEAD") {
      if (typeof cmd.body === "string") {
        init.body = cmd.body;
        if (!init.headers["content-type"]) {
          init.headers["content-type"] = "application/json";
        }
      } else {
        init.body = JSON.stringify(cmd.body);
        init.headers["content-type"] = "application/json";
      }
    }

    var res = await fetch(url, init);
    var contentType = (res.headers && res.headers.get("content-type")) || "";
    var isConversationPost =
      method === "POST" &&
      path.indexOf("/f/conversation") >= 0 &&
      path.indexOf("/prepare") < 0;

    if (res.ok && isConversationPost && res.body && typeof res.body.getReader === "function") {
      try {
        var streamApi = globalThis.__cgwConversationStream;
        if (!streamApi) {
          return {
            type: "apiResult",
            ok: true,
            status: res.status,
            streaming: true,
            streamComplete: false,
            streamError: "stream_parser_missing",
          };
        }

        var reader = res.body.getReader();
        var decoder = new TextDecoder();
        var state = { parts: [""], streamComplete: false };
        var raw = await streamApi.readConversationStream(reader, decoder, function (buffer) {
          streamApi.parseSseChunk(state, buffer);
        });
        streamApi.parseSseChunk(state, raw);
        var parsed = streamApi.finalizeParseResult(state);

        return {
          type: "apiResult",
          ok: true,
          status: res.status,
          streaming: true,
          streamComplete: parsed.streamComplete,
          assistantText: parsed.assistantText,
          assistantMessageId: parsed.assistantMessageId,
          conversationId: parsed.conversationId,
        };
      } catch (streamErr) {
        return {
          type: "apiError",
          ok: false,
          status: res.status || 0,
          error: "stream_read_failed",
          message: streamErr && streamErr.message ? String(streamErr.message) : "stream_read_failed",
        };
      }
    }

    var text = await res.text();
    var json = parseJsonSafe(text);

    if (!res.ok) {
      return {
        type: "apiError",
        ok: false,
        status: res.status,
        error: "http_" + res.status,
        bodyText: text && text.length < 2000 ? text : null,
        json: json,
      };
    }

    return {
      type: "apiResult",
      ok: true,
      status: res.status,
      json: json,
      bodyText: json ? null : text,
    };
  }

  function mimeToUseCase(mime) {
    if (!mime) return "my_files";
    var m = String(mime).toLowerCase();
    if (m.indexOf("image/") === 0) return "multimodal";
    if (
      m === "text/markdown" ||
      m === "text/plain" ||
      m === "application/pdf" ||
      m.indexOf("text/") === 0 ||
      m.indexOf("application/vnd.openxmlformats") === 0 ||
      m.indexOf("application/msword") === 0 ||
      m === "application/json"
    ) {
      return "my_files";
    }
    return "ace_upload";
  }

  function isSnorlaxProjectId(gizmoId) {
    return !!(gizmoId && String(gizmoId).indexOf("g-p-") === 0);
  }

  function timezoneOffsetMin() {
    return -new Date().getTimezoneOffset();
  }

  function defaultPrivateSharing() {
    return [
      {
        type: "private",
        capabilities: {
          can_read: true,
          can_view_config: false,
          can_write: false,
          can_delete: false,
          can_export: false,
          can_share: false,
        },
      },
    ];
  }

  function uploadStreamLooksReady(text) {
    if (!text) return false;
    return (
      text.indexOf("file_ready") >= 0 ||
      text.indexOf("file.processing.file_ready") >= 0 ||
      text.indexOf("processing.complete") >= 0 ||
      text.indexOf("file.processing.complete") >= 0
    );
  }

  async function processUploadStream(session, fileId, fileName, useCase) {
    var res = await fetch(BASE + "/backend-api/files/process_upload_stream", {
      method: "POST",
      credentials: "include",
      headers: mergeHeaders(session.token, { "content-type": "application/json" }),
      body: JSON.stringify({
        file_id: fileId,
        use_case: useCase || "my_files",
        index_for_retrieval: true,
        file_name: fileName || "upload.bin",
      }),
    });

    var text = await res.text();
    if (res.ok || uploadStreamLooksReady(text)) {
      return { ok: true, status: res.status, bodyText: text };
    }

    return { ok: false, status: res.status, bodyText: text };
  }

  async function waitForUploadProcessing(session, fileId, fileName, useCase, timeoutMs) {
    var deadline = Date.now() + (timeoutMs || 30000);
    var lastText = "";

    while (Date.now() < deadline) {
      var result = await processUploadStream(session, fileId, fileName, useCase);
      lastText = result.bodyText || "";
      if (result.ok || uploadStreamLooksReady(lastText)) {
        return { ok: true, bodyText: lastText };
      }
      await sleep(800);
    }

    return { ok: false, bodyText: lastText };
  }

  async function waitForFileFinalize(session, fileId, timeoutMs) {
    var deadline = Date.now() + (timeoutMs || 45000);
    var lastText = "";

    while (Date.now() < deadline) {
      var res = await fetch(
        BASE + "/backend-api/files/" + encodeURIComponent(fileId) + "/uploaded",
        {
          method: "POST",
          credentials: "include",
          headers: mergeHeaders(session.token, { "content-type": "application/json" }),
          body: JSON.stringify({}),
        }
      );

      var text = await res.text();
      lastText = text;
      var json = parseJsonSafe(text);

      if (res.ok && json && json.status === "success") {
        return { ok: true, status: res.status, json: json, bodyText: text };
      }

      if (res.ok && json && json.status === "retry") {
        await sleep(400);
        continue;
      }

      if (!res.ok) {
        return {
          ok: false,
          status: res.status,
          json: json,
          bodyText: text && text.length < 2000 ? text : null,
        };
      }

      await sleep(400);
    }

    return { ok: false, bodyText: lastText, error: "finalize_timeout" };
  }

  function buildAttachAttempts(cmd, fileId) {
    var gizmoId = cmd.gizmoId;
    var attempts = [];
    var seen = {};

    function add(path, body) {
      if (!path) return;
      var key = path + "|" + JSON.stringify(body || {});
      if (seen[key]) return;
      seen[key] = true;
      attempts.push({ path: path, body: body });
    }

    if (cmd.attachBodies && cmd.attachBodies.length) {
      var attachPath =
        cmd.attachPath ||
        "/backend-api/projects/" + encodeURIComponent(gizmoId) + "/files";
      for (var b = 0; b < cmd.attachBodies.length; b++) {
        add(attachPath, cmd.attachBodies[b]);
      }
    }

    if (cmd.attachPath && cmd.attachBody) {
      add(cmd.attachPath, cmd.attachBody);
    } else if (cmd.attachPath) {
      add(cmd.attachPath, { file_id: fileId });
    }

    var projectsPath =
      "/backend-api/projects/" + encodeURIComponent(gizmoId) + "/files";
    add(projectsPath, {
      files: [buildUpsertFileRef(fileId, cmd.fileName || fileId)],
    });
    add(projectsPath, {
      files: [{ file_id: fileId, name: cmd.fileName || fileId }],
    });
    add(projectsPath, { file_id: fileId });
    add(projectsPath, { file_ids: [fileId] });
    add(projectsPath, { file_id: fileId, project_id: gizmoId });

    var gizmosPath =
      "/backend-api/gizmos/" + encodeURIComponent(gizmoId) + "/files";
    add(gizmosPath, { file_id: fileId, gizmo_id: gizmoId });

    return attempts;
  }

  async function tryAttachAttempts(session, attempts) {
    var log = [];
    var last = null;

    for (var a = 0; a < attempts.length; a++) {
      var attempt = attempts[a];
      var attachRes = await fetch(BASE + attempt.path, {
        method: "POST",
        credentials: "include",
        headers: mergeHeaders(session.token, { "content-type": "application/json" }),
        body: JSON.stringify(attempt.body),
      });

      var attachText = await attachRes.text();
      var attachJson = parseJsonSafe(attachText);
      last = {
        path: attempt.path,
        body: attempt.body,
        status: attachRes.status,
        text: attachText,
        json: attachJson,
      };
      log.push({
        path: attempt.path,
        body: attempt.body,
        status: attachRes.status,
        ok: attachRes.ok,
      });

      if (attachRes.ok) {
        return {
          ok: true,
          log: log,
          winner: last,
        };
      }
    }

    return { ok: false, log: log, last: last };
  }

  function normalizeUpsertFileLocation(location) {
    if (!location) return "fs";
    var value = String(location).toLowerCase();
    if (value === "fs" || value === "sediment") return value;
    if (value.indexOf("file-service://") === 0 || value.indexOf("fs://") === 0) {
      return "fs";
    }
    if (value.indexOf("sediment://") === 0) return "sediment";
    return "fs";
  }

  function buildUpsertFileRef(fileId, fileName, existingRef) {
    if (existingRef && typeof existingRef === "object") {
      var existingId = existingRef.file_id || existingRef.fileId || fileId;
      return {
        file_id: existingId,
        name: existingRef.name || fileName || existingId,
        location: normalizeUpsertFileLocation(existingRef.location),
      };
    }

    return {
      file_id: fileId,
      name: fileName || fileId,
      location: "fs",
    };
  }

  async function attachViaUpsert(session, cmd, fileId, priorLog) {
    if (!cmd.gizmoId) {
      return {
        type: "apiError",
        ok: false,
        error: "missing_gizmo_id",
        message: "Project attach requires gizmoId",
        fileId: fileId,
      };
    }

    var fileRefs = [buildUpsertFileRef(fileId, cmd.fileName, null)];
    if (cmd.existingFiles && cmd.existingFiles.length) {
      for (var i = 0; i < cmd.existingFiles.length; i++) {
        var existing = cmd.existingFiles[i];
        var existingId = existing && (existing.file_id || existing.fileId);
        if (existingId && existingId !== fileId) {
          fileRefs.push(buildUpsertFileRef(existingId, existing.name, existing));
        }
      }
    } else if (cmd.existingFileIds && cmd.existingFileIds.length) {
      for (var j = 0; j < cmd.existingFileIds.length; j++) {
        var legacyId = cmd.existingFileIds[j];
        if (legacyId && legacyId !== fileId) {
          fileRefs.push(buildUpsertFileRef(legacyId, legacyId, null));
        }
      }
    }

    var upsertBody = cmd.upsertBody;
    if (!upsertBody) {
      if (isSnorlaxProjectId(cmd.gizmoId)) {
        return {
          type: "apiError",
          ok: false,
          error: "snorlax_attach_requires_detail_body",
          message:
            "Snorlax attach must use a detail-round-trip upsert body from the host app",
          fileId: fileId,
        };
      }

      upsertBody = {
        id: cmd.gizmoId,
        instructions: cmd.projectInstructions || "",
        display: {
          name: cmd.projectTitle || "Project",
          description: "",
          prompt_starters: [],
        },
        tools: [],
        files: fileRefs,
        training_disabled: false,
        sharing: defaultPrivateSharing(),
      };
    }

    var upsert = await apiRequest({
      method: "POST",
      path: "/backend-api/gizmos/snorlax/upsert",
      body: upsertBody,
    });

    if (upsert.ok) {
      var responseGizmoId =
        (upsert.json &&
          ((upsert.json.resource && upsert.json.resource.gizmo && upsert.json.resource.gizmo.id) ||
            (upsert.json.gizmo && upsert.json.gizmo.id) ||
            upsert.json.id)) ||
        null;
      return {
        type: "apiResult",
        ok: true,
        status: upsert.status,
        json: upsert.json,
        fileId: fileId,
        attachVia: "snorlax_upsert",
        attachAttempts: priorLog || [],
        responseGizmoId: responseGizmoId,
      };
    }

    return {
      type: "apiError",
      ok: false,
      status: upsert.status || 0,
      error: "attach_failed",
      message: upsert.error || "upsert_failed",
      json: upsert.json,
      fileId: fileId,
      attachVia: "snorlax_upsert",
      attachAttempts: priorLog || [],
      bodyText: upsert.bodyText,
    };
  }

  async function attachFileToProject(session, cmd, fileId) {
    if (!cmd.gizmoId || !fileId) {
      return { type: "apiResult", ok: true, fileId: fileId };
    }

    resolveAccountIdFromSession(session);

    if (cmd.attachViaUpsertOnly === true || isSnorlaxProjectId(cmd.gizmoId)) {
      return await attachViaUpsert(session, cmd, fileId, []);
    }

    var attempts = buildAttachAttempts(cmd, fileId);
    var maxRounds = cmd.attachRetries != null ? cmd.attachRetries + 1 : 3;
    var result = null;

    for (var round = 0; round < maxRounds; round++) {
      if (round > 0) await sleep(1200);
      result = await tryAttachAttempts(session, attempts);
      if (result.ok) break;
      var retryable =
        result.last &&
        (result.last.status === 404 || result.last.status === 409 || result.last.status === 425);
      if (!retryable) break;
    }

    if (result && result.ok && result.winner) {
      return {
        type: "apiResult",
        ok: true,
        status: result.winner.status,
        json: result.winner.json,
        fileId: fileId,
        attachPath: result.winner.path,
        attachAttempts: result.log,
      };
    }

    if (cmd.allowUpsertAttachFallback === true) {
      return await attachViaUpsert(session, cmd, fileId, result ? result.log : []);
    }

    var last = result && result.last;
    return {
      type: "apiError",
      ok: false,
      status: last ? last.status : 0,
      error: "attach_failed",
      json: last ? last.json : null,
      fileId: fileId,
      attachPath: last ? last.path : null,
      attachAttempts: result ? result.log : [],
      bodyText: last && last.text && last.text.length < 2000 ? last.text : null,
    };
  }

  function extractUploadedFileId(json) {
    if (!json || typeof json !== "object") return null;
    return (
      json.file_id ||
      json.fileId ||
      json.id ||
      (json.file && (json.file.file_id || json.file.id)) ||
      (json.data && (json.data.file_id || json.data.id)) ||
      null
    );
  }

  async function tryUploadProjectLibrary(session, cmd, bytes, mimeType, fileName) {
    if (!cmd.gizmoId || !isSnorlaxProjectId(cmd.gizmoId)) {
      return { ok: false, error: "not_snorlax_project" };
    }

    var blob = new Blob([bytes], { type: mimeType });
    var form = new FormData();
    form.append("file", blob, fileName);
    form.append("gizmo_id", cmd.gizmoId);
    form.append("project_id", cmd.gizmoId);

    var res = await fetch(BASE + "/backend-api/files/library", {
      method: "POST",
      credentials: "include",
      headers: mergeHeaders(session.token, {}),
      body: form,
    });

    var text = await res.text();
    var json = parseJsonSafe(text);
    if (!res.ok) {
      return {
        ok: false,
        status: res.status,
        error: "library_upload_failed",
        json: json,
        bodyText: text && text.length < 2000 ? text : null,
      };
    }

    var fileId = extractUploadedFileId(json);
    if (!fileId) {
      return {
        ok: false,
        status: res.status,
        error: "library_upload_failed",
        message: "missing_file_id",
        json: json,
        bodyText: text && text.length < 2000 ? text : null,
      };
    }

    return {
      ok: true,
      status: res.status,
      fileId: fileId,
      json: json,
      bodyText: text && text.length < 2000 ? text : null,
      libraryUpload: true,
    };
  }

  async function uploadFile(cmd) {
    var session = await getAccessToken();
    if (!session.ok) {
      return {
        type: "apiError",
        ok: false,
        error: session.error || "session_expired",
        status: session.status || 401,
      };
    }

    try {
      var binary = atob(cmd.base64 || "");
      var bytes = new Uint8Array(binary.length);
      for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

      var mimeType = cmd.mimeType || "application/octet-stream";
      var blob = new Blob([bytes], { type: mimeType });
      var fileName = cmd.fileName || "upload.bin";
      var fileSize = bytes.length;
      var useCase = cmd.useCase || mimeToUseCase(mimeType);

      if (cmd.useProjectLibrary !== false && cmd.gizmoId && isSnorlaxProjectId(cmd.gizmoId)) {
        var libraryResult = await tryUploadProjectLibrary(
          session,
          cmd,
          bytes,
          mimeType,
          fileName
        );
        if (libraryResult.ok) {
          return {
            type: "apiResult",
            ok: true,
            status: libraryResult.status || 200,
            json: libraryResult.json,
            fileId: libraryResult.fileId,
            libraryUpload: true,
            location: "sediment",
          };
        }
      }

      var registerRes = await fetch(BASE + "/backend-api/files", {
        method: "POST",
        credentials: "include",
        headers: mergeHeaders(session.token, { "content-type": "application/json" }),
        body: JSON.stringify({
          file_name: fileName,
          file_size: fileSize,
          use_case: useCase,
          timezone_offset_min: timezoneOffsetMin(),
        }),
      });

      var registerText = await registerRes.text();
      var registerJson = parseJsonSafe(registerText);
      if (!registerRes.ok) {
        return {
          type: "apiError",
          ok: false,
          status: registerRes.status,
          error: "upload_failed",
          json: registerJson,
          bodyText: registerText && registerText.length < 2000 ? registerText : null,
        };
      }

      var fileId =
        (registerJson && (registerJson.file_id || registerJson.id)) || null;
      var uploadUrl = registerJson && registerJson.upload_url;
      if (!fileId || !uploadUrl) {
        return {
          type: "apiError",
          ok: false,
          status: registerRes.status,
          error: "upload_failed",
          message: "missing_file_id_or_upload_url",
          json: registerJson,
          bodyText: registerText && registerText.length < 2000 ? registerText : null,
        };
      }

      var putRes = await fetch(uploadUrl, {
        method: "PUT",
        body: blob,
        headers: {
          "Content-Type": mimeType,
          "x-ms-blob-type": "BlockBlob",
          "x-ms-version": "2020-04-08",
        },
      });

      if (!putRes.ok) {
        var putText = await putRes.text();
        return {
          type: "apiError",
          ok: false,
          status: putRes.status,
          error: "upload_failed",
          message: "blob_put_failed",
          fileId: fileId,
          bodyText: putText && putText.length < 2000 ? putText : null,
        };
      }

      await waitForUploadProcessing(session, fileId, fileName, useCase, 30000);

      var finalized = await waitForFileFinalize(session, fileId, 45000);
      if (!finalized.ok) {
        return {
          type: "apiError",
          ok: false,
          status: finalized.status || 0,
          error: "upload_failed",
          message: finalized.error || "finalize_failed",
          fileId: fileId,
          bodyText:
            finalized.bodyText && finalized.bodyText.length < 2000
              ? finalized.bodyText
              : null,
        };
      }

      if (!cmd.gizmoId || cmd.skipProjectAttach === true) {
        return {
          type: "apiResult",
          ok: true,
          status: finalized.status || 200,
          json: finalized.json || registerJson,
          fileId: fileId,
        };
      }

      return await attachFileToProject(session, cmd, fileId);
    } catch (e) {
      return {
        type: "apiError",
        ok: false,
        error: "upload_exception",
        message: e && e.message ? String(e.message) : "unknown",
      };
    }
  }

  async function deleteProjectFile(cmd) {
    var session = await getAccessToken();
    if (!session.ok) {
      return {
        type: "apiError",
        ok: false,
        error: session.error || "session_expired",
        status: session.status || 401,
      };
    }

    var gizmoId = cmd.gizmoId;
    var fileId = cmd.fileId;
    if (!gizmoId || !fileId) {
      return { type: "apiError", ok: false, error: "missing_gizmo_or_file_id" };
    }

    var attempts = [
      {
        method: "DELETE",
        path:
          "/backend-api/projects/" +
          encodeURIComponent(gizmoId) +
          "/files/" +
          encodeURIComponent(fileId),
      },
      {
        method: "DELETE",
        path:
          "/backend-api/gizmos/" +
          encodeURIComponent(gizmoId) +
          "/files/" +
          encodeURIComponent(fileId),
      },
      {
        method: "DELETE",
        path: "/backend-api/files/" + encodeURIComponent(fileId),
      },
      {
        method: "DELETE",
        path:
          "/backend-api/projects/" + encodeURIComponent(gizmoId) + "/files",
        body: { file_id: fileId },
      },
      {
        method: "DELETE",
        path:
          "/backend-api/gizmos/" + encodeURIComponent(gizmoId) + "/files",
        body: { file_id: fileId },
      },
      {
        method: "DELETE",
        path:
          "/backend-api/projects/" + encodeURIComponent(gizmoId) + "/files",
        body: { files: [{ file_id: fileId }] },
      },
    ];

    var log = [];
    var last = null;
    for (var i = 0; i < attempts.length; i++) {
      var attempt = attempts[i];
      try {
        var res = await fetch(BASE + attempt.path, {
          method: attempt.method,
          credentials: "include",
          cache: "no-store",
          headers: mergeHeaders(session.token, { "content-type": "application/json" }),
          body: attempt.body ? JSON.stringify(attempt.body) : undefined,
        });
        last = {
          path: attempt.path,
          status: res.status,
          ok: res.ok,
        };
        log.push(last);
        if (res.ok || res.status === 204) {
          return {
            type: "apiResult",
            ok: true,
            status: res.status,
            json: { path: attempt.path, log: log },
          };
        }
      } catch (err) {
        last = { path: attempt.path, error: String(err) };
        log.push(last);
      }
    }

    return {
      type: "apiError",
      ok: false,
      error: "delete_exhausted",
      status: last && last.status ? last.status : 0,
      json: { log: log },
    };
  }

  function listComposerFileUi() {
    var fileInputs = [];
    var nodes = document.querySelectorAll('input[type="file"]');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      fileInputs.push({
        accept: el.getAttribute("accept") || "",
        multiple: !!el.multiple,
        hidden: !el.offsetParent && el.type === "file",
        id: el.id || "",
        name: el.name || "",
        testId: el.getAttribute("data-testid") || "",
      });
    }

    var attachSelectors = [
      'button[data-testid*="attach"]',
      'button[aria-label*="Attach"]',
      'button[aria-label*="Upload"]',
      '[data-testid="composer-action-attach"]',
      'button[aria-label*="Add photos"]',
    ];
    var attachButtons = [];
    for (var s = 0; s < attachSelectors.length; s++) {
      var selector = attachSelectors[s];
      var matches = document.querySelectorAll(selector);
      for (var m = 0; m < matches.length; m++) {
        var btn = matches[m];
        attachButtons.push({
          selector: selector,
          testId: btn.getAttribute("data-testid") || "",
          ariaLabel: btn.getAttribute("aria-label") || "",
          text: (btn.textContent || "").trim().slice(0, 80),
        });
      }
    }

    return {
      href: location.href,
      fileInputs: fileInputs,
      attachButtons: attachButtons,
    };
  }

  async function fetchBlobUrl(cmd) {
    var url = cmd.url || "";
    if (!url || url.indexOf("blob:") !== 0) {
      return { type: "apiError", ok: false, error: "invalid_blob_url" };
    }

    try {
      var res = await fetch(url);
      if (!res.ok) {
        return {
          type: "apiError",
          ok: false,
          error: "blob_fetch_failed",
          status: res.status,
        };
      }
      var buf = await res.arrayBuffer();
      var bytes = new Uint8Array(buf);
      var binary = "";
      var chunk = 0x8000;
      for (var j = 0; j < bytes.length; j += chunk) {
        binary += String.fromCharCode.apply(null, bytes.subarray(j, j + chunk));
      }
      return {
        type: "apiResult",
        ok: true,
        status: res.status,
        mimeType: res.headers.get("content-type") || "application/octet-stream",
        base64: btoa(binary),
        byteLength: bytes.length,
      };
    } catch (err) {
      return {
        type: "apiError",
        ok: false,
        error: err && err.message ? err.message : "blob_fetch_error",
      };
    }
  }

  async function downloadFile(cmd) {
    var session = await getAccessToken();
    if (!session.ok) {
      return {
        type: "apiError",
        ok: false,
        error: session.error || "session_expired",
        status: session.status || 401,
      };
    }

    var fileId = cmd.fileId;
    if (!fileId) {
      return { type: "apiError", ok: false, error: "missing_file_id" };
    }

    var paths =
      cmd.paths && cmd.paths.length
        ? cmd.paths
        : (function () {
            var gizmoId = cmd.gizmoId;
            var location = cmd.location || "";
            var preferProject =
              !!gizmoId || String(location).toLowerCase() === "fs";
            var built = [];
            if (preferProject && gizmoId) {
              built.push(
                "/backend-api/projects/" +
                  encodeURIComponent(gizmoId) +
                  "/files/" +
                  encodeURIComponent(fileId) +
                  "?download=1",
                "/backend-api/projects/" +
                  encodeURIComponent(gizmoId) +
                  "/files/" +
                  encodeURIComponent(fileId),
                "/backend-api/gizmos/" +
                  encodeURIComponent(gizmoId) +
                  "/files/" +
                  encodeURIComponent(fileId) +
                  "?download=1",
                "/backend-api/gizmos/" +
                  encodeURIComponent(gizmoId) +
                  "/files/" +
                  encodeURIComponent(fileId)
              );
            }
            built.push(
              "/backend-api/files/" +
                encodeURIComponent(fileId) +
                "?download=1",
              "/backend-api/files/" + encodeURIComponent(fileId)
            );
            return built;
          })();

    var attempts = [];
    var failFast =
      !!cmd.failFast && String(cmd.location || "").toLowerCase() === "fs";
    var projectPathEnd = 0;
    for (var p = 0; p < paths.length; p++) {
      if (paths[p].indexOf("/backend-api/files/") !== 0) {
        projectPathEnd = p + 1;
      }
    }

    var lastErr = null;
    for (var i = 0; i < paths.length; i++) {
      try {
        var res = await fetch(BASE + paths[i], {
          method: "GET",
          credentials: "include",
          cache: "no-store",
          headers: { authorization: "Bearer " + session.token },
        });
        if (!res.ok) {
          attempts.push({ status: res.status, path: paths[i] });
          lastErr = { status: res.status, path: paths[i], attempts: attempts };
          if (
            failFast &&
            projectPathEnd > 0 &&
            i + 1 >= projectPathEnd &&
            attempts.length >= projectPathEnd
          ) {
            var allProject404 = true;
            for (var a = 0; a < projectPathEnd; a++) {
              if (attempts[a].status !== 404) {
                allProject404 = false;
                break;
              }
            }
            if (allProject404) {
              break;
            }
          }
          continue;
        }
        var buf = await res.arrayBuffer();
        var bytes = new Uint8Array(buf);
        var binary = "";
        var chunk = 0x8000;
        for (var j = 0; j < bytes.length; j += chunk) {
          binary += String.fromCharCode.apply(
            null,
            bytes.subarray(j, j + chunk)
          );
        }
        var base64 = btoa(binary);
        var text = null;
        try {
          text = new TextDecoder("utf-8").decode(bytes);
        } catch (_e) {
          /* ignore */
        }
        return {
          type: "apiResult",
          ok: true,
          status: res.status,
          base64: base64,
          text: text,
          byteLength: bytes.length,
        };
      } catch (e) {
        lastErr = {
          message: e && e.message ? String(e.message) : "unknown",
          attempts: attempts,
        };
      }
    }

    return {
      type: "apiError",
      ok: false,
      error: "download_failed",
      status: lastErr && lastErr.status ? lastErr.status : 0,
      message:
        lastErr && lastErr.path
          ? "download_failed " + lastErr.status + " " + lastErr.path
          : lastErr && lastErr.message
            ? String(lastErr.message)
            : "download_failed",
      detail: {
        status: lastErr && lastErr.status ? lastErr.status : 0,
        path: lastErr && lastErr.path ? lastErr.path : null,
        attempts: attempts,
      },
    };
  }

  function normalizeSidebarProject(item) {
    if (!item) return null;
    var wrap = item.gizmo || null;
    var raw =
      (wrap && wrap.gizmo) || wrap || item;
    var display =
      (raw && raw.display) ||
      (wrap && wrap.display) ||
      item.display ||
      null;
    var id =
      (raw && raw.id) ||
      (wrap && wrap.id) ||
      item.id ||
      null;
    if (!id) return null;
    var title =
      (display && display.name) ||
      (raw && raw.name) ||
      "Project";
    var instructions =
      (raw && raw.instructions) ||
      (wrap && wrap.instructions) ||
      null;
    return { id: String(id), title: String(title), instructions: instructions };
  }

  async function listProjects() {
    var session = await getAccessToken();
    if (!session.ok) {
      return {
        type: "apiError",
        ok: false,
        error: session.error || "session_expired",
        status: session.status || 401,
      };
    }

    var deviceId = getOaiDeviceId();
    if (!deviceId) {
      return {
        type: "apiError",
        ok: false,
        error: "missing_oai_device_id",
        message:
          "Sign in to ChatGPT in this tab and refresh the page (oai-did cookie missing).",
      };
    }

    var byId = {};
    var lastStatus = 200;
    var lastItemCount = 0;
    var ownedPasses = [true, false];

    for (var p = 0; p < ownedPasses.length; p++) {
      var ownedOnly = ownedPasses[p];
      var cursor = null;

      for (var page = 0; page < 50; page++) {
        var query = {
          owned_only: ownedOnly ? "true" : "false",
          conversations_per_gizmo: "0",
        };
        if (cursor) query.cursor = cursor;

        var res = await apiRequest({
          method: "GET",
          path: "/backend-api/gizmos/snorlax/sidebar",
          query: query,
        });

        if (!res.ok) return res;
        lastStatus = res.status || 200;

        var data = res.json || {};
        var items = data.items || [];
        lastItemCount += items.length;

        for (var i = 0; i < items.length; i++) {
          var norm = normalizeSidebarProject(items[i]);
          if (norm) byId[norm.id] = norm;
        }

        cursor = data.cursor;
        if (!cursor || cursor === "0" || cursor === 0) break;
      }

      if (Object.keys(byId).length > 0) break;
    }

    if (Object.keys(byId).length === 0) {
      var boot = await apiRequest({
        method: "GET",
        path: "/backend-api/gizmos/bootstrap",
      });
      if (boot.ok && boot.json) {
        var lists = [boot.json.gizmos, boot.json.items, boot.json.resources];
        for (var li = 0; li < lists.length; li++) {
          var arr = lists[li];
          if (!arr || !arr.length) continue;
          for (var bi = 0; bi < arr.length; bi++) {
            var norm = normalizeSidebarProject({ gizmo: arr[bi] });
            if (norm) byId[norm.id] = norm;
          }
        }
      }
    }

    var projects = Object.keys(byId).map(function (k) {
      return byId[k];
    });

    return {
      type: "apiResult",
      ok: true,
      status: lastStatus,
      json: {
        projects: projects,
        itemCount: lastItemCount,
        hasDeviceId: !!deviceId,
        hasAccountId: !!getChatGptAccountId(),
      },
    };
  }

  async function probeApi(cmd) {
    var path = (cmd && cmd.path) || "/backend-api/gizmos/snorlax/sidebar";
    var session = await getAccessToken();
    var res = await apiRequest({
      method: "GET",
      path: path,
      query:
        path.indexOf("snorlax/sidebar") >= 0
          ? { owned_only: "true", conversations_per_gizmo: "5" }
          : undefined,
    });
    var keys = [];
    var itemCount = null;
    if (res.json && typeof res.json === "object") {
      keys = Object.keys(res.json);
      if (res.json.items && res.json.items.length !== undefined) {
        itemCount = res.json.items.length;
      }
    }
    return {
      type: "apiResult",
      ok: res.ok,
      status: res.status,
      json: {
        itemCount: itemCount,
        jsonKeys: keys,
        hasDeviceId: !!getOaiDeviceId(),
        hasAccountId: !!getChatGptAccountId(),
        authenticated: session.ok,
      },
      error: res.ok ? null : res.error,
    };
  }

  function extractGizmoIdFromHref(href) {
    if (!href) return null;
    var m =
      href.match(/\/g\/g-p-([^/?#]+)/i) ||
      href.match(/\/g\/p-([^/?#]+)/i) ||
      href.match(/[?&]gizmo=([^&]+)/i);
    return m ? decodeURIComponent(m[1]) : null;
  }

  async function discoverProjectsDom() {
    var byId = {};
    var links = document.querySelectorAll("a[href]");
    for (var i = 0; i < links.length; i++) {
      var a = links[i];
      var href = a.getAttribute("href") || "";
      var id = extractGizmoIdFromHref(href);
      if (!id) continue;
      var title =
        (a.textContent && a.textContent.trim()) ||
        a.getAttribute("aria-label") ||
        "Project";
      if (!byId[id]) byId[id] = { id: id, title: title };
    }

    if (Object.keys(byId).length === 0 && location.pathname.indexOf("/g/") < 0) {
      try {
        var nav = document.querySelector('nav a[href*="/g/"]');
        if (nav) nav.click();
      } catch (_e) {
        /* ignore */
      }
    }

    var projects = Object.keys(byId).map(function (k) {
      return byId[k];
    });
    return {
      type: "apiResult",
      ok: true,
      json: { projects: projects, href: location.href },
    };
  }

  async function getApiContext() {
    var hasDevice = !!getOaiDeviceId();
    var session = await getAccessToken();
    resolveAccountIdFromSession(session);
    var hasAccount = !!getChatGptAccountId();
    return {
      type: "apiResult",
      ok: session.ok || hasDevice,
      json: {
        authenticated: session.ok,
        hasDeviceId: hasDevice,
        hasAccountId: hasAccount,
        userId: session.userId || null,
        email: session.email || null,
        href: location.href,
        bridgeReady: true,
      },
      error: session.ok ? null : session.error,
      status: session.status,
    };
  }

  async function handleCommand(cmd) {
    if (!cmd || !cmd.action) {
      return { type: "apiError", ok: false, error: "missing_action" };
    }

    switch (cmd.action) {
      case "getSession": {
        var s = await getAccessToken();
        if (!s.ok) {
          return {
            type: "apiError",
            ok: false,
            error: s.error,
            status: s.status,
          };
        }
        return {
          type: "apiResult",
          ok: true,
          status: 200,
          accountId: s.accountId || getChatGptAccountId(),
          json: {
            authenticated: true,
            userId: s.userId,
            email: s.email,
            accountId: s.accountId || getChatGptAccountId(),
          },
        };
      }
      case "apiRequest":
        return await apiRequest(cmd);
      case "listProjects":
        return await listProjects();
      case "getApiContext":
        return await getApiContext();
      case "probeApi":
        return await probeApi(cmd);
      case "discoverProjectsDom":
        return await discoverProjectsDom();
      case "uploadFile":
        return await uploadFile(cmd);
      case "attachProjectFile": {
        var attachSession = await getAccessToken();
        if (!attachSession.ok) {
          return {
            type: "apiError",
            ok: false,
            error: attachSession.error || "session_expired",
            status: attachSession.status || 401,
          };
        }
        resolveAccountIdFromSession(attachSession);
        return await attachFileToProject(attachSession, cmd, cmd.fileId);
      }
      case "deleteProjectFile":
        return await deleteProjectFile(cmd);
      case "downloadFile":
        return await downloadFile(cmd);
      case "listComposerFileUi":
        return {
          type: "apiResult",
          ok: true,
          json: listComposerFileUi(),
        };
      case "fetchBlobUrl":
        return await fetchBlobUrl(cmd);
      case "ping":
        return { type: "pong", ok: true };
      case "echo":
        return {
          type: "apiResult",
          ok: true,
          json: { probe: cmd.probe || null, action: cmd.action },
        };
      default:
        return { type: "apiError", ok: false, error: "unknown_action" };
    }
  }

  function ensureMessageListener() {
    if (globalThis.__cgwApiBridgeOwnListener) return;

    function attachListener() {
      if (!window.chrome || !window.chrome.webview) return false;
      if (globalThis.__cgwApiOnHostMessage) {
        try {
          window.chrome.webview.removeEventListener("message", globalThis.__cgwApiOnHostMessage);
        } catch (_e) {
          /* ignore */
        }
      }
      globalThis.__cgwApiOnHostMessage = onHostMessage;
      globalThis.__cgwApiBridgeOwnListener = true;
      globalThis.__cgwApiMessageListenerAttached = true;
      window.chrome.webview.addEventListener("message", onHostMessage);
      return true;
    }

    function onHostMessage(ev) {
      var data = ev.data;
      if (typeof data === "string") {
        try {
          data = JSON.parse(data);
        } catch (_e) {
          return;
        }
      }
      if (!data || data.channel !== BRIDGE_CHANNEL) return;

      var id = data.id || "";
      var cmd = Object.assign({}, data);
      delete cmd.channel;
      delete cmd.id;

      function deliver(result) {
        reply(id, result);
      }

      function deliverError(err) {
        deliver({
          type: "apiError",
          ok: false,
          error: "handler_exception",
          message: err && err.message ? String(err.message) : "unknown",
        });
      }

      function commandBudgetMs(action) {
        switch (action) {
          case "uploadFile":
          case "attachProjectFile":
            return 180000;
          case "apiRequest":
            return 120000;
          default:
            return 30000;
        }
      }

      function commandWithTimeout() {
        var budgetMs = commandBudgetMs(action);
        var work = Promise.resolve(handleCommand(cmd));
        var guard = new Promise(function (_, reject) {
          setTimeout(function () {
            reject(new Error("command_timeout"));
          }, budgetMs);
        });
        return Promise.race([work, guard]);
      }

      var action = cmd.action || "";
      var run = action === "ping" || action === "echo"
        ? function () { return Promise.resolve(handleCommand(cmd)); }
        : commandWithTimeout;

      // Sync probes reply immediately; async API work is deferred off the host message stack.
      if (action === "ping" || action === "echo") {
        run().then(deliver).catch(deliverError);
      } else {
        setTimeout(function () {
          run().then(deliver).catch(deliverError);
        }, 0);
      }
    }

    if (!attachListener()) {
      var tries = 0;
      var timer = setInterval(function () {
        if (attachListener() || ++tries >= 100) clearInterval(timer);
      }, 50);
    }
  }

  globalThis.__cgwApiResults = globalThis.__cgwApiResults || {};

  globalThis.__cgwApiStartCommand = function (cmd, resultId) {
    cmd = cmd || {};
    if (cmd.action === "ping") {
      globalThis.__cgwApiResults[resultId] = { type: "pong", ok: true };
      return;
    }
    if (cmd.action === "echo") {
      globalThis.__cgwApiResults[resultId] = {
        type: "apiResult",
        ok: true,
        json: { probe: cmd.probe || null, action: cmd.action },
      };
      return;
    }

    setTimeout(function () {
      Promise.resolve(handleCommand(cmd))
        .then(function (r) {
          globalThis.__cgwApiResults[resultId] = r;
        })
        .catch(function (err) {
          globalThis.__cgwApiResults[resultId] = {
            type: "apiError",
            ok: false,
            error: "handler_exception",
            message: err && err.message ? String(err.message) : "unknown",
          };
        });
    }, 0);
  };

  globalThis.__cgwApiInvoke = function (cmd) {
    cmd = cmd || {};
    if (cmd.action === "ping") {
      return { type: "pong", ok: true };
    }
    if (cmd.action === "echo") {
      return {
        type: "apiResult",
        ok: true,
        json: { probe: cmd.probe || null, action: cmd.action },
      };
    }
    return handleCommand(cmd);
  };

  globalThis.__cgwApiForceAttachListener = function () {
    ensureMessageListener();
  };

  ensureMessageListener();
  if (globalThis.__cgwBridgeKernel) {
    globalThis.__cgwBridgeKernel.registerChannel(BRIDGE_CHANNEL, function (cmd) {
      return handleCommand(cmd);
    });
  }
  post({ channel: BRIDGE_CHANNEL, type: "apiBridgeReady", ok: true, protocolVersion: PROTOCOL_VERSION });
})();
