# ADR: Parallel utility worker outbox

**Status:** Accepted  
**Date:** 2026-07-04  
**Plan:** [utility-worker-parallel-plan.md](../plans/utility-worker-parallel-plan.md)  
**Supersedes (partial):** serial outbox drain in [utility-worker-lane-adr.md](utility-worker-lane-adr.md)  
**Epic:** CMD-446 (parallel utility worker)

---

## Context

The utility worker lane ([CMD-358](https://linear.app/cmd0112/issue/CMD-358)) drains `utility-outbox.json` **serially** via `UtilityWorkerCoordinator._outboxGate`. Ephemeral per-job chats ([CMD-412](https://linear.app/cmd0112/issue/CMD-412)) isolate conversations but still share one WebView and one pump loop.

Authors enqueue multiple manual or auto-spill jobs; wall-clock time grows linearly. Ephemeral jobs are naturally parallel-safe (one ChatGPT conversation per job). The pinned multiplexed `[CGW:worker]` conversation is **not** parallel-safe (API push/pull keyed to `sentMessageId` on one thread).

## Decision summary

| Topic | Decision |
|-------|----------|
| Parallelism model | **Slot pool** — N independent background WebViews, each running one outbox job at a time |
| Outbox | **Claim/lease** per entry (`ClaimedBySlot`, `ClaimedAt`); file lock per adventure on RMW |
| Eligible jobs | **Ephemeral lane only** when `MaxParallelUtilityWorkerJobs > 1` |
| Pinned worker | **Serial slot 0 only** — legacy `UtilityWorkerJobRunner` pinned path when parallel disabled or max slots = 1 |
| Default rollout | `MaxParallelUtilityWorkerJobs = 0` (legacy serial) until verification; then default **3** and remove serial pump |
| Probe lane | Unchanged — `_probeGate` independent of parallel drain |
| SessionHost | Future ([CMD-365](https://linear.app/cmd0112/issue/CMD-365)) may host slots OOP; in-process slot pool is phase 1 |

## Non-goals

- Parallel pinned API push/pull on one conversation
- Per-job utility threads (`[CGW:memory]`, etc.) — still retired
- Parallel play injection (separate ADR; batching only)

## Deprecation (phase 3)

After parallel verification:

| Retired | Replacement |
|---------|-------------|
| `_outboxGate` serial-only pump | Slot-limited parallel pump |
| `PeekNext` without claim | `TryClaimNext(bundle, slotId)` |
| Single `_utilityWorkerWebView` for all concurrent jobs | `UtilityWorkerParallelSlotPool` |
| ADR "one multiplexed conversation for all jobs" | One pinned conversation for probe/setup + serial fallback only |
| Pinned fallback from parallel ephemeral slots | Fail fast; no fallback to shared pin under parallel |

## Related

- [utility-worker-lane-adr.md](utility-worker-lane-adr.md)
- [ephemeral-project-chat.md](../developer/ephemeral-project-chat.md)
- [utility-job-orchestration.md](../developer/utility-job-orchestration.md)
