(function (global) {
  "use strict";

  function parseJsonSafe(text) {
    if (!text) return null;
    try {
      return JSON.parse(text);
    } catch (_e) {
      return null;
    }
  }

  function isAssistantMessage(message) {
    if (!message || typeof message !== "object") return false;
    var author = message.author;
    return author && String(author.role || "").toLowerCase() === "assistant";
  }

  function partsFromContent(content) {
    if (!content || typeof content !== "object") return null;
    if (Array.isArray(content.parts)) return content.parts.slice();
    return null;
  }

  function textFromParts(parts) {
    if (!parts || !parts.length) return "";
    return parts
      .map(function (p) {
        return typeof p === "string" ? p : "";
      })
      .join("");
  }

  function setPartsText(parts, text) {
    if (!parts || !parts.length) return [text || ""];
    parts[0] = text || "";
    return parts;
  }

  function ensureAssistantState(state) {
    if (!state.parts) state.parts = [""];
    if (!state.assistantMessageId) state.assistantMessageId = null;
    if (state.streamComplete == null) state.streamComplete = false;
    return state;
  }

  function applySnapshot(state, message) {
    ensureAssistantState(state);
    if (!isAssistantMessage(message)) return;
    if (message.id) state.assistantMessageId = String(message.id);
    var parts = partsFromContent(message.content);
    if (parts) state.parts = parts.slice();
  }

  function applyPatch(state, patch) {
    ensureAssistantState(state);
    if (!patch || typeof patch !== "object") return;

    if (patch.v && patch.v.message && isAssistantMessage(patch.v.message)) {
      applySnapshot(state, patch.v.message);
      return;
    }

    if (patch.message && isAssistantMessage(patch.message)) {
      applySnapshot(state, patch.message);
      return;
    }

    var op = patch.o || patch.operation;
    var path = patch.p || patch.path || "";
    var value = patch.v;

    if (op === "append" && typeof path === "string" && path.indexOf("/message/content/parts/0") >= 0) {
      state.parts = setPartsText(state.parts, textFromParts(state.parts) + String(value || ""));
      return;
    }

    if (op === "replace" && typeof path === "string" && path.indexOf("/message/content/parts/0") >= 0) {
      state.parts = [String(value || "")];
      return;
    }

    if (op === "add" && value && value.message && isAssistantMessage(value.message)) {
      applySnapshot(state, value.message);
      return;
    }

    if (value && typeof value === "object" && value.message && isAssistantMessage(value.message)) {
      applySnapshot(state, value.message);
    }
  }

  function applyEventObject(state, obj) {
    if (!obj || typeof obj !== "object") return;

    var type = obj.type || obj.event || "";
    if (
      type === "message_stream_complete" ||
      type === "conversation_stream_complete" ||
      type === "stream_end"
    ) {
      state.streamComplete = true;
    }

    if (obj.conversation_id && !state.conversationId) {
      state.conversationId = String(obj.conversation_id);
    }

    if (obj.message && isAssistantMessage(obj.message)) {
      applySnapshot(state, obj.message);
    }

    if (obj.v) {
      if (typeof obj.v === "string" && (obj.o === "append" || obj.operation === "append")) {
        applyPatch(state, obj);
      } else if (Array.isArray(obj.v)) {
        for (var i = 0; i < obj.v.length; i++) applyPatch(state, obj.v[i]);
      } else if (typeof obj.v === "object") {
        if (obj.v.message) applySnapshot(state, obj.v.message);
        else applyPatch(state, obj);
      }
    } else {
      applyPatch(state, obj);
    }
  }

  function parseSseChunk(state, chunkText) {
    ensureAssistantState(state);
    if (!chunkText) return state;

    var blocks = String(chunkText).split(/\r?\n\r?\n/);
    for (var b = 0; b < blocks.length; b++) {
      var block = blocks[b];
      if (!block) continue;

      var lines = block.split(/\r?\n/);
      for (var i = 0; i < lines.length; i++) {
        var line = lines[i];
        if (!line || line.indexOf("data:") !== 0) continue;

        var payload = line.slice(5).trim();
        if (!payload) continue;
        if (payload === "[DONE]") {
          state.streamComplete = true;
          continue;
        }

        var obj = parseJsonSafe(payload);
        if (obj) applyEventObject(state, obj);
      }
    }

    return state;
  }

  async function readConversationStream(reader, decoder, onChunk) {
    var buffer = "";
    while (true) {
      var read = await reader.read();
      if (read.done) break;
      buffer += decoder.decode(read.value, { stream: true });
      if (onChunk) onChunk(buffer);
    }
    buffer += decoder.decode();
    return buffer;
  }

  function finalizeParseResult(state) {
    ensureAssistantState(state);
    var text = textFromParts(state.parts).trim();
    return {
      assistantText: text || null,
      assistantMessageId: state.assistantMessageId || null,
      conversationId: state.conversationId || null,
      streamComplete: !!state.streamComplete,
    };
  }

  global.__cgwConversationStream = {
    parseSseChunk: parseSseChunk,
    readConversationStream: readConversationStream,
    finalizeParseResult: finalizeParseResult,
    parseSseText: function (text) {
      var state = { parts: [""], streamComplete: false };
      parseSseChunk(state, text);
      return finalizeParseResult(state);
    },
  };
})(typeof globalThis !== "undefined" ? globalThis : window);
