# Utility inference routing tracker

**Status:** Living policy (2026-06-29)  
**Related:** [strategic-value-additions-tracker.md]() (SVA-05, SVA-12, SVA-13) · [local-generative-assist-use-cases.md]() · [local-inference-quality-guide.md]() · [utility-worker-lane-adr.md](../utility-worker-lane-adr.md)

Canonical **Track A vs Track B** decisions for where ChatGPT vs local Ollama inference runs in the Adventures stack.

---

## Two tracks

| Track | Question | Production posture (2026-06-29) |
|-------|----------|----------------------------------|
| **A — Utility engine swap** | Run the **same** AI Tools job (`propose_memories`, `extract_entities`, etc.) on Ollama instead of the ChatGPT utility worker? | **ChatGPT default.** Local + dual-run for QA only. |
| **B — Local generative assist** | Run **bounded** tasks with their own schema, often read-only, outside the standard review queue? | **Preferred production surface** — [SVA-13](#sva-13-referent-aware-transcript-highlighting), CMD-399 re-rank, triage (proposed). |

**Rule:** Parse compliance alone does **not** justify Track A promotion. Gates require author-useful recall and accept rate on real adventures.

---

## Lane matrix (who runs what)

| Workload | Engine | Lane doc |
|----------|--------|----------|
| Narrator (play turns) | ChatGPT | [play-send-orchestration-adr.md](../play-send-orchestration-adr.md) |
| Play AI Tools (proposals → review queue) | **ChatGPT utility worker** (default) | Track A — [utility-worker-lane-adr.md](../utility-worker-lane-adr.md) |
| Auto/light utility (post-turn injection) | ChatGPT play thread | [play-thread-utility-orchestration-adr.md](../play-thread-utility-orchestration-adr.md) |
| Local generative assist | Ollama via `OpenAiCompatibleChatClient` | Track B — [local-generative-assist-use-cases.md]() |
| Local embeddings (retrieval only) | ONNX — not generative | [local-semantic-retrieval-adr.md](../local-semantic-retrieval-adr.md) (SVA-01) |

---

## Track A — promotion gates (per job)

Before any job defaults to local inference on the **same** handler path as ChatGPT:

| Gate | Metric |
|------|--------|
| Recall | Proposals match author intent vs ChatGPT baseline on fixture + field sessions |
| Accept rate | % of proposals accepted in review hub (not parse-success alone) |
| Naming | Entity/memory labels align with canon (no systematic misnames) |
| Regression | Dual-run harness shows no worse outcomes on 50+ turn sessions |

**Dual-run harness:** `LocalUtilityInference*` + review hub compliance diagnostics. Entry: Play **More actions…** → Advanced, Preferences hub, shell **⋯** menu → Local inference lab.

---

## Field session log (2026-06-29 — Greyford Gate)

Dual-run on ChatGPT worker vs Ollama (`qwen2.5:7b-instruct`) for:

| Job | Observation |
|-----|-------------|
| `bootstrap_sections` | Local often JSON-compliant but under-recalls sections |
| `update_summary` | Local summaries usable; misses nuance vs ChatGPT |
| `propose_memories` | Local proposes fewer, sometimes misnamed entries |
| `process_turn` | Local bundles parse; recall gap vs ChatGPT |

**Outcome:** No job met Track A promotion bar. Policy rework → **SVA-13** elevated as first Track B production ship.

---

## Track B — production candidates

See [local-generative-assist-use-cases.md]() (LGA-01–08). Priority order (2026-06-29):

1. **SVA-13** — pronoun referent resolution for Continuous view ([CMD-410](https://linear.app/cmd0112/issue/CMD-410))
2. **CMD-399** (icebox) — generative re-rank for utility canon slices (LGA-01)
3. Retrieval audit, triage, canon hygiene (LGA-02–08)

---

## Shipped infrastructure (SVA-12 spike)

| Component | Location |
|-----------|----------|
| OpenAI-compatible client | `ChatGPTWrapper.Core/LocalInference/OpenAiCompatibleChatClient.cs` |
| Lab scenarios (8) | `LocalInferenceLabScenarios.cs` |
| Console lab | `ChatGPTWrapper.LocalInferenceLab` |
| In-app lab | `LocalInferenceLabDialog` |
| Dual-run diagnostics | `LocalUtilityResponseDiagnostics`, `UtilityJsonRepairService` |
| Setup | `scripts/setup-local-inference.ps1` |

Default stack: Ollama @ `http://127.0.0.1:11434`, model `qwen2.5:7b-instruct` (`CGW_OLLAMA_*` env vars). Quality tuning: [local-inference-quality-guide.md]().

---

## Open decisions (UIR)

| ID | Topic | Status |
|----|-------|--------|
| UIR-01 | Per-job Track A default flags | ChatGPT-only until gates pass |
| UIR-06 | `IInferenceProvider` abstraction + feature flags | Proposed with SVA-05 |
| UIR-07 | Offline graceful disable when Ollama unreachable | Required before Track B ships |

---

## Status log

| Date | Update |
|------|--------|
| 2026-06-29 | Document created; dual-run field session; Track B production path via SVA-13 |

*Last updated: 2026-06-29*
