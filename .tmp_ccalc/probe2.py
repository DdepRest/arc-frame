import time
import win32gui, win32api, win32con

h = win32gui.FindWindow("TForm1", None)
print("TForm1:", h)
win32gui.ShowWindow(h, win32con.SW_RESTORE)
win32gui.ShowWindow(h, win32con.SW_MAXIMIZE)
try:
    win32gui.SetForegroundWindow(h)
except Exception as e:
    print("fg err", e)
time.sleep(0.6)
print("rect:", win32gui.GetWindowRect(h), "enabled:", win32gui.IsWindowEnabled(h))


def dialogs(title):
    rows = []

    def cb(w, _):
        if win32gui.IsWindowVisible(w):
            cls = win32gui.GetClassName(w)
            if cls.startswith("T") and cls not in ("TApplication", "TaskListOverlayWnd"):
                rows.append((cls, win32gui.GetWindowText(w)[:40], win32gui.GetWindowRect(w)))
        return True

    win32gui.EnumWindows(cb, None)
    print(f"--- {title}: {len(rows)} окон")
    for r in rows:
        print("   ", r)


def click(x, y, settle=0.6):
    win32api.SetCursorPos((x, y))
    time.sleep(0.06)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.05)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(settle)


dialogs("до кликов")
print(">> клик по инструменту «Коробка (рама)» (22,157)")
click(22, 157)
dialogs("после клика по инструменту")
print(">> клик по холсту (700,400)")
click(700, 400, 1.2)
dialogs("после клика по холсту")
