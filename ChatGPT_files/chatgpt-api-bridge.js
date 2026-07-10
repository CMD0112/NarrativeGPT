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

  var SENTINEL_HEADER_PREFIX = "openai-sentinel";
  var SENTINEL_TTL_MS = 120000;

  function recordSentinelDiagnostic(patch) {
    try {
      var prev = globalThis.__CGW_LAST_SENTINEL_DIAGNOSTIC__ || {};
      globalThis.__CGW_LAST_SENTINEL_DIAGNOSTIC__ = Object.assign(
        { at: Date.now() },
        prev,
        patch || {}
      );
    } catch (_e) {
      /* ignore */
    }
  }

  function readSentinelDiagnostic() {
    try {
      return globalThis.__CGW_LAST_SENTINEL_DIAGNOSTIC__ || null;
    } catch (_e) {
      return null;
    }
  }

  function installSentinelFetchTap() {
    if (globalThis.__cgwSentinelTapInstalled) return;
    globalThis.__cgwSentinelTapInstalled = true;
    var original = globalThis.fetch;
    if (typeof original !== "function") return;

    globalThis.fetch = function cgwSentinelTapFetch(input, init) {
      try {
        var url = typeof input === "string" ? input : input && input.url ? input.url : "";
        var method = (init && init.method) || (input && input.method) || "GET";
        var upperMethod = method.toUpperCase();
        if (upperMethod === "POST" && url.indexOf("/backend-api/sentinel/chat-requirements/") >= 0) {
          recordSentinelDiagnostic({
            stage: "wire:chat-requirements",
            wireUrl: url,
          });
        }
        if (
          upperMethod === "POST" &&
          url.indexOf("/backend-api/f/conversation") >= 0 &&
          url.indexOf("/prepare") < 0
        ) {
          var hdrs = init && init.headers ? init.headers : null;
          var captured = extractSentinelHeaders(hdrs);
          if (captured && Object.keys(captured).length > 0) {
            globalThis.__CGW_SENTINEL_CAPTURE__ = {
              capturedAt: Date.now(),
              headers: captured,
              source: "page-fetch-tap",
            };
            recordSentinelDiagnostic({
              stage: "tap:captured",
              source: "page-fetch-tap",
            });
          }
        }
      } catch (_e) {
        /* ignore */
      }
      return original.apply(this, arguments);
    };
    globalThis.__CGW_NATIVE_FETCH__ = original;
  }

  function headerBagToObject(headers) {
    var out = {};
    if (!headers) return out;
    if (typeof Headers !== "undefined" && headers instanceof Headers) {
      headers.forEach(function (v, k) {
        out[k] = v;
      });
      return out;
    }
    if (Array.isArray(headers)) {
      headers.forEach(function (pair) {
        if (pair && pair.length >= 2) out[pair[0]] = pair[1];
      });
      return out;
    }
    if (typeof headers === "object") {
      Object.keys(headers).forEach(function (k) {
        out[k] = headers[k];
      });
    }
    return out;
  }

  function extractSentinelHeaders(headers) {
    var bag = headerBagToObject(headers);
    var out = {};
    Object.keys(bag).forEach(function (k) {
      var lower = k.toLowerCase();
      if (
        lower.indexOf("sentinel") >= 0 ||
        lower === "oai-echo-logs" ||
        lower === "oai-telemetry"
      ) {
        out[k] = bag[k];
      }
    });
    return out;
  }

  function hasSentinelHeader(headers) {
    if (!headers) return false;
    return Object.keys(headers).some(function (k) {
      return k.toLowerCase().indexOf("sentinel") >= 0;
    });
  }

  function readSentinelCapture() {
    try {
      var cap = globalThis.__CGW_SENTINEL_CAPTURE__;
      if (!cap || !cap.headers) return null;
      if (Date.now() - (cap.capturedAt || 0) > SENTINEL_TTL_MS) return null;
      return cap;
    } catch (_e) {
      return null;
    }
  }

  function clearSentinelCapture() {
    try {
      delete globalThis.__CGW_SENTINEL_CAPTURE__;
    } catch (_e) {
      globalThis.__CGW_SENTINEL_CAPTURE__ = null;
    }
  }

  function discoverSentinelSdkSrc() {
    try {
      var scripts = Array.prototype.slice.call(document.scripts || []);
      for (var i = 0; i < scripts.length; i++) {
        var src = scripts[i].src || "";
        if (/\/sentinel\/[^/]+\/sdk\.js/i.test(src)) return src;
      }
    } catch (_e) {
      /* ignore */
    }
    return BASE + "/sentinel/20260423af3c/sdk.js";
  }

  function resolvePageSentinelSdk() {
    if (globalThis.SentinelSDK && typeof globalThis.SentinelSDK.token === "function") {
      return { sdk: globalThis.SentinelSDK, source: "global:SentinelSDK" };
    }

    var fromWebpack = webpackFind(function (mod) {
      if (!mod) return false;
      var candidates = [mod, mod.default, mod.SentinelSDK, mod.sentinel];
      for (var i = 0; i < candidates.length; i++) {
        var e = candidates[i];
        if (e && typeof e.token === "function") return true;
      }
      return false;
    });
    if (fromWebpack) {
      var sdk =
        fromWebpack.token
          ? fromWebpack
          : fromWebpack.default && fromWebpack.default.token
            ? fromWebpack.default
            : fromWebpack.SentinelSDK || fromWebpack.sentinel;
      if (sdk && typeof sdk.token === "function") {
        return { sdk: sdk, source: "webpack:SentinelSDK" };
      }
    }

    return null;
  }

  async function ensureSentinelSdkInitialized(sdk) {
    if (!sdk) return false;
    if (typeof sdk.init === "function") {
      try {
        await sdk.init("__default__");
      } catch (_e) {
        try {
          await sdk.init();
        } catch (_e2) {
          recordSentinelDiagnostic({ stage: "sdk:init_failed", error: String(_e2 && _e2.message ? _e2.message : _e2) });
        }
      }
    }
    return typeof sdk.token === "function";
  }

  async function loadSentinelSdkAsync() {
    var resolved = resolvePageSentinelSdk();
    if (resolved) {
      recordSentinelDiagnostic({ stage: "sdk:resolved", source: resolved.source });
      await ensureSentinelSdkInitialized(resolved.sdk);
      return resolved.sdk;
    }

    if (globalThis.__CGW_SENTINEL_SDK_LOADING__) {
      try {
        await globalThis.__CGW_SENTINEL_SDK_LOADING__;
      } catch (_e) {
        return null;
      }
      resolved = resolvePageSentinelSdk();
      if (resolved) {
        await ensureSentinelSdkInitialized(resolved.sdk);
        return resolved.sdk;
      }
      return null;
    }

    var src = discoverSentinelSdkSrc();
    globalThis.__CGW_SENTINEL_SDK_LOADING__ = new Promise(function (resolve, reject) {
      var script = document.createElement("script");
      script.src = src;
      script.async = true;
      script.onload = function () {
        resolve();
      };
      script.onerror = function () {
        reject(new Error("sentinel_sdk_load_failed"));
      };
      document.head.appendChild(script);
    });

    try {
      await globalThis.__CGW_SENTINEL_SDK_LOADING__;
    } catch (_e) {
      globalThis.__CGW_SENTINEL_SDK_LOADING__ = null;
      recordSentinelDiagnostic({
        stage: "sdk:load_failed",
        error: _e && _e.message ? String(_e.message) : "sentinel_sdk_load_failed",
      });
      return null;
    }

    resolved = resolvePageSentinelSdk();
    if (resolved) {
      recordSentinelDiagnostic({ stage: "sdk:loaded", source: resolved.source });
      await ensureSentinelSdkInitialized(resolved.sdk);
      return resolved.sdk;
    }

    recordSentinelDiagnostic({ stage: "sdk:missing_after_load", error: "sentinel_sdk_token_missing" });
    return null;
  }

  function mapFinalizeResponseToHeaders(json) {
    if (!json || typeof json !== "object") return {};
    var out = {};
    if (json.headers && typeof json.headers === "object") {
      Object.keys(json.headers).forEach(function (k) {
        out[k] = json.headers[k];
      });
    }

    var aliases = [
      ["openai-sentinel-chat-requirements-token", [
        "openai-sentinel-chat-requirements-token",
        "chat_requirements_token",
        "chat-requirements-token",
        "requirements_token",
      ]],
      ["openai-sentinel-proof-token", [
        "openai-sentinel-proof-token",
        "proof_token",
        "proofofwork_token",
      ]],
      ["openai-sentinel-turnstile-token", [
        "openai-sentinel-turnstile-token",
        "turnstile_token",
        "turnstile",
      ]],
    ];

    aliases.forEach(function (pair) {
      var headerName = pair[0];
      if (out[headerName]) return;
      for (var j = 0; j < pair[1].length; j++) {
        var key = pair[1][j];
        if (json[key]) {
          out[headerName] = json[key];
          break;
        }
      }
    });

    return extractSentinelHeaders(out);
  }

  function headersFromSdkTokenPayload(payload) {
    if (!payload) return null;
    if (typeof payload === "string") {
      if (payload.indexOf("gAAAAAB") === 0) {
        return {
          "openai-sentinel-chat-requirements-token": payload,
        };
      }
      var parsed = parseJsonSafe(payload);
      if (parsed) return headersFromSdkTokenPayload(parsed);
      return null;
    }
    if (typeof payload !== "object") return null;

    var direct = extractSentinelHeaders(payload);
    if (Object.keys(direct).length > 0) return direct;

    if (payload.headers) {
      direct = extractSentinelHeaders(payload.headers);
      if (Object.keys(direct).length > 0) return direct;
    }

    return null;
  }

  async function postSentinelChatRequirementsFinalize(session, body) {
    var url = BASE + "/backend-api/sentinel/chat-requirements/finalize";
    var res = await fetch(url, {
      method: "POST",
      credentials: "include",
      cache: "no-store",
      headers: mergeHeaders(session.token, {
        accept: "*/*",
        "content-type": "application/json",
      }),
      body: JSON.stringify(body),
    });
    var text = await res.text();
    var json = parseJsonSafe(text);
    if (!res.ok) {
      return {
        ok: false,
        status: res.status,
        error: "sentinel_finalize_failed",
        json: json,
        text: text,
      };
    }
    return {
      ok: true,
      status: res.status,
      json: json,
      headers: mapFinalizeResponseToHeaders(json),
    };
  }

  async function tryAcquireFreshSentinelViaSdk(session) {
    recordSentinelDiagnostic({ stage: "sdk:start" });
    var sdk = await loadSentinelSdkAsync();
    if (!sdk) {
      recordSentinelDiagnostic({ stage: "sdk:unavailable", error: "sdk_load_null" });
      return null;
    }

    var flow = "__default__";
    try {
      var tokenPayload = await sdk.token(flow);
      var direct = headersFromSdkTokenPayload(tokenPayload);
      if (direct && Object.keys(direct).length > 0) {
        recordSentinelDiagnostic({ stage: "sdk:token-direct", source: "sdk:token-direct" });
        return { headers: direct, source: "sdk:token-direct" };
      }

      var body = typeof tokenPayload === "string" ? parseJsonSafe(tokenPayload) : tokenPayload;
      if (!body || typeof body !== "object") {
        recordSentinelDiagnostic({ stage: "sdk:token_parse_failed", error: "token_payload_invalid" });
        return null;
      }

      var finalizeBody = {
        prepare_token: body.c || body.prepare_token || body.token,
        proofofwork: body.p || body.proofofwork,
        turnstile: body.t || body.turnstile,
      };
      if (!finalizeBody.prepare_token) {
        recordSentinelDiagnostic({ stage: "sdk:token_missing_prepare", error: "prepare_token_missing" });
        return null;
      }

      var finalized = await postSentinelChatRequirementsFinalize(session, finalizeBody);
      if (!finalized.ok || !finalized.headers || Object.keys(finalized.headers).length === 0) {
        recordSentinelDiagnostic({
          stage: "sdk:finalize_failed",
          error: finalized.error || "sentinel_finalize_failed",
          finalizeStatus: finalized.status,
        });
        return null;
      }

      recordSentinelDiagnostic({
        stage: "sdk:finalize_ok",
        source: "sdk:chat-requirements-finalize",
        finalizeStatus: finalized.status,
      });
      return { headers: finalized.headers, source: "sdk:chat-requirements-finalize" };
    } catch (_e) {
      recordSentinelDiagnostic({
        stage: "sdk:token_threw",
        error: _e && _e.message ? String(_e.message) : "sdk_token_failed",
      });
      return null;
    }
  }

  async function refreshConversationSentinelHeaders(session) {
    clearSentinelCapture();
    recordSentinelDiagnostic({ stage: "refresh:start" });

    var fromSdk = await tryAcquireFreshSentinelViaSdk(session);
    if (fromSdk && fromSdk.headers && Object.keys(fromSdk.headers).length > 0) {
      return fromSdk;
    }

    var fromPage = await tryAcquireSentinelFromPage();
    if (fromPage && fromPage.headers && Object.keys(fromPage.headers).length > 0) {
      recordSentinelDiagnostic({ stage: "page-module", source: fromPage.source });
      return fromPage;
    }

    recordSentinelDiagnostic({ stage: "page-module", error: "webpack_probe_miss" });

    var cached = readSentinelCapture();
    if (cached && cached.headers && Object.keys(cached.headers).length > 0) {
      recordSentinelDiagnostic({ stage: "tap-cache", source: cached.source || "fetch-tap-cache" });
      return { headers: cached.headers, source: cached.source || "fetch-tap-cache" };
    }

    recordSentinelDiagnostic({ stage: "exhausted", error: "sentinel_unavailable" });
    return null;
  }

  function webpackFind(predicate) {
    var found = null;
    var chunkNames = ["webpackChunk_N_E", "webpackChunkchatgpt"];
    for (var c = 0; c < chunkNames.length && !found; c++) {
      var chunk = globalThis[chunkNames[c]];
      if (!chunk || typeof chunk.push !== "function") continue;
      try {
        chunk.push([
          ["cgw-sentinel-probe-" + Date.now()],
          {},
          function (__webpack_require__) {
            for (var moduleId in __webpack_require__.m) {
              if (found) break;
              try {
                var exp = __webpack_require__(moduleId);
                if (predicate(exp, moduleId)) found = exp;
              } catch (_e) {
                /* ignore */
              }
            }
          },
        ]);
      } catch (_e) {
        /* ignore */
      }
    }
    return found;
  }

  function pickSentinelRunner(exp) {
    if (!exp) return null;
    var candidates = [exp, exp.default, exp.SentinelSDK, exp.sentinel];
    var methodNames = [
      "token",
      "init",
      "getConversationHeaders",
      "getSentinelHeaders",
      "buildSentinelHeaders",
      "prepareConversationHeaders",
      "getChatRequirementsHeaders",
      "fetchConversationSentinelHeaders",
      "buildChatRequirementsHeaders",
    ];
    for (var i = 0; i < candidates.length; i++) {
      var e = candidates[i];
      if (!e || typeof e !== "object") continue;
      for (var m = 0; m < methodNames.length; m++) {
        var name = methodNames[m];
        if (typeof e[name] === "function") {
          return { fn: e[name].bind(e), kind: name };
        }
      }
    }
    return null;
  }

  async function tryAcquireSentinelFromPage() {
    var exp = webpackFind(function (mod) {
      return !!pickSentinelRunner(mod);
    });
    var runner = pickSentinelRunner(exp);
    if (!runner) return null;
    try {
      var result = await runner.fn();
      var headers = extractSentinelHeaders(result);
      if (Object.keys(headers).length > 0) {
        return { headers: headers, source: "page-module:" + runner.kind };
      }
      if (result && typeof result === "object") {
        headers = extractSentinelHeaders(result.headers || result);
        if (Object.keys(headers).length > 0) {
          return { headers: headers, source: "page-module:" + runner.kind };
        }
      }
    } catch (_e) {
      /* ignore */
    }
    return null;
  }

  async function acquireConversationSentinelHeaders(cmd) {
    installSentinelFetchTap();

    var session = await getAccessToken();
    if (!session.ok) {
      return {
        type: "apiError",
        ok: false,
        error: session.error || "session_expired",
        status: session.status || 401,
      };
    }

    var allowCache = cmd && cmd.fresh === false;
    if (allowCache) {
      var cached = readSentinelCapture();
      if (cached && cached.headers && Object.keys(cached.headers).length > 0) {
        var tapDiag = readSentinelDiagnostic();
        return {
          type: "apiResult",
          ok: true,
          status: 200,
          json: {
            headers: cached.headers,
            source: cached.source || "fetch-tap-cache",
            ageMs: Date.now() - (cached.capturedAt || 0),
            diagnostic: tapDiag,
          },
        };
      }
    }

    var fresh = await refreshConversationSentinelHeaders(session);
    var diagnostic = readSentinelDiagnostic();
    if (fresh && fresh.headers && Object.keys(fresh.headers).length > 0) {
      return {
        type: "apiResult",
        ok: true,
        status: 200,
        json: {
          headers: fresh.headers,
          source: fresh.source,
          diagnostic: diagnostic,
        },
      };
    }

    return {
      type: "apiError",
      ok: false,
      status: 0,
      error: "sentinel_unavailable",
      message:
        "No fresh sentinel from SentinelSDK chat-requirements finalize, page module, or fetch tap.",
      json: {
        diagnostic: diagnostic,
      },
    };
  }

  async function ensureConversationSentinelHeaders(existing, session) {
    var merged = Object.assign({}, existing || {});
    Object.keys(merged).forEach(function (k) {
      if (k.toLowerCase().indexOf("sentinel") >= 0) {
        delete merged[k];
      }
    });

    if (!session || !session.ok) {
      session = await getAccessToken();
      if (!session.ok) return merged;
    }

    var fresh = await refreshConversationSentinelHeaders(session);
    if (fresh && fresh.headers) {
      Object.keys(fresh.headers).forEach(function (k) {
        merged[k] = fresh.headers[k];
      });
    }
    return merged;
  }

  installSentinelFetchTap();

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
    var isConversationPost =
      method === "POST" &&
      path.indexOf("/f/conversation") >= 0 &&
      path.indexOf("/prepare") < 0;

    var extraHeaders = cmd.headers;
    if (isConversationPost) {
      extraHeaders = await ensureConversationSentinelHeaders(cmd.headers, session);
    }

    var init = {
      method: method,
      credentials: "include",
      cache: "no-store",
      headers: mergeHeaders(session.token, extraHeaders),
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

    var res;
    try {
      res = await fetch(url, init);
    } finally {
      if (isConversationPost) {
        clearSentinelCapture();
      }
    }
    var contentType = (res.headers && res.headers.get("content-type")) || "";

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

  function isProjectSourcesUpload(cmd) {
    return (
      cmd.pureApiProjectSources === true ||
      String(cmd.entrySurface || "").toLowerCase() === "project_sources"
    );
  }

  function buildChatComposerRegisterBody(cmd, fileName, fileSize, mimeType, useCase) {
    return {
      file_name: fileName,
      file_size: fileSize,
      use_case: useCase,
      timezone_offset_min: timezoneOffsetMin(),
      reset_rate_limits: false,
      supports_direct_azure_multipart: true,
      mime_type: mimeType,
      entry_surface: (cmd && cmd.entrySurface) || "chat_composer",
      selection_method: (cmd && cmd.selectionMethod) || "api",
      client_resolved_mime_type: mimeType,
      mime_resolution_source: "filename_extension",
    };
  }

  function buildProjectSourcesRegisterBody(cmd, fileName, fileSize, mimeType) {
    return {
      file_name: fileName,
      file_size: fileSize,
      use_case: cmd.useCase || "agent",
      gizmo_id: cmd.gizmoId,
      timezone_offset_min: timezoneOffsetMin(),
      reset_rate_limits: false,
      supports_direct_azure_multipart: true,
      mime_type: mimeType,
      entry_surface: "project_sources",
      selection_method: cmd.selectionMethod || "api",
      client_resolved_mime_type: mimeType,
      mime_resolution_source: "filename_extension",
      store_in_library: false,
    };
  }

  function buildProjectSourcesProcessStreamBody(cmd, fileId, fileName, useCase) {
    return {
      file_id: fileId,
      use_case: useCase || "agent",
      gizmo_id: cmd.gizmoId,
      index_for_retrieval: true,
      file_name: fileName || "upload.bin",
      entry_surface: "project_sources",
      metadata: {
        store_in_library: false,
        is_temporary_chat: false,
        is_project_thread: true,
      },
    };
  }

  function buildProjectSourcesAttachRef(cmd, fileId, fileName, fileSize, mimeType) {
    return {
      file_id: fileId,
      name: fileName || fileId,
      size: fileSize,
      type: mimeType || "application/octet-stream",
      last_modified: Date.now(),
      location: "fs",
    };
  }

  async function processUploadStream(session, fileId, fileName, useCase, cmd) {
    var body = {
      file_id: fileId,
      use_case: useCase || "my_files",
      index_for_retrieval: true,
      file_name: fileName || "upload.bin",
    };
    if (cmd && isProjectSourcesUpload(cmd)) {
      body = buildProjectSourcesProcessStreamBody(cmd, fileId, fileName, useCase);
    } else {
      body.entry_surface = (cmd && cmd.entrySurface) || "chat_composer";
      if (cmd && cmd.conversationId) {
        body.metadata = {
          is_temporary_chat: false,
          library_eligibility_reason: "project_recall_gate_disabled",
          is_project_thread: true,
          library_file_info: {
            origination_message_id: cmd.parentMessageId || null,
            origination_thread_id: cmd.conversationId,
          },
        };
      }
    }

    var res = await fetch(BASE + "/backend-api/files/process_upload_stream", {
      method: "POST",
      credentials: "include",
      headers: mergeHeaders(session.token, { "content-type": "application/json" }),
      body: JSON.stringify(body),
    });

    var text = await res.text();
    if (res.ok || uploadStreamLooksReady(text)) {
      return { ok: true, status: res.status, bodyText: text };
    }

    return { ok: false, status: res.status, bodyText: text };
  }

  async function waitForUploadProcessing(session, fileId, fileName, useCase, timeoutMs, cmd) {
    var deadline = Date.now() + (timeoutMs || 30000);
    var lastText = "";
    var pollMs = isProjectSourcesUpload(cmd) ? 350 : 800;

    while (Date.now() < deadline) {
      var result = await processUploadStream(session, fileId, fileName, useCase, cmd);
      lastText = result.bodyText || "";
      if (result.ok || uploadStreamLooksReady(lastText)) {
        return { ok: true, bodyText: lastText };
      }
      await sleep(pollMs);
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

  async function attachProjectSourcesFile(session, cmd, fileId, fileSize, mimeType) {
    var path =
      "/backend-api/projects/" + encodeURIComponent(cmd.gizmoId) + "/files";
    var body = {
      files: [
        buildProjectSourcesAttachRef(cmd, fileId, cmd.fileName, fileSize, mimeType),
      ],
    };
    var result = await tryAttachAttempts(session, [{ path: path, body: body }]);
    if (result.ok && result.winner) {
      return {
        type: "apiResult",
        ok: true,
        status: result.winner.status,
        json: result.winner.json,
        fileId: fileId,
        attachPath: result.winner.path,
        attachAttempts: result.log,
        location: "fs",
        entrySurface: "project_sources",
      };
    }

    var last = result.last;
    return {
      type: "apiError",
      ok: false,
      status: last ? last.status : 0,
      error: "attach_failed",
      json: last ? last.json : null,
      fileId: fileId,
      attachPath: last ? last.path : null,
      attachAttempts: result.log,
      bodyText: last && last.text && last.text.length < 2000 ? last.text : null,
    };
  }

  async function attachFileToProject(session, cmd, fileId) {
    if (!cmd.gizmoId || !fileId) {
      return { type: "apiResult", ok: true, fileId: fileId };
    }

    resolveAccountIdFromSession(session);

    if (isProjectSourcesUpload(cmd)) {
      return await attachProjectSourcesFile(
        session,
        cmd,
        fileId,
        cmd.fileSize || 0,
        cmd.mimeType || "application/octet-stream"
      );
    }

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

      if (cmd.useProjectLibrary === true && cmd.gizmoId && isSnorlaxProjectId(cmd.gizmoId)) {
        var libraryResult = await tryUploadProjectLibrary(
          session,
          cmd,
          bytes,
          mimeType,
          fileName
        );
        if (libraryResult.ok) {
          if (cmd.waitForLibraryFinalize !== false) {
            await waitForUploadProcessing(
              session,
              libraryResult.fileId,
              fileName,
              useCase,
              30000
            );
            var finalized = await waitForFileFinalize(session, libraryResult.fileId, 45000);
            if (!finalized.ok) {
              return {
                type: "apiError",
                ok: false,
                status: finalized.status || 0,
                error: "upload_failed",
                message: finalized.error || "finalize_failed",
                fileId: libraryResult.fileId,
                bodyText:
                  finalized.bodyText && finalized.bodyText.length < 2000
                    ? finalized.bodyText
                    : null,
              };
            }
          }
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

      var registerBody = isProjectSourcesUpload(cmd)
        ? buildProjectSourcesRegisterBody(cmd, fileName, fileSize, mimeType)
        : buildChatComposerRegisterBody(cmd, fileName, fileSize, mimeType, useCase);

      var registerRes = await fetch(BASE + "/backend-api/files", {
        method: "POST",
        credentials: "include",
        headers: mergeHeaders(session.token, { "content-type": "application/json" }),
        body: JSON.stringify(registerBody),
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

      var streamUseCase = isProjectSourcesUpload(cmd) ? cmd.useCase || "agent" : useCase;
      var streamTimeoutMs = isProjectSourcesUpload(cmd) ? 90000 : 30000;
      await waitForUploadProcessing(
        session,
        fileId,
        fileName,
        streamUseCase,
        streamTimeoutMs,
        cmd
      );

      if (!isProjectSourcesUpload(cmd)) {
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
      }

      if (!cmd.gizmoId || cmd.skipProjectAttach === true) {
        return {
          type: "apiResult",
          ok: true,
          status: registerRes.status,
          json: registerJson,
          fileId: fileId,
          location: "fs",
          entrySurface: "project_sources",
        };
      }

      // Stream processing includes gizmo_id + entry_surface and often binds the file.
      // C# verifies via project list and calls attachProjectFile only when still unlisted.
      return {
        type: "apiResult",
        ok: true,
        status: registerRes.status,
        json: registerJson,
        fileId: fileId,
        location: "fs",
        entrySurface: "project_sources",
        streamBound: true,
      };
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

  var CGW_PROJECT_FILE_INPUT_MARK = "data-cgw-project-file-input";

  function isInsideComposer(el) {
    if (!el) return false;
    return !!(
      el.closest('[data-testid="composer"]') ||
      el.closest("#cgw-play-composer-root")
    );
  }

  function clickFirstVisible(selector, clicked, label) {
    var nodes = document.querySelectorAll(selector);
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (isInsideComposer(el)) continue;
      var style = window.getComputedStyle(el);
      if (style.display === "none" || style.visibility === "hidden") continue;
      if (el.disabled) continue;
      try {
        el.click();
        clicked.push(label || selector);
        return true;
      } catch (_e) {
        /* ignore */
      }
    }
    return false;
  }

  function clickButtonsByText(pattern, clicked, label) {
    var re = pattern instanceof RegExp ? pattern : new RegExp(pattern, "i");
    var nodes = document.querySelectorAll("button, a, [role='tab'], [role='button']");
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (isInsideComposer(el)) continue;
      var text =
        (el.textContent && el.textContent.trim()) ||
        el.getAttribute("aria-label") ||
        "";
      if (!re.test(text)) continue;
      var style = window.getComputedStyle(el);
      if (style.display === "none" || style.visibility === "hidden") continue;
      try {
        el.click();
        clicked.push(label || text.slice(0, 40));
        return true;
      } catch (_e2) {
        /* ignore */
      }
    }
    return false;
  }

  function clearProjectFileInputMarks() {
    var marked = document.querySelectorAll("input[" + CGW_PROJECT_FILE_INPUT_MARK + '="1"]');
    for (var i = 0; i < marked.length; i++) {
      marked[i].removeAttribute(CGW_PROJECT_FILE_INPUT_MARK);
    }
  }

  function scoreProjectFileInput(el) {
    var score = 0;
    if (el.closest('[role="dialog"]')) score += 12;
    if (el.closest("aside")) score += 8;
    if (el.closest('[class*="project"]')) score += 5;
    if (el.closest("main")) score += 2;
    var accept = el.getAttribute("accept") || "";
    if (/\.md|text\/|markdown/i.test(accept)) score += 3;
    if (el.multiple) score += 1;
    var testId = el.getAttribute("data-testid") || "";
    if (/file|upload|knowledge|project/i.test(testId)) score += 4;
    return score;
  }

  function findBestProjectFileInput(requireDialog) {
    clearProjectFileInputMarks();
    var nodes = document.querySelectorAll('input[type="file"]');
    var best = null;
    var bestScore = -1;
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (isInsideComposer(el)) continue;
      if (requireDialog && !el.closest('[role="dialog"]')) continue;
      var score = scoreProjectFileInput(el);
      if (score > bestScore) {
        bestScore = score;
        best = el;
      }
    }
    var minScore = requireDialog ? 12 : 2;
    if (!best || bestScore < minScore) return { found: false, score: bestScore };
    best.setAttribute(CGW_PROJECT_FILE_INPUT_MARK, "1");
    return {
      found: true,
      score: bestScore,
      accept: best.getAttribute("accept") || "",
      testId: best.getAttribute("data-testid") || "",
      multiple: !!best.multiple,
      inDialog: !!best.closest('[role="dialog"]'),
    };
  }

  async function waitForSourcesDialog(maxMs) {
    var deadline = Date.now() + (maxMs || 3000);
    while (Date.now() < deadline) {
      if (document.querySelector('[role="dialog"]')) return true;
      await sleep(200);
    }
    return !!document.querySelector('[role="dialog"]');
  }

  function listProjectFileUi() {
    var fileInputs = [];
    var nodes = document.querySelectorAll('input[type="file"]');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (isInsideComposer(el)) continue;
      fileInputs.push({
        accept: el.getAttribute("accept") || "",
        multiple: !!el.multiple,
        hidden: !el.offsetParent && el.type === "file",
        id: el.id || "",
        name: el.name || "",
        testId: el.getAttribute("data-testid") || "",
        score: scoreProjectFileInput(el),
        inDialog: !!el.closest('[role="dialog"]'),
      });
    }

    return {
      href: location.href,
      fileInputs: fileInputs,
      composerInputsExcluded: true,
    };
  }

  function clickSourcesTab(clicked) {
    var nodes = document.querySelectorAll('[role="tab"], button, [role="button"]');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (isInsideComposer(el)) continue;
      var text =
        (el.textContent && el.textContent.trim()) ||
        el.getAttribute("aria-label") ||
        "";
      if (!/^sources$/i.test(text)) continue;
      var style = window.getComputedStyle(el);
      if (style.display === "none" || style.visibility === "hidden") continue;
      if (el.disabled) continue;
      try {
        el.click();
        clicked.push("tab:sources");
        return true;
      } catch (_e) {
        /* ignore */
      }
    }
    if (clickFirstVisible('[role="tab"][aria-label*="Sources"]', clicked, "tab:sources-role")) return true;
    if (clickFirstVisible('button[aria-label*="Sources"]', clicked, "tab:sources-aria")) return true;
    return clickButtonsByText(/^sources$/i, clicked, "tab:sources-fallback");
  }

  function clickAddSources(clicked) {
    var nodes = document.querySelectorAll("button, [role='button']");
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (isInsideComposer(el)) continue;
      var text =
        (el.textContent && el.textContent.trim()) ||
        el.getAttribute("aria-label") ||
        "";
      if (!/^add sources$/i.test(text)) continue;
      var style = window.getComputedStyle(el);
      if (style.display === "none" || style.visibility === "hidden") continue;
      if (el.disabled) continue;
      try {
        el.click();
        clicked.push("add-sources");
        return true;
      } catch (_e2) {
        /* ignore */
      }
    }
    var addSelectors = [
      'button[aria-label*="Add sources"]',
      'button[aria-label*="Add Sources"]',
      '[data-testid*="add-sources"]',
      '[data-testid*="add-source"]',
    ];
    for (var a = 0; a < addSelectors.length; a++) {
      if (clickFirstVisible(addSelectors[a], clicked, addSelectors[a])) return true;
    }
    return clickButtonsByText(/add sources/i, clicked, "add-sources-phrase");
  }

  async function prepareProjectKnowledgeUpload(cmd) {
    var clicked = [];

    if (!clickSourcesTab(clicked)) {
      return {
        type: "apiResult",
        ok: false,
        json: {
          href: location.href,
          clicked: clicked,
          ui: listProjectFileUi(),
        },
        error: "sources_tab_not_found",
      };
    }

    await sleep(500);

    if (!clickAddSources(clicked)) {
      return {
        type: "apiResult",
        ok: false,
        json: {
          href: location.href,
          clicked: clicked,
          ui: listProjectFileUi(),
        },
        error: "add_sources_not_found",
      };
    }

    await waitForSourcesDialog(4000);
    await sleep(300);
    var fileInput = findBestProjectFileInput(true);
    if (!fileInput || !fileInput.found) {
      fileInput = findBestProjectFileInput(false);
    }
    return {
      type: "apiResult",
      ok: !!(fileInput && fileInput.found),
      json: {
        href: location.href,
        clicked: clicked,
        fileInput: fileInput,
        strategy: "sources_tab_add_sources",
        ui: listProjectFileUi(),
      },
      error: fileInput && fileInput.found ? null : "project_file_input_not_found",
    };
  }

  async function confirmProjectKnowledgeUpload(cmd) {
    var clicked = [];
    clickButtonsByText(/^(save|done|upload|add|confirm)$/i, clicked, "confirm");
    clickButtonsByText(/save changes|upload file|add to project|add files|add sources/i, clicked, "confirm-phrase");
    var confirmSelectors = [
      'button[type="submit"]',
      'button[data-testid*="save"]',
      'button[data-testid*="confirm"]',
      'button[data-testid*="upload"]',
      'button[aria-label*="Save"]',
      'button[aria-label*="Upload"]',
    ];
    for (var c = 0; c < confirmSelectors.length; c++) {
      clickFirstVisible(confirmSelectors[c], clicked, confirmSelectors[c]);
    }
    await sleep(400);
    return {
      type: "apiResult",
      ok: true,
      json: {
        href: location.href,
        clicked: clicked,
        ui: listProjectFileUi(),
      },
    };
  }

  function pollProjectKnowledgeUpload(cmd) {
    var fileName = cmd.fileName || "";
    var base = fileName.split("/").pop().split("\\").pop();
    if (!base) {
      return {
        ready: false,
        pending: false,
        error: "missing_file_name",
      };
    }

    var lowerBase = base.toLowerCase();
    var nameNodes = document.querySelectorAll(
      '[data-testid*="file"], [class*="file-name"], li, tr, [role="listitem"]'
    );
    var nameSeen = false;
    for (var i = 0; i < nameNodes.length; i++) {
      var text = (nameNodes[i].textContent || "").trim().toLowerCase();
      if (text.indexOf(lowerBase) >= 0) {
        nameSeen = true;
        break;
      }
    }

    var busy =
      !!document.querySelector(
        '[aria-busy="true"], [data-testid*="upload"], [data-testid*="spinner"]'
      ) && !nameSeen;

    return {
      ready: nameSeen,
      pending: busy,
      fileName: base,
      href: location.href,
      fileInputCount: listProjectFileUi().fileInputs.length,
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

  function normalizeSameOriginBackendPath(url) {
    if (!url) return null;
    if (String(url).indexOf("/backend-api/") === 0) return String(url);
    try {
      var u = new URL(String(url), BASE);
      if (
        u.hostname === "chatgpt.com" &&
        u.pathname.indexOf("/backend-api/") === 0
      ) {
        return u.pathname + u.search;
      }
    } catch (_e) {
      /* ignore */
    }
    return null;
  }

  function isLikelyDownloadRedirectEnvelopeText(text) {
    if (!text || text.length === 0 || text.length > 4096) return false;
    var t = text.trim();
    if (t.charAt(0) !== "{") return false;
    if (t.indexOf('"download_url"') === -1) return false;
    return t.indexOf('"status"') !== -1 && t.toLowerCase().indexOf("success") !== -1;
  }

  function tryParseDownloadRedirectPath(text) {
    if (!isLikelyDownloadRedirectEnvelopeText(text)) return null;
    var j = parseJsonSafe(text);
    if (!j || !j.download_url) return null;
    return normalizeSameOriginBackendPath(String(j.download_url));
  }

  function bytesToDownloadPayload(bytes) {
    var binary = "";
    var chunk = 0x8000;
    for (var j = 0; j < bytes.length; j += chunk) {
      binary += String.fromCharCode.apply(null, bytes.subarray(j, j + chunk));
    }
    var text = null;
    try {
      text = new TextDecoder("utf-8").decode(bytes);
    } catch (_e) {
      /* ignore */
    }
    return {
      base64: btoa(binary),
      text: text,
      byteLength: bytes.length,
    };
  }

  async function fetchAuthorizedDownloadBytes(session, path) {
    var res = await fetch(BASE + path, {
      method: "GET",
      credentials: "include",
      cache: "no-store",
      headers: { authorization: "Bearer " + session.token },
    });
    if (!res.ok) {
      return { ok: false, status: res.status, path: path };
    }
    var buf = await res.arrayBuffer();
    var bytes = new Uint8Array(buf);
    var payload = bytesToDownloadPayload(bytes);
    return {
      ok: true,
      status: res.status,
      path: path,
      bytes: bytes,
      base64: payload.base64,
      text: payload.text,
      byteLength: payload.byteLength,
    };
  }

  function decodeUtf8Prefix(bytes, maxLen) {
    var n = Math.min(bytes.length, maxLen || 16);
    if (n <= 0) return "";
    try {
      return new TextDecoder("utf-8").decode(bytes.subarray(0, n)).trimStart();
    } catch (_e) {
      return "";
    }
  }

  function decodeUtf8Text(bytes) {
    if (!bytes || bytes.length === 0) return "";
    try {
      return new TextDecoder("utf-8").decode(bytes).trim();
    } catch (_e) {
      return "";
    }
  }

  function isLikelyApiErrorJsonText(text) {
    if (!text || text.length === 0 || text.length > 4096) return false;
    var t = text.trim();
    if (t.charAt(0) !== "{") return false;
    return (
      t.indexOf('"detail"') !== -1 ||
      t.indexOf('"error"') !== -1 ||
      t.toLowerCase().indexOf("not found") !== -1
    );
  }

  function isLikelyDownloadMetadataJsonStubText(text) {
    if (!text || text.length === 0 || text.length > 4096) return false;
    if (isLikelyDownloadRedirectEnvelopeText(text)) return false;
    var t = text.trim();
    if (t.charAt(0) !== "{") return false;
    if (isLikelyApiErrorJsonText(t)) return false;
    return (
      t.indexOf('"file_id"') !== -1 ||
      (t.indexOf('"name"') !== -1 && t.indexOf('"size"') !== -1)
    );
  }

  function isLikelyDownloadStubPayload(bytes, expectedMinBytes) {
    if (!bytes || bytes.length === 0) return true;
    if (expectedMinBytes > 0 && bytes.length < expectedMinBytes) {
      var prefix = decodeUtf8Prefix(bytes, 16);
      if (prefix.charAt(0) === "{" || prefix.charAt(0) === "[") return true;
    }
    var text = decodeUtf8Text(bytes);
    if (isLikelyDownloadMetadataJsonStubText(text)) return true;
    return isLikelyApiErrorJsonText(text);
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
                "/backend-api/files/download/" +
                  encodeURIComponent(fileId) +
                  "?gizmo_id=" +
                  encodeURIComponent(gizmoId) +
                  "&download_intent=true",
                "/backend-api/files/download/" +
                  encodeURIComponent(fileId) +
                  "?gizmo_id=" +
                  encodeURIComponent(gizmoId) +
                  "&inline=false&download_intent=false",
                "/backend-api/files/download/" +
                  encodeURIComponent(fileId) +
                  "?gizmo_id=" +
                  encodeURIComponent(gizmoId) +
                  "&inline=false&download_intent=true",
                "/backend-api/files/download/" +
                  encodeURIComponent(fileId) +
                  "?gizmo_id=" +
                  encodeURIComponent(gizmoId) +
                  "&inline=true&download_intent=false",
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
    var lastStub = null;
    var expectedMinBytes =
      typeof cmd.expectedMinBytes === "number" && cmd.expectedMinBytes > 0
        ? cmd.expectedMinBytes
        : 0;
    var failFast =
      !!cmd.failFast && String(cmd.location || "").toLowerCase() === "fs";
    var requireProjectPaths = !!cmd.requireProjectPaths && !!cmd.gizmoId;
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
            (failFast || requireProjectPaths) &&
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
        var payload = bytesToDownloadPayload(bytes);
        var text = payload.text;
        var redirectPath = text ? tryParseDownloadRedirectPath(text) : null;
        if (redirectPath) {
          attempts.push({
            status: res.status,
            path: paths[i],
            redirect: redirectPath,
          });
          var redirected = await fetchAuthorizedDownloadBytes(session, redirectPath);
          if (!redirected.ok) {
            attempts.push({
              status: redirected.status,
              path: redirectPath,
            });
            lastErr = {
              status: redirected.status,
              path: redirectPath,
              attempts: attempts,
            };
            continue;
          }
          if (isLikelyDownloadStubPayload(redirected.bytes, expectedMinBytes)) {
            attempts.push({
              status: redirected.status,
              path: redirectPath,
              stub: true,
              byteLength: redirected.byteLength,
            });
            lastStub = {
              status: redirected.status,
              path: redirectPath,
              byteLength: redirected.byteLength,
            };
            continue;
          }
          return {
            type: "apiResult",
            ok: true,
            status: redirected.status,
            path: redirectPath,
            redirectFrom: paths[i],
            base64: redirected.base64,
            text: redirected.text,
            byteLength: redirected.byteLength,
          };
        }
        if (isLikelyDownloadStubPayload(bytes, expectedMinBytes)) {
          attempts.push({
            status: res.status,
            path: paths[i],
            stub: true,
            byteLength: bytes.length,
          });
          lastStub = {
            status: res.status,
            path: paths[i],
            byteLength: bytes.length,
          };
          continue;
        }
        return {
          type: "apiResult",
          ok: true,
          status: res.status,
          path: paths[i],
          base64: payload.base64,
          text: payload.text,
          byteLength: payload.byteLength,
        };
      } catch (e) {
        lastErr = {
          message: e && e.message ? String(e.message) : "unknown",
          attempts: attempts,
        };
      }
    }

    if (lastStub) {
      return {
        type: "apiError",
        ok: false,
        error: "download_stub",
        status: lastStub.status,
        message:
          "download_stub " + lastStub.byteLength + " " + lastStub.path,
        detail: {
          status: lastStub.status,
          path: lastStub.path,
          stubByteLength: lastStub.byteLength,
          attempts: attempts,
        },
      };
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

  async function downloadInterpreterFile(cmd) {
    var session = await getAccessToken();
    if (!session.ok) {
      return {
        type: "apiError",
        ok: false,
        error: session.error || "session_expired",
        status: session.status || 401,
      };
    }

    var conversationId = cmd.conversationId;
    var messageId = cmd.messageId;
    var sandboxPath = cmd.sandboxPath;
    if (!conversationId || !messageId || !sandboxPath) {
      return { type: "apiError", ok: false, error: "missing_interpreter_download_params" };
    }

    var path =
      cmd.path ||
      "/backend-api/conversation/" +
        encodeURIComponent(conversationId) +
        "/interpreter/download?message_id=" +
        encodeURIComponent(messageId) +
        "&sandbox_path=" +
        encodeURIComponent(sandboxPath);

    try {
      var res = await fetch(BASE + path, {
        method: "GET",
        credentials: "include",
        cache: "no-store",
        headers: { authorization: "Bearer " + session.token },
      });
      if (!res.ok) {
        return {
          type: "apiError",
          ok: false,
          error: "interpreter_download_failed",
          status: res.status,
          message: "interpreter_download_failed " + res.status + " " + path,
          detail: { status: res.status, path: path },
        };
      }

      var buf = await res.arrayBuffer();
      var bytes = new Uint8Array(buf);
      var payload = bytesToDownloadPayload(bytes);
      var redirectPath = payload.text ? tryParseDownloadRedirectPath(payload.text) : null;
      if (redirectPath) {
        var redirected = await fetchAuthorizedDownloadBytes(session, redirectPath);
        if (!redirected.ok) {
          return {
            type: "apiError",
            ok: false,
            error: "interpreter_download_failed",
            status: redirected.status,
            message:
              "interpreter_download_failed " +
              redirected.status +
              " " +
              redirectPath,
            detail: {
              status: redirected.status,
              path: redirectPath,
              redirectFrom: path,
            },
          };
        }
        return {
          type: "apiResult",
          ok: true,
          status: redirected.status,
          path: redirectPath,
          redirectFrom: path,
          base64: redirected.base64,
          text: redirected.text,
          byteLength: redirected.byteLength,
        };
      }

      if (isLikelyDownloadStubPayload(bytes, 0)) {
        return {
          type: "apiError",
          ok: false,
          error: "download_stub",
          status: res.status,
          message: "download_stub " + bytes.length + " " + path,
          detail: { status: res.status, path: path, stubByteLength: bytes.length },
        };
      }

      return {
        type: "apiResult",
        ok: true,
        status: res.status,
        path: path,
        base64: payload.base64,
        text: payload.text,
        byteLength: payload.byteLength,
      };
    } catch (e) {
      return {
        type: "apiError",
        ok: false,
        error: "interpreter_download_exception",
        message: e && e.message ? String(e.message) : "unknown",
      };
    }
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
      case "acquireConversationSentinelHeaders":
        return await acquireConversationSentinelHeaders(cmd);
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
      case "downloadInterpreterFile":
        return await downloadInterpreterFile(cmd);
      case "listComposerFileUi":
        return {
          type: "apiResult",
          ok: true,
          json: listComposerFileUi(),
        };
      case "listProjectFileUi":
        return {
          type: "apiResult",
          ok: true,
          json: listProjectFileUi(),
        };
      case "prepareProjectKnowledgeUpload":
        return await prepareProjectKnowledgeUpload(cmd);
      case "confirmProjectKnowledgeUpload":
        return await confirmProjectKnowledgeUpload(cmd);
      case "pollProjectKnowledgeUpload":
        return {
          type: "apiResult",
          ok: true,
          json: pollProjectKnowledgeUpload(cmd),
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
