# Canon audit (`audit_canon`) — workflow design

**Status:** Accepted backlog (AIT-T2-D) — not started  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Tracker:** [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md)  
**Context:** [design-ai-tools-context.md](design-ai-tools-context.md)

---

## Decision (2026-07-04)

| Topic | Decision |
|-------|----------|
| Job id | **`audit_canon`** |
| Catalog | Design AI Tools |
| Lane | Ephemeral worker + SIO input |
| Output | JSON `{ warnings: [...] }` — same family as `continuity_check` |
| Review UX | **Dismiss-only** + links to fix jobs |

---

## Role

Pre-play **holistic consistency check** across scenario, published sources, entities, and context-index — **without play transcript**. Catches structural problems before first turn.

Distinct from `continuity_check` (session play QA) and `propose_source_edits` (authoring edits).

---

## Check categories

| Category | Example |
|----------|---------|
| Scenario ↔ entities | NPC in scenario missing from `entities.json` |
| Scenario ↔ sources | `plot.md` contradicts `PlotEssentials` |
| Entities ↔ context-index | Entity lacks trigger keywords for injection |
| Sources ↔ sources | `cast` implied in `world.md` but empty in entities |
| Format | Markdown sections missing required canon-format headers |

---

## Context

| SIO input | Purpose |
|-----------|---------|
| `scenario.json` | Registry baseline |
| `entities.json` | World model |
| `context-index.json` | Trigger coverage |
| `world.md`, `plot.md`, `instructions-snippet.md` | Published canon |

No play summary, state, or transcript.

---

## Output

```json
{
  "warnings": [
    {
      "message": "Warden Greta appears in plot.md but has no entity record.",
      "severity": "warning",
      "category": "scenario-entities",
      "refs": ["plot.md", "entities.json"]
    }
  ]
}
```

### Fix links (hub actions)

| Warning category | Suggested action |
|------------------|------------------|
| `scenario-entities` | `propose_entities_file` or entity panel |
| `scenario-sources` / `sources-sources` | `propose_source_edits` |
| `entities-context-index` | `refresh_context_index` (AIT-T2-E) |

---

## Implementation priority

| P | Item |
|---|------|
| P2 | Job id + Design catalog row |
| P2 | SIO publish list + prompt |
| P3 | Design workspace warnings panel |
| P3 | Share warning types with `continuity_check` where practical |

---

*Last updated: 2026-07-04*
