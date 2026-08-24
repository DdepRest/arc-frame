#!/usr/bin/env python3
"""Apply RU translations (freebuff-ru/ru.py) to the Freebuff Desktop UI bundle.

Usage:
  python apply.py --dry-run   # only report, change nothing
  python apply.py             # back up, apply, verify
"""
import os
import re
import shutil
import sys
from collections import Counter
from datetime import datetime
from pathlib import Path

from ru import RU_BARE, RU_BY_INDEX, RU_HOME, RU_ORCH, RU_WORDS

BASE = Path(os.environ["LOCALAPPDATA"]) / "Programs" / "@codebufffreebuff-desktop" / "resources"
ASSETS = BASE / "orchestrator" / "ui" / "assets"
# Auto-detect the current UI bundle so updates that rename the hashed
# file (e.g. index-OC28VIMe.js) don't require editing this script.
_matches = sorted(ASSETS.glob("index-*.js"))
if not _matches:
    raise SystemExit(f"no index-*.js bundle found in {ASSETS}")
UI = _matches[0]
print("bundle:", UI.name)

SCAN_PATS = [
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
    r'\bgp\("((?:[^"\\]|\\.)*)"',
    r'\bil\("((?:[^"\\]|\\.)*)"',
]

HAS_CYR = re.compile(r"[\u0400-\u04FF]")


def noise(s):
    if len(s) < 2:
        return True
    if HAS_CYR.search(s):
        return True
    if not re.search(r"[A-Za-z]", s):
        return True
    if re.match(r"^[\d\-.%+×·•…]+$", s):
        return True
    if re.match(r"^[\W_]+$", s):
        return True
    if re.match(r"^[a-z][a-z0-9-]*$", s):
        return True
    if re.search(r"[{}\\]", s):
        return True
    if re.match(r"^[a-z0-9_.-]+(\.[a-z0-9]+)+$", s, re.I):
        return True
    return False


def extract(data):
    c = Counter()
    for pat in SCAN_PATS:
        c.update(re.findall(pat, data))
    items = [(s, n) for s, n in c.items() if not noise(s)]
    items.sort(key=lambda x: (-x[1], x[0]))
    return items


def display(raw):
    return raw.replace("\\'", "'").replace('\\"', '"').replace("\\n", "\n")


def asc(s):
    return s.encode("ascii", "replace").decode("ascii")


class _Tee:
    """Duplicate stdout to a log file (used in --auto mode).

    Under pythonw.exe there is no console, so sys.stdout may be None;
    the log file must still receive everything.
    """

    def __init__(self, stream, logf):
        self.stream = stream
        self.logf = logf

    def write(self, s):
        if self.stream is not None:
            self.stream.write(s)
        self.logf.write(s)
        self.logf.flush()

    def flush(self):
        if self.stream is not None:
            self.stream.flush()
        self.logf.flush()


