import sys, time
import win32gui, win32con, win32api

hwnds = []
def cb(h, _):
    if win32gui.GetClassName(h) == "TForm1" and win32gui.IsWindowVisible(h):
        hwnds.append(h)
    return True
win32gui.EnumWindows(cb, None)
print("TForm1:", hwnds)
if not hwnds:
    sys.exit(1)
h = hwnds[0]
print("title:", win32gui.GetWindowText(h))
print("rect:", win32gui.GetWindowRect(h))
print("client:", win32gui.GetClientRect(h))
print("zoomed:", bool(win32gui.GetWindowLong(h, win32con.GWL_STYLE) & win32con.WS_MAXIMIZE))
print("foreground:", win32gui.GetForegroundWindow(), "(ccalc" , win32gui.GetForegroundWindow() == h, ")")
print("cursor:", win32api.GetCursorPos())

try:
    m = win32gui.GetMenu(h)
    print("menu hwnd:", m)
    n = win32gui.GetMenuItemCount(m) if m else 0
    print("items:", n)
    for i in range(n):
        try:
            txt = win32gui.GetMenuString(m, i, win32con.MF_BYPOSITION)
        except Exception as e:
            txt = f"<err {e}>"
        try:
            r = win32gui.GetMenuItemRect(h, m, i)
        except Exception as e:
            r = f"<err {e}>"
        print(f"  [{i}] {txt!r} rect={r}")
except Exception as e:
    print("menu error:", e)

# также system menu / children classes сверху окна
def child(hh, _):
    r = win32gui.GetWindowRect(hh)
    if r[1] < 200:
        print("child", win32gui.GetClassName(hh), win32gui.GetWindowText(hh)[:30], r)
    return True
win32gui.EnumChildWindows(h, child, None)
