# Design AI Tools — context & prompt architecture

**Status:** Design canon (2026-07-04)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Policy:** [ai-tools-design-segregation.md](ai-tools-design-segregation.md)  
**Matrix:** [ai-tools-context-matrix.md](ai-tools-context-matrix.md)  
**Backlog:** [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md) — [staged decisions](ai-tools-backlog-tracker.md#staged-decisions-locked-2026-07-04)

Per-job context requirements for the **Design AI Tools** catalog (8 jobs today + accepted backlog). Design jobs run on the **design thread** or **ephemeral worker + source I/O** — never with play rolling summary or post-turn transcript unless the author explicitly references a design conversation turn.

---

## Shared design context rules

| Rule | Detail |
|------|--------|
| **No play packet** | Do not inject `=== ROLLING SUMMARY ===`, play transcript, or post-turn proposal queues |
| **Scenario baseline** | Inline `scenario.json` fields or publish pointer when file exists |
| **Sources** | Published Project sources via TASK-SCOPED pointers — [utility-source-file-io.md](utility-source-file-io.md) |
| **User intent** | Design UI supplies `UserPrompt` / edit intent — required for revision jobs |
| **Guide keys** | `expand_section` → `bootstrap_sections`; `design_extract_step` → `design_adventure` |
| **Review** | Design jobs write to import / source-edit / entity review — not play post-turn queues |

---

## `design_adventure` — Adventure design (AI)

**Lane:** Design thread (conversational)  
**Response:** Conversational — no fixed JSON contract per turn

### Decisions (locked)

| Topic | Decision |
|-------|----------|
| First-turn canon-format | **Inject** `canon-format.md` reference on first message |
| Structured field extract | **Suggest** `design_extract_step` when author needs scenario fields populated |

### Context

| Input | When |
|-------|------|
| User message | Every turn |
| Scenario excerpt (`Title`, `Genre`, `Setting`, `PlayerRole`, `OpeningSituation`, `PlotEssentials`, `WorldRules`) | **First turn only** or when adventure is partial |
| Published source pointers | When author asks to align with existing `world.md` / `plot.md` |

### Job core (target)

- No utility story block.
- First message may prepend `=== DESIGN ADVENTURE ===` with methodology (brainstorm, ask clarifying questions, propose publishable outlines).
- Continuation turns: user prompt only (thread carries history).

---

## `design_extract_step` — Design extract (AI)

**Lane:** Design thread  
**Response:** JSON object — structured scenario / source fields  
**Guide key:** `design_adventure`

### Context

| Input | When |
|-------|------|
| Design-thread history | Implicit in thread |
| **Current scenario field snapshot** | Always — so extract proposes **deltas**, not blind overwrite |
| `DesignStep` parameter | Selects which field group to extract |

### Job core

`AdventureDesignExtractionService.BuildExtractPrompt(bundle, designStep)` — extend to include current values per field.

---

## `draft_framework` — Draft framework

**Lane:** Design thread  
**Response:** Conversational markdown outlines  
**Catalog:** **Adjacent helper** — not a catalog row until explicitly promoted

### Context

| Input | When |
|-------|------|
| User prompt | Always |
| Scenario excerpt | When adventure exists |
| Empty template pointers | P1 — publish `world.md` / `plot.md` stubs + `canon-format.md` |

### Job core (today)

Static `=== ADVENTURE DRAFTING ===` block in `GenerationJobHandlers` — expand with scenario excerpt when bundle present.

---

## `propose_json_import` — JSON import (AI)

**Lane:** Design thread  
**Response:** JSON object (scenario + entities + optional diff)  
**Attachment launch:** Supported

### Context

| Input | When |
|-------|------|
| Staged reference files | Author-selected imports (publish → pointer) |
| **Current `scenario.json` + `entities.json` snapshots** | Always — merge/diff, not blind replace |
| User import intent | Required |

### Job core

`SourceJsonImportService.BuildImportPrompt` — ensure local snapshots inline or via SIO.

### SIO

- **Input:** author-staged source files
- **Output:** JSON in assistant reply (no scrape loop)

---

## `propose_source_edits` — Source edits (AI)

**Lane:** Design thread + worker SIO  
**Doc:** [propose-source-edits-review.md](propose-source-edits-review.md)

### Context

| Input | When |
|-------|------|
| Published `world.md`, `plot.md`, `scenario.md`, `instructions-snippet.md` | Always (pointers) |
| User edit intent | Required |
| Play transcript / summary | **Never** |

### SIO

- **Input:** canon markdown files
- **Output:** Delimited `source-edits.json` scrape

**Exclude `cast.md`** until apply path exists — cast changes route through entity workflow.

---

## `propose_entities_file` — Entities file (AI)

**Lane:** Ephemeral worker + full SIO loop  
**Response:** Delimited full `entities.json`

### Context

| Input | When |
|-------|------|
| Published `entities.json` | Required |
| `scenario.json` | When present |
| `context-index.json` | When present — preserve trigger keywords |
| Compact entity index in JC | Orientation only |
| User revision intent | Required |

### When to use vs play extract

| Use `propose_entities_file` | Use `extract_entities` |
|-----------------------------|------------------------|
| Bulk restructure, import merge, design-time | Per-turn scoped extract + update |
| Full-file delimited output | Proposal queue per item |

---

## `bootstrap_sections` — Canon sections (AI)

**Lane:** Worker  
**Response:** JSON array of section entities

### Context

| Input | When |
|-------|------|
| Scenario fields inline | P0 — Genre, Setting, Plot essentials, etc. |
| Published `scenario.json` | P1 pointer |
| `canon-format.md` | Inline reference block (worker) or pointer at scale |

### Job core

`BuildBootstrapSectionsPrompt` — already includes scenario + format reference.

---

## `expand_section` — Expand section (AI)

**Lane:** Worker sub-action  
**Guide key:** `bootstrap_sections`

### Context

| Input | When |
|-------|------|
| Target section entity record | Inline in JC (today) |
| Published `entities.json` | P1 — pointer for full file |
| Transcript | **Never** |

Mirror [expand-entity-enhancement.md](expand-entity-enhancement.md) pattern: inline when small, pointer when large.

---

## Accepted backlog — design jobs

### `audit_canon` (AIT-T2-D)

Pre-play holistic check — scenario ↔ sources ↔ entities ↔ context-index.  
**Doc:** [audit-canon-workflow.md](audit-canon-workflow.md)

### `refresh_context_index` (AIT-T2-E)

Maintain `context-index.json` triggers when entities/sources change.  
**Doc:** [refresh-context-index-workflow.md](refresh-context-index-workflow.md)

---

## Implementation checklist

| P | Item |
|---|------|
| P0 | This doc linked from index; design jobs excluded from play assembler profiles |
| P1 | `design_extract_step` — current scenario snapshot in prompt |
| P1 | `propose_json_import` — local snapshots for diff |
| P1 | `propose_entities_file` — context-index in publish list |
| P2 | `draft_framework` / `bootstrap_sections` — SIO pointers vs inline thresholds |
| P2 | `audit_canon` job id + handler |

---

## Code touchpoints

| Area | File |
|------|------|
| Job IDs | `GenerationJobId.cs` |
| Prompt routing | `GenerationJobHandlers.BuildJobPrompt` |
| Design catalog | `GenerationJobGuideService` → `EditableDesignUtilityJobIds` (target) |
| Source I/O | `UtilitySourceFileIoCatalog`, `SourceFileRevisionService` |
| Design thread | `UtilityJobRouter.DesignJobs`, `AdventureDesignView` |

---

*Last updated: 2026-07-04*
