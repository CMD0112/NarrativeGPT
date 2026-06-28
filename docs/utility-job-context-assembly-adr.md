# ADR: Utility job context assembly (worker-first)

**Status:** Proposed (spike)  
**Date:** 2026-06-28  
**Epic:** [CMD-390](https://linear.app/cmd0112/issue/CMD-390)  
**Spike:** [CMD-391](https://linear.app/cmd0112/issue/CMD-391)  
**Design track:** [Enhancements/utility-job-context-assembly.md](Enhancements/utility-job-context-assembly.md)

**Builds on:** [injection-policy-adr.md](injection-policy-adr.md) · [play-thread-utility-orchestration-adr.md](play-thread-utility-orchestration-adr.md) · [utility-worker-lane-adr.md](utility-worker-lane-adr.md)

**Parallel (not a dependency):** [CMD-381](https://linear.app/cmd0112/issue/CMD-381) local semantic retrieval for narrator play-packet pointers

---

## Context

Utility generation jobs (memories, entities, summary, continuity, process turn) need **story context** and sometimes **canon slices** in their prompts. Today that material is assembled through **three divergent paths**:

| Path | Entry | Story block | Reference-first |
|------|-------|-------------|-----------------|
| **Worker lane** | `UtilityWorkerOrchestrator` → `UtilityMessagePushService` | `UtilityStoryContextBuilder` → `StoryContextBlock` | `ApplyReferenceFirstDefaults` assumes play-thread visibility |
| **Legacy inline** | `GenerationJobService.RunInlineJobAsync` | Same builder | Same flags |
| **Injection-first bundled** | `PlayUtilityInjectionService` | **No** `StoryContextBlock` — flags only | Omits job turn slices; relies on same-send narrator context |

### Why this hurts utility quality

1. **Worker isolation** — The registered worker conversation (`[CGW:worker]`) cannot see the play thread. `ApplyReferenceFirstDefaults` sets `OmitRedundantJobTurnSlices` when the adventure has play-thread turns, and in the worker push path can set `StoryContextHasTranscript = hasPlayThreadTurns` instead of the actual built transcript — so the job body may omit inline slices **and** lack a compensating story block.

2. **Bundled dedup is implicit** — Injection-first prepends `[[cgw:utility]]` to a play packet but does not compute an explicit overlap manifest against the merged narrator context (summary, state, transcript window, pointers).

3. **Scattered assembly** — `UtilityStoryContextBuilder`, `UtilityStoryContextProfiles`, `GenerationJobHandlers.BuildContinuityCheckPrompt`, and per-job prompt builders each slice context differently (e.g. continuity duplicates summary/state/transcript inside the job core **and** may append a story block).

4. **Static profiles** — Per-job caps (`MaxTurnPairs`, section toggles) are fixed; they do not select **task-relevant canon** (entities mentioned, location, continuity-relevant source sections).

5. **No worker lore channel** — Worker packets rarely include `[[cgw:sources]]`-style retrieval guidance or thin excerpts. Heavy jobs on an isolated conversation may lack published lore access that linked Project adventures provide to the narrator.

[injection-policy-adr.md](injection-policy-adr.md) § Utility job channel states jobs should omit story slices already in the inline play thread feed. That rule is **lane-dependent**: it applies to play-thread delivery, not to worker solo delivery.

---

## Decision summary

| Topic | Decision |
|-------|----------|
| Single assembler | Introduce **`UtilityJobContextAssembler`** as the only entry point for story context + flags + manifest |
| Lane-aware rules | Assembly **must** branch on `UtilityExecutionChannel` / lane (worker solo vs play bundled vs play utility-only) |
| Worker solo | **Self-contained** every send — full required story window + worker lore channel; never assume play thread visibility |
| Play bundled | **Delta-only** vs merged play packet manifest; omit sections narrator context already carries this send |
| Reference-first | Extends CMD-292: utility-specific matrix (see below); worker solo **never** applies play-thread omit heuristics |
| Task-scoped canon | Lexical selection first (aliases, `context-index.json`, state location, entity ids); optional semantic ranker deferred to CMD-399 |
| Lore on worker | Linked adventures: emit pointer block or task-scoped excerpts on worker packets — not full published bodies |
| Preview | `UtilityContextManifest` recorded for AI Actions / prompt-history (CMD-397) |
| CMD-381 synergy | **Optional icebox** — shared embedding index may feed worker canon slices later; not required for assembler v1 |

---

## 1. Execution lanes and normative rules

Aligns with [utility-worker-lane-adr.md](utility-worker-lane-adr.md) routing.

| Lane | Channel | Context rule |
|------|---------|--------------|
| **Worker solo** | `WorkerBackground` | Full self-contained story block + lore channel |
| **Play bundled** | Auto in same send as narrator | Delta-only vs play packet manifest |
| **Play utility-only** | Manual on play thread | Thread-aware dedup when turns visible on **this** conversation; else self-contained |
| **Legacy inline** | Retiring | Migrate to assembler; remove duplicate paths (CMD-396) |

### Worker solo (normative)

- Always build `StoryContextBlock` via assembler profile for `jobId`.
- Set `OmitRedundantJobTurnSlices = false` unless an explicit **same-conversation** transcript window is proven (future: worker thread history audit).
- Set `StoryContextHasTranscript` from **actual** built block, not from play-thread turn count.
- Include worker lore channel when adventure is Project-linked and job requires canon (continuity, process turn, expand entity).

### Play bundled (normative)

- Input: `PlayPacketContextSnapshot` from `PromptInjectionService.PrepareSend` (summary included, state included, transcript tail chars, pointer ids, attachment mode).
- Compare against job profile requirements; omit redundant sections from **both** story block and job-core inline slices.
- Never omit job guide, schema contract, or task-scoped canon the narrator packet did not include.

### Play utility-only (normative)

- Same as bundled when the utility-only send runs on the play thread WebView with visible prior turns.
- Manual jobs that spill to worker use worker solo rules.

---

## 2. Job content matrix (v1)

Finalize per job during implementation; defaults below lock spike intent.

| Job | Transcript | Summary | State | Entity index | Pinned memory | Canon slices | Worker lore |
|-----|------------|---------|-------|--------------|---------------|--------------|-------------|
| `propose_memories` | Trigger turn | Omit if in play ctx | Omit if bundled | No | Optional | No | Pointer-only if linked |
| `extract_entities` | Trigger turn | Omit if bundled | No | Compact | No | Mentioned entities | Pointer-only |
| `update_summary` | Wide window | Prior summary | Yes | No | No | No | No |
| `continuity_check` | Recent window | Yes | Yes | Full compact | Optional | **Task-scoped** excerpts | **Required** when linked |
| `process_turn` | Trigger + prior | Yes | Yes | Yes | Yes | Task-scoped | Pointer-only |
| `expand_entity` | No | No | No | Target only | No | Target section body | Inline if small |
| `bootstrap_lore` | No | No | No | No | No | Scenario excerpt | No |

Profiles in `UtilityStoryContextProfiles` become **inputs** to the assembler, not parallel logic.

---

## 3. Reference-first matrix (utility extension)

Extends [injection-policy-adr.md](injection-policy-adr.md) § Utility job channel.

| Content | Worker solo | Play bundled |
|---------|-------------|--------------|
| Narrator contract / instructions | Never inline — cite Project | Never inline |
| Full published lore bodies | Pointers or task-scoped excerpts | Omit if narrator delegated this send |
| Rolling summary | Include when profile requires | Omit if in narrator context this send |
| State snapshot | Include when profile requires | Omit if in narrator context this send |
| Transcript window | Include required window | Omit if same window in narrator packet **and** visible on thread |
| Job guide + JSON schema | Always | Always |
| Task-scoped canon excerpts | When profile requires | Delta vs narrator pointers |

**Completeness before trim:** Assemble full manifest, then apply lane dedup, then char caps (`MaxContextChars`, profile caps).

---

## 4. `UtilityJobContextAssembler` contract

Single async entry point (CMD-392):

```csharp
// Illustrative — names may adjust during implementation
Task<UtilityJobContextAssemblyResult> AssembleAsync(
    AdventureBundle bundle,
    string jobId,
    UtilityContextAssemblyRequest request,
    CancellationToken cancellationToken = default);

sealed class UtilityContextAssemblyRequest
{
    public UtilityExecutionChannel Channel { get; init; }
    public PlayPacketContextSnapshot? PlayPacketSnapshot { get; init; } // null when worker solo
    public GenerationJobContext JobContext { get; init; }
    public CoreWebView2? PlayCore { get; init; }
}

sealed class UtilityJobContextAssemblyResult
{
    public string StoryContextBlock { get; init; }
    public GenerationJobContext Flags { get; init; }  // Omit*, Includes*, SuppressInlineGuide
    public UtilityContextManifest Manifest { get; init; }
}
```

**Call sites to migrate:**

| Current | Replace with |
|---------|--------------|
| `UtilityStoryContextBuilder.BuildAsync` + push defaults | Assembler in `UtilityMessagePushService` |
| `GenerationJobService` inline story build | Assembler |
| `PlayUtilityInjectionService.ApplyReferenceFirstDefaults` | Assembler (bundled lane) |
| `BuildContinuityCheckPrompt` inline slices | Job core uses flags only; no duplicate summary/state/transcript (CMD-396) |

---

## 5. Task-scoped canon slices (lexical v1)

**Scope inputs:** triggering turn text, `bundle.State`, entity ids from turn, job-specific target entity.

**Selection (lexical, no embeddings):**

1. `context-index.json` triggers and aliases
2. State location → place/region sections
3. Entity mention index → cast/entity sections
4. Cap total excerpt chars per job profile

**Render modes:** inline excerpt (small), `[[cgw:sources]]` pointer line (large), omit (bundled + narrator already retrieved).

Semantic ranking shared with CMD-381 is **CMD-399 only** — same index optional, different consumer (worker job payload vs narrator THIS TURN bucket).

---

## 6. Worker lore channel

For Project-linked adventures when job matrix marks **Required** or **Pointer-only**:

- Emit compact `[[cgw:sources v="2"]]` block scoped to task (not full ALWAYS RETRIEVE narrator set).
- Use `ContextPointerResolver` lexical path today; do not block assembler on CMD-381.
- Never inline full `cast.md` / `world.md` on worker when Project RAG is available.

---

## 7. `UtilityContextManifest` (preview / flight recorder)

Serializable record attached to prompt-history and AI Actions preview (CMD-397):

| Field | Purpose |
|-------|---------|
| `lane` | worker solo / play bundled / play utility-only |
| `jobId` | |
| `sectionsIncluded` | summary, state, transcript, entity index, … |
| `sectionsOmitted` | reason: bundled overlap / profile / budget |
| `canonSliceIds` | source section ids or pointer keys |
| `transcriptSource` | live API / DOM / local log |
| `charCounts` | per section + total |

---

## 8. Non-goals

- Utility transport (outbox, push/pull, injection scheduling) — CMD-326, CMD-358
- Utility response parse/retrieval — CMD-332
- Replacing ChatGPT as utility inference engine
- Narrator play-packet pointer selection — CMD-381
- Mandatory embedding index for utility v1

---

## 9. Migration / rollout

1. **CMD-392** — Assembler behind feature flag; worker path first (highest isolation pain).
2. **CMD-393** — Bundled dedup with snapshot from play send.
3. **CMD-394–396** — Lore channel, lexical canon, handler consolidation.
4. **CMD-397** — Preview manifest.
5. Deprecate direct `ApplyReferenceFirstDefaults` play-thread heuristics on worker push.

No change to `PlayUtilityInjectionMode` default (`LegacyInlineSend`).

---

## 10. Success criteria

- [ ] Worker `continuity_check` useful after 50+ play turns with zero worker history
- [ ] Injection-first bundled jobs do not duplicate summary/transcript in same send
- [ ] AI Actions preview shows lane + manifest
- [ ] One assembler replaces three divergent call sites
- [ ] Linked Project: worker jobs include lore retrieval guidance without inlining full sources

---

## Key code references (today)

| File | Role |
|------|------|
| `UtilityStoryContextBuilder.cs` | Story block sections |
| `UtilityStoryContextProfiles.cs` | Static per-job caps |
| `PlayUtilityInjectionService.cs` | Injection-first; flags without story block |
| `UtilityWorkerOrchestrator.cs` | Worker orchestration |
| `UtilityMessagePushService.cs` | Push + reference-first defaults |
| `GenerationJobHandlers.cs` | Job body + duplicate continuity slices |
| `GenerationJobService.cs` | Legacy inline story build |

---

## Related Linear

| Issue | Focus |
|-------|-------|
| [CMD-390](https://linear.app/cmd0112/issue/CMD-390) | Epic |
| [CMD-391](https://linear.app/cmd0112/issue/CMD-391) | This ADR spike |
| [CMD-392](https://linear.app/cmd0112/issue/CMD-392)–[CMD-398](https://linear.app/cmd0112/issue/CMD-398) | Implementation chain |
| [CMD-399](https://linear.app/cmd0112/issue/CMD-399) | Optional CMD-381 synergy (icebox) |
| [CMD-43](https://linear.app/cmd0112/issue/CMD-43) | Superseded scope — utility story-context dedup |
