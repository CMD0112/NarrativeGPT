# ADR: Project source publication lab

**Status:** Proposed (2026-06-30)  
**Linear:** [CMD-429](https://linear.app/cmd0112/issue/CMD-429) · Epic [CMD-428](https://linear.app/cmd0112/issue/CMD-428)  
**Plan:** [project-source-publication-redesign.md](../Enhancements/project-source-publication-redesign.md)

## Context

Authors publish adventure lore to ChatGPT Projects via **manual drag-and-drop** (Source Manager). Programmatic API publication (`SourceSyncDialog`) is diagnostics/repair only but currently shares attach machinery with batch sync and uses inconsistent success criteria — producing ghost files, upsert forks, and fragile exception ladders.

Utility worker jobs ([CMD-411](https://linear.app/cmd0112/issue/CMD-411)) solved a parallel problem: API attach blocked on linked projects (`http_403`), so **browser-native DOM upload** with shadow compositor ([CMD-413](https://linear.app/cmd0112/issue/CMD-413)) and attach-worker fallback ([CMD-414](https://linear.app/cmd0112/issue/CMD-414)) became the reliable path for conversation attachments.

## Decision

1. **Manual publish remains authoritative** for production readiness (`ManuallyPublishedSha256`).
2. **Publication lab** uses a dedicated state machine with a **single verify gate** (project-scoped download + byte match).
3. **Extract a shared browser-file delivery kernel** used by utility worker DOM attach and publication DOM lanes — same CDP staging, compositor scope, attach-worker fallback; different DOM targets and verifiers.
4. **Snorlax diagnostics: DOM-first lane order**, then library, then register+project-files. **Never detail upsert** for per-file publication.
5. **Batch sync** (`ProjectSourceSyncService`) remains separate; may keep upsert fallback with fork guards but must not be called from publication.

## Consequences

### Positive

- Aligns with utility worker proven patterns (shadow compositor, attach worker).
- Eliminates listing-as-success false positives.
- Clear module boundary for diagnostics vs repair.
- Flight recorder can log `publicationLane` attempts ([CMD-402](https://linear.app/cmd0112/issue/CMD-402)).

### Negative

- Refactor cost: merge `NativeComposerFileStaging` and `ProjectKnowledgeFileStaging` into kernel.
- Live QA required for project knowledge DOM selectors (`ChatGPT Fragile`).
- Batch sync ghost/fork issues remain until phase E audit.

## Alternatives considered

| Alternative | Rejected because |
|-------------|------------------|
| Fix catch ladders in current pipeline | Continues treating symptoms; lane order backwards |
| Detail upsert for publication | Creates sidebar fork duplicates (`merged=N`) |
| Utility worker send for project files | Wrong DOM surface (composer vs project knowledge) |
| API-first publication | Logs prove ghosts despite HTTP 200 |

## Related

- [utility-worker-attachment-delivery.md](../Enhancements/utility-worker-attachment-delivery.md)
- [instruction-sources-paradigm.md](../user/instruction-sources-paradigm.md)
- [chat-file-io-feasibility.md](../Enhancements/chat-file-io-feasibility.md)
