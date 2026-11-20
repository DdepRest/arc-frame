import time
import win32gui, win32api

# Клетки палитры (экранные координаты, вычислены по скриншоту)
POINTS = []
for y in (112, 136, 157, 174, 195, 218, 235, 255, 273, 305):
    for x in (22, 49, 73, 96, 119):
        POINTS.append((x, y))
for y in (342, 362, 381, 400, 416, 427):
    for x in (22, 49, 73, 96, 119):
        POINTS.append((x, y))

hint_classes = ("THintWindow", "#32768", "TToolTip", "tooltips_class32")


def visible_top_level():
    out = {}

    def cb(h, _):
        if win32gui.IsWindowVisible(h):
            out[h] = (win32gui.GetClassName(h), win32gui.GetWindowText(h), win32gui.GetWindowRect(h))
        return True

    win32gui.EnumWindows(cb, None)
    return out


for (x, y) in POINTS:
    before = visible_top_level()
    win32api.SetCursorPos((x, y))
    time.sleep(0.05)
    win32api.SetCursorPos((x + 2, y))
    time.sleep(1.1)
    after = visible_top_level()
    new = [(h, v) for h, v in after.items() if h not in before]
    label = ""
    for h, (cls, txt, rect) in new:
        if cls in hint_classes or "hint" in cls.lower() or "tip" in cls.lower():
            label = f"{cls}:{txt}"
    print(f"({x},{y}) -> {label!r}", flush=True)
    win32api.SetCursorPos((900, 700))
    time.sleep(0.15)
