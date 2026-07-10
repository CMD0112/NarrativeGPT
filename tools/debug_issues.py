import json
from pathlib import Path

p = Path(r"C:\Users\Crimi\.cursor\projects\e-Documents-Code-chatgpt-wrapper\agent-tools\5cc085fe-09f2-485e-b4b5-0ba1d53a7c4d.txt")
data = json.loads(p.read_text(encoding="utf-8"))
issues = data["issues"]
sample = issues[0]
Path(r"e:\Documents\Code\chatgpt-wrapper\tools\debug_sample.json").write_text(
    json.dumps({k: sample.get(k) for k in sorted(sample.keys())}, indent=2)[:4000],
    encoding="utf-8",
)
print("keys:", sorted(sample.keys()))
print("count:", len(issues))