def main():
    auto = "--auto" in sys.argv
    if auto:
        logf = (Path(__file__).parent / "apply.log").open("a", encoding="utf-8")
        logf.write(f"\n===== run {datetime.now().strftime('%Y-%m-%d %H:%M:%S')} =====\n")
        sys.stdout = _Tee(sys.stdout, logf)
    dry = "--dry-run" in sys.argv
    data = UI.read_text(encoding="utf-8")
    orig = data
    items = extract(data)
    by_display = {display(raw): (raw, n) for raw, n in items}

    # order by raw length descending
    pairs = []
    order = sorted(RU_BY_INDEX.items(), key=lambda kv: -len(items[kv[0]][0]) if kv[0] < len(items) else 0)
    # Index pass must be idempotent: match by the original raw literal, not by
    # current position in the (shrinking) candidate list. Load the original list.
    cand_file = Path(__file__).parent / "candidates_indexed.txt"
    raws = []
    for line in cand_file.read_text(encoding="utf-8").splitlines():
        parts = line.split("\t", 3)
        if len(parts) >= 3:
            raws.append(parts[2].replace("\\n", "\n"))
    missing = []
    for idx, ru in order:
        if idx >= len(raws):
            missing.append(idx)
            continue
        raw = raws[idx]
        if raw not in data:
            # already translated (or absent in this build) — skip silently
            continue
        pairs.append((raw, ru, display(raw)))

    if missing:
        print("WARNING: indices out of range:", missing)

    # --- dry run: dump every quoted occurrence of single-word keys for review ---
    single = [(raw, ru) for raw, ru, _ in pairs if re.match(r"^[A-Za-z]+$", raw)]
    if dry:
        print(f"=== single-word keys: {len(single)} ===")
        for raw, ru in single:
            occ = [m.start() for m in re.finditer(re.escape('"' + raw + '"'), data)]
            print(f"{asc(repr(raw))} -> {asc(repr(ru))}  occurrences: {len(occ)}")
            for i in occ[:4]:
                print("     ", asc(repr(data[max(0, i - 40) : i + 40])))
        print(f"=== multi-word keys: {len(pairs) - len(single)} ===")
        for raw, ru, _ in pairs:
            if re.match(r"^[A-Za-z]+$", raw):
                continue
            n = data.count('"' + raw + '"')
            if n == 0:
                print("NO-MATCH", asc(repr(raw)), "->", asc(repr(ru)))
        return

    # --- apply ---
    bak = UI.with_suffix(UI.suffix + ".bak-ru2")
    if not bak.exists():
        shutil.copy2(UI, bak)
        print("backup:", bak)

    def dangerous(word):
        checks = [
            '==="' + word + '"',
            '!== "' + word + '"',
            '== "' + word + '"',
            '!= "' + word + '"',
            '.includes("' + word + '")',
            '.indexOf("' + word + '")',
            'case "' + word + '":',
            'value:"' + word + '"',
        ]
        for c in checks:
            if c in data:
                return True, c
        return False, None

    for en, ru in RU_BARE.items():
        d, c = dangerous(en)
        if d:
            print("SKIP (dangerous)", asc(repr(en)), c)
            continue
        old = '"' + en + '"'
        n = data.count(old)
        data = data.replace(old, '"' + ru + '"')
        if n:
            print(f"bare {n}x: {asc(en[:60])}")

    for en, ru in RU_WORDS.items():
        d, c = dangerous(en)
        if d:
            print("SKIP (dangerous)", asc(repr(en)), c)
            continue
        old = '"' + en + '"'
        n = data.count(old)
        data = data.replace(old, '"' + ru + '"')
        if n:
            print(f"word {n}x: {asc(en)}")

    for old, new in RU_HOME.items():
        n = data.count(old)
        data = data.replace(old, new)
        if n:
            print(f"home {n}x: {asc(old)}")
    ctx_pat = [
        'children:"{r}"',
        '"aria-label":"{r}"',
        '"data-tooltip":"{r}"',
        'placeholder:"{r}"',
        'title:"{r}"',
        'label:"{r}"',
        'text:"{r}"',
        'message:"{r}"',
        'hint:"{r}"',
        'description:"{r}"',
        'gp("{r}")',
        'il("{r}")',
    ]

    report = []
    for raw, ru, disp in pairs:
        if re.match(r"^[A-Za-z]+$", raw):
            done = 0
            for pat in ctx_pat:
                old = pat.format(r=raw)
                done += data.count(old)
                data = data.replace(old, pat.format(r=ru))
        else:
            old = '"' + raw + '"'
            done = data.count(old)
            data = data.replace(old, '"' + ru + '"')
        report.append((disp, ru, done))

    if data != orig:
        UI.write_text(data, encoding="utf-8")
        print("UI bundle updated")
    else:
        print("UI bundle unchanged (already translated)")
    print("applied", len(pairs), "strings")
    for disp, ru, done in report:
        # done==0: nothing matched now. If the Russian text is already present,
        # the string was translated on an earlier run — not a real miss.
        if done == 0 and ru not in data:
            print("ZERO:", asc(repr(disp)), "->", asc(ru))

    # --- orchestrator.js: mission default prompt + effort labels ---
    ORCH = BASE / "orchestrator" / "orchestrator.js"
    od = ORCH.read_text(encoding="utf-8")
    obak = ORCH.with_suffix(ORCH.suffix + ".bak-ru2")
    if not obak.exists():
        shutil.copy2(ORCH, obak)
        print("backup:", obak)
    for old, new in RU_ORCH.items():
        n = od.count(old)
        if n:
            od = od.replace(old, new)
            print(f"orch {n}x: {asc(old[:60])}")
        else:
            print("orch NO-MATCH:", asc(repr(old)))
    if od != ORCH.read_text(encoding="utf-8"):
        ORCH.write_text(od, encoding="utf-8")
        print("orchestrator.js updated")
    else:
        print("orchestrator.js unchanged (already translated)")

    # --- verify both files are still valid JS ---
    import subprocess
    for f in (UI, ORCH):
        r = subprocess.run(["node", "--check", str(f)], capture_output=True, text=True)
        if r.returncode == 0:
            print("JS OK:", f.name)
        else:
            print("JS INVALID:", f.name)
            print((r.stderr or "")[:1500])

    # --- leftover English UI strings (informational; new strings from an app
    # update that the dictionary doesn't cover yet will show up here) ---
    leftover = [s for s, _ in extract(data) if not HAS_CYR.search(s)]
    if leftover:
        print(f"leftover English UI strings: {len(leftover)}")
        for s in leftover[:60]:
            print("   ", asc(s))
    print("done")


if __name__ == "__main__":
    main()
