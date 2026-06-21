#!/usr/bin/env python3
"""Generate Linear remediation payloads from audit + issues export."""
import json
import re
from collections import defaultdict
from pathlib import Path

ISSUES_PATH = Path(
    r"C:\Users\Crimi\.cursor\projects\e-Documents-Code-chatgpt-wrapper\agent-tools\5cc085fe-09f2-485e-b4b5-0ba1d53a7c4d.txt"
)
OUT_PATH = Path(__file__).resolve().parent / "linear_fixes.json"

KIND = {"Bug", "Feature", "Improvement"}
AREA = {"Play", "Browse", "Design", "Management", "Projects", "WebView"}
DOMAIN = {
    "Navigation",
    "Play Packet",
    "Continuous View",
    "Sources",
    "Instructions",
    "Utility Jobs",
    "Metadata",
    "Composer",
}
LAYER = {"C#", "JavaScript", "WPF", "Docs"}
WORK_TYPE = {
    "Epic",
    "Architecture",
    "Regression",
    "Tech Debt",
    "Spike",
    "Verified",
    "Quick Win",
    "Shell UX Plan",
}
FLAGS = {
    "Optional",
    "Legacy",
    "Blocker",
    "Needs Manual QA",
    "Has Tests",
    "ChatGPT Fragile",
    "shell-ux-wave",
}
CLOSED = {"Done", "Canceled", "Duplicate", "Out of Scope", "Could Not Reproduce"}

# Explicit label overrides: full replacement label sets
LABEL_OVERRIDES = {
    "CMD-148": ["Feature", "Browse", "Continuous View", "WPF"],
    "CMD-147": ["Feature", "Browse", "Continuous View", "WPF"],
    "CMD-146": ["Feature", "Browse", "Continuous View", "WPF"],
    "CMD-91": ["Epic", "Design", "WPF", "Improvement"],
    "CMD-87": ["Bug", "Design", "Sources", "WPF", "Has Tests"],
    "CMD-80": ["Improvement", "Browse", "Continuous View", "WPF"],
    "CMD-126": ["Improvement", "Docs", "Play"],  # remove Verified
    "CMD-138": ["Feature", "Management", "Metadata", "C#"],
    "CMD-137": ["Improvement", "Play", "WPF"],
    "CMD-136": ["Tech Debt", "Play", "C#"],
    "CMD-135": ["Improvement", "Browse (Browse", "WPF"],  # typo fix below
    "CMD-133": ["Feature", "Play", "C#", "Architecture"],
    "CMD-132": ["Improvement", "Play", "Docs"],
    "CMD-131": ["Feature", "Play", "WPF"],
    "CMD-130": ["Feature", "Play", "WPF"],
    "CMD-129": ["Feature", "Play", "C#"],
    "CMD-128": ["Feature", "Play", "C#"],
    "CMD-145": ["Improvement", "Play", "Sources", "C#", "Verified"],
    "CMD-141": ["Feature", "Design", "Sources", "C#", "Verified"],
    "CMD-142": ["Feature", "Design", "Sources", "WPF", "Verified"],
    "CMD-106": ["Improvement", "Browse", "WPF", "Architecture", "shell-ux-wave", "Verified"],
    "CMD-103": ["Improvement", "Browse", "Navigation", "WPF", "shell-ux-wave", "Verified"],
    "CMD-100": ["Improvement", "Browse", "Navigation", "WPF", "shell-ux-wave", "Verified"],
    "CMD-99": ["Improvement", "Browse", "Navigation", "WPF", "shell-ux-wave", "Verified"],
    "CMD-98": ["Improvement", "Browse", "WPF", "shell-ux-wave", "Verified"],
    "CMD-97": ["Improvement", "WPF", "Architecture", "shell-ux-wave", "Verified"],
    "CMD-96": ["Improvement", "Tech Debt", "WPF", "Browse", "shell-ux-wave", "Verified"],
    "CMD-90": ["Feature", "Projects", "Navigation", "WPF"],
    "CMD-86": ["Improvement", "Architecture", "C#", "Management"],
    "CMD-78": ["Bug", "Navigation", "Docs", "Has Tests", "Verified"],
    "CMD-69": ["Spike", "Architecture", "Docs"],
    "CMD-29": ["Epic", "Metadata", "C#", "Management"],
    "CMD-65": ["Feature", "Play", "Navigation", "C#"],
    "CMD-94": ["Improvement", "Instructions", "C#"],
    "CMD-89": ["Spike", "Sources", "C#"],
    "CMD-55": ["Improvement", "Design", "Utility Jobs"],
    "CMD-45": ["Feature", "Play", "Metadata", "C#"],
    "CMD-44": ["Feature", "Play", "Composer", "WebView"],
    "CMD-21": ["Spike", "Management", "WPF"],
    "CMD-11": ["Feature", "Design", "Sources", "C#"],
}

