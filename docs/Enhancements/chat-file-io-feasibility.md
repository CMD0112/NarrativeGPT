# Chat File I/O Feasibility

# Chat file I/O feasibility (archived)

**Status:** **Archived (2026-07-03)** — spike complete; live diagnostic runner **removed**. API attach automation retired. **Utility programmatic file I/O** uses [utility-source-file-io.md](utility-source-file-io.md). Production Play attach uses DOM composer; `ChatFileTransport` layer retained for upload/list/download.

See [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md) and [utility-source-file-io-retired-methodologies.md](utility-source-file-io-retired-methodologies.md).

This document records the feasibility study for **uploading files to chat messages** and **downloading files from chat threads** in ChatGPT Wrapper. It compares the existing API-bridge stack (proven for Project knowledge files) with DOM/WebView hooks.

**Related:** [ChatGPT API Integration](../developer/chatgpt-api-integration.md) · [WebView Bridges](../developer/webview-bridges.md)

---

## Executive summary

| Scenario | API path | DOM / WebView path | Status |
|----------|----------|-------------------|--------|
| Upload bytes to ChatGPT storage | **Proven** (`uploadFile` bridge) | Not used | Production for Project sync |
| Attach files to outgoing play messages | API blocked on **unprovisioned** threads (`http_403`); works in browser on live `/c/{id}` with prepare+sentinel | **Native composer + DOM submit** (default); legacy wrapper uses CDP pre-upload | Production for Play (native default) |
| List files in a conversation thread | **Spike implemented** (`ListConversationFilesAsync`) | Not implemented | Parser covers metadata + asset pointers |
| Download by known `file_id` | **Proven** (`downloadFile` bridge) | N/A | Reused from Project sync |
| Download via browser UI (exports, blobs) | Partial (`fetchBlobUrl` bridge) | **Spike implemented** (`DownloadStarting` → `chat-downloads/`) | Needs manual classification |

**Recommendation:** Proceed **API-first** for upload/list/download. Use **WebView download interception** and **`fetchBlobUrl`** only for assets that never expose a stable `file_id`.

---

## What already existed

Project knowledge files are fully automated via:

- `chatgpt-api-bridge.js` — `uploadFile`, `downloadFile`
- `ChatGptProjectApiService` — orchestration, path candidates, Snorlax attach
- Manual fallback — WPF drag-and-drop to browser (`SourceManagerDialog`)

Chat sends were **text-only** (`content_type: "text"`) via `ChatGptConversationSendService`.

---

## Phase 0 — Diagnostics (archived)

### Multi-lane live runner (CMD-435) — removed 2026-07-03

`LiveChatFileIoRunner` / `run-chat-file-io-diagnostics.ps1` were **deleted 2026-07-03**. Historical lane matrix and gate results: [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md).

**Utility file I/O gate (replacement):**

```powershell
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-utility-source-file-io-diagnostics.ps1 -E2E
```

### Former lanes (archived)

| Lane | Transport | Purpose | Pass criteria |
|------|-----------|---------|---------------|
| `storage` | API | Upload → list → download on known thread | Requires `CGW_CHAT_CONVERSATION_ID` |
| `api-text` | API | Server conversation provisioning (text-only send) | Text send returns server `conversation_id` |
| `api-text` | API | Warmup + attach via `ChatGptChatFileService` (`ApiChatSendTransport`) | `warmup_send_context` + `send_with_attachment_on_server_thread` |
| `api-attach-probe` | API | Regression probe for API attach on **unprovisioned** client-bootstrap threads | **Expected `http_403`** on `send_with_attachment_unprovisioned` |
| `dom` | DOM + API verify | WebView2 + adventure-bridge attach → list → download | DOM send + file listed (flaky in diagnostic host) |
| `playwright` | Playwright + API verify | Headless Chrome composer attach → list → download | Send (`wire_attach=true`) + file listed + download; **set `CGW_CHAT_CONVERSATION_ID`** to server `/c/{uuid}` |
| `all` | Mixed | Full matrix (api-text → probe → playwright → storage) | Each selected lane passes |

Report: `%LocalAppData%\ChatGPTWrapper\chat-file-io-report.{txt,json}` — per-step `lane`, `transport`, `classification` (`pass`, `expected-block`, `fail`).

