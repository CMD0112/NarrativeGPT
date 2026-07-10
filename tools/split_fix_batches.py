import json
from pathlib import Path

fixes = json.loads(Path("e:/Documents/Code/chatgpt-wrapper/tools/linear_fixes.json").read_text())
for i in range(0, len(fixes), 12):
    batch = fixes[i : i + 12]
    Path(f"e:/Documents/Code/chatgpt-wrapper/tools/fix_batches/batch_{i//12:02d}.json").write_text(
        json.dumps(batch, indent=2), encoding="utf-8"
    )
print(f"{len(fixes)} fixes in {(len(fixes)+11)//12} batches")
