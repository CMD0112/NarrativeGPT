# Linear issue reference (agents)

Canonical workspace reference for **creating and updating** Linear issues on team **CMD0112** (prefix **CMD-**), project **[ChatGPT Wrapper](https://linear.app/cmd0112/project/chatgpt-wrapper-b2ae13366b93)**.

**Agents:** read this file before creating or editing issues via Linear MCP. Entry point for all tools: [AGENTS.md](../AGENTS.md). For git/PR linking and CI workflow, see [linear-integration.md](linear-integration.md). For clickable issue links in chat, follow `.cursor/rules/linear-issue-links.mdc`.

**Human mirror:** [Issue Taxonomy & Workflow Guide](https://linear.app/cmd0112/document/issue-taxonomy-and-workflow-guide-1d30b366d19d) in Linear.

**Dual canon:** `docs/linear/linear-issue-reference.md` and the Linear taxonomy doc must stay aligned. Agents are required to update **both** whenever labels, statuses, or workflow policy change (see [Maintaining this reference](#maintaining-this-reference) and `.cursor/rules/linear-issues.mdc`).

**Last synced with Linear labels:** 2026-07-05

---

## Quick checklist (create / update)

1. **Search first** — `list_issues` for duplicates before creating.
2. **Title** — imperative, specific scope (see [Title guidelines](#title-guidelines)).
3. **Description** — use the [issue body template](#issue-body-template).
4. **Labels** — apply taxonomy (see [Label taxonomy](#label-taxonomy)); at most **one per group**.
5. **Project** — attach to **ChatGPT Wrapper** when creating.
6. **Status** — match [workflow statuses](#workflow-statuses); use the disambiguation table; if none fit, [recommend a new status](#recommending-a-new-status) to the user before forcing a wrong one.
7. **Relations** — set `blockedBy` / parent epic when applicable.
8. **PR linkage** — branch name or `Fixes CMD-XX` / `Ref CMD-XX` per [linear-integration.md](linear-integration.md).
9. **Attachments** — upload evidence when it aids triage or QA (see [Issue attachments](#issue-attachments)); never upload secrets.

---

## Label taxonomy

> **Mutually exclusive groups:** `area`, `domain`, `layer`, and `work-type` — pick **at most one label per group** per issue.

Standalone flags and issue-kind labels are independent; apply zero or more as needed.

**Before creating issues:** run `list_issue_labels` (team: `CMD0112`) and confirm names match this file. Label descriptions below elaborate Linear’s definitions with repo-specific guidance — not just copy-paste.

### Issue kind (pick exactly one)

| Label | Use when | Examples in this repo | Do not use when |
|-------|----------|----------------------|-----------------|
| **Bug** | Observable behavior diverges from documented intent, regression in shipped code, or broken user workflow | Play packet missing on re-send; bridge command returns error; dialog fails to save | The work is net-new capability (use **Feature**); you only want cleanup (use **Improvement** + **Tech Debt**) |
| **Feature** | Adds a capability the user did not have before — new UI, service, workflow, or integration surface | Cast phrase import; new generation job type; adventure dashboard filter | Extends an existing feature without new capability (use **Improvement**); parent tracking only (use **Epic**) |
| **Improvement** | Makes existing behavior better, clearer, faster, or more consistent without a wholly new feature | Dialog layout polish; clearer error message; performance of existing packet build | Entirely new subsystem (use **Feature**); code-only cleanup with no user-visible benefit (use **Tech Debt**) |

### `area` (pick exactly one — where the user experiences the change)

| Label | Use when | Examples | Do not use when |
|-------|----------|----------|-----------------|
| **Play** | Change affects the adventure **play** loop: composer, turns, play settings, in-session AI actions, play-side panels | Turn send pipeline; play prompt injection UI; narrator controls during play | Design-time drafting only (use **Design**); global browse chrome (use **Browse**) |
| **Browse** | Change affects **non-play** reading/navigation: continuous view overlay, phrase highlights, browse chrome, tab chrome outside play/design | CV segment ordering; highlight editor; main window status bar | Play-turn-specific logic (use **Play**); design wizard (use **Design**) |
| **Design** | Change affects adventure **design mode**: wizard, design thread, framework drafting, design-side tooling | Design wizard step; design chat draft; entity editor in design context | Runtime play behavior (use **Play**); dashboard CRUD (use **Management**) |
| **Management** | Change affects adventure **lifecycle** outside a single play/design session: dashboard, create/delete/rename, storage layout, import/export | Adventure rename; JSON import review; dashboard sorting | In-session play or design (use **Play** / **Design**); ChatGPT Project API (use **Projects**) |
| **Projects** | Change involves **ChatGPT Projects**: linking, gizmo resolution, source publish/sync, project-scoped navigation | Project link wizard; source sync dialog; project thread routing | Local-only adventure files with no Project API (use **Management** or **Sources** domain) |
| **WebView** | Primary risk/surface is **WebView2 host**, ChatGPT page DOM, session cookies, or injection timing — even if UI is WPF-hosted | Bridge attach failure; DOM selector break; cache/session isolation | Pure native WPF with no web dependency (use **WPF** layer, omit **WebView** area unless bridge involved) |

### `domain` (pick at most one — primary technical concept)

Omit when the issue is pure WPF chrome with no named subsystem (see note below).

| Label | Use when | Examples | Do not use when |
|-------|----------|----------|-----------------|
| **Navigation** | Routing between threads/tabs/projects, URL resolution, tab pins, linked play/design/project surfaces | `AdventureNavigationService`; design/play thread pin; open linked project | Packet content only (use **Play Packet**); file CRUD (use **Sources** / **Metadata**) |
| **Play Packet** | `[[cgw:*]]` tags, turn meta, transcript assembly, injection builders, packet size/shape | `PromptInjectionService`; start packet; thin vs fat packet | CV rendering only (use **Continuous View**); instruction contract text (use **Instructions**) |
| **Continuous View** | Segment model, CSS/formatting, ordering, overlay behavior in continuous transcript view | `ContinuousViewFormatDialog`; segment ordering; CV preview | Play packet construction (use **Play Packet**); browse chrome unrelated to CV (use **Browse** area) |
| **Sources** | `sources/*.md`, Source Manager, publish/sync, deterministic or LLM import/export | `SourceManagerDialog`; source sync; JSON import from sources | Instruction snippet contract (use **Instructions**); `log.json` (use **Metadata**) |
| **Instructions** | Instruction contract, OOC canon, Instructions Designer, `instructions-snippet.md` | Instruction designer; contract validation; publish instructions to Project | Lore body in source files (use **Sources**); runtime packet tags (use **Play Packet**) |
| **Utility Jobs** | Play-thread utility orchestration: generation jobs, AI actions, schema responses, hidden utility traffic, structured retrieval | `GenerationJobHandlers`; injection-driven utility execution; `UtilityParseLogService` | Ad-hoc play-turn send without job orchestration (use **Play Packet** + **Play** area); design-thread-only source jobs (use **Design** + **Sources**) |
| **Metadata** | `log.json`, `thread-metadata.json`, turn timeline, chat records, persistence of session metadata | Turn timeline; thread metadata migration; chat record shape | Source lore content (use **Sources**); packet injection strings (use **Play Packet**) |
| **Composer** | Native or wrapper composer UI, attachments, in-chat file staging/I/O | Attachment staging; composer file picker; play compose bridge | Packet builder logic without composer UI (use **Play Packet**) |

**When to omit domain:** Dialog padding, theme tokens, or generic WPF layout with no packet/navigation/metadata touch — **area** + **layer** suffice. Cross-cutting ADRs may use **Architecture** work-type instead of forcing a domain.

### `layer` (pick exactly one — primary code layer touched)

| Label | Use when | Examples | Do not use when |
|-------|----------|----------|-----------------|
| **C#** | Primary work in services, models, MainWindow partials, or xUnit tests | `ContinuityService`; adventure models; unit tests | Primary work is XAML-only (use **WPF** or **WinUI**); primary work is injected JS (use **JavaScript**) |
| **JavaScript** | Primary work in `ChatGPT_files/*.js`, wrapper-assets, or DOM injection scripts | `adventure-bridge.js`; `cgw-play-compose.js` | C# bridge host only with no JS change (use **C#** or **WebView** area) |
| **WPF** | Primary work in legacy WPF XAML, dialogs, controls, layout, styling (pre–WinUI migration) | `AdventurePlayView.xaml`; `WrapperControls.xaml` | Deliverable targets WinUI host (use **WinUI**); backend service logic (use **C#**); markdown guides (use **Docs**) |
| **WinUI** | Primary work in WinUI 3 / Windows App SDK XAML, controls, shell, and Fluent styling | WinUI `MainWindow`; `ContentDialog` ports; Mica shell | Legacy WPF-only surface with no WinUI target (use **WPF**); markdown guides (use **Docs**) |
| **Docs** | Primary deliverable is `docs/*.md`, guides, ADRs, or in-repo instruction architecture notes | `adventure-panel.md`; architecture ADR; prompt construction guide | Code change with docs as minor follow-up (use code layer + optional docs link in issue) |

### `work-type` (pick at most one modifier)

| Label | Use when | Examples | Do not use when |
|-------|----------|----------|-----------------|
| **Epic** | Parent issue coordinating multiple children toward one initiative | CMD-95 shell UX; source-import initiative | Single PR-sized leaf work; audit-complete leaf (use **Verified** on Done) |
| **Architecture** | Cross-cutting design decision, ADR, or strategy — may spawn child issues | Play layout tier model; session host split evaluation | Routine feature in one subsystem (use kind + area/domain) |
| **Regression** | Previously **Done** behavior broke again; track until fixed and re-verified | CV segment order regression after theme change | New defect never shipped (use **Bug** only); issue is closing (use **Verified**, remove **Regression**) |
| **Tech Debt** | Refactor, cleanup, or maintainability — no intentional new user-facing behavior | Rename service; extract partial class; dead code removal | User-visible enhancement (use **Improvement**); investigatory (use **Spike**) |
| **Spike** | Time-boxed research; answer is unknown; may spawn follow-ups | Evaluate WebView out-of-process host; prototype DOM selector | Work with clear acceptance criteria ready to implement (use normal kind) |
| **Verified** | Issue is **Done** and audit confirms shipped correctly — post-close label | After play-session QA passed; after code review + unit tests green | Issue still open; epic parent (epics use **Done** without **Verified** per policy) |
| **Quick Win** | Completable in one focused session (~≤2h), single PR, low risk | Fix typo in dialog; small label tweak; one-liner bug | Multi-day feature; needs epic coordination |
| **Shell UX Plan** | Child of CMD-95 shell chrome / design-system wave — filter plan board by this | Theme token migration step; shell status bar item | Unrelated play packet work; after CMD-95 epic sign-off (remove or use **Legacy**) |

### Standalone flags (zero or more — not grouped)

| Label | Use when | Examples | Do not use when |
|-------|----------|----------|-----------------|
| **Optional** | Valid work explicitly **not** required for current milestone sign-off | Polish after epic core ships; nice-to-have from audit | Blocker for release; regression (use **Regression** / **Blocker**) |
| **Legacy** | Direction superseded; do not invest unless scope fundamentally changes | Old proposal-extraction approach after source-file pivot | Active roadmap item; still reproduces (use **Bug** / **Regression**) |
| **Blocker** | This issue **blocks** milestone exit or another active issue’s progress | CMD-XX must land before epic sign-off; breaks CI for all PRs | Issue merely waits on another (use **Blocked** status + `blockedBy`, not necessarily this label) |
| **Needs Manual QA** | Automated tests insufficient — requires real ChatGPT session (play, design, or Projects) | End-to-end play turn; source sync against live Project; DOM-dependent flow | Pure unit-tested C# with no WebView (use **Has Tests**); after QA passed (remove; add **Verified**) |
| **Has Tests** | PR adds or updates automated coverage, or issue explicitly requires tests before Done | New xUnit for packet builder; CI test gate for service | Manual-only verification (use **Needs Manual QA**); docs-only change |
| **ChatGPT Fragile** | Implementation depends on ChatGPT DOM/selectors/session behavior that OpenAI may change without notice | Selector in injected JS; composer DOM assumption | Pure local WPF/C# with no chatgpt.com dependency |
| **shell-ux-wave** | Same wave constraint as **Shell UX Plan** — tag issues in CMD-95 execution batch | Shell chrome PR series item | Non-shell work; after CMD-95 epic sign-off |
| **winui-migration-wave** | Child of [CMD-478](https://linear.app/cmd0112/issue/CMD-478) WinUI 3 shell migration — filter plan board by this | WinUI scaffold; Mica shell; dialog port batch | Unrelated feature work; after CMD-478 epic sign-off |

**Status cap (wave labels):** Issues with **Shell UX Plan**, **shell-ux-wave**, or **winui-migration-wave** should not move past **In Review** until their parent epic sign-off (CMD-95 or CMD-478 respectively).

### Label decision tree

1. Pick **kind** (Bug / Feature / Improvement)
2. Pick **area** (where the user sees it)
3. Pick **domain** if the issue touches a named concept (packet, sources, navigation, …)
4. Pick **layer** (primary code touched)
5. Add **work-type** if Epic, Architecture, Verified, etc.
6. Add **flags** (Blocker, Needs Manual QA, Has Tests, ChatGPT Fragile, wave labels)

### Label conflicts to avoid

| Conflict | Rule |
|----------|------|
| **Verified** + **Regression** | Same `work-type` group — on Done, use **Verified** only |
| **Epic** + **Verified** | Do not put both on the same issue |
| **Needs Manual QA** + **Verified** | Remove **Needs Manual QA** when adding **Verified** |
| Multiple labels in one group | Never — pick the best single fit |

---

## Workflow statuses

Statuses express **where an issue is in its lifecycle**. They are not interchangeable with labels: e.g. **Blocked** (status) means waiting on a dependency; **Blocker** (label) means this issue blocks others.

**Before changing status:** confirm the issue fits an existing status below. If it does not, see [Recommending a new status](#recommending-a-new-status).

### Backlog

#### Backlog

**Use when:** Work is **committed** to the roadmap or a milestone — you intend to do it, but it is not the next active item.

- Scheduled features, bugs, and tech debt with a real priority order
- Promoted from **Icebox** once you decide to schedule it
- Stays here while lower-priority than current **Todo** queue

**Do not use when:** The idea is still exploratory (“maybe someday”) → **Icebox**. The issue is ready to pick up now → **Todo**. Work is actively blocked by another issue → **Blocked** (with `blockedBy`).

**Agent default:** New issues that are real but not imminent → **Backlog**.

#### Icebox

**Use when:** Ideas and explorations **not committed** to any milestone — “maybe someday” inventory.

- Large speculative refactors (e.g. combine Play/Design surfaces)
- Feature requests under evaluation with no milestone slot
- Spikes not yet approved for scheduling

**Do not use when:** You have decided to schedule the work → move to **Backlog**. The issue is merely waiting on another open issue → **Blocked**, not Icebox. Deprioritized committed work stays in **Backlog** with lower priority.

**Promotion:** Icebox → Backlog when committed to a milestone or sprint plan.

---

### Unstarted

#### Todo

**Use when:** Work is **committed, unblocked, and next** in the solo-dev pick-up queue.

- Clear scope; no open `blockedBy` dependencies
- Ready for branch copy or implementation start
- Typically 1–3 issues max in **Todo** at a time (rest stay **Backlog**)

**Do not use when:** Still ideation → **Icebox**. Waiting on CMD-XX or external prerequisite → **Blocked**. Already coding or PR open → **In Progress** / **In Review**.

**Promotion:** Backlog → Todo when it becomes the next priority and is unblocked.

#### Blocked

**Use when:** The issue is **ready to start** (committed scope) but **cannot proceed yet** because of a dependency.

- Another **open** issue must land first → set `blockedBy` to that issue
- Missing prerequisite (e.g. API not available, design decision pending)
- External blocker (tooling, access) documented in description

**Do not use when:** The idea is not committed → **Icebox**, not Blocked. Low priority but could start → keep in **Backlog**/**Todo**. This issue blocks others → use **Blocker** label on *this* issue, **Blocked** status on the *dependent* issue.

**Required:** Always set `blockedBy` when using **Blocked**. Comment why blocked.

**Promotion:** Blocked → Todo when all blockers are **Done** (or canceled as non-blocking); clear `blockedBy`.

---

### Started

#### In Progress

**Use when:** Active implementation — branch exists, code is being written, or draft PR is open.

- Triggered by copying git branch name (personal pref) or manual start
- Draft PRs keep the issue here (team automation)
- Cap **2–3** issues in this status for solo dev

**Do not use when:** Only thinking/planning with no commit → **Todo** or **Backlog**. PR is open for review (non-draft) → **In Review**. CI green awaiting merge → **Ready to Merge**.

#### In Review

**Use when:** Implementation is **code-complete** and awaiting verification — code review, CI, post-merge QA, or manual ChatGPT session test.

- PR opened for review (team automation)
- After merge when using `Ref CMD-XX` or **Needs Manual QA** — code is on `main` but not verified Done
- Epics may sit here during sign-off review

**Do not use when:** Still actively coding with no reviewable PR → **In Progress**. CI green and merge-ready with no further review needed before merge → **Ready to Merge**. Fully verified and shipped → **Done** + **Verified**.

**Pairs with:** **Needs Manual QA** — requires play/design session before **Done**.

#### Ready to Merge

**Use when:** Implementation is complete on a PR, **CI is green**, and the change is approved to merge — but the issue is **not Done yet** because merge or post-merge verification remains.

- Optional hop between **In Progress** and **In Review** when using git branches and CI gates
- Merge queue state: “safe to merge; verification may continue after”
- Team automation sets this when PR checks pass

**Do not use when:** No PR or CI failing → **In Progress** or **In Review**. Already merged and doing manual QA → **In Review**. Fully verified → **Done**.

**After merge:** Often returns to **In Review** for manual QA (`Ref CMD-XX`) unless `Fixes CMD-XX` auto-closes to **Done**.

---

### Completed

#### Done

**Use when:** Work is **shipped and verified** — no further action unless regression.

- PR merged and acceptance criteria met
- Manual QA completed when **Needs Manual QA** was set
- Add **Verified** label on leaf issues; remove **Needs Manual QA** and **Regression**

**Do not use when:** Code merged but play session not run → stay **In Review**. Partial implementation → keep in progress states.

**Auto-close:** `Fixes CMD-XX` in PR may set **Done** on merge if no manual QA required.

#### Done — Review Later

**Use when:** Work is **complete and accepted for now**, but may need to be revisited if new context, edge cases, feedback, or follow-up requirements emerge.

- PR merged and current acceptance criteria met for the known scope
- Shipped solution is good enough to stop active work, with documented caveats or open questions
- Spike or time-boxed outcome accepted pending future evaluation

**Do not use when:** Work is fully verified with no expected revisit → **Done** + **Verified**. Code merged but manual QA not run when **Needs Manual QA** applies → stay **In Review**. Implementation incomplete → **In Progress** / **In Review**. Abandoned or won't pursue → **Out of Scope** / **Canceled**.

**Agent default:** **Comment** when closing here — note what might trigger revisit (edge cases, feedback, follow-up ideas). Add **Verified** if current scope is met; remove **Needs Manual QA**. Link follow-up issues in **Related** when they exist.

**Auto-close:** Git automations do **not** set this status — choose it manually when **Done** is too strong a close.

---

### Canceled

#### Canceled

**Use when:** Work is abandoned without a more specific reason — generic close.

- Deprioritized with no narrative worth preserving
- Duplicate effort absorbed elsewhere without a clear “out of scope” story

**Prefer** **Out of Scope** or **Duplicate** when the reason matters for future searches.

#### Out of Scope

**Use when:** Closed **without implementing** — the request was valid but is **intentionally not pursued**.

- Superseded by another approach (e.g. source files vs proposal extraction)
- Wrong problem framed; solving differently
- Too costly for current milestones; may revisit later via new issue
- Deliberate product/architecture “no” — not a bug, not a duplicate ticket

**Do not use when:** Another issue already tracks the same work → **Duplicate**. Could not confirm the bug → **Could Not Reproduce**.

#### Could Not Reproduce

**Use when:** Reported behavior **could not be confirmed** after investigation.

- Typical for intermittent WebView/DOM/session issues
- Environment-specific glitches with no reliable repro steps
- User report insufficient to trigger a fix

**If it resurfaces:** Open a **new** issue with repro steps; optionally link this one in **Related**.

**Do not use when:** Bug confirmed but won't fix → **Out of Scope**. Same work already filed → **Duplicate**.

---

### Duplicate

#### Duplicate

**Use when:** This issue tracks the **same work** as another issue — consolidate discussion on the canonical ticket.

- Set relation to duplicate-of the surviving issue
- Do not use for superseded *approach* (use **Out of Scope** with explanation)

---

### Status disambiguation (common mistakes)

| Situation | Correct status | Wrong status |
|-----------|----------------|--------------|
| “Maybe merge Play and Design someday” | **Icebox** | Blocked, Backlog |
| Waiting on CMD-42 to merge | **Blocked** + `blockedBy: CMD-42` | Icebox, Todo |
| Draft PR, still coding | **In Progress** | In Review |
| PR merged, need play test | **In Review** | Done |
| CI green, ready to click merge | **Ready to Merge** | Done |
| Superseded design direction | **Out of Scope** | Duplicate, Canceled |
| Flaky WebView glitch, no repro | **Could Not Reproduce** | Out of Scope |
| Shipped, accepted for now, may revisit | **Done — Review Later** | Done, In Review |
| Fully verified, no expected revisit | **Done** + **Verified** | Done — Review Later |

### Staging lanes (solo dev)

| Lane | Statuses | Meaning |
|------|----------|---------|
| **Now** | In Progress, In Review, Ready to Merge | Active coding / review / merge queue — **cap 2–3** issues |
| **Next** | Todo | Committed, unblocked, pick-up order |
| **Waiting** | Blocked | `blockedBy` set; dependency not Done |
| **Committed** | Backlog | Scheduled work, priority-ordered |
| **Someday** | Icebox | Not committed to current milestone |
| **Shipped** | Done + **Verified** | Closed and audit-confirmed |
| **Shipped (provisional)** | Done — Review Later | Accepted for now; may revisit |
| **Closed** | Out of Scope, Duplicate, Canceled, Could Not Reproduce | Will not pursue |

### Status flow

```text
Icebox → Backlog → Todo → In Progress → In Review → Ready to Merge → Done
              ↑         ↑        ↑              ↑
           Blocked ────┴────────┴──────────────┘
```

### Promotion rules (agents)

| Transition | When |
|------------|------|
| Icebox → Backlog | Committed to a milestone |
| Backlog → Todo | Unblocked and next in priority |
| Todo → In Progress | Work started (branch copy or explicit start) |
| In Progress → In Review | PR opened for review (non-draft) |
| In Review → Ready to Merge | CI green (often automatic) |
| Ready to Merge → In Review | After merge when manual QA remains |
| In Review → Done | Manual QA passed or fully automated coverage; no expected revisit |
| In Review → Done — Review Later | Accepted for now; document what may trigger revisit |
| Any → Blocked | `blockedBy` added; issue is committed but waiting |
| Blocked → Todo | All blockers Done; clear `blockedBy` |
| → Done | Add **Verified** on leaf issues; remove **Needs Manual QA** |

### Agent status rules

1. **Do not** set **Done** on merge unless the PR used `Fixes CMD-XX` and the issue has no **Needs Manual QA**.
2. **Do** leave **In Review** after merge when using `Ref CMD-XX` or when **Needs Manual QA** applies.
3. **Do** set **Blocked** + `blockedBy` together — never leave a blocked issue in **Todo**.
4. **Cap In Progress** at 2–3 issues; prefer updating existing issues over opening duplicates.
5. **Epic Done:** Epic may be **Done** when core scope shipped even if optional children remain; list open children in the epic description.
6. **Wave / plan labels** (**Shell UX Plan**, **shell-ux-wave**): do not move past **In Review** until epic sign-off.
7. **Comment** when moving to **Blocked**, **Out of Scope**, **Could Not Reproduce**, or **Done — Review Later** — future readers need the why.
8. **Done — Review Later** vs **Done:** use **Done — Review Later** when work is accepted for now but may need revisit; use **Done** + **Verified** when fully verified with no expected follow-up.

### Recommending a new status

Agents **must not** create or assign workflow statuses that do not exist in Linear. If repeated work genuinely does not fit any status above:

1. **Stop** — do not force the issue into a misleading status.
2. In chat, add a prominent callout:

   **Status recommendation** — propose a new CMD0112 status because [gap]. Suggested: **{Name}** in category **{Backlog|Unstarted|Started|Completed|Canceled}**. Would be used when [scenario]. Transitions: [from → to]. Rationale: [why existing statuses fail].

3. Use the **closest existing status** for any Linear update until the user approves.
4. If the user approves, update **team workflow settings**, then sync **both** canons (`docs/linear/linear-issue-reference.md` + Linear taxonomy doc) and `linear-integration.md` if git automations apply.

Draw the user’s attention explicitly — do not bury the recommendation in a comment on the issue alone.

### Git ↔ status (summary)

Full automation table: [linear-integration.md](linear-integration.md#2-team-workflow-automations-cmd0112).

| Git event | Typical status |
|-----------|----------------|
| Copy git branch | In Progress |
| Draft PR | In Progress |
| PR opened / review activity | In Review |
| CI green | Ready to Merge |
| PR merged (`Ref CMD-XX`) | In Review (manual QA) |
| PR merged (`Fixes CMD-XX`) | Done (auto-close) |

---

## Issue documentation guidelines

### Title guidelines

- Use **imperative mood** and name the **surface + outcome**.
- Good: `Fix duplicate turn meta when re-sending play packet`
- Good: `Add cast phrase import from entity dialog`
- Avoid: `Bug`, `Play fixes`, `WIP`, `Misc improvements`

### Issue body template

Use this structure in the issue **description** (Markdown). Omit sections that do not apply.

```markdown
## Context
Why this work exists. Link related issues, docs, or user reports.

## Acceptance criteria
- [ ] Observable, testable outcome 1
- [ ] Observable, testable outcome 2

## Technical notes
Optional: likely files/services, constraints, risks (ChatGPT Fragile, etc.).

## Test plan
Required when **Needs Manual QA** is set — steps for play/design verification in a real session.

## Related
- CMD-XX (blocks / blocked by / parent epic)
- docs/example.md
```

### Epic body template

Epics add tracking sections:

```markdown
## Goal
One paragraph outcome for the initiative.

## Scope
### In scope
- …

### Out of scope
- …

## Child issues
| Issue | Status | Notes |
|-------|--------|-------|
| CMD-XX | Todo | … |

## Sign-off criteria
What must be true before the epic is **Done**.
```

### Comments vs description

| Update type | Where |
|-------------|-------|
| Scope, acceptance criteria, taxonomy | **Description** (edit issue) |
| Progress, PR links, implementation notes | **Comment** |
| Status change rationale | **Comment** (brief) |
| Post-merge QA result | **Comment** + move to **Done** + add **Verified** |

### Issue attachments

**Plan:** CMD0112 has **unlimited file uploads** on Linear. Prefer attaching visual or binary evidence to issues instead of pasting large blobs into descriptions or chat-only notes.

#### Where to attach

| Target | Use when | MCP / method |
|--------|----------|--------------|
| **Issue attachment** | Persistent evidence tied to the ticket — repro, before/after, QA proof | `prepare_attachment_upload` → PUT bytes → `create_attachment_from_upload` |
| **Comment + attachment** | New evidence during implementation or QA (progress update) | Same upload flow on the issue; reference the file in the comment |
| **Link attachment** | External canonical source already hosted elsewhere | `save_issue` `attachments: [{ url, title }]` — PRs, GitHub blobs, docs URLs |
| **Description only** | Small inline context — short text, markdown links, tiny ASCII | No file upload |

Issue attachments appear on the issue record and survive status changes. Use **link attachments** for PRs and repo docs; use **file uploads** for screenshots, recordings, exports, and logs that only exist locally.

#### When agents should upload

Upload when the file **materially helps** someone reproduce, review, or verify the issue:

| Situation | Typical attachments |
|-----------|---------------------|
| **Bug** / **Regression** | Screenshot or short recording of wrong behavior; annotated UI state |
| **Needs Manual QA** | Before/after screenshots from a real play/design session; short clip of repro steps |
| **Post-merge QA** | Pass/fail evidence when closing to **Done** + **Verified** |
| **WebView** / **ChatGPT Fragile** | DOM/UI screenshot, trimmed console excerpt (scrubbed), selector context |
| **Feature** / **Improvement** (UI) | Mockup, layout comparison, or “expected vs actual” when words are insufficient |
| **Spike** | Findings diagram or export when too large for the description |

**Do not upload** when a GitHub PR, commit, or `docs/*.md` link already holds the artifact. **Do not upload** build outputs, whole repos, or redundant duplicates of PR diff content.

#### When not to upload

- **Secrets** — `.env`, credentials, session cookies, API tokens, personal data
- **Unbounded logs** — full WebView/network dumps unless trimmed and scrubbed
- **Replaceable repo content** — cite `docs/…` or the PR instead
- **Chat-only handoff** — if the user needs the file in Linear for tracking, attach it to the issue

#### MCP upload workflow (preferred)

Use direct upload for anything beyond a few KB. **Do not** use deprecated `create_attachment` (base64 through MCP) except as a last resort for tiny files.

1. **Create or locate the issue** — attachments require an existing issue id (e.g. `CMD-123`).
2. **`prepare_attachment_upload`** — pass `issue`, `filename`, `contentType`, exact `size` (bytes), optional `title`/`subtitle`.
3. **PUT raw bytes** — `curl -X PUT --data-binary @path` (or equivalent) to `uploadRequest.url` with **every** header from `uploadRequest.headers` verbatim (signed URL; expires in ~60s).
4. **`create_attachment_from_upload`** — pass `issue` and `assetUrl` from step 2.

**Sequencing:** finish prepare → PUT → finalize for **one file** before starting the next. Do not batch multiple `prepare_attachment_upload` calls — earlier signed URLs expire while later files are prepared.

**Limits:** single file < 2 GB (Linear API). With unlimited plan, prefer attaching over omitting evidence for size reasons alone; still avoid huge unhelpful dumps.

#### Naming and titles

- **Filename:** descriptive and stable — `play-packet-duplicate-meta.png`, not `Screenshot 2026-06-21.png`.
- **Title:** what the image proves — `Repro: duplicate turn meta on re-send`.
- **Subtitle (optional):** session context — `Play thread, CV overlay enabled`.

### Agent create defaults

| Field | Default unless user specifies otherwise |
|-------|------------------------------------------|
| Team | CMD0112 |
| Project | ChatGPT Wrapper |
| Status | **Backlog** (idea/planned) or **Todo** (ready soon) |
| Priority | Normal; use Urgent only when user says blocker |
| Assignee | Unassigned unless user requests |

### Agent update discipline

- **Never** delete issues or labels without explicit user instruction.
- **Prefer** `save_issue` / `save_comment` over silent local-only notes.
- **Sync labels** when scope shifts (e.g. Bug → Improvement after investigation).
- **Link PRs** in comments with https GitHub URLs.
- **Attach evidence** to the issue when triage or QA benefits (see [Issue attachments](#issue-attachments)); unlimited uploads enabled.
- **Cite issues** in chat as `https://linear.app/cmd0112/issue/CMD-XX` (see `.cursor/rules/linear-issue-links.mdc`).

---

## Maintaining this reference

`docs/linear/linear-issue-reference.md` and the [Issue Taxonomy & Workflow Guide](https://linear.app/cmd0112/document/issue-taxonomy-and-workflow-guide-1d30b366d19d) are **dual canons**. Any taxonomy or workflow change must be reflected in **both** in the same agent session — never update only one.

### When to sync

| Change | Update in Linear | Update here | Also check |
|--------|------------------|-------------|------------|
| New / renamed / retired label | Label settings or MCP | Label taxonomy table | Sync date |
| Label description or parent group | Linear label | Matching table row | Conflict rules |
| New / renamed status | Team workflow settings | Status reference + promotion rules | `linear-integration.md` if git-linked |
| Workflow / QA / epic policy | Linear taxonomy doc | Matching section | Agent status rules |
| Issue body templates | Both docs | This file | `.cursor/rules/linear-issues.mdc` if essentials change |
| Attachment / upload policy | Linear taxonomy doc | [Issue attachments](#issue-attachments) | Agent update discipline |
| Git ↔ status automations | Linear + taxonomy doc | Status + promotion sections | `linear-integration.md` |

### Sync workflow (either direction)

**Linear → workspace** (label/status changed in Linear UI):

1. Add or edit the matching row/section in this file.
2. Copy the same wording into the Linear taxonomy doc (`save_document` MCP or Linear UI).
3. Update **Last synced with Linear labels** at the top.
4. Run `list_issue_labels` / `list_issue_statuses` (team: `CMD0112`) to verify.

**Workspace → Linear** (policy drafted or edited here first):

1. Apply the equivalent change in Linear (label, status, or taxonomy doc).
2. Confirm tables and rules in this file match.
3. Update **Last synced with Linear labels**.
4. If git/CI behavior changed, update `linear-integration.md`.

### Step 1 — Create or update the label in Linear

- Set a clear **name** and **description** in Linear (Settings → Labels, or `create_issue_label` MCP).
- Assign the correct **parent group** (`area`, `domain`, `layer`, `work-type`) when applicable.
- Standalone flags have **no parent**.

### Step 2 — Add an entry here (and mirror to Linear doc)

1. Open `docs/linear/linear-issue-reference.md`.
2. Add a row to the correct table in [Label taxonomy](#label-taxonomy):
   - Issue kind, `area`, `domain`, `layer`, `work-type`, or Standalone flags.
3. Document:
   - **When to use** (one line, actionable)
   - **Group membership** (if new group, document exclusivity rules)
   - **Conflicts** with existing labels (if any)
4. If the label affects workflow (e.g. caps max status), add a rule under [Agent status rules](#agent-status-rules).
5. **Copy the same entry** into the Linear taxonomy doc.
6. Update **Last synced with Linear labels** at the top of this file.

### Step 3 — Cross-link

- Add a one-line pointer in [linear-integration.md](linear-integration.md) if the change affects git or QA workflow.
- Temporary wave labels (e.g. epic-scoped) may stay out of the Linear doc if documented only in both issue descriptions and here — note that in the table row.

### Step 4 — Verify

Run Linear MCP `list_issue_labels` and `list_issue_statuses` (team: `CMD0112`) and confirm name, description, parent, and status list match this file.

### Entry template (copy for new labels)

```markdown
| **Label Name** | When to use — one clear sentence. |
```

For wave or epic-scoped labels, also note status caps and parent epic id (e.g. CMD-95).

---

## Related documentation

- [AGENTS.md](../AGENTS.md) — agent entry point (Linear workflow, dual canon)
- [linear-integration.md](linear-integration.md) — PR magic words, CI, git automations
- [INDEX.md](../INDEX.md) — documentation hub
- `.cursor/rules/linear-issue-links.mdc` — link format for Cursor chat
