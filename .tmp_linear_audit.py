import json
import sys
from collections import Counter

sys.stdout.reconfigure(encoding="utf-8")

path = r"C:\Users\Crimi\.cursor\projects\e-Documents-Code-chatgpt-wrapper\agent-tools\4f290e3b-8320-41dc-b93f-ae3169c8dbd7.txt"
out_path = r"e:\Documents\Code\chatgpt-wrapper\.tmp_linear_audit_out.txt"
raw = open(path, encoding="utf-8").read()
idx = raw.find("{")
d = json.loads(raw[idx:])
issues = d["issues"]
lines = []

def p(s=""):
    lines.append(s)
open_st = {"In Progress", "In Review", "Todo", "Backlog", "Blocked", "Icebox"}
areas = {"Play", "Browse", "Design", "Management", "Projects", "WebView"}
domains = {
    "Navigation",
    "Play Packet",
    "Continuous View",
    "Sources",
    "Instructions",
    "Utility Jobs",
    "Metadata",
    "Composer",
}
kinds = {"Bug", "Feature", "Improvement"}

p(f"Total: {len(issues)}")
p("\n=== STATUS ===")
for k, v in sorted(Counter(i["status"] for i in issues).items(), key=lambda x: -x[1]):
    p(f"  {k}: {v}")

by_id = {i["id"]: i for i in issues}
children = {}
for i in issues:
    pid = i.get("parentId")
    if pid:
        children.setdefault(pid, []).append(i["id"])

p("\n=== EPIC HEALTH ===")
for i in sorted(issues, key=lambda x: x["id"]):
    if "Epic" not in (i.get("labels") or []):
        continue
    kids = children.get(i["id"], [])
    open_kids = [k for k in kids if by_id[k]["status"] not in ("Done", "Out of Scope", "Duplicate", "Canceled", "Could Not Reproduce")]
    p(f"  {i['id']} [{i['status']}] open_children={len(open_kids)} / {len(kids)} -> {open_kids}")

p("\n=== ALL ISSUES BY STATUS ===")
order = ["In Progress", "In Review", "Ready to Merge", "Todo", "Blocked", "Backlog", "Icebox", "Done", "Out of Scope", "Duplicate", "Canceled", "Could Not Reproduce"]
for st in order:
    subset = [i for i in issues if i["status"] == st]
    if subset:
        p(f"\n  -- {st} ({len(subset)}) --")
        for i in sorted(subset, key=lambda x: x["id"]):
            pri = (i.get("priority") or {}).get("name", "?")
            labs = ", ".join(i.get("labels") or [])
            p(f"    {i['id']} P:{pri} | {labs}")
            p(f"      {i['title'][:90]}")

p("\n=== DONE MISSING VERIFIED ===")
for i in sorted(issues, key=lambda x: x["id"]):
    if i["status"] != "Done":
        continue
    labs = set(i.get("labels") or [])
    if "Verified" in labs or "Epic" in labs:
        continue
    p(f"  {i['id']}: {sorted(labs)}")

p("\n=== LABEL ANOMALIES ===")
for i in sorted(issues, key=lambda x: x["id"]):
    labs = set(i.get("labels") or [])
    kind = labs & kinds
    area = labs & areas
    domain = labs & domains
    issues_list = []
    if len(kind) != 1 and i["status"] not in ("Out of Scope", "Duplicate"):
        issues_list.append(f"kind={kind}")
    if "Epic" not in labs and i["status"] in open_st:
        if not area:
            issues_list.append("no area")
        if not domain:
            issues_list.append("no domain")
    if issues_list:
        p(f"  {i['id']} [{i['status']}]: {', '.join(issues_list)} | {sorted(labs)}")

open("e:\\Documents\\Code\\chatgpt-wrapper\\.tmp_linear_audit_out.txt", "w", encoding="utf-8").write("\n".join(lines))
print("Wrote", out_path)
