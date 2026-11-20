import win32gui
import win32service
import ctypes
from ctypes import wintypes

user32 = ctypes.windll.user32

try:
    desk = win32service.OpenInputDesktop(0, False, 0x01ff)
    desk.SetThreadDesktop()
except:
    pass

hwnd = None
def cb(h, _):
    global hwnd
    if win32gui.IsWindowVisible(h):
        cls = win32gui.GetClassName(h)
        if cls == "TForm1":
            hwnd = h
    return True

win32gui.EnumWindows(cb, None)
print("HWND:", hwnd)

def get_menu_text(hmenu, pos):
    buf = ctypes.create_unicode_buffer(256)
    user32.GetMenuStringW(hmenu, pos, buf, 256, 0x0400) # MF_BYPOSITION = 0x0400
    return buf.value

if hwnd:
    hmenu = user32.GetMenu(hwnd)
    count = user32.GetMenuItemCount(hmenu)
    print("Menu item count:", count)
    
    for i in range(count):
        txt = get_menu_text(hmenu, i)
        print(f"Top menu {i}: {txt}")
        hsub = user32.GetSubMenu(hmenu, i)
        if hsub:
            subcount = user32.GetMenuItemCount(hsub)
            for j in range(subcount):
                stxt = get_menu_text(hsub, j)
                mid = user32.GetMenuItemID(hsub, j)
                print(f"   Sub {j} (ID {mid}): {stxt}")
