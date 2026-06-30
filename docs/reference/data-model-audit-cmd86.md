# Data model audit (CMD-86)

Focused audit appendix for [CMD-190](https://linear.app/cmd0112/issue/CMD-190) dynamic canon schema work. Full inventory lives in [data-model-reference.md](data-model-reference.md).

**Audit date:** 2026-06-19

---

## Authority model

| Layer | Role | Wins when |
|-------|------|-----------|
| `sources/*.md` | Author-editable canon text; Project RAG | Design regenerate, pull from sources, canon reconcile pull |
| `scenario.json` / `entities.json` | Local JSON cache for UI, packets, jobs | Play edits, utility job accept, push to sources export baseline |
| `source-manifest.json` `sections[]` | Parsed section cache + entity id linkage | Pointer resolution, drift detection |
| ChatGPT thread | Live play transcript | Narrative truth during play (synced to `log.json` on confirm) |

```mermaid
flowchart LR
  Sources[sources md]
  JSON[scenario entities json]
  Manifest[source-manifest sections]
  Sources -->|"ProjectSourceImportService"| JSON
  JSON -->|"SectionedExportService"| Sources
  Sources --> Manifest
  JSON --> Manifest
```

---

## Importable lore files (generation / consumption)

| File | Primary JSON target | Writers | Readers |
|------|---------------------|---------|---------|
| `scenario.md` | `scenario.json` opening fields | Export, import, design finalize | Play packets, design UI |
| `cast.md` | `entities.player`, `party[]`, `characters[]` | Export, import | Reference, phrase import, packets |
| `world.md` | `entities` locations/factions/concepts + `scenario.worldRules` | Export, import | Packets, Reference |
| `plot.md` | `entities` quests/mysteries/conflicts/consequences/events + plot fields | Export, import | Packets, Reference |
| `lexicon.md` | `scenario` lexicon fields | Export, import | Packets |
| `entities.json` | (self) | Utility jobs, entity editor, import | Reference, extraction index |

Services: `SectionedImportService`, `SectionedExportService`, `ProjectSourceImportService`, `CanonReconciliationService`.

---

## Top drift findings (severity)

| ID | Severity | Finding | Fix track |
|----|----------|---------|-----------|
| D1 | **High** | Party export prepends name as body line 1; import maps line 1 → `Condition` | CMD-193 — labeled fields via registry |
| D2 | **High** | Missing `## player` in cast.md does not clear player but also does not restore; no change-report coverage | CMD-193 — preserve-on-absent + report |
| D3 | **Medium** | NPC `Role`/`Motives` duplicated in `description`; typed properties empty after import | CMD-193 — `CanonFieldMapper` |
| D4 | **Medium** | World/plot export includes fields import drops (faction Members, mystery Theories, etc.) | CMD-193 — registry parity |
| D5 | **Medium** | `EntityEditMapper` / Reference grid hard-code six kinds; player/party invisible | CMD-195 |
| D6 | **Low** | `docs/reference/data-model-reference.md` cited SourceManifest schema 3; code is 5 | Fixed in this audit |
| D7 | **Medium** | No `entities.json` migration; global `AdventureJson.SchemaVersion` only | CMD-194 — v2 + `ExtendedFields` |
| D8 | **Medium** | Label lists duplicated in import, export, normalizer, UI | CMD-191/193 — `CanonSchemaRegistry` |

---

## Handoff to CMD-190 children

| Issue | Scope |
|-------|--------|
| CMD-191 | ADR + `CanonSchemaRegistry` |
| CMD-192 | `sources/canon-format.md` + prompt citation |
| CMD-193 | Unified import/export + change report |
| CMD-194 | `entities.json` schema v2 + migration |
| CMD-195 | Schema-driven Reference + entity editor |

---

## Related

- [canon-schema.md](canon-schema.md) — schema contract (CMD-191)
- [instruction-sources-paradigm.md](../user/instruction-sources-paradigm.md)
