# Local inference quality guide

**Status:** Living reference (2026-06-29)  
**Related:** [utility-inference-routing-tracker.md]() · [local-generative-assist-use-cases.md]() · SVA-12 lab (`LocalInferenceLabDialog`, `scripts/setup-local-inference.ps1`)

Practical guidance for **Ollama** and OpenAI-compatible local endpoints used by Track B assists and Track A dual-run QA.

---

## Default stack

| Setting | Default |
|---------|---------|
| Base URL | `http://127.0.0.1:11434/v1` |
| Model | `qwen2.5:7b-instruct` |
| Env overrides | `CGW_OLLAMA_BASE_URL`, `CGW_OLLAMA_MODEL` |
| Client | `OpenAiCompatibleChatClient` in `ChatGPTWrapper.Core/LocalInference/` |

Run `scripts/setup-local-inference.ps1` for first-time Ollama + model pull.

---

## Model selection

| Use case | Suggested models | Notes |
|----------|------------------|-------|
| Utility-shaped JSON (lab / dual-run) | `qwen2.5:7b-instruct`, `llama3.1:8b-instruct` | Prefer instruct-tuned; verify JSON schema compliance in lab |
| Pronoun referent (SVA-13) | Same as above; test on cast-heavy fixtures | Smaller models faster; 7B+ for ambiguous scenes |
| Re-rank / short classification (LGA-01) | `qwen2.5:3b-instruct` or 7B | Low temperature; short max tokens |
| **Avoid** for production utility swap | Uncensored / base models | Poor instruction following for CGW JSON contracts |

**Promotion bar:** Model must pass lab scenarios **and** field dual-run gates in [utility-inference-routing-tracker.md]() — not lab-only.

---

## Ollama settings

| Parameter | Recommendation | Rationale |
|-----------|----------------|-----------|
| `temperature` | `0.1`–`0.3` for structured JSON; `0.5`–`0.7` for creative assist | Utility jobs need deterministic shapes |
| `num_ctx` | ≥ 8192 for long transcript context; 4096 for slice-only tasks | OOM vs recall tradeoff |
| `top_p` | `0.9` default | Minor effect vs temperature on instruct models |
| `repeat_penalty` | Default | Raise slightly if loops in summaries |

Set per-request in `OpenAiCompatibleChatClient` options; lab dialog exposes overrides for experiments.

---

## Context vs chunking

| Pattern | When | How |
|---------|------|-----|
| **Full story block** | Worker-isolated jobs need transcript | `UtilityStoryContextBuilder` profile — mirror in lab scenarios |
| **Canon slices only** | Entity/memory jobs with reference-first flags | `UtilityCanonSliceSelector` — do not duplicate full sources in prompt |
| **Chunked transcript** | Context exceeds `num_ctx` | Tail N turns + summary + state table; document truncation in flight recorder |
| **Multi-pass** | Complex extract on long sessions | Pass 1: outline entities mentioned; Pass 2: structured extract per entity — lab only until ADR |

**Anti-pattern:** Stuffing entire `sources/*.md` into local context — use pointers and slices like ChatGPT worker path ([utility-job-context-assembly-adr.md](../utility-job-context-assembly-adr.md)).

---

## JSON reliability

| Technique | CGW usage |
|-----------|-----------|
| Schema in system prompt | `GenerationJobHandlers` shapes mirrored in `LocalInferenceLabScenarios` |
| `UtilityJsonRepairService` | Tolerates minor markdown fences / trailing prose in dual-run |
| Compliance badges | Review hub shows parse success vs author-useful proposals |
| Few-shot examples | Lab scenarios include 1–2 turn fixtures |

Track A promotion requires **accept rate**, not repair-success alone.

---

## Dual-run workflow (Track A QA)

1. Enable utility worker (green probe) on test adventure.
2. Open Local inference lab or use in-app dual-run from review flow.
3. Run job pair: ChatGPT worker vs Ollama.
4. Log recall, naming, accept decisions in routing tracker field session table.
5. Do not change production defaults until per-job gates pass.

---

## Offline and failure modes

| Condition | Expected behavior |
|-----------|-------------------|
| Ollama not running | Track B features hidden/disabled; ChatGPT paths unaffected |
| Timeout | Surface error in lab; no silent fallback to empty proposals |
| Model not pulled | Probe fails with actionable message (`ollama pull …`) |

Required before Track B production ship (UIR-07).

---

## Testing

```powershell
# Gated live tests (requires Ollama + model)
$env:CGW_RUN_OLLAMA_TESTS = "1"
dotnet test tests/ChatGPTWrapper.ApiDiagnostics --filter "FullyQualifiedName~LocalInference"
```

Console lab: `dotnet run --project ChatGPTWrapper.LocalInferenceLab -- probe` / `chat` / `entity-demo`.

---

## Related documents

- [utility-inference-routing-tracker.md]() — Track A/B policy
- [local-generative-assist-use-cases.md]() — what to build on local inference
- [strategic-value-additions-tracker.md]() — SVA-12, SVA-13

*Last updated: 2026-06-29*
