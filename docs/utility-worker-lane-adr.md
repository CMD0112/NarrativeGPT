# ADR: Utility worker lane (dual-lane utility orchestration)

**Status:** Accepted  
**Date:** 2026-06-25  
**Plan:** [utility-worker-lane-plan.md](utility-worker-lane-plan.md)  
**Builds on:** [play-thread-utility-orchestration-adr.md](play-thread-utility-orchestration-adr.md) (CMD-326) · [utility-delivery-pivot-adr.md](utility-delivery-pivot-adr.md) (CMD-248 lessons)

---

## Context

CMD-248 retired per-job dedicated utility threads because push/pull failed too often (DomOnly fallback, unregistered conversations, heuristic capture). CMD-326 adds injection-first utility on the play thread but cannot provide true parallel background I/O.

## Decision summary

| Topic | Decision |
|-------|----------|
| Lanes | **Play injection** (auto/light) + **Utility worker** (manual/heavy/overflow) |
| Worker model | **One** multiplexed worker conversation per adventure (`[CGW:worker]`) |
| Transport | **API-only after registration**; one DOM bootstrap per new worker chat registers the conversation, then API push/pull |
| Pull | Always keyed to `sentMessageId` from API push |
| Queue | Durable `utility-outbox.json`; resumable run state machine |
| Isolation | **UtilityWorkerCoordinator** — probe lane and outbox lane independent; one outbox job per gate hold; batched UI refresh |
| Capability gate | Ping probe must pass before worker jobs run |
| Design jobs | Unchanged — design thread only |

## Lane routing

| Trigger | Default lane |
|---------|--------------|
| Auto post-turn (injection-first) | Play injection |
| Auto overflow (`MaxUtilitySectionsPerSend`) | Worker when `AutoSpillToWorker` + capabilities green |
| Manual companion jobs | Worker when capabilities green; else play injection / legacy inline |
| Heavy (`continuity_check`, `process_turn`) | Worker preferred |
| Player-utility (CONTINUE) | Play injection |
| Design jobs | Design thread |

## Non-goals

- Per-job utility threads (`[CGW:memory]`, etc.)
- Hidden utility WebView
- DomOnly as production fallback for **ongoing jobs** (worker uses API once registered)
- Restoring `UtilityDeliveryMode.SeparateThread` per-job mode

Worker setup and probe use the same transport as generation jobs (`UtilityWorkerTransportService` → `PlaySendDeliveryPolicy`). A manually created Project chat is registered via DOM ping before API verification.

**Coordinator (2026-06):** `UtilityWorkerCoordinator` replaces shell-level `_utilityJobGate`. Verify uses `_probeGate` only; outbox pump acquires `_outboxGate` per job so UI never waits on a full batch drain. Play send passes an explicit `TurnService` on the delivery request so worker tab setup cannot corrupt play bridge routing.

## Related

- [utility-job-orchestration.md](utility-job-orchestration.md)
- [adventure-thread-registry.md](adventure-thread-registry.md)
