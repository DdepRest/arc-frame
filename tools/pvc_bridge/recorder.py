"""
Action Recorder for CCalc 9.12
Records mouse clicks and keyboard inputs relative to CCalc window.
Press F10 to stop recording and save trace.
"""

import os
import sys
import time
import json
import ctypes
from ctypes import wintypes

user32 = ctypes.windll.user32

try:
    import win32gui
    import win32con
    import win32api
    import win32service
    desk = win32service.OpenInputDesktop(0, False, 0x01ff)
    desk.SetThreadDesktop()
except Exception:
    pass

def find_ccalc_hwnd():
    found = []
    def cb(h, _):
        if win32gui.IsWindowVisible(h):
            cls = win32gui.GetClassName(h)
            if cls == "TForm1":
                found.append(h)
        return True
    try:
        win32gui.EnumWindows(cb, None)
    except Exception:
        pass
    return found[0] if found else None

def main():
    print("=" * 60)
    print(" РЕКОРДЕР ДЕЙСТВИЙ В CCALC 9.12 ДЛЯ A.R.C. FRAME")
    print("=" * 60)
    
    hwnd = find_ccalc_hwnd()
    if not hwnd:
        print("\n[!] Окно CCalc не найдено! Сначала откройте CCalc 9.12.")
        input("\nНажмите Enter для выхода...")
        return

    txt = win32gui.GetWindowText(hwnd)
    print(f"\n[OK] Найдено окно CCalc: '{txt}' (HWND: {hwnd})")
    print("\nИнструкция:")
    print("1. Переключитесь в окно CCalc.")
    print("2. Спокойно постройте окно 1060х1340 от начала до конца:")
    print("   - Вкладка 'Чертёж' -> Рама (Квадрат) -> введите размеры")
    print("   - Импост -> Створка -> Подоконник")
    print("   - Вкладка 'Лист заказа' -> Нажмите 'Пересчитать заказ'")
    print("3. Как только закончите, нажмите клавишу [F10] на клавиатуре!")
    print("\nЗапись начнётся через 3 секунды...")
    time.sleep(3)
    print("\n>>> ИДЁТ ЗАПИСЬ ДЕЙСТВИЙ (нажмите F10 для завершения) <<<")

    actions = []
    prev_lbutton = False
    start_time = time.time()

    try:
        while True:
            # Check F10 key to stop
            if user32.GetAsyncKeyState(0x79) & 0x8000: # VK_F10 = 0x79
                print("\n[OK] Нажата клавиша F10. Остановка записи...")
                break

            # Check left mouse button
            lbutton_down = bool(user32.GetAsyncKeyState(0x01) & 0x8000) # VK_LBUTTON = 0x01
            
            if lbutton_down and not prev_lbutton:
                # Click event occurred
                pt = wintypes.POINT()
                user32.GetCursorPos(ctypes.byref(pt))
                
                # Check active window
                fg_hwnd = user32.GetForegroundWindow()
                fg_title = win32gui.GetWindowText(fg_hwnd)
                fg_cls = win32gui.GetClassName(fg_hwnd)
                
                # Coordinates relative to active window
                wrect = win32gui.GetWindowRect(fg_hwnd)
                rel_x = pt.x - wrect[0]
                rel_y = pt.y - wrect[1]
                
                # Control under mouse
                child_h = user32.WindowFromPoint(pt)
                ch_cls = win32gui.GetClassName(child_h) if child_h else ""
                ch_txt = win32gui.GetWindowText(child_h) if child_h else ""

                evt = {
                    "time_offset_s": round(time.time() - start_time, 2),
                    "action": "click",
                    "screen_x": pt.x,
                    "screen_y": pt.y,
                    "rel_x": rel_x,
                    "rel_y": rel_y,
                    "window_title": fg_title,
                    "window_class": fg_cls,
                    "control_class": ch_cls,
                    "control_text": ch_txt[:30]
                }
                actions.append(evt)
                print(f"[{evt['time_offset_s']}s] Клик: X={rel_x}, Y={rel_y} | Окно: '{fg_title[:20]}' | Элемент: {ch_cls}")

            prev_lbutton = lbutton_down
            time.sleep(0.02) # 50Hz polling
    except KeyboardInterrupt:
        print("\nЗапись прервана пользователем.")

    out_file = os.path.join(os.path.dirname(__file__), "recorded_trace.json")
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump(actions, f, ensure_ascii=False, indent=2)

    print(f"\n[УСПЕХ] Записано {len(actions)} действий!")
    print(f"Файл сохранён: {out_file}")
    input("\nНажмите Enter, чтобы закрыть...")

if __name__ == "__main__":
    main()
