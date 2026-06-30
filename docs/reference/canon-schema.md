# Canon schema (CMD-191)

Architecture decision record for dynamic canon field mapping.

## Decision

Use a **hybrid model**:

- Keep typed C# entry classes with stable identity columns (`Id`, `Name`/`Title`, `Aliases`, `ImagePath`, `Pinned`).
- Add `extendedFields` (`Dictionary<string, string>`) on each entry for schema-defined long-tail attributes.
- Single **`CanonSchemaRegistry`** drives import, export, normalizer labels, change reports, and Reference UI.

Defer full JSON-bag-only storage to a follow-up.

## Schema-as-data (CMD-196)

- **`Adventure/Schema/canon-schema.json`** — repo-bundled canonical schema (embedded resource at runtime).
- **`CanonSchemaLoader`** — deserializes JSON into `CanonSchemaCatalog`; falls back to `CanonSchemaBootstrap` if missing.
- **`CanonSchemaExporter`** — serializes catalog for drift tests and regeneration.
- **`CanonSchemaMigrationService`** — bumps `AdventureMetadata.CanonSchemaVersion` on load when registry version advances.
- **`CanonEntityPropertyGraph`** — reflection-based get/set replaces per-type mapper switches in `CanonFieldMapper`.
- **`CanonFormatGenerator`** — generates `sources/canon-format.md` from the loaded registry (CMD-197). Refreshed on **Refresh export** and shown in **Designer → Sources** (pipeline checklist + upload callout) and **Source Manager** for Project upload.
- **`NarratorScalesLoader` / `NarratorScalesGenerator`** — generates `sources/narrator-scales.md` from bundled `Adventure/Schema/narrator-scales.json` (preset definitions for play/instruction injection). Same reference-file lifecycle as canon-format.
- **`CanonValidationService`** — linter for sectioned sources (CMD-198).

JSON shape mirrors `CanonEntityKindSpec` + `CanonFieldSpec` (`format`, `role`, `controlType`, `alternateLabels`). Global schema only — per-adventure custom schemas remain out of scope.

## Registry

Code: `ChatGPTWrapper/Adventure/Services/Canon/`

| Type | Role |
|------|------|
| `CanonFieldFormat` | Markdown encoding (`BoldLine`, `PlainLine`, `BlockquoteFlavor`, `FreeformBody`) |
| `CanonFieldSpec` | Label ↔ `jsonKey`, multiline, UI role |
| `CanonEntityKindSpec` | Section id, collection key, Play grid flags |
| `CanonSchemaRegistry` | Static catalog |
| `CanonFieldMapper` | Shared import/export/get/set |

## Kinds (initial parity)

| Kind | Section | UI category |
|------|---------|---------------|
| `player` | `cast.md#player` | Player |
| `party` | `cast.md#party` | Party |
| `npc` | `cast.md#npcs` | Characters |
| `location` | `world.md#locations` | Locations |
| `faction` | `world.md#factions` | Factions |
| `concept` | `world.md#concepts` | Concepts |
| `quest` | `plot.md#quests` | Quests |
| `mystery` | `plot.md#mysteries` | (registry only) |
| `conflict` | `plot.md#conflicts` | (registry only) |
| `consequence` | `plot.md#consequences` | (registry only) |

## Consumers

- `SectionedImportService` / `SectionedExportService`
- `SourceMarkdownNormalizer` label lists
- `ProjectSourceImportService.BuildChangeReport`
- `EntityEditMapper` + `AdventurePlayView` Reference grid
- `sources/canon-format.md` model reference (CMD-192)

## Related

- [runtime-canon-schema-plan.md](../plans/runtime-canon-schema-plan.md) — CMD-196 follow-up epic (schema-as-data, validation, generic UI)
- [data-model-audit-cmd86.md](data-model-audit-cmd86.md)
- [data-model-reference.md](data-model-reference.md)
