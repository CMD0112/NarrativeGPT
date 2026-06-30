# Local generative assist — use case catalog (Track B)

**Status:** Living catalog (2026-06-29)  
**Routing policy:** [utility-inference-routing-tracker.md]() — Track B only; not drop-in replacements for AI Tools review-queue jobs.  
**Quality tuning:** [local-inference-quality-guide.md]()  
**Lab harness:** SVA-12 — `LocalInferenceLabScenarios`, `LocalInferenceLabDialog`

Track B features use **bounded prompts**, **dedicated JSON/text contracts**, and often **read-only** outputs. They do not route through `GenerationJobHandlers` → `PendingReviewService` unless explicitly designed to.

---

## Index

| ID | Use case | Status | Linear / initiative |
|----|----------|--------|---------------------|
| [LGA-01](#lga-01-generative-re-rank-canon-slices) | Generative re-rank for utility canon slices | Icebox | CMD-399 |
| [LGA-02](#lga-02-retrieval-audit-explain-pointers) | Retrieval audit — explain pointer choices | Proposed | SVA-01 synergy |
| [LGA-03](#lga-03-utility-job-triage) | Utility job triage (run/skip/defer) | Proposed | SVA-05 |
| [LGA-04](#lga-04-canon-hygiene-lint) | Canon hygiene lint (sources vs JSON) | Proposed | SVX-06 synergy |
| [LGA-05](#lga-05-lab-scenario-regression) | Lab scenario regression in CI | Spike landed | SVA-12 |
| [LGA-06](#lga-06-dual-run-compliance-qa) | Dual-run compliance QA (Track A) | Active | SVA-12 |
| [LGA-07](#lga-07-pronoun-referent-resolution) | Pronoun referent resolution | **Backlog — first production ship** | SVA-13 / CMD-410 |
| [LGA-08](#lga-08-headless-run-job-cli) | Headless `run-job` against Ollama | Proposed | SVX-16 |

---

## LGA-01: Generative re-rank canon slices

**Goal:** After lexical + embedding retrieval (SVA-01), use a small local model to re-order canon slices before utility job assembly.

**Input:** Candidate sections from `UtilityCanonSliceSelector` + job scope signals.  
**Output:** Ranked list with rationale (debug string per slice).  
**Does not:** Bypass `ContextBudgetAllocator` or injection ADR.

**Icebox:** CMD-399 — depends on SVA-11 context assembly v1 (shipped) and optional SVA-01 embeddings.

---

## LGA-02: Retrieval audit — explain pointers

**Goal:** Author-facing explanation of why `[[cgw:sources]]` pointers resolved to specific sections (complements flight recorder pointer list).

**Input:** `ContextPointerResolver` result + optional semantic scores.  
**Output:** Human-readable bullet list; optional export to flight record detail.  
**Synergy:** SVA-03 flight recorder, SVA-01 semantic retrieval ADR.

---

## LGA-03: Utility job triage

**Goal:** Local model suggests whether to run, skip, or defer heavy utility jobs based on turn signals (length, recap gap, continuity flags).

**Input:** Turn metadata, settings, outbox depth.  
**Output:** `{ action: run|skip|defer, reason }` — advisory only; author override always wins.  
**Does not:** Replace `GenerationJobScheduler` defaults without explicit opt-in.

---

## LGA-04: Canon hygiene lint

**Goal:** Read-only scan of `sources/*.md` vs `entities.json` / manifest for drift, orphan sections, label mismatches.

**Input:** Adventure bundle + `CanonSchemaRegistry`.  
**Output:** Actionable lint report (not auto-fix).  
**Synergy:** `CanonValidationService`, SVX-06 prose linting.

---

## LGA-05: Lab scenario regression

**Goal:** CI-gated runs of `LocalInferenceLabScenarios` against Ollama when `CGW_RUN_OLLAMA_TESTS=1`.

**Status:** Spike landed — unit + gated live tests in `ChatGPTWrapper.ApiDiagnostics`.

---

## LGA-06: Dual-run compliance QA

**Goal:** Run same utility job on ChatGPT worker and Ollama; compare parse compliance and proposal quality for Track A gates.

**Harness:** `LocalUtilityResponseDiagnostics`, review hub compliance badges.  
**Policy:** QA only — see [utility-inference-routing-tracker.md]() field session log.

---

## LGA-07: Pronoun referent resolution

**Goal:** Resolve pronouns in narrator text to cast entities for Continuous/Weave highlighting.

**Pipeline:**

1. Cast manifest from `entities.json` + player
2. Local model → `{ referents[], ambiguous[] }` (lab scenario: `pronoun-tracking`)
3. Map to entity highlight colors ([CMD-271](https://linear.app/cmd0112/issue/CMD-271))
4. Cache under `adventures/{id}/cache/referent-highlights/`

**Status:** **First planned Track B production feature** — [CMD-410](https://linear.app/cmd0112/issue/CMD-410) / SVA-13.  
**Does not:** Mutate canon; block send; require cloud inference.

---

## LGA-08: Headless run-job CLI

**Goal:** `ChatGPTWrapper.LocalInferenceLab` (or sibling) exposes `run-job` for automation / SVX-16 onboarding bots.

**Input:** Scenario id + fixture adventure path.  
**Output:** JSON stdout for CI or scripted playtesting (SVX-12).

---

## Related

- [strategic-value-additions-tracker.md]() — portfolio context
- [utility-job-context-assembly-adr.md](../utility-job-context-assembly-adr.md) — worker story context (ChatGPT path)
- [local-semantic-retrieval-adr.md](../local-semantic-retrieval-adr.md) — embeddings-only (not generative)

*Last updated: 2026-06-29*
