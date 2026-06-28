# ADR: Play-thread utility orchestration (CMD-327)

**Status:** Accepted  
**Date:** 2026-06-25  
**Epic:** [CMD-326](https://linear.app/cmd0112/issue/CMD-326)  
**Plan:** [play-thread-utility-orchestration-plan.md](play-thread-utility-orchestration-plan.md)  
**Supersedes (partial):** inline separate-send as default for play-thread utility jobs

---

## Context

After CMD-248 retired dedicated utility threads, play jobs still post **separate composer turns** via `RunInlineJobAsync`. CMD-326 moves utility work to **injection-first** transport on the play thread: hidden packet sections, schema-shaped replies, structured retrieval, and consistent hiding.

Design-thread jobs (`propose_json_import`, `design_extract_step`, etc.) remain on the design WebView per [utility-delivery-pivot-adr.md](utility-delivery-pivot-adr.md).

---

## Decision summary

| Topic | Decision |
|-------|----------|
| Auto job timing | **Next player send** — scheduler enqueues after turn *N*; jobs drain into turn *N+1* packet |
| Max jobs per packet | **2** (`MaxUtilitySectionsPerSend`, adventure setting) |
| Manual jobs | **Immediate utility-only send** on button click (hidden sections, no player line); not queued behind unrelated play |
| Player-utility | CONTINUE / scene advance stay **player-line** actions (`InjectedOnly`); not `[[cgw:utility]]` sections |
| Utility tag | `[[cgw:utility job="…" channel="auto\|manual" v="1"]]` sibling sections **before** context in merged packet |
| Response tag | `[[cgw:utility-response job="…" v="1"]]` — unchanged; schema contract appended in job body |
| Story context in injection | **Reference-first** — omit redundant transcript slices when play thread already has turns |
| Retrieval trigger | **Post-send** on captured assistant text (`CompletePlayTurnAfterSendAsync`); strip `utility-response` from narrator turn text |
| Persistence | `adventures/{id}/utility-results/{runId}.json` + `utility-results-index.json`; JSONL tail retained |
| Rollout | `PlayUtilityInjectionMode`: default **`LegacyInlineSend`**; opt-in **`InjectionFirst`** per adventure |
| Failure (auto) | Jobs stay queued if packet not sent; partial apply + `UtilityJobLastErrors` on parse/apply failure |

---

## 1. Auto vs manual execution

### Auto (background)

1. Author sends play turn; turn is accepted.
2. `GenerationJobScheduler.GetJobsAfterTurn` yields job ids (memories, entities, summary, …).
3. When `PlayUtilityInjectionMode == InjectionFirst`, **enqueue** on `AdventureMetadata.PlayUtilityInjectionQueue` — do **not** call `RunInlineJobAsync`.
4. On the **next** `PromptInjectionService.PrepareSend`, drain up to `MaxUtilitySectionsPerSend` jobs into `[[cgw:utility channel="auto"]]` sections prepended to the merged packet.
5. Record drained jobs in `LastDispatchedUtilityJobs` for retrieval after the assistant reply.

`SameTurnFollowUp` (inject on the same send as the triggering turn) is **not** implemented — it complicates turn pairing and duplicates context already in the triggering packet.

### Manual (companion / AI Actions)

1. Author clicks Extract entities, Propose memories, etc.
2. Build one utility section (`channel="manual"`) with reference-first context.
3. Send **utility-only packet** immediately via play thread DOM send (no visible player line).
4. Retrieve and apply on capture — same pipeline as bundled auto jobs.

Manual jobs are **not** added to the auto queue unless the author sends a normal play turn before the immediate send completes (gate prevents overlap).

---

## 2. Player-utility vs background utility

| Class | Transport | Counts as play turn? |
|-------|-----------|----------------------|
| Player turn | Player line + context | Yes |
| Background auto/manual | `[[cgw:utility]]` sections | No |
| Player-utility (CONTINUE, etc.) | Injected player instruction | Yes (narrator reply) |
| Utility response | `[[cgw:utility-response]]` in assistant body | No |

Data jobs (memories, entities, summary) never use player-utility channel.

---

## 3. Tag schema

```text
[[cgw:utility job="propose_memories" channel="auto" v="1"]]
… job body + schema contract …
[[/cgw:utility]]

[[cgw:utility-response job="propose_memories" v="1"]]
… JSON or plain text …
[[/cgw:utility-response]]
```

- `channel`: `auto` | `manual` (required on utility requests in injection-first mode).
- `v`: matches `ContextTagFormat.UtilityTagSchemaVersion` (currently `1`).
- Utility sections are **siblings** prepended to the normal play packet (before attachment manifest / guidance / context).

---

## 4. Schema registry

`UtilityResponseSchemaRegistry` centralizes response contracts:

- Delegates JSON array/object expectations to `GenerationJobHandlers.ExpectsJsonArrayResponse` / `ExpectsJsonObjectResponse`.
- Appends `AppendInlineUtilityResponseContract` to every injection-first job body.
- Validates captured responses before `GenerationJobHandlers.ApplyResponse`.

Per-job JSON Schema files are **deferred**; v1 uses existing handler expectations + lenient JSON parse.

---

## 5. Hiding contract

| Surface | Mechanism |
|---------|-----------|
| Thread metadata | `IsUtility`, `HiddenInDisplay`, `UtilityChannel` on `ThreadMessageRecord` |
| DOM (play) | Existing `RegisterUtilityHideAsync` + `HideInlineUtilityDuringPlay` |
| Continuous view | Filter `IsUtility` / `utility-response` tagged blocks (CMD-331 extends) |
| File cards | CMD-331 — hide download cards linked to utility message ids |

Injection-first utility **requests** are never shown as author player lines in CV.

---

## 6. Retrieval

1. After successful play send, `PlayUtilityRetrievalService.ProcessAssistantResponse` runs on full assistant text.
2. Match `utility-response` blocks to `LastDispatchedUtilityJobs` by `job` attribute (fallback: single block → sole pending job).
3. Validate → `ApplyResponse` → `UtilityJobResultStore.Save` → `UtilityParseLogService.Append` → `ThreadMetadataService.RecordUtilityExchange`.
4. `StripUtilityResponsesForNarrator` removes response tags before `TurnTimelineService.AcceptTurn`.

---

## 7. Migration and feature flag

```csharp
public enum PlayUtilityInjectionMode
{
    LegacyInlineSend,  // default — RunInlineJobAsync
    InjectionFirst,    // enqueue + packet injection + retrieval service
}
```

Adventures with `UtilityDeliveryMode != InlinePlayThread` are unaffected (legacy path retired by CMD-248).

---

## 8. Deferred

- Per-job JSON Schema files and strict validation (CMD-330 follow-up).
- File-card hiding selectors (CMD-331).
- Player-utility channel tagging in metadata (CONTINUE uses existing injected player line).
- `SameTurnFollowUp` auto timing.
- Default flip to `InjectionFirst` after epic QA sign-off.

---

## Appendix — key files

| Layer | Files |
|-------|-------|
| Orchestration | `PlayUtilityInjectionService.cs`, `PlayUtilityRetrievalService.cs`, `MainWindow.GenerationJobs.cs` |
| Packet | `PromptInjectionService.cs`, `ContextTagFormat.cs` |
| Jobs | `GenerationJobService.cs`, `GenerationJobHandlers.cs`, `GenerationJobScheduler.cs` |
| Store | `UtilityJobResultStore.cs`, `UtilityParseLogService.cs` |
| Metadata | `ThreadMetadataService.cs`, `ThreadMessageRecord.cs` |
| Send guard | `PlayInjectionSendGuard.cs` |
