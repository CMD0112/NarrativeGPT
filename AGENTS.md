# Agent instructions

Guidance for AI agents working in this repository.

## Workspace layout

This Cursor workspace (`chatgpt-wrapper.code-workspace`) has two roots:

| Root | Purpose |
|------|---------|
| **chatgpt-wrapper** | ChatGPT Wrapper application — source, tests, `docs/` |
| **NarrativeGPT** | **Obsidian vault** for narrative/worldbuilding documentation (`.md` notes, wikilinks, tags) |

**NarrativeGPT** is not application code. Use it for lore, long-form notes, and Obsidian-linked reference material. Use `docs/` in the repo for developer and product documentation.

The GitHub repository is also named **NarrativeGPT**; the product and Linear project use **ChatGPT Wrapper**. See [docs/linear/linear-integration.md](docs/linear/linear-integration.md).

Cursor rule: [.cursor/rules/narrativegpt-vault.mdc](.cursor/rules/narrativegpt-vault.mdc)

## Linear issues (CMD0112)

Before **creating or updating** Linear issues (via Linear MCP or otherwise), read:

**[docs/linear/linear-issue-reference.md](docs/linear/linear-issue-reference.md)** — label taxonomy, workflow statuses, issue body templates, and maintenance procedures.

| Topic | Document |
|-------|----------|
| Issue taxonomy & templates | [docs/linear/linear-issue-reference.md](docs/linear/linear-issue-reference.md) |
| PR linking, CI, git automations | [docs/linear/linear-integration.md](docs/linear/linear-integration.md) |
| Cursor rule (always applied) | [.cursor/rules/linear-issues.mdc](.cursor/rules/linear-issues.mdc) |
| Issue links in chat | [.cursor/rules/linear-issue-links.mdc](.cursor/rules/linear-issue-links.mdc) |

Team **CMD0112**, project **ChatGPT Wrapper**, issue prefix **CMD-**.

### Dual canon (required)

Two documents define taxonomy and workflow. **Update both in the same session** whenever either changes:

| Canon | Location |
|-------|----------|
| Workspace (agents) | `docs/linear/linear-issue-reference.md` |
| Human mirror | [Issue Taxonomy & Workflow Guide](https://linear.app/cmd0112/document/issue-taxonomy-and-workflow-guide-1d30b366d19d) |

**Triggers:** new/renamed/retired labels, label description or parent changes, status changes, promotion rules, staging lanes, git↔status policy, issue/epic templates, attachment/upload policy.

**Workflow:**

1. Apply the change in Linear or in `docs/linear/linear-issue-reference.md`.
2. Mirror the same change in the other document.
3. Update **Last synced with Linear labels** in `docs/linear/linear-issue-reference.md`.
4. If git/CI workflow changed, also update `docs/linear/linear-integration.md`.
5. Verify with `list_issue_labels` / `list_issue_statuses` (team: `CMD0112`).

Do not leave taxonomy or workflow changes in only one place. Full procedure: **Maintaining this reference** in [docs/linear/linear-issue-reference.md](docs/linear/linear-issue-reference.md).

### Quick essentials

- Label groups `area`, `domain`, `layer`, `work-type`: at most **one per group** per issue.
- Each label and status has explicit **use when / do not use when** guidance in the reference — read before assigning.
- Search existing issues before creating duplicates.
- Issue descriptions: Context → Acceptance criteria → Technical notes → Test plan (if **Needs Manual QA**) → Related.
- **Done** leaf issues: add **Verified**, remove **Needs Manual QA**.
- **Done — Review Later:** work accepted for now but may need revisit (edge cases, feedback, follow-up) — comment why; add **Verified** if current scope met. See reference for **Done** vs **Done — Review Later**.
- PR linkage: `Fixes CMD-XX` (auto-close) vs `Ref CMD-XX` (manual QA).
- **Attachments:** unlimited Linear uploads — attach repro/QA screenshots and recordings to issues when they aid triage; never upload secrets. See **Issue attachments** in the reference.
- If no status fits: post a **Status recommendation** callout to the user; do not invent statuses.

## General documentation

- **Hub:** [docs/INDEX.md](docs/INDEX.md)
- **Architecture:** [docs/developer/architecture.md](docs/developer/architecture.md)
- **Testing:** [docs/developer/testing.md](docs/developer/testing.md)

## Testing (ApiDiagnostics)

When **adding or editing** tests under `tests/ChatGPTWrapper.ApiDiagnostics/`:

1. Read **[docs/developer/testing.md](docs/developer/testing.md)** — file-lock + logged diagnostics paradigm.
2. Follow **[.cursor/rules/api-diagnostics-tests.mdc](.cursor/rules/api-diagnostics-tests.mdc)** (auto-applied when test files are in context).
3. Prefer **`LoggedTestBase`** or **`DiagnosticTestSession`** for disk/flow tests; assert traces with **`DiagnosticTraceAssert`**.
4. Tag logged tests: `[Trait("Diagnostics", "Logged")]`.
5. Do **not** hand-roll `TestRootOverride` without `ResetStoresForTests()` and isolation.

Reference implementations: `Unit/DiagnosticTestParadigmTests.cs`, `Unit/DiagnosticsLogTests.cs`, `Unit/PlaySendTraceTests.cs`.
