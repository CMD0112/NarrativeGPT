# Context index refresh (`refresh_context_index`) — workflow design

**Status:** Accepted backlog (AIT-T2-E) — not started  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Tracker:** [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md)  
**Related:** [design-ai-tools-context.md](design-ai-tools-context.md) · CMD-381 narrator pointer selection

---

## Decision (2026-07-04)

| Topic | Decision |
|-------|----------|
| Job id | **`refresh_context_index`** |
| v0 delivery | **Rule-based service** on entity/source accept — no AI required |
| v1 delivery | Optional AI job in Design catalog — output **JSON patch** to `context-index.json` |
| Triggers | Entity accept, source edit accept, `propose_entities_file` apply, manual design action |

---

## Role

Maintain `context-index.json` trigger keywords and entity/source linkage when canon changes — improves narrator injection and utility canon slice selection.

---

## v0 rule-based (P2 — no new job id required)

1. For each entity: ensure index contains `id`, `name`, primary aliases
2. For each published source section: map triggers from `ContextPointerResolver` rules
3. Diff preview before write
4. Hook: `EntityReviewService` accept, `SourceEditReviewQueue` accept

---

## v1 AI-assisted (P3)

| Input | Output |
|-------|--------|
| `entities.json` + source headings + current index | **JSON patch** (RFC-style add/remove/replace ops) with rationale |
| User scope prompt | Focus on one NPC or chapter |

Publish via SIO when index is large; inline when small.

---

## Context

| Input | When |
|-------|------|
| `entities.json` | Always |
| `context-index.json` | Current baseline |
| Source file pointers | `world.md`, `plot.md` — heading scan |

No play context.

---

## Implementation priority

| P | Item |
|---|------|
| P2 | `ContextIndexRefreshService` rule-based + accept hooks |
| P3 | Optional AI job + Design catalog row |
| P3 | Link from `audit_canon` warnings |

---

*Last updated: 2026-07-04*
