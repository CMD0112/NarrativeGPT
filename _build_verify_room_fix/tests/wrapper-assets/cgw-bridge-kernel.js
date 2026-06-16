(function (global) {
  "use strict";

  if (global.__cgwBridgeKernel && global.__cgwBridgeKernel.protocolVersion >= 1) {
    return;
  }

  var PROTOCOL_VERSION = 1;
  var channels = Object.create(null);

  function postMessage(msg) {
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(
          typeof msg === "string" ? msg : JSON.stringify(msg)
        );
      }
    } catch (_e) {
      /* ignore */
    }
  }

  function normalizeOutbound(msg, channel) {
    var payload = typeof msg === "object" && msg !== null ? Object.assign({}, msg) : { type: String(msg) };
    if (channel && !payload.channel) payload.channel = channel;
    if (!payload.protocolVersion) payload.protocolVersion = PROTOCOL_VERSION;
    return payload;
  }

  global.__cgwBridgeKernel = {
    protocolVersion: PROTOCOL_VERSION,
    registerChannel: function (channelId, handler) {
      if (!channelId || typeof handler !== "function") return;
      channels[channelId] = handler;
    },
    post: function (channel, msg) {
      postMessage(normalizeOutbound(msg, channel));
    },
    reply: function (channel, id, payload) {
      var body = normalizeOutbound(payload, channel);
      if (id) body.id = id;
      postMessage(body);
    },
    dispatch: function (cmd) {
      if (!cmd || typeof cmd !== "object") return null;
      var channel = cmd.channel || cmd.feature || null;
      if (!channel || !channels[channel]) return null;
      return channels[channel](cmd);
    },
  };
})(typeof globalThis !== "undefined" ? globalThis : window);