Env: `CGW_CHAT_GIZMO_ID`, `CGW_CHAT_CONVERSATION_ID` (bare UUID or `g-p-…/c/{uuid}` — normalized at startup), `CGW_CHAT_EXPECT_API_ATTACH_BLOCKED=1` (override probe expectation).

When `CGW_CHAT_CONVERSATION_ID` is set, **`api-text`** skips provision/text-send and runs the server-thread attach re-probe directly (see [CMD-436](https://linear.app/cmd0112/issue/CMD-436)).

**Golden API attach sample:** `tests/ChatGPTWrapper.ApiDiagnostics/Fixtures/api-send-samples/POST_backend-api_f_conversation_attachments.json` (browser HAR 200, `text` + `metadata.attachments` with `source: local`). Live runs seed this into `%LocalAppData%\ChatGPTWrapper\api-send-samples\` before diagnostics execute.

### DevTools / network capture strategy (CMD-436)

| Surface | Role | When to use |
|---------|------|-------------|
| **WebView2 in diagnostics** | `ChatGptApiDiscovery` + `ChatGptApiSendSampleCapture` hook `WebResourceResponseReceived` on the diagnostic host — captures `prepare`, `f/conversation` (incl. attach), seeds `ConversationConduitCache` / `ConversationParentCache` | **Automated** `api-text` / `dom` lanes; always on when bridge registers |
| **Chrome DevTools MCP** | `list_network_requests` / `get_network_request` on a **standalone Chrome** session | **Agent-assisted** one-off captures when reproducing in real Chrome (export to `api-send-samples/` or repo fixtures) |
| **WebView2 CDP** (`CallDevToolsProtocolMethodAsync`) | `DomFileStagingCore` — `DOM.setFileInputFiles` for composer/knowledge targets | **`dom` lane** attach staging (not API send headers) |

`api-text` server-thread path now runs `warmup_send_context` (prefetch parent + conduit) before attach, and `diagnose_attach_gap` on failure (`classification: gap-diagnosis`) listing cache + golden template state.

**2026-07-02:** Shipped `file_token_size` on attach metadata + preserve golden `client_prepare_state: none`. Live shortcut still `http_403` with body shape aligned to browser — remaining gap is **request headers** (sentinel / in-page fetch vs bridge `apiRequest`), not body fields.

**2026-07-02 (sentinel):** `chatgpt-api-bridge.js` installs a fetch tap to capture native `openai-sentinel-*` headers, probes ChatGPT webpack modules for a sentinel builder, and merges headers into `apiRequest` conversation POSTs. `acquireConversationSentinelHeaders` bridge action + `PrefetchSentinelAsync` in warmup. Golden attach fixture now includes `requestHeaders` key names from browser HAR.

**2026-07-02 (fresh sentinel):** Conversation POSTs no longer replay tap-cached sentinel tokens (single-use / anti-replay). `refreshConversationSentinelHeaders` loads page `SentinelSDK`, runs `token()` → `POST /backend-api/sentinel/chat-requirements/finalize`, then clears `__CGW_SENTINEL_CAPTURE__` after each `f/conversation` send. Fetch-tap cache remains a last-resort fallback for warmup probes only.

`ChatGptApiSendSampleCapture` now records `requestHeaders` (wire, from WebView network) and `bridgeDeclaredHeaders` (C# → bridge `apiRequest`) on prepare/send samples. `diagnose_attach_gap` reports header key diff vs golden.

### API attach automation gate (Track A)

**Procedure:** `CGW_CHAT_LANE=api-text` via `run-chat-file-io-diagnostics.ps1` — **3 independent sessions**, no manual composer seed.

| Pass | Fail |
|------|------|
| `sentinel_source` SDK/page-derived in warmup | Document **API attach automation no-go** in this doc |
| `send_with_attachment_on_server_thread` HTTP 200 | Utility worker attach stays DOM-only |
| Golden attach sample not overwritten by 403 | Product: DOM canonical for Play attach |

**Recorded outcome (2026-07-02):** **FAIL** — gate not met; approach **retired**. See [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md) for full state, live run evidence, and re-open criteria.

**Product policy:** DOM canonical for Play attach; API storage round-trip shippable; no further API attach automation work unless re-opened.

### Download + permission logging

`ChatGptWebViewFileDiagnostics` registers on each ChatGPT WebView when the API bridge registers:

| Output | Path |
|--------|------|
| JSONL event log | `%LocalAppData%\ChatGPTWrapper\chat-file-diagnostics.jsonl` |
| Auto-saved downloads | `%LocalAppData%\ChatGPTWrapper\chat-downloads\` |

Events logged:

- `DownloadStarting` — URI, chosen save path
- `downloadFinished` — state, byte count, interrupt reason
- `permissionRequested` — `FileReadWrite` / `ClipboardRead`

On trusted `chatgpt.com` origins, `FileReadWrite` is **auto-allowed** to support DOM file-input experiments.

### Composer file UI probe

Bridge commands (read-only DOM scan):

| Command | Bridge | Returns |
|---------|--------|---------|
| `listComposerFileUi` | `chatgpt-api-bridge.js` | `input[type=file]` nodes + attach button matches |
| `listComposerFileUi` | `adventure-bridge.js` | Same via `cgw-composer-dom.js` when kernel loaded |

C# entry point: `ChatGptChatFileService.ProbeComposerFileUiAsync(core)`.

**How to use:** Enter Play/Browse with a ChatGPT tab, then call `GetChatFileService()?.ProbeComposerFileUiAsync(core)` from diagnostic code or inspect bridge JSON manually.

---

## Phase 1 — API upload spike (implemented)

### Chat attachment upload

`ChatGptProjectApiService.UploadChatAttachmentBytesAsync`:

- Uses `ResolveUploadUseCase` (`multimodal` for images, `my_files` for PDF/text)
- `useProjectLibrary: false`, `skipProjectAttach: true`
- Same register → Azure PUT pipeline as Project upload

`ChatGptChatFileService.UploadChatAttachmentAsync` wraps this into `ChatAttachmentRef`.

### Send with attachments

`ChatGptConversationSendService.SendUserMessageWithAttachmentsAsync` builds a multimodal send body:

```json
{
  "messages": [{
    "content": {
      "content_type": "multimodal_text",
      "parts": ["text", { "content_type": "image_asset_pointer", "asset_pointer": "file-service://file-…" }]
    },
    "metadata": { "attachments": [{ "id": "file-…", "name": "…", "mime_type": "…", "size": … }] }
  }]
}
```

If a captured template exists at `api-send-samples/POST_backend-api_f_conversation_attachments.json`, it is merged instead of the fallback shape.

### Capture workflow

1. Use ChatGPT normally: attach an image/PDF and send a message.
2. `ChatGptApiSendSampleCapture` saves:
   - `POST_backend-api_f_conversation_attachments.json` when the send body contains attachments / asset pointers
   - `POST_backend-api_files.json` / `POST_backend-api_files_library.json` on upload POSTs
3. Re-run programmatic send; compare assistant acknowledgement.

**Go criterion:** Captured attachment send succeeds across three sessions → treat fallback payload as validated or replace with captured template.

### Play thread attachments (native composer — default)

Linked play threads return `http_403` for API attachment sends. Default Play mode uses ChatGPT's **native composer** paperclip:

1. User attaches in the visible composer (ChatGPT handles upload).
2. `cgw-play-compose.js` intercepts Send and posts `cgwComposeSend` with `attachmentsPreStaged: true`.
3. Host runs `PrepareSend` → `adventure-bridge.js` `submitPrompt` with `attachmentsPreStaged` (no CDP staging, no base64 over `PostWebMessage`).

Legacy *wrapper composer* opt-in still uses CDP pre-upload via `PlayComposeNativeUploadService`.

---

## Phase 2 — API download spike (implemented)

### List conversation files

`ConversationFileParser.ExtractFiles(conversationJson)` walks `mapping` nodes and collects:

| Source | Fields |
|--------|--------|
| `metadata.attachments[]` | `id`, `name`, `mime_type` |
| `content.attachments[]` | same |
| `content.parts[]` objects | `asset_pointer` → `file-service://…` |
| `content.parts[]` text | `filecite…` tokens (display markers; not downloadable) |

`ChatGptChatFileService.ListConversationFilesAsync` = `FetchConversationAsync` + parser.

### Download

`ChatGptChatFileService.DownloadConversationFileAsync` reuses `ChatGptProjectApiService.DownloadFileAsync` with optional `gizmoId` + `location` from the parsed ref.

**Go criterion:** Round-trip upload (Phase 1) → list → download bytes match.

**Known gap:** Assistant-generated images may use CDN URLs without `file_id` — use DOM/`fetchBlobUrl` path.

---

## Phase 3 — DOM fallback (partial)

| Technique | Implementation | Notes |
|-----------|----------------|-------|
| `DownloadStarting` handler | `ChatGptWebViewFileDiagnostics` | Saves to `chat-downloads/` when browser has no path |
| `PermissionRequested` | Auto-allow `FileReadWrite` on chatgpt.com | Enables future file-input tests |
| `fetchBlobUrl` bridge command | `chatgpt-api-bridge.js` | Same-origin blob URLs only |
| CDP `setFileInputFiles` | **Not implemented** | Deferred — high fragility; try only if API attach blocked |

**Not implemented:** Programmatic click of attach button + synthetic `DataTransfer` drop.

---

## Architecture

See [chat-file-io-transport-redesign.md](chat-file-io-transport-redesign.md) for the full transport diagram. Summary:

```mermaid
%%{init: {"flowchart":{"nodeSpacing":50,"rankSpacing":56,"padding":16,"subGraphTitleMargin":12,"diagramPadding":8,"htmlLabels":true},"themeVariables":{"fontSize":"13px"}} }%%
flowchart TB
  subgraph app [WPF]
    ChatFile[ChatGptChatFileService]
    Transport[ChatFileTransportRegistry]
    Conv[ChatGptConversationSendService]
    Proj[ChatGptProjectApiService]
    Diag[ChatGptWebViewFileDiagnostics]
  end
  subgraph webview [WebView2 page]
    APIBridge[chatgpt-api-bridge.js]
    PlayBridge[adventure-bridge.js]
  end

  ChatFile --> Transport
  Transport --> Conv
  ChatFile --> Proj
  Diag --> WebView2
  Conv --> APIBridge
  Proj --> APIBridge
  ChatFile -->|"listComposerFileUi"| APIBridge
  PlayBridge -->|"listComposerFileUi"| ComposerDom[cgw-composer-dom.js]
```

File bytes stay on **`chatgpt-api-bridge.js`** (base64, long timeouts). Adventure bridge is for DOM discovery and native composer submit.

---

## Go / no-go matrix

| Criterion | Go | No-go / fallback |
|-----------|-----|------------------|
| Captured attachment send stable ×3 sessions | Use API attach in product | DOM file-input or manual-only |
| `file_id` found in conversation GET | API list + download | `DownloadStarting` + blob fetch |
| Download works for assistant assets | Full story | Document per asset type limits |
| DOM attach success ≥80% after reload | Keep as fallback | API-only |

---

## API surface (spike)

| Type | Members |
|------|---------|
| `ChatGptChatFileService` | `UploadChatAttachmentAsync`, `SendWithAttachmentsAsync`, `ListConversationFilesAsync`, `DownloadConversationFileAsync`, `DownloadConversationFileToPathAsync`, `ProbeComposerFileUiAsync`, `FetchBlobUrlAsync` |
| `ChatGptConversationSendService` | `SendUserMessageWithAttachmentsAsync` |
| `ChatGptProjectApiService` | `UploadChatAttachmentBytesAsync` |
| `MainWindow` | `GetChatFileService()` |

---

## Manual validation checklist

1. Open ChatGPT tab → attach file manually → confirm `POST_backend-api_f_conversation_attachments.json` appears under `api-send-samples/`.
2. Call upload + send with attachments on a test thread → assistant acknowledges file.
3. `ListConversationFilesAsync` returns the uploaded file id.
4. Download bytes match local file.
5. Trigger a ChatGPT UI download → entry in `chat-file-diagnostics.jsonl` and file in `chat-downloads/`.
6. Run `ProbeComposerFileUiAsync` → note selectors for attach button and hidden file inputs.

---

## Out of scope

- Replacing Project source sync (`ProjectSourceSyncService`)
- OpenAI official API / API keys
- Clipboard image paste automation
- Product UI (play composer attach button) — Phase 4
