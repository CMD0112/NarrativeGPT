# Linear integration

This repository is tracked in the **[ChatGPT Wrapper](https://linear.app/cmd0112/project/chatgpt-wrapper-b2ae13366b93)** Linear project (team **CMD0112**, issue prefix **CMD-**).

| Resource | URL |
|----------|-----|
| Linear project | https://linear.app/cmd0112/project/chatgpt-wrapper-b2ae13366b93 |
| GitHub repository | https://github.com/CMD0112/NarrativeGPT |
| Issue taxonomy & workflow | [docs/linear/linear-issue-reference.md](linear-issue-reference.md) *(workspace canon for agents)* · [Linear doc](https://linear.app/cmd0112/document/issue-taxonomy-and-workflow-guide-1d30b366d19d) |
| Repository & git workflow | [Linear doc](https://linear.app/cmd0112/document/repository-and-git-workflow-870d25cea521) |

The GitHub repo is named **NarrativeGPT**; the product and Linear project use **ChatGPT Wrapper**.

---

## Day-to-day workflow

1. Pick an issue from the [ChatGPT Wrapper project](https://linear.app/cmd0112/project/chatgpt-wrapper-b2ae13366b93) (**Todo** or promote from **Backlog**).
2. In Linear, use **Copy git branch name** (`Ctrl+Shift+.`) → issue moves to **In Progress**.
3. Implement; open a **draft PR** while still iterating (stays **In Progress**).
4. Mark PR ready for review → **In Review** (code review + CI running).
5. When CI is green → **Ready to Merge**; merge when satisfied.
6. After merge: **Done** + **Verified** (manual for `Ref CMD-XX`; automatic for `Fixes CMD-XX`).

See [Status lifecycle](#status-lifecycle) below for the full map including **Blocked**, **Backlog**, and **Icebox**.

For label rules, status lanes, epic policy, and agent issue templates, see [linear-issue-reference.md](linear-issue-reference.md). The [Issue Taxonomy & Workflow Guide](https://linear.app/cmd0112/document/issue-taxonomy-and-workflow-guide-1d30b366d19d) in Linear is the human-facing mirror — keep both in sync when taxonomy changes.

---

## Linking PRs and branches

Linear’s [GitHub integration](https://linear.app/docs/github-integration) links issues when:

- The branch name contains the issue id (Linear’s copied branch name does this), **or**
- The PR title contains `CMD-XX`, **or**
- The PR description uses a magic word + issue id, e.g. `Fixes CMD-18`.

**Closing magic words:** `fix`, `fixes`, `fixed`, `close`, `closes`, `closed`, `resolve`, `resolves`, `complete`, `completes`, `implement`, `implements`, …

**Link without auto-close on merge:** `ref`, `references`, `part of`, `related to`, `contributes to`, `towards`.

Example PR description:

```text
Fixes CMD-19

Adds SourceJsonImportService and review queue for AI-assisted import.
```

---

## CI

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs on every push/PR to `main`:

- `dotnet build chatgpt-wrapper.sln -c Release`
- `dotnet test` with `Category=Unit` (integration, live, and performance tiers run locally)

See [Testing](../developer/testing.md) for local commands and live/perf tiers.

---

## One-time setup (workspace admin)

These steps require Linear workspace admin and GitHub org access. Do them once per workspace/repo.

### 1. Connect GitHub in Linear

1. Linear → **Settings** → **Integrations** → **GitHub** → **Enable**.
2. Install the Linear GitHub app on the **CMD0112** org.
3. Grant access to **`NarrativeGPT`** (and enable **code access** if you want Reviews / Code Intelligence / coding sessions).

### 2. Team workflow automations (CMD0112)

In **Settings → Teams → CMD0112 → Issue statuses & automations**, configure git automations to drive the full started-status pipeline (not just review states).

#### Status lifecycle

```text
Icebox → Backlog → Todo → In Progress → In Review → Ready to Merge → Done
              ↑         ↑        ↑              ↑
           Blocked ────┴────────┴──────────────┘
```

| When | Status | How |
|------|--------|-----|
| Idea not committed | **Icebox** | Manual |
| Scheduled for a milestone | **Backlog** | Manual |
| Next up, unblocked | **Todo** | Manual (solo dev queue) |
| Waiting on another issue | **Blocked** | Manual + set `blockedBy` |
| Copy git branch / coding | **In Progress** | Personal pref on branch copy |
| Draft PR open | **In Progress** | Team automation |
| PR opened for review | **In Review** | Team automation |
| Review requested / PR activity | **In Review** | Team automation *(required for Ready to Merge below)* |
| CI checks passing | **Ready to Merge** | Team automation |
| PR merged (`Ref CMD-XX`) | **In Review** | Team automation — post-merge verify |
| QA passed / shipped (no expected revisit) | **Done** + **Verified** | Manual |
| Accepted for now, may revisit | **Done — Review Later** | Manual |
| PR merged (`Fixes CMD-XX`) | **Done** | Magic word auto-close |

#### Linear settings (CMD0112 → Issue statuses & automations)

| Git event | Set status to |
|-----------|---------------|
| **PR draft opened** | **In Progress** |
| **PR opened** | **In Review** |
| **PR review requested / activity** | **In Review** |
| **PR ready for merge** *(CI green)* | **Ready to Merge** |
| **PR merged to `main`** | **In Review** |

**Personal prefs** (Settings → Account → Preferences → Behavior):

| Setting | Value |
|---------|-------|
| On git branch copy → move to started status | **In Progress** |
| On git branch copy → auto-assign to self | On |

#### Staging lanes (solo dev)

| Lane | Statuses |
|------|----------|
| **Now** | In Progress, In Review, Ready to Merge *(cap 2–3 issues total)* |
| **Next** | Todo |
| **Waiting** | Blocked |
| **Committed** | Backlog |
| **Someday** | Icebox |
| **Shipped** | Done + Verified |
| **Shipped (provisional)** | Done — Review Later |

**Do not** set PR merged → **Done** directly — use **In Review** after merge for play-session QA, then mark **Done** manually and add **Verified**. Use **Done — Review Later** manually when work is accepted for now but may need revisit (git automations never set this status).

#### PR magic words

| Issue type | PR description | On merge |
|------------|----------------|----------|
| Unit-tested, no manual QA | `Fixes CMD-XX` | Auto-**Done** |
| **Needs Manual QA** / play verify | `Ref CMD-XX` | Stays **In Review** until you verify → **Done** + **Verified** |

Branch format (Integrations → GitHub): keep Linear’s default `{username}/{issue-key}-{slug}`.

### 3. Personal preferences (each developer)

Linear → **Settings** → **Account** → **Preferences** → **Behavior**:

- **On git branch copy, move issue to started status** — recommended.
- **On git branch copy, auto-assign to yourself** — recommended for solo dev.

Connect your personal GitHub account under **Connected accounts** so PR reviews sync correctly.

### 4. Cursor / agents

The Linear MCP plugin is enabled in Cursor for this workspace.

- **Issue taxonomy & templates:** [linear-issue-reference.md](linear-issue-reference.md) (loaded via `.cursor/rules/linear-issues.mdc`)
- **Issue links in chat:** `https://linear.app/cmd0112/issue/CMD-XX` (see `.cursor/rules/linear-issue-links.mdc`)

---

## Referencing issues in docs

Use https links in repo markdown so they work on GitHub and in the IDE:

```markdown
[CMD-18](https://linear.app/cmd0112/issue/CMD-18/deterministic-import-regenerate-json-from-canonical-source-files)
```

Several guides already cross-reference CMD issues (e.g. [adventure-panel.md](../user/adventure-panel.md), [prompt-construction-guide.md](../user/prompt-construction-guide.md)).

---

## Related documentation

- [Linear issue reference](linear-issue-reference.md) — label taxonomy, statuses, issue body templates, agent maintenance
- [Testing](../developer/testing.md) — tiers, filters, CI
- [Build & Deploy](../developer/build-and-deploy.md)
- [docs/INDEX.md](../INDEX.md) — documentation hub
