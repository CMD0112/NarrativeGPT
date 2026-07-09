# Story digest (`update_summary`) — refinement

**Status:** Design note (2026-07-04)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Parent review:** [ai-tools-jobs-review.md](ai-tools-jobs-review.md)  
**Related:** [memory-propose-refinement.md](memory-propose-refinement.md) · [continuity-check-redesign.md](continuity-check-redesign.md) · [utility-source-file-io.md](utility-source-file-io.md)

---

## User decisions — chat capture (2026-07-04)

| # | Question | Decision |
|---|----------|----------|
| 1 | Auto default? | **On** — `AutoUpdateSummary = true` for new settings; interval stays **5** turns |
| 2 | Digest length? | **Fixed guide target** — **150–250 words** in instruction guide (not per-adventure override for P0) |
| 3 | Memory index in packet? | **Yes** — compact index of **accepted memories since last digest revision** in job core |
| 4 | Source I/O? | **Yes (P1)** — input-only publish `summary.json` when digest exceeds threshold (~2k chars) |
| 5 | `process_turn` summary leg? | **Off by default** — keep `ProcessTurnIncludeSummary = false` |

---

## Decisions (2026-07-04)

| Topic | Decision |
|-------|----------|
| Job fate | **Keep — refine** (prompt, dedup, context alignment) |
| Response shape | **Plain text** rolling digest (unchanged) |
| Auto post-turn | **Default on**; interval every **5** turns |
| Digest length | **150–250 words** in guide |
| Memory awareness | **Accepted memories since last revision** — light inline index |
| Source file I/O | **P1** — input publish `summary.json` when digest large |
| `process_turn` | Summary leg **off** by default |

---

## Role

`update_summary` maintains the **rolling story digest** — a compressed narrative the wrapper injects into play packets (`=== ROLLING SUMMARY ===`). It is **not**:

- Discrete event bullets (`propose_memories`)
- World-model referents (`extract_entities`)
- Player-facing recap UI (`RecapService` / local `generate_recap` — retired job)

Authors accept proposals via Summary review (World settings / review hub).

---

## Current implementation

| Aspect | Today |
|--------|--------|
| **Job core** | `RecapService.BuildSummaryUpdatePrompt` — freeform prose instruction + `CURRENT SUMMARY` + `RECENT TURNS` (last **6** pairs, compact `->` format) |
| **Dedup** | When story block has transcript, `omitRecentTurns=true` drops `RECENT TURNS` from job core — **but** `CURRENT SUMMARY` still inlined |
| **Story profile** | `UtilityStoryContextProfiles`: **8** turn pairs, `IncludeRollingSummary=true`, 12k char cap |
| **Guide** | Short — plain text only, preserve major events |
| **Apply** | `SummaryReviewService.QueueProposal` → single pending proposal (revision tracked) |
| **Scheduler** | After extract + memories; when `turn.Index % SummaryUpdateIntervalTurns == 0` (default interval **5**) |
| **Source I/O** | None |
| **`process_turn`** | Optional third leg (`includeSummary` default **false**) — defers here |

### Gaps (to fix in implementation)

1. **Summary duplication** — rolling digest in story block **and** job core `CURRENT SUMMARY` when assembler includes summary.
2. **Turn window mismatch** — job core uses 6 turns; assembler profile allows 8 pairs.
3. **No structured job header** — unlike `=== MEMORY PROPOSAL JOB ===`, digest job is unstructured prose.
4. **No explicit digest contract** — length, tone, compression rubric (addressed in target guide).
5. **No memory index** — digest may miss accepted memories not yet woven into rolling text.

---

## Prompt architecture (target)

### Instruction guide (expand)

```
You maintain the rolling story digest for interactive fiction.

Digest vs other artifacts:
- Digest (this job): compressed ongoing narrative for narrator context — prose paragraph(s).
- Memories (other job): discrete event bullets — not the digest.
- Entities (other job): durable referents — not plot prose.

Retrieve the current digest baseline before rewriting (published summary.json or === CURRENT DIGEST ===).
Integrate recent play and accepted memories since the digest was last revised; compress older material.
Preserve: major events, relationships, active conflicts, open threads, consequences.
Drop: atmosphere-only detail superseded by newer beats.
Target length: 150–250 words unless the current digest is substantially shorter and still accurate.
Output plain text only — no markdown fences, JSON, or commentary.
```

### Job core (in order)

| Section | When |
|---------|------|
| `=== STORY DIGEST UPDATE JOB ===` | Always — contract + baseline pointer |
| `=== CURRENT DIGEST ===` | When **not** already in story block (`UtilityStoryContextDedup`) |
| `=== MEMORIES SINCE LAST REVISION ===` | When accepted memories exist after `Summary.ResolvedProposalRevision` / last accept timestamp |
| `=== RECENT TURNS ===` | When **not** omitted by dedup (align count with assembler profile — **8** pairs) |

Use `UtilityStoryContextDedup.ShouldIncludeSummary` (add if missing) to omit `CURRENT DIGEST` when story block contains `=== ROLLING SUMMARY ===`.

### Memory index (since last revision)

Compact lines — events accepted after the digest was last accepted:

```
[turn:38] Greta blocked the party at Greyford Gate.
[turn:40] Found a rusted key under the altar.
```

**Source:** `Memory.Entries` filtered by `CreatedAt` or turn anchor after last summary accept. **Exclude** pending review queue (digest integrates **accepted** canon only).

---

## Source file I/O (P1)

| Approach | When |
|----------|------|
| Inline `=== CURRENT DIGEST ===` | Default; digest under **~2k chars** |
| Input-only publish `summary.json` | Digest over threshold; aligns with [continuity-check-redesign](continuity-check-redesign.md) publish list |

Output remains **plain text** in assistant reply — no scrape loop.

---

## Interval and auto policy

| Setting | Target default |
|---------|----------------|
| `AutoUpdateSummary` | **`true`** (new settings / template) |
| `SummaryUpdateIntervalTurns` | **`5`** |

Existing adventures keep saved preferences until changed.

When auto runs on interval turn, digest integrates **accepted** memories and transcript — not pending summary/entity/memory proposals (continuity brief may note pending summary replacement separately).

---

## `process_turn` overlap

If catch-all survives:

- Summary leg must call same `BuildSummaryUpdatePrompt` / dedup rules.
- **`ProcessTurnIncludeSummary` stays `false` by default** — digest is interval-gated and heavier than exchange-scoped jobs.

---

## Implementation priority

| P | Item |
|---|------|
| P0 | Structured job header; `UtilityStoryContextDedup` for current digest |
| P0 | Align recent-turn window with profile (8 pairs); shared transcript formatter |
| P0 | Memory-since-revision index in job core |
| P0 | Guide: 150–250 word target + digest vs memories/entities |
| P0 | `AutoUpdateSummary` default **true** on `AdventureMetadata.Settings` |
| P1 | Optional `summary.json` input publish (threshold ~2k chars) |
| P3 | Per-adventure digest length override (utility job overrides) — backlog |

---

## Code touchpoints

| Area | File |
|------|------|
| Job core | `RecapService.BuildSummaryUpdatePrompt` |
| Memory index | New helper (e.g. `SummaryDigestContextService`) |
| Dedup | `UtilityStoryContextDedup` (extend for rolling summary marker) |
| Guide | `GenerationJobGuideService` |
| Profile | `UtilityStoryContextProfiles` (`UpdateSummary` case) |
| Default | `AdventureMetadata.Settings.AutoUpdateSummary` |
| Apply | `GenerationJobHandlers.ApplyUpdateSummary` → `SummaryReviewService` |

---

*Last updated: 2026-07-04*
