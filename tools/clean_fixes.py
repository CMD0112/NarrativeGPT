import json
from pathlib import Path

fixes = json.loads(Path("linear_fixes.json").read_text(encoding="utf-8"))
clean = []
for fix in fixes:
    if "description" in fix and "truncated, use `get_issue`" in fix["description"]:
        fix = {k: v for k, v in fix.items() if k != "description"}
    if len(fix) > 1:
        clean.append(fix)

out = Path("linear_fixes_clean.json")
out.write_text(json.dumps(clean, indent=2), encoding="utf-8")
print(f"{len(clean)} clean fixes")

for i in range(0, len(clean), 12):
    batch = clean[i : i + 12]
    Path(f"fix_batches/batch_{i//12:02d}.json").write_text(
        json.dumps(batch, indent=2), encoding="utf-8"
    )
print(f"{(len(clean)+11)//12} batches")
