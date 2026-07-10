# Entity internal state — tracker

**Status:** Model + persistence shipped; job/UI deferred — 2026-07-04  
**Model reference:** [entity-internal-state-model.md](entity-internal-state-model.md)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Proposed job id:** `propose_entity_state`

---

## Shipped (2026-07-04)

| Deliverable | Location |
|-------------|----------|
| Shared state blocks | `EntityInternalStateBlocks.cs` |
| Per-kind state types + document | `EntityInternalStateKinds.cs`, `EntityInternalStateDocument.cs` |
| Reflection-based field schema | `EntityInternalStateSchema.cs` |
| Edit mapper + form host | `EntityInternalStateEditMapper.cs`, `EntityInternalStateFormHost` |
| Entity editor **Internal** tab | `EntityEditDialog.xaml` |
| Service (lookup / upsert / kind resolution) | `EntityInternalStateService.cs` |
| AI job `propose_entity_state` | `EntityInternalStateProposalService.cs`, `GenerationJobHandlers` |
| Persistence | `entity-state.json` via `AdventureSaveScope.EntityInternalState` |
| Vehicle canon entry | `VehicleEntry` in `EntitiesDocument` |
| Inventory category | `InventoryEntry.Category`, `Tags`, `ExtendedFields` |
| Extraction normalizer + prompts | Extended types in `EntityExtractionService` guide/index |

---

## Still deferred

| Area | Gap |
|------|-----|
| **Review hub** | Accept/reject UI for `EntityInternalStateDocument.ReviewQueue` in Proposal Review Hub |
| **Apply from panel** | Inline diff when AI proposes state for open entity |
| **Play grid** | Vehicles not in Reference filters yet |
| **Canon registry** | `vehicles` collection not in `CanonSchemaBootstrap` |

---

## Design decisions (locked)

| Topic | Decision |
|-------|----------|
| Proposed job id | **`propose_entity_state`** |
| Catalog | **Play sub-action** (entity panel), not Tools catalog row |
| Data home | Satellite **`entity-state.json`** — separate from `entities.json` |
| Canon vs state | Description/role/status → canon; mood/injuries/trust/progress → internal state |

---

## Triggers to wire job + UI

- Extract/update workflow stable in production playtests
- Author feedback that description-only updates are insufficient
- Clear examples (trust, injury, revealed secret) that should persist without bloating description

---

## Related

- [entity-internal-state-model.md](entity-internal-state-model.md)
- [entity-extract-update-workflow.md](entity-extract-update-workflow.md)
- [ai-tools-jobs-review.md](ai-tools-jobs-review.md)

---

*Last updated: 2026-07-04*