# Fix typo in CMD-135
LABEL_OVERRIDES["CMD-135"] = ["Improvement", "Browse", "WPF"]

# Special done fixes with flag cleanup
DONE_FLAG_CLEANUP = {
    "CMD-66": {"remove": ["Needs Manual QA", "Regression"], "add": ["Verified"]},
    "CMD-62": {"remove": ["Needs Manual QA", "Regression"], "add": ["Verified"]},
}


def label_names(iss):
    raw = iss.get("labels") or []
    return [x if isinstance(x, str) else x.get("name", "") for x in raw]


def status_name(iss):
    status = iss.get("status") or iss.get("state")
    if isinstance(status, dict):
        return status.get("name") or status.get("type") or "?"
    return str(status or "?")


def issue_id(iss):
    return iss.get("identifier") or iss.get("id")


def has_section(desc, section):
    if not desc:
        return False
    return bool(
        re.search(rf"^##\s*{re.escape(section)}\s*$", desc, re.MULTILINE | re.IGNORECASE)
    )


def ensure_description(desc, title):
    if not desc or "truncated, use `get_issue`" in desc:
        return None
    if has_section(desc, "Context") and has_section(desc, "Acceptance criteria"):
        return None

    body = (desc or "").strip()
    intro = ""
    if body:
        for line in body.splitlines():
            stripped = line.strip()
            if stripped and not stripped.startswith("#") and not stripped.startswith("|"):
                intro = stripped[:500]
                break
    if not intro:
        intro = f"Work tracked for **{title}**."

    parts = []
    if not has_section(body, "Context"):
        parts.append(f"## Context\n{intro}")
    if not has_section(body, "Acceptance criteria"):
        parts.append("## Acceptance criteria\n- [ ] Deliverables in description below are complete and verified")

    if body:
        parts.append(body)

    return "\n\n".join(parts)


def normalize_done_labels(labels, is_epic):
    if is_epic:
        return [l for l in labels if l not in {"Verified", "Needs Manual QA", "Regression"}]
    out = [l for l in labels if l not in {"Needs Manual QA", "Regression"}]
    if "Verified" not in out:
        out.append("Verified")
    return out


def main():
    with ISSUES_PATH.open(encoding="utf-8") as f:
        data = json.load(f)
    issues = data["issues"] if isinstance(data, dict) else data
    by_id = {}
    for i in issues:
        ident = issue_id(i)
        if ident:
            by_id[ident] = i

    fixes = []

    for ident, iss in sorted(by_id.items()):
        if not ident.startswith("CMD-"):
            continue
        st = status_name(iss)
        labels = label_names(iss)
        is_epic = "Epic" in labels
        desc = iss.get("description") or ""
        fix = {"id": ident}

        # Explicit overrides win
        if ident in LABEL_OVERRIDES:
            fix["labels"] = LABEL_OVERRIDES[ident]
        elif ident in DONE_FLAG_CLEANUP:
            spec = DONE_FLAG_CLEANUP[ident]
            new_labels = [l for l in labels if l not in spec["remove"]]
            for a in spec["add"]:
                if a not in new_labels:
                    new_labels.append(a)
            fix["labels"] = new_labels
        elif st == "Done" and not is_epic and ident not in {"CMD-10"} and "Verified" not in labels:
            fix["labels"] = normalize_done_labels(labels, False)

        # CMD-42: unblock — CMD-27 Done
        if ident == "CMD-42":
            fix["state"] = "Backlog"
            fix["removeBlockedBy"] = ["CMD-27"]
            # Description updated separately via get_issue — export may be truncated

        # Open issues: description template
        if st not in CLOSED and ident not in {"CMD-42"}:
            new_desc = ensure_description(desc, iss.get("title", ""))
            if new_desc and "description" not in fix:
                fix["description"] = new_desc

        # Manual QA test plan for theme issues
        if "Needs Manual QA" in labels and st not in CLOSED:
            if not has_section(desc, "Test plan") and ident in {
                "CMD-118", "CMD-117", "CMD-116", "CMD-112", "CMD-111"
            }:
                tp = (
                    "## Test plan\n"
                    "1. Open Preferences → Appearance & Themes.\n"
                    "2. Apply built-in and custom theme; verify WPF chrome and WebView CV styling.\n"
                    "3. Restart app; confirm theme persists and contrast remains readable.\n"
                )
                base = fix.get("description") or desc
                if not has_section(base, "Test plan"):
                    fix["description"] = base.rstrip() + "\n\n" + tp

        if len(fix) > 1:
            fixes.append(fix)

    OUT_PATH.write_text(json.dumps(fixes, indent=2), encoding="utf-8")
    print(f"Generated {len(fixes)} fixes -> {OUT_PATH}")


if __name__ == "__main__":
    main()
