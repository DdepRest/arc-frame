import time
import win32gui, win32api, win32con

h = win32gui.FindWindow("TForm1", None)


def click(x, y, settle=0.6):
    win32api.SetCursorPos((x, y))
    time.sleep(0.06)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.05)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(settle)


def dbl(x, y, settle=1.0):
    click(x, y, 0.06)
    click(x, y, settle)


def drag(x1, y1, x2, y2, settle=1.0):
    win32api.SetCursorPos((x1, y1))
    time.sleep(0.1)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.15)
    steps = 12
    for i in range(1, steps + 1):
        win32api.SetCursorPos((x1 + (x2 - x1) * i // steps, y1 + (y2 - y1) * i // steps))
        time.sleep(0.03)
    time.sleep(0.1)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(settle)


def dialogs(title):
    rows = []

    def cb(w, _):
        if win32gui.IsWindowVisible(w):
            cls = win32gui.GetClassName(w)
            if cls.startswith("T") or cls == "#32770":
                rows.append((cls, win32gui.GetWindowText(w)[:40], win32gui.GetWindowRect(w)))
        return True

    win32gui.EnumWindows(cb, None)
    print(f"--- {title}: {[r[0] for r in rows]}")
    for r in rows:
        if r[0] not in ("TForm1", "TApplication", "TaskManagerWindow", "TscShellContainerClass"):
            print("   ", r)


try:
    win32gui.SetForegroundWindow(h)
except Exception:
    pass
time.sleep(0.3)

print("A) инструмент «Створка» + двойной клик в правом проёме")
click(73, 157)
dbl(1220, 573, 1.2)
dialogs("A")

print("B) инструмент «Створка» + протяжка в правом проёме")
click(73, 157)
drag(1160, 330, 1260, 800, 1.2)
dialogs("B")
