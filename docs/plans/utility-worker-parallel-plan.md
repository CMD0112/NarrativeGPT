# Parallel Utility Worker — Implementation Plan

**ADR:** [utility-worker-parallel-adr.md](../adr/utility-worker-parallel-adr.md)

Three-phase delivery aligned with product goal: build → verify → deprecate serial path.

---

## Phase 1 — Parallel architecture (build)

| Step | Deliverable | Files |
|------|-------------|-------|
| 1.1 | Outbox claim/lease + per-adventure file lock | `UtilityOutboxEntry`, `UtilityOutboxService` |
| 1.2 | Parallel policy (`MaxParallelUtilityWorkerJobs`) | `AdventureMetadata`, `UtilityWorkerParallelPolicy`, settings merge |
| 1.3 | Slot pool (background STA WebViews, cookie sync) | `UtilityWorkerParallelSlotPool`, `UtilityWorkerParallelSlotHost` |
| 1.4 | Coordinator parallel pump | `UtilityWorkerCoordinator` |
| 1.5 | Host contract + MainWindow wiring | `IUtilityWorkerHost`, `MainWindow.UtilityWorker*.cs` |
| 1.6 | `RunClaimedAsync` entry point | `UtilityWorkerJobRunner` |

**Gate:** `MaxParallelUtilityWorkerJobs > 1` requires ephemeral lane (`UseEphemeralUtilityWorkerChat` or transition-catalog force).

## Phase 2 — Verification

| Step | Deliverable |
|------|-------------|
| 2.1 | Unit: claim races, stale lease reclaim, policy caps | `UtilityOutboxParallelTests.cs` |
| 2.2 | Unit: coordinator schedules N concurrent claims | `UtilityWorkerCoordinatorParallelTests.cs` |
| 2.3 | Logged diagnostics: two+ jobs drain with distinct `RunId` overlap | ApiDiagnostics logged test |
| 2.4 | Manual QA: enqueue 3 manual jobs, confirm overlapping status + distinct ephemeral chats | Linear **Needs Manual QA** |

**Exit criteria:** All automated tests green; manual QA evidence attached to epic; no outbox corruption under concurrent enqueue + drain.

## Phase 3 — Deprecate serial path

| Step | Deliverable |
|------|-------------|
| 3.1 | Default `MaxParallelUtilityWorkerJobs` to **3** | `AdventureMetadata` |
| 3.2 | Remove serial-only pump branch | `UtilityWorkerCoordinator` |
| 3.3 | Update utility-worker-lane ADR (one conversation → probe/fallback only) | `docs/adr/utility-worker-lane-adr.md` |
| 3.4 | Remove `PeekNext` from hot path; keep as test helper if needed | `UtilityOutboxService` |
| 3.5 | Play settings UI for max parallel slots | Play settings dialog | Done — AI Tools → Concurrent utility worker jobs |

**Gate:** Phase 2 sign-off required before phase 3 merge.

---

## Settings

| Field | Default (phase 1) | Default (phase 3) |
|-------|-------------------|-------------------|
| `MaxParallelUtilityWorkerJobs` | `0` (serial) | `3` |

`0` or `1` → legacy serial coordinator path. `2`–`4` → parallel ephemeral slot pool.

---

*Last updated: 2026-07-04*
