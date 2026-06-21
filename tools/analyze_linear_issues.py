#!/usr/bin/env python3
"""Analyze Linear issues against docs/linear-issue-reference.md canon."""
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

INPUT = Path(
    r"C:\Users\Crimi\.cursor\projects\e-Documents-Code-chatgpt-wrapper\agent-tools\b22816c8-691d-4e50-87ca-b136c9bf4699.txt"
)
OUTPUT = Path(__file__).resolve().parent / "linear_audit_report.json"

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

GROUPS = {
    "kind": KIND,
    "area": AREA,
    "domain": DOMAIN,
    "layer": LAYER,
    "work-type": WORK_TYPE,
}

CLOSED = {"Done", "Canceled", "Duplicate", "Out of Scope", "Could Not Reproduce"}


def classify_labels(labels):
    by_group = defaultdict(list)
    unknown = []
    for label in labels:
        name = label if isinstance(label, str) else label.get("name", "")
        placed = False
        for group, names in GROUPS.items():
            if name in names:
                by_group[group].append(name)
                placed = True
                break
        if not placed and name not in FLAGS:
            unknown.append(name)
    return by_group, unknown


def has_section(desc, section):
    if not desc:
        return False
    return bool(
        re.search(rf"^##\s*{re.escape(section)}\s*$", desc, re.MULTILINE | re.IGNORECASE)
    )


def status_name(iss):
    status = iss.get("status") or iss.get("state") or {}
    if isinstance(status, dict):
        return status.get("name") or status.get("type") or "?"
    return str(status)


def label_names(iss):
    raw = iss.get("labels") or []
    return [x if isinstance(x, str) else x.get("name", "") for x in raw]


def main():
    with INPUT.open(encoding="utf-8") as f:
        data = json.load(f)

    issues = data["issues"] if isinstance(data, dict) and "issues" in data else data
    violations = defaultdict(list)
    stats = defaultdict(int)

    for iss in issues:
        ident = iss.get("identifier") or iss.get("id", "?")
        title = iss.get("title", "")
        st = status_name(iss)
        labels = label_names(iss)
        by_group, unknown = classify_labels(labels)
        desc = iss.get("description") or ""
        is_epic = "Epic" in labels

        stats["total"] += 1
        stats[f"status:{st}"] += 1

        for group, names in by_group.items():
            if len(names) > 1:
                violations["multi_group"].append(
                    {"id": ident, "group": group, "labels": names, "title": title}
                )

        if unknown:
            violations["unknown_labels"].append(
                {"id": ident, "labels": unknown, "title": title}
            )

        if not by_group.get("kind"):
            violations["missing_kind"].append({"id": ident, "status": st, "title": title})

        if not by_group.get("area"):
            violations["missing_area"].append({"id": ident, "status": st, "title": title})

        if not by_group.get("layer"):
            violations["missing_layer"].append({"id": ident, "status": st, "title": title})

        if st == "Done" and not is_epic:
            if "Verified" not in labels:
                violations["done_no_verified"].append(
                    {"id": ident, "labels": labels, "title": title}
                )
            if "Needs Manual QA" in labels:
                violations["done_with_manual_qa"].append({"id": ident, "title": title})
            if "Regression" in labels:
                violations["done_with_regression"].append({"id": ident, "title": title})

        if is_epic and "Verified" in labels:
            violations["epic_with_verified"].append({"id": ident, "title": title})

        if "Verified" in labels and st != "Done":
            violations["verified_not_done"].append(
                {"id": ident, "status": st, "title": title}
            )

        if "Needs Manual QA" in labels and st not in CLOSED:
            if not has_section(desc, "Test plan"):
                violations["manual_qa_no_testplan"].append(
                    {"id": ident, "status": st, "title": title}
                )

        if st not in CLOSED:
            if not has_section(desc, "Context"):
                violations["missing_context"].append({"id": ident, "status": st, "title": title})
            if not has_section(desc, "Acceptance criteria"):
                violations["missing_acceptance"].append(
                    {"id": ident, "status": st, "title": title}
                )

        if st == "Blocked":
            rels = iss.get("relations") or iss.get("blockedBy") or []
            has_blocker = bool(rels)
            if not has_blocker:
                violations["blocked_no_relation"].append({"id": ident, "title": title})

        wave = {"Shell UX Plan", "shell-ux-wave"} & set(labels)
        if wave and st in ("Done", "Ready to Merge"):
            violations["wave_past_cap"].append({"id": ident, "status": st, "title": title})

        if "Verified" in labels and "Regression" in labels:
            violations["verified_regression_conflict"].append({"id": ident, "title": title})

    report = {
        "stats": dict(stats),
        "violations": {k: v for k, v in sorted(violations.items(), key=lambda x: -len(x[1]))},
        "counts": {k: len(v) for k, v in violations.items()},
    }
    OUTPUT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Wrote {OUTPUT}")
    print(f"Total issues: {stats['total']}")
    for cat, count in sorted(report["counts"].items(), key=lambda x: -x[1]):
        print(f"  {cat}: {count}")


if __name__ == "__main__":
    main()
