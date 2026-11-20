import json

with open(r'tools/pvc_bridge/recorded_trace.json', 'r', encoding='utf-8') as f:
    events = json.load(f)

print(f"Total events: {len(events)}")

for idx, e in enumerate(events):
    t = e['time_offset_s']
    w_title = e['window_title']
    w_cls = e['window_class']
    c_cls = e['control_class']
    c_txt = e['control_text']
    x, y = e['rel_x'], e['rel_y']
    sx, sy = e['screen_x'], e['screen_y']
    label = e.get("label")
    line = (f"#{idx:02d} [{t:5.1f}s] Window: '{w_title}' ({w_cls}) | "
            f"Ctrl: {c_cls} '{c_txt}' | rel=({x}, {y}) screen=({sx}, {sy})")
    if label:
        line += f" | label='{label}'"
    print(line)
