import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

b = os.path.join(
    os.environ["LOCALAPPDATA"], "Programs", "@codebufffreebuff-desktop",
    "resources", "orchestrator", "ui", "assets", "index-OC28VIMe.js")
data = open(b, encoding="utf-8").read()

pats = [
    r'children:"((?:[^"\\]|\\.)*)"',
    r'"aria-label":"((?:[^"\\]|\\.)*)"',
    r'"data-tooltip":"((?:[^"\\]|\\.)*)"',
    r'placeholder:"((?:[^"\\]|\\.)*)"',
    r'title:"((?:[^"\\]|\\.)*)"',
    r'label:"((?:[^"\\]|\\.)*)"',
    r'text:"((?:[^"\\]|\\.)*)"',
    r'message:"((?:[^"\\]|\\.)*)"',
    r'hint:"((?:[^"\\]|\\.)*)"',
    r'description:"((?:[^"\\]|\\.)*)"',
]
found = {}
for pat in pats:
    for m in re.finditer(pat, data):
        s = m.group(1)
        if re.search(r"[\u0400-\u04FF]", s):
            continue
        if len(s) < 3 or not re.search(r"[A-Za-z]", s):
            continue
        if re.match(r"^[a-z][a-z0-9-]*$", s):
            continue
        found[s] = found.get(s, 0) + 1

items = sorted(found.items(), key=lambda x: (-x[1], x[0]))
print("leftover UI-context English strings:", len(items))
for s, n in items:
    print(f"{n}x  {s}")
