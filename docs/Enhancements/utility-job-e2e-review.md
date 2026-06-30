# Utility job E2E review — bottom-up (CMD-390)

**Status:** Living review (2026-06-28)  
**Epic:** [CMD-390](https://linear.app/cmd0112/issue/CMD-390)  
**ADR:** [utility-job-context-assembly-adr.md](../utility-job-context-assembly-adr.md)  
**Design track:** [utility-job-context-assembly.md]()

Grounded in codebase walk-through plus extended diagnostics from real sessions (including utility worker setup/pin work in [agent transcript b49712e7](https://github.com/cmd0112/chatgpt-wrapper)).

**Inference routing (Ollama vs ChatGPT):** [utility-inference-routing-tracker.md]()

---

## Executive summary

Utility execution is **three parallel pipelines** sharing `GenerationJobHandlers.BuildJobPrompt` + `ApplyResponse`, but diverging on routing, context assembly, transport, and retrieval.

| Lane | Context (v1) | Retrieval |
|------|--------------|-----------|
| **Worker outbox** | Assembler + worker lore + canon slices; self-contained story block | `UtilityMessagePullService` — poll by `sentMessageId` |
| **Play injection bundled** | Assembler bundled sync; snapshot dedup; no story block | `PlayUtilityRetrievalService` on assistant reply |
| **Legacy inline** | Assembler via `GenerationJobService` | DOM capture on play thread |

**CMD-390 v1 landed (2026-06-28).** Remaining: manual QA on 50+ play turns; CMD-399 semantic synergy (icebox); retire legacy `ApplyReferenceFirstDefaults` when flag-off path removed.

---

## Prerequisites already landed (transcript b49712e7)

These are **load-bearing for context work** — jobs never reach assembly if they fail earlier.

| Fix | Component | Relevance to CMD-390 |
|-----|-----------|----------------------|
| **`TryReconcilePinFromCapabilities`** | `UtilityWorkerPinService` | AI Tools enqueue requires **pin**, not just green caps. Reconcile on load, Threads evaluate, probe, enqueue. |
| **Metadata save preservation** | `AdventureStore.PreserveThreadRegistryFromDisk`, `UtilitySessions` merge | Stale play-only saves no longer drop worker pin — worker jobs stay routable. |
| **Setup UI alignment** | `UtilityWorkerSetupCopy.FormatStepVerified` | Green verify + missing pin now surfaces explicit “pin worker chat” — reduces false “ready”. |

**Still operational (not CMD-390 scope):**

- Recurring **`http_403`** on API send/pull → worker uses **DOM / off-screen** path; jobs can take minutes on large packets.
- Auto **Create worker chat** often hits `project_chat_not_ready` (ChatGPT UI lands on homepage); manual pin remains fallback.
- **`submitPrompt` / composer restore** bug in `adventure-bridge.js` affects **play DOM fallback**, not off-screen worker tab — bundled play path only.

---

## Bottom-up component map

```
Trigger (auto post-turn | AI Actions | spill)
  → UtilityJobRouter
  → [Worker] UtilityOutboxService → UtilityWorkerCoordinator → UtilityWorkerOrchestrator
  → [Play inj] PlayUtilityInjectionQueue → PrepareSend → BuildUtilitySection
  → [Inline] GenerationJobService.RunInlineJobAsync
  → UtilityJobContextAssembler (worker only today) / legacy builders
  → GenerationJobHandlers.BuildJobPrompt
  → Push (UtilityWorkerTransportService | play DOM)
  → Pull (UtilityMessagePullService | PlayUtilityRetrievalService)
  → ApplyResponse → UtilityJobResultStore
```

### Worker lane detail

1. **Coordinator** passes `playCore` from `GetPlayWebView()` for transcript capture; worker tab is off-screen (`EnsureWorkerWebViewBackgroundHostedAsync`).
2. **Orchestrator** state: `Queued → Pushed → Pulling → Complete|Failed`; resume after crash uses `SentMessageId`.
3. **Assembler (CMD-392)** when enabled: worker solo dedup from **built story block**, not play-thread turn count; falls back to `BuildPreviewFromLocal` when play WebView unavailable.
4. **Push** wraps `[[cgw:utility … channel="worker"]]`, schema contract appended.
5. **Pull** validates schema, unwraps `utility-response`, saves run record with message ids.

**Observed in logs (session `f2f025f21f3e`):** `process_turn` — 6747-char packet, DOM ~2.5 min, 5564-char response, then **`no_proposals_parsed`** (JSON shape ≠ parser contract). Transport and retrieval **succeeded**; model output / handler duplication may have contributed.

---

## Play injection bundled — critical gap for CMD-393

`BuildUtilitySection` uses `ApplyReferenceFirstDefaults`:

- Sets `OmitRedundantJobTurnSlices` / `StoryContextHasTranscript` from **play log turn count**
- **No** `StoryContextBlock`
- Built **before** narrator packet is merged; no snapshot of what the play half will include

Utility sections are **prepended** to the merged play packet. Dedup target must be **play portion content this send**, not thread history.

---

## Response retrieval — stable; do not refactor for CMD-390

| | Worker | Play injection |
|--|--------|----------------|
| Correlation | `sentMessageId` | `LastDispatchedUtilityJobs` + job id in response tag |
| Multi-job | One outbox entry | Up to `MaxUtilitySectionsPerSend` |
| Strip from narrator | N/A | `StripUtilityResponsesForNarrator` |

CMD-397 should **persist `UtilityContextManifest` on `UtilityJobRunRecord`**, not change pull logic.

**Related (transcript):** proposal **rehydrate** from `utility-results/` when review queues clobbered — separate from context assembly but affects “job succeeded” UX after apply.

---

## Issue-by-issue review (revised)

### CMD-392 — Assembler (in progress)

| AC | Status |
|----|--------|
| Worker path | Done |
| Play bundled / manual / inline | **Not migrated** |
| Tests per lane | Worker only |

**Transcript constraint:** Under `WorkerOnly`, finishing worker path first was correct; play migration still required before bundled dedup.

---

### CMD-393 — Lane-aware dedup

**Must:**

1. Build `PlayPacketContextSnapshot` after narrator assembly in `PrepareSend` (reuse `InjectionSectionManifestBuilder` signals).
2. Pass snapshot into `BuildAndDrainUtilitySections` / assembler for `AutoBackground`.
3. Never apply play-thread omit heuristics on worker solo.

**Field note:** Large DOM packets (~7k chars) — bundled dedup reduces waste; worker dedup avoids empty job cores when pin/caps were broken (now mitigated by pin reconcile).

---

### CMD-394 — Worker lore channel

Worker conversation **cannot** inherit Project RAG from narrator sends. Required for `continuity_check` / heavy jobs on isolated chat.

Use lexical `ContextPointerResolver` only (no CMD-381 block).

**Field note:** With DOM-only delivery, lore block adds bytes — keep task-scoped, not full ALWAYS RETRIEVE set.

---

### CMD-395 — Lexical canon slices

Replace static profile caps with scope-driven excerpts. **`MaxTurnPairs = 0` in profiles means unlimited** in `TranscriptFilterService` — document when refactoring.

**Field evidence:** `process_turn` failure had valid response but wrong JSON keys — thinner, task-focused context + CMD-396 dedup may improve schema adherence (not guaranteed; parser/guide may need separate follow-up).

---

### CMD-396 — Handler consolidation

`BuildContinuityCheckPrompt` still duplicates summary/state/transcript/entity index when story block present.

**Do after CMD-393** so omit flags are trustworthy on bundled path.

---

### CMD-397 — Preview manifest

- `BuildLiveStoryContextPreviewAsync` must use assembler (same as production).
- Persist manifest on `UtilityJobRunRecord`.
- Show **pin state + caps green + lane** (transcript showed UI split between these).

---

### CMD-398 — Docs

This document + ADR + `prompt-construction-guide.md` utility section.

---

### CMD-399 — Optional semantic (icebox)

Unchanged. Promote after CMD-381.

---

## Recommended order (unchanged, with gates)

1. Finish **CMD-392** play-path migration  
2. **CMD-393** snapshot + bundled dedup  
3. **CMD-396** handler thin  
4. **CMD-394** worker lore  
5. **CMD-395** lexical slices  
6. **CMD-397** preview + run metadata  
7. **CMD-398** doc sync  

**Out of epic but affects E2E success rate:**

- API `http_403` root cause (transport)  
- `process_turn` schema/parser tolerance (apply path)  
- Composer DOM restore on play send (`adventure-bridge.js`)  

---

## CMD-392 acceptance criteria (revised checklist)

- [x] Worker lane uses assembler  
- [ ] `PlayUtilityInjectionService.BuildUtilitySection` uses assembler + snapshot (CMD-393)  
- [ ] `RunInlineJobAsync` uses assembler (play utility-only rules)  
- [ ] Unit tests: worker solo, bundled dedup, play utility-only  
- [ ] Preview uses assembler (CMD-397)  

---

*Last updated: 2026-06-28 — incorporates extended diagnostics + worker pin reconciliation from field sessions.*
