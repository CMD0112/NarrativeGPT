# Utility Worker Lane — Implementation Plan

Execution plan for the **dual-lane utility architecture** (play injection + registered utility worker).

**Companion ADR:** [utility-worker-lane-adr.md](../adr/utility-worker-lane-adr.md)

**Builds on:** [play-thread-utility-orchestration-plan.md](play-thread-utility-orchestration-plan.md) · [utility-delivery-pivot-adr.md](../adr/utility-delivery-pivot-adr.md)

**Linear epic:** [CMD-358](https://linear.app/cmd0112/issue/CMD-358) (companion to [CMD-326](https://linear.app/cmd0112/issue/CMD-326) play injection)

---

## Phases

| Phase | Deliverable | Issue |
|-------|-------------|-------|
| 0 | ADR (this doc's companion) | [CMD-359](https://linear.app/cmd0112/issue/CMD-359) |
| 1 | Run correlation, `UtilityOutboxService`, extended `UtilityJobResultStore` | [CMD-360](https://linear.app/cmd0112/issue/CMD-360) |
| 2 | `UtilityWorkerSessionService`, capability gate, ping job, `UtilityWorker` registry kind | [CMD-361](https://linear.app/cmd0112/issue/CMD-361) |
| 3 | `UtilityMessagePushService`, `UtilityMessagePullService`, `UtilityWorkerOrchestrator` | [CMD-362](https://linear.app/cmd0112/issue/CMD-362) |
| 4 | `UtilityJobRouter`, `MainWindow` wiring, auto spill | [CMD-363](https://linear.app/cmd0112/issue/CMD-363) |
| 5 | `UtilityWorkerPinService`, `ThreadWebViewResolver`, settings | [CMD-364](https://linear.app/cmd0112/issue/CMD-364) |
| 6 | SessionHost process (future) | [CMD-365](https://linear.app/cmd0112/issue/CMD-365) |
| 7 | Docs, diagnostics, epic sign-off | [CMD-367](https://linear.app/cmd0112/issue/CMD-367) |

See ADR for normative routing and transport rules.

*Last updated: 2026-06-25*
