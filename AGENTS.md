# Agent instructions

Guidance for AI agents working in this repository.

## Linear issues (CMD0112)

Before **creating or updating** Linear issues (via Linear MCP or otherwise), read:

**[docs/linear-issue-reference.md](docs/linear-issue-reference.md)** — label taxonomy, workflow statuses, issue body templates, and maintenance procedures.

| Topic | Document |
|-------|----------|
| Issue taxonomy & templates | [docs/linear-issue-reference.md](docs/linear-issue-reference.md) |
| PR linking, CI, git automations | [docs/linear-integration.md](docs/linear-integration.md) |
| Cursor rule (always applied) | [.cursor/rules/linear-issues.mdc](.cursor/rules/linear-issues.mdc) |
| Issue links in chat | [.cursor/rules/linear-issue-links.mdc](.cursor/rules/linear-issue-links.mdc) |

Team **CMD0112**, project **ChatGPT Wrapper**, issue prefix **CMD-**.

### Dual canon (required)

Two documents define taxonomy and workflow. **Update both in the same session** whenever either changes:

| Canon | Location |
|-------|----------|
| Workspace (agents) | `docs/linear-issue-reference.md` |
| Human mirror | [Issue Taxonomy & Workflow Guide](https://linear.app/cmd0112/document/issue-taxonomy-and-workflow-guide-1d30b366d19d) |

**Triggers:** new/renamed/retired labels, label description or parent changes, status changes, promotion rules, staging lanes, git↔status policy, issue/epic templates.

**Workflow:**

1. Apply the change in Linear or in `docs/linear-issue-reference.md`.
2. Mirror the same change in the other document.
3. Update **Last synced with Linear labels** in `docs/linear-issue-reference.md`.
4. If git/CI workflow changed, also update `docs/linear-integration.md`.
5. Verify with `list_issue_labels` / `list_issue_statuses` (team: `CMD0112`).

Do not leave taxonomy or workflow changes in only one place. Full procedure: **Maintaining this reference** in [docs/linear-issue-reference.md](docs/linear-issue-reference.md).

### Quick essentials

- Label groups `area`, `domain`, `layer`, `work-type`: at most **one per group** per issue.
- Each label and status has explicit **use when / do not use when** guidance in the reference — read before assigning.
- Search existing issues before creating duplicates.
- Issue descriptions: Context → Acceptance criteria → Technical notes → Test plan (if **Needs Manual QA**) → Related.
- **Done** leaf issues: add **Verified**, remove **Needs Manual QA**.
- PR linkage: `Fixes CMD-XX` (auto-close) vs `Ref CMD-XX` (manual QA).
- If no status fits: post a **Status recommendation** callout to the user; do not invent statuses.

## General documentation

- **Hub:** [docs/INDEX.md](docs/INDEX.md)
- **Architecture:** [docs/architecture.md](docs/architecture.md)
- **Testing:** [docs/testing.md](docs/testing.md)
