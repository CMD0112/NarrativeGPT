# Retired utility file I/O methodologies

**Canon (2026-07-03):** [utility-source-file-io.md](utility-source-file-io.md) — Project sources publish → filename pointer → ephemeral utility thread → delimited text scrape → delete ephemeral thread.

This document records **explored but retired** approaches for programmatic utility file loops. Code for diagnostic-only spikes has been **removed from the repo**; production Play attach and manual utility reference-file flows are documented separately where they still apply.

**Related issues:** [CMD-441](https://linear.app/cmd0112/issue/CMD-441) (epic), [CMD-435](https://linear.app/cmd0112/issue/CMD-435)–[CMD-437](https://linear.app/cmd0112/issue/CMD-437) (chat attach spike), [CMD-411](https://linear.app/cmd0112/issue/CMD-411) (utility worker DOM attach).

---

## Canon (keep)

| Step | Mechanism |
|------|-----------|
| Upload | `UtilitySourceFileIoService.PublishBytesToProjectAsync` / `PublishLocalFileToProjectAsync` |
| Reference | `BuildSourceRetrieveLine` + `BuildTaskScopedPointerLine` in `[[cgw:sources]]` TASK-SCOPED |
| Send | `EphemeralProjectChatService.RunOnceAsync` (create → send → capture → delete) |
| Output | `TryExtractDelimitedBlock` / `TryExtractE2eOutput` |
| Gate | `run-utility-source-file-io-diagnostics.ps1 -E2E` — **12/12 live pass** (2026-07-03) |
| Production job | `propose_entities_file` / `EntitiesFileRevisionService` |

---

## Retired — code removed

### Chat file I/O diagnostic lanes (CMD-435)

**What we tried:** Multi-lane live runner for chat-thread attach: API attach (`api-text`, `api-attach-probe`), WebView2 DOM attach (`dom`), Playwright headless attach (`playwright`), storage round-trip (`storage`).

**Why retired for utility file I/O:** Utility text jobs do not use chat-thread multimodal attach. API attach on project threads failed (`http_403`, sentinel unavailable). DOM attach was flaky in the diagnostic WebView (`submit_disabled`). Playwright attach was validated for **chat** threads only and was never wired into the utility source loop.

**Removed:**

| Area | Former paths |
|------|----------------|
| Live runner | `tests/.../Live/LiveChatFileIoRunner.cs`, `LiveChatFileIoTests.cs` |
| Orchestrator | `tests/.../Live/ChatFileIo/*` |
| Reporting | `tests/.../Reporting/ChatFileIoReport.cs` |
| Script | `tests/.../scripts/run-chat-file-io-diagnostics.ps1` |
| Unit tests | `ChatFileIo*Tests.cs`, `ChatFileIoApiGapDiagnostics*`, `ChatFileIoApiSendSampleSeeder*` |
| Playwright chat attach | `HeadlessBrowserChatAttachmentSend.cs`, `HeadlessBrowserChatFileDownload.cs`, `ChatComposerDomScript.cs` |

**Preserved (different domain):** `ChatFileTransport/` layer, `ChatGptChatFileService` upload/list/download, Play DOM attach via `AdventureTurnService`, bridge sentinel observability. See [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md).

### Chat-downloads output scrape (`propose_entities_file`)

**What we tried:** After utility reply, read `entities.json` from `%LocalAppData%\ChatGPTWrapper\chat-downloads\` when delimited inline output was missing.

**Why retired:** Non-deterministic; depends on WebView download side effects instead of the delimited output contract. Canon requires scrape from assistant text.

**Removed:** `EntitiesFileRevisionService.TryLoadRecentDownloadedEntities` and all call sites.

---

## Retired for programmatic utility file I/O — code retained elsewhere

These remain in the repo for **Play attach**, **manual utility QA**, or **small inline refs** — not for the programmatic utility file loop.

| Methodology | Still used for | Doc |
|-------------|----------------|-----|
| **DOM composer attach** | Play images; manual utility reference files (binary); `ForceUtilityWorkerDomAttach` QA | [utility-worker-attachment-delivery.md](utility-worker-attachment-delivery.md) |
| **Packet embed** (`=== FILE: … ===`) | Small text refs; `extract_entities` with user-selected files | `UtilityReferenceAttachmentPolicy` |
| **API attach send** | Not routed in product; transport kept for observability | [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md) |
| **Interpreter sandbox download** | Optional output when model exports `/mnt/data/…` | `ChatGptProjectApiService.DownloadInterpreterSandboxFileAsync` |
| **Conversation file list/download** | Play thread file ops — not utility source verify | `ChatGptChatFileService` |

**Do not** use chat-thread attach or chat-download folders to verify Project source publishes. Verify via **project source download** (`UtilitySourceFileIoService` publish pipeline).

---

## Re-open criteria

Revisit a retired lane only if:

- ChatGPT exposes stable programmatic attach on project threads without sentinel gaps, **and** product needs chat-thread file I/O (not sources pointer), or
- Delimited scrape fails consistently for a job class and a new output contract is defined, or
- Binary utility input cannot be published to Project sources and DOM/Playwright attach is explicitly re-scoped under a new epic.

---

*Last updated: 2026-07-03 — post E2E gate with ephemeral delete.*
