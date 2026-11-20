import time
import win32gui, win32api, win32con

h = win32gui.FindWindow("TForm1", None)


def click(x, y, settle=0.8):
    win32api.SetCursorPos((x, y))
    time.sleep(0.06)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.05)
    win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(settle)


def dbl(x, y, settle=1.0):
    click(x, y, 0.06)
    click(x, y, settle)


def dialogs(title):
    rows = []

    def cb(w, _):
        if win32gui.IsWindowVisible(w):
            cls = win32gui.GetClassName(w)
            if cls.startswith("T") or cls == "#32770":
                rows.append((cls, win32gui.GetWindowText(w)[:40], win32gui.GetWindowRect(w)))
        return True

    win32gui.EnumWindows(cb, None)
    print(f"--- {title}:")
    for r in rows:
        print("   ", r)


try:
    win32gui.SetForegroundWindow(h)
except Exception:
    pass
time.sleep(0.4)

print(">> инструмент «Створка» (73,157)")
click(73, 157)
print(">> клик в правый проём (1220,573)")
click(1220, 573, 1.0)
dialogs("после клика створки")

print(">> двойной клик по створке (1220,573)")
dbl(1220, 573, 1.2)
dialogs("после двойного клика")
