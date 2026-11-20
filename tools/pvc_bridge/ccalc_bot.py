"""
CCalc RPA Automation Bridge for A.R.C. Frame
Controls "Расчет Конструкций 9.12 Plus Dealer (Deceuninck)" at 1920x1080.
Outputs clean JSON to stdout for A.R.C. Frame integration.
"""

import sys
import os
import time
import json
import argparse
import subprocess
import ctypes
from ctypes import wintypes

# Ensure stdout and stderr always output UTF-8 so Russian text is never corrupted
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

try:
    import win32gui
    import win32con
    import win32process
    import win32api
except ImportError:
    pass

PROG_PATH = r"C:\Program Files\ПРОФИПЛАСТ\Программа135\Программа\ProgMain9.12.Dealer+_чистый.exe"
PROG_DIR = r"C:\Program Files\ПРОФИПЛАСТ\Программа135\Программа"

# --- Диагностика и типы ошибок RPA ------------------------------------------
# Весь отладочный вывод идёт в stderr: stdout зарезервирован под JSON-ответ,
# который разбирает PvcAutoCalcService в C#.


def _log(msg: str) -> None:
    """Пишет диагностическое сообщение в stderr, не ломая JSON в stdout."""
    try:
        print(f"[ccalc_bot] {msg}", file=sys.stderr, flush=True)
    except Exception:
        pass


class CCalcStageError(RuntimeError):
    """Шаг RPA не удался, но его имеет смысл повторить."""


class CCalcNoOrderError(CCalcStageError):
    """CCalc сообщил об отсутствии заказа: нужен новый заказ и повтор с рамы."""


# --- Win32: межпроцессное чтение/запись текста -------------------------------
_user32 = ctypes.WinDLL("user32", use_last_error=True)
try:
    _user32.SendMessageTimeoutW.argtypes = [
        wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM,
        wintypes.UINT, wintypes.UINT, ctypes.POINTER(ctypes.c_size_t)]
    _user32.SendMessageTimeoutW.restype = ctypes.c_size_t
    _user32.SendMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    _user32.SendMessageW.restype = ctypes.c_ssize_t
except Exception:
    pass

_SMTO_ABORTIFHUNG = 0x0002
_WM_GETTEXT = 0x000D
_LB_GETITEMRECT = 0x0198

# --- Палитра инструментов вкладки «Чертёж» -----------------------------------
# Координаты — в системе рекордера (экран = угол окна + rel), проверены реальными
# подсказками THintWindow при развёрнутом окне, а не «на глаз» по треку:
# трек писал rel-координаты, а бот применял их как экранные (сдвиг на 8 px) и
# часть кликов попадала в соседние кнопки.
# Карта палитры снята experimentально: клик по ячейке включает тип элемента,
# который видно в верхнем ComboBox. Экранные центры → координаты рекордера (+8):
#   рама «рама оконная (62 мм)» (22,158); створка «створка оконная 76мм» (76,158);
#   импост «Импост 82 мм» (49,182); сетка «ПМС Anwis» (124,234).
TOOL_FRAME = (30, 166)       # «Коробка (рама)» — рамка окна
TOOL_IMPOST_V = (84, 190)    # «Импост вертикальный» (проверено: клик по канвасу даёт TKolvoDlg)
TOOL_SASH = (84, 166)        # «Створка»
TOOL_SELECT = (30, 389)      # «Выделение объекта» — снять активный инструмент
TOOL_EXTRA = (127, 263)      # «Установочные и дополнительные элементы»
TOOL_NET = (132, 242)        # «Москитная сетка»
TOOL_HANDLE = (132, 166)     # «Ручка HOPPE» (экран (124,158))
TOOL_GLASS = (111, 214)      # «CПД-32 (4-10-4-10-4)» — стеклопакет (экран (103,206))
TOOL_GLASS_BEAD = (114, 268) # «стеклопакет сразу со штапиками» (экран (106,260))
TOOL_HARDWARE = (111, 166)   # «VORNE пов-отк (Variat)» — фурнитура (экран (103,158))
# Клик по световому проёму правой части рамы (из трека)
CANVAS_SASH_POINT = (1068, 325)
CANVAS_CENTER_POINT = (1102, 485)
CANVAS_FRAME_POINT = (714, 433)


# В интерфейсе CCalc часть текстов набрана латинскими двойниками кириллицы
# («Cтворка oк.», «Paмa oкoннaя»), поэтому прямое сравнение не находит пункты.
_LOOKALIKE = str.maketrans({
    "a": "а", "c": "с", "e": "е", "o": "о", "p": "р", "x": "х",
    "y": "у", "k": "к", "m": "м", "t": "т", "h": "н", "b": "в", "n": "п",
})


def _norm_text(text: str) -> str:
    """Приводит текст к сравнимому виду: нижний регистр, двойники → кириллица."""
    return " ".join((text or "").lower().translate(_LOOKALIKE).split())


def _msg_text(hwnd, limit: int = 1024) -> str:
    """Читает текст контрола через WM_GETTEXT (работает и для чужого процесса)."""
    if not hwnd:
        return ""
    try:
        buf = ctypes.create_unicode_buffer(limit)
        res = ctypes.c_size_t()
        _user32.SendMessageTimeoutW(
            hwnd, _WM_GETTEXT, limit, ctypes.cast(buf, ctypes.c_void_p).value,
            _SMTO_ABORTIFHUNG, 400, ctypes.byref(res))
        return buf.value
    except Exception:
        try:
            return win32gui.GetWindowText(hwnd)
        except Exception:
            return ""


def _msg_set_text(hwnd, text) -> bool:
    """Пишет текст в контрол через WM_SETTEXT (Unicode, чужой процесс)."""
    if not hwnd:
        return False
    try:
        buf = ctypes.create_unicode_buffer(str(text))
        _user32.SendMessageW(hwnd, win32con.WM_SETTEXT, 0, ctypes.cast(buf, ctypes.c_void_p).value)
        return True
    except Exception:
        return False


class CCalcBot:
    def __init__(self, dry_run=False, prog_path=None, username="окно", password="1234"):
        self.dry_run = dry_run
        self.hwnd = None
        self.order_data = {}
        self.username = username or "окно"
        self.password = password or "1234"
        self.prog_path = prog_path if prog_path and os.path.exists(prog_path) else PROG_PATH
        self.prog_dir = os.path.dirname(self.prog_path) if os.path.exists(self.prog_path) else PROG_DIR
        # Состояние самовосстановления (см. dismiss_all_blocking_dialogs)
        self._no_order_recovered = False
        self._recovering = False

    def _attach_desktop(self):
        try:
            import win32service
            desk = win32service.OpenInputDesktop(0, False, 0x01ff)
            desk.SetThreadDesktop()
        except Exception:
            pass

    def find_ccalc_window(self):
        """Find running CCalc TForm1 main window (strictly TForm1, never Splash or Login dialogs)."""
        self._attach_desktop()
        found = []
        def enum_cb(h, _):
            cls = win32gui.GetClassName(h)
            txt = win32gui.GetWindowText(h)
            # Exclude background/splash/dialog windows
            if cls in ("TApplication", "TSplashForm", "TLoginDlg", "THintDlg", "TDialog1", "TZakParDlg"):
                return True
            # Matches main CCalc window strictly
            if cls == "TForm1":
                found.append(h)
            return True
        try:
            win32gui.EnumWindows(enum_cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def find_splash_window(self):
        """Find CCalc TSplashForm startup screen."""
        self._attach_desktop()
        found = []
        def enum_cb(h, _):
            if win32gui.IsWindowVisible(h):
                cls = win32gui.GetClassName(h)
                if cls == "TSplashForm":
                    found.append(h)
            return True
        try:
            win32gui.EnumWindows(enum_cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def find_login_dialog(self):
        """Find CCalc modal login window (TLoginDlg or dialog containing login fields)."""
        self._attach_desktop()
        found = []
        def enum_cb(h, _):
            if not win32gui.IsWindowVisible(h):
                return True
            cls = win32gui.GetClassName(h)
            txt = win32gui.GetWindowText(h)
            if cls == "TLoginDlg":
                found.append(h)
                return True
            if cls in ("TDialog1", "TLoginForm", "TLogin", "TPasswordDlg"):
                found.append(h)
            elif "Login" in cls or any(k in txt.lower() for k in ["пароль", "вход", "авторизац"]):
                # Check if it has child edit controls
                edits = []
                def ch_cb(ch, _):
                    ccls = win32gui.GetClassName(ch)
                    if "edit" in ccls.lower():
                        edits.append(ch)
                    return True
                win32gui.EnumChildWindows(h, ch_cb, None)
                if len(edits) >= 1:
                    found.append(h)
            return True
        try:
            win32gui.EnumWindows(enum_cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def dismiss_error_dialog(self):
        """Dismisses any modal Access Violation or Error dialogs."""
        self._attach_desktop()
        def enum_cb(h, _):
            if win32gui.IsWindowVisible(h):
                txt = win32gui.GetWindowText(h).lower()
                cls = win32gui.GetClassName(h)
                if "error" in txt or "violation" in txt or "ошибка" in txt:
                    try:
                        win32gui.PostMessage(h, win32con.WM_KEYDOWN, win32con.VK_RETURN, 0)
                        win32gui.PostMessage(h, win32con.WM_KEYUP, win32con.VK_RETURN, 0)
                        win32gui.PostMessage(h, win32con.WM_CLOSE, 0, 0)
                    except Exception:
                        pass
                def ch_cb(ch, _):
                    ctxt = win32gui.GetWindowText(ch).lower()
                    if "access violation" in ctxt or "read of address" in ctxt:
                        try:
                            win32gui.PostMessage(h, win32con.WM_KEYDOWN, win32con.VK_RETURN, 0)
                            win32gui.PostMessage(h, win32con.WM_KEYUP, win32con.VK_RETURN, 0)
                        except Exception:
                            pass
                    return True
                win32gui.EnumChildWindows(h, ch_cb, None)
            return True
        try:
            win32gui.EnumWindows(enum_cb, None)
        except Exception:
            pass

    def dismiss_hint_dialog(self):
        """Dismisses THintDlg (Tip of the Day / 'Обратите внимание') if it appears."""
        self._attach_desktop()
        def enum_cb(h, _):
            if win32gui.IsWindowVisible(h):
                cls = win32gui.GetClassName(h)
                txt = win32gui.GetWindowText(h).lower()
                if cls == "THintDlg" or "обратите внимание" in txt or "совет" in txt:
                    try:
                        win32gui.PostMessage(h, win32con.WM_KEYDOWN, win32con.VK_ESCAPE, 0)
                        win32gui.PostMessage(h, win32con.WM_KEYUP, win32con.VK_ESCAPE, 0)
                        win32gui.PostMessage(h, win32con.WM_CLOSE, 0, 0)
                    except Exception:
                        pass
            return True
        try:
            win32gui.EnumWindows(enum_cb, None)
        except Exception:
            pass

    def handle_login(self, login_hwnd):
        """Fills in User & Password and clicks OK on TLoginDlg."""
        self._attach_desktop()
        try:
            win32gui.ShowWindow(login_hwnd, win32con.SW_SHOWNORMAL)
            time.sleep(0.1)
            win32gui.SetForegroundWindow(login_hwnd)
            time.sleep(0.1)
        except Exception:
            pass

        edits = []
        buttons = []

        def enum_children(h, _):
            cls = win32gui.GetClassName(h)
            rect = win32gui.GetWindowRect(h)
            txt = win32gui.GetWindowText(h)
            if "edit" in cls.lower():
                edits.append((h, rect[1]))  # sort by top Y
            elif any(b in cls.lower() for b in ["button", "btn"]):
                buttons.append((h, rect[1], txt))
            return True

        try:
            win32gui.EnumChildWindows(login_hwnd, enum_children, None)
        except Exception:
            pass

        edits.sort(key=lambda x: x[1])

        user_val = self.username or "окно"
        pass_val = self.password or "1234"

        def set_control_text(ctrl_hwnd, text):
            try:
                win32gui.SetForegroundWindow(login_hwnd)
                rect = win32gui.GetWindowRect(ctrl_hwnd)
                lParam = win32api.MAKELONG(max(5, (rect[2] - rect[0]) // 2), max(5, (rect[3] - rect[1]) // 2))
                win32gui.PostMessage(ctrl_hwnd, win32con.WM_LBUTTONDOWN, win32con.MK_LBUTTON, lParam)
                time.sleep(0.05)
                win32gui.PostMessage(ctrl_hwnd, win32con.WM_LBUTTONUP, 0, lParam)
                time.sleep(0.05)
                win32gui.SendMessage(ctrl_hwnd, win32con.WM_SETFOCUS, 0, 0)
                time.sleep(0.05)
            except Exception:
                pass

            # 1. Select all
            win32gui.SendMessage(ctrl_hwnd, win32con.EM_SETSEL, 0, -1)
            time.sleep(0.05)

            # 2. Paste via clipboard (universal for ANSI Delphi TEdit)
            pasted = False
            try:
                import win32clipboard
                win32clipboard.OpenClipboard()
                win32clipboard.EmptyClipboard()
                win32clipboard.SetClipboardText(text, win32clipboard.CF_UNICODETEXT)
                win32clipboard.CloseClipboard()
                time.sleep(0.05)
                win32gui.SendMessage(ctrl_hwnd, win32con.WM_PASTE, 0, 0)
                time.sleep(0.05)
                pasted = True
            except Exception:
                pass

            # 3. Fallback WM_SETTEXT
            try:
                curr_txt = win32gui.GetWindowText(ctrl_hwnd)
                if not curr_txt:
                    win32gui.SendMessage(ctrl_hwnd, win32con.WM_SETTEXT, 0, text)
            except Exception:
                pass

            # 4. Notify parent form of EN_CHANGE
            try:
                cid = win32gui.GetDlgCtrlID(ctrl_hwnd)
                win32gui.SendMessage(login_hwnd, win32con.WM_COMMAND, win32api.MAKELONG(cid, win32con.EN_CHANGE), ctrl_hwnd)
            except Exception:
                pass

        # In TLoginDlg:
        # Edit 1 (top): Username ("окно")
        # Edit 2: Password ("1234")
        if len(edits) >= 2:
            set_control_text(edits[0][0], user_val)
            set_control_text(edits[1][0], pass_val)
            if len(edits) >= 3:
                set_control_text(edits[2][0], pass_val)
        elif len(edits) == 1:
            set_control_text(edits[0][0], pass_val)

        # Locate OK button
        btn_h = None
        if buttons:
            for bh, _, btxt in buttons:
                if any(k in btxt.lower() for k in ["ок", "ok", "войти", "вход"]):
                    btn_h = bh
                    break
            if not btn_h:
                btn_h = buttons[0][0]

        if btn_h:
            try:
                win32gui.SendMessage(btn_h, win32con.BM_CLICK, 0, 0)
            except Exception:
                pass

        try:
            win32gui.SendMessage(login_hwnd, win32con.WM_COMMAND, 1, btn_h if btn_h else 0)
        except Exception:
            pass

        # Send Return to password field and dialog
        if len(edits) >= 2:
            try:
                win32gui.PostMessage(edits[1][0], win32con.WM_KEYDOWN, win32con.VK_RETURN, 0)
                win32gui.PostMessage(edits[1][0], win32con.WM_KEYUP, win32con.VK_RETURN, 0)
            except Exception:
                pass
        try:
            win32gui.PostMessage(login_hwnd, win32con.WM_KEYDOWN, win32con.VK_RETURN, 0)
            win32gui.PostMessage(login_hwnd, win32con.WM_KEYUP, win32con.VK_RETURN, 0)
        except Exception:
            pass

        time.sleep(1.0)
        self.dismiss_hint_dialog()

    def launch_ccalc(self):
        """Launches CCalc using ShellExecute, os.startfile, or subprocess."""
        if not os.path.exists(self.prog_path):
            raise FileNotFoundError(f"CCalc executable not found at: {self.prog_path}")

        try:
            res = win32api.ShellExecute(0, "open", self.prog_path, "", self.prog_dir, 1)
            if res > 32:
                return True
        except Exception:
            pass

        try:
            os.startfile(self.prog_path)
            return True
        except Exception:
            pass

        try:
            subprocess.Popen([self.prog_path], cwd=self.prog_dir)
            return True
        except Exception:
            pass

        return False

    def ensure_running(self):
        """Ensure CCalc is running, logged in, and brought to foreground."""
        if self.dry_run:
            return True

        self._attach_desktop()

        # 1. Handle any existing visible login dialog first
        login_h = self.find_login_dialog()
        if login_h:
            self.handle_login(login_h)
            time.sleep(1.0)
            self.dismiss_hint_dialog()

        # 2. Check if TForm1 is already open and ready (not in splash or login phase)
        if not self.find_splash_window() and not self.find_login_dialog():
            self.hwnd = self.find_ccalc_window()
        else:
            self.hwnd = None

        # 3. If not ready, launch CCalc if needed and wait for login/main window
        if not self.hwnd:
            if not self.find_splash_window() and not self.find_login_dialog():
                self.launch_ccalc()

            # CCalc splash screen and DB initialization can take up to 25 seconds
            for _ in range(60):
                time.sleep(1)
                self.dismiss_error_dialog()
                login_h = self.find_login_dialog()
                if login_h:
                    self.handle_login(login_h)
                    time.sleep(1.0)

                self.dismiss_hint_dialog()
                if not self.find_splash_window() and not self.find_login_dialog():
                    self.hwnd = self.find_ccalc_window()
                    if self.hwnd:
                        break

        # 4. Final check for login or hint dialogs
        self.dismiss_error_dialog()
        self.dismiss_hint_dialog()
        login_h = self.find_login_dialog()
        if login_h:
            self.handle_login(login_h)
            time.sleep(1.0)
            self.dismiss_hint_dialog()

        self.hwnd = self.find_ccalc_window()
        if not self.hwnd:
            raise RuntimeError("Не удалось запустить или обнаружить главное окно CCalc 9.12 (TForm1).")

        # Restore and bring to foreground
        try:
            win32gui.ShowWindow(self.hwnd, win32con.SW_RESTORE)
            win32gui.ShowWindow(self.hwnd, win32con.SW_MAXIMIZE)
            time.sleep(0.5)
            win32gui.SetForegroundWindow(self.hwnd)
        except Exception:
            pass
        return True

    def _find_and_click_button(self, dlg_hwnd, keywords=("ok", "ок", "да", "yes")):
        """Finds first visible button matching keywords in dialog and clicks it."""
        buttons = []
        def cb(h, _):
            if win32gui.IsWindowVisible(h) and "button" in win32gui.GetClassName(h).lower():
                buttons.append((h, win32gui.GetWindowText(h).lower()))
            return True
        try:
            win32gui.EnumChildWindows(dlg_hwnd, cb, None)
        except Exception:
            pass
        for h, txt in buttons:
            if any(k in txt for k in keywords):
                # В CCalc «создайте сначала заказ» — TaskDialog (DirectUIHWND):
                # его кнопки игнорируют BM_CLICK, нужен реальный клик мышью.
                self._real_click(h)
                return h
        # fallback: press Enter
        win32gui.PostMessage(dlg_hwnd, win32con.WM_KEYDOWN, win32con.VK_RETURN, 0)
        win32gui.PostMessage(dlg_hwnd, win32con.WM_KEYUP, win32con.VK_RETURN, 0)
        return None

    def _real_click(self, hwnd) -> bool:
        """Реальный клик мышью по центру контрола (для DirectUI/TaskDialog)."""
        if not hwnd:
            return False
        try:
            left, top, right, bottom = win32gui.GetWindowRect(hwnd)
        except Exception:
            return False
        x, y = (left + right) // 2, (top + bottom) // 2
        try:
            win32api.SetCursorPos((x, y))
            time.sleep(0.12)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
            time.sleep(0.06)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            time.sleep(0.25)
            return True
        except Exception as e:
            _log(f"реальный клик по {hwnd} не удался: {e}")
            return False

    def _find_dialog_by_class(self, cls_name, timeout=3.0):
        """Returns hwnd of first visible top-level window with given class, or None."""
        deadline = time.time() + timeout
        while time.time() < deadline:
            found = []
            def cb(h, _):
                if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) == cls_name:
                    found.append(h)
                return True
            try:
                win32gui.EnumWindows(cb, None)
            except Exception:
                pass
            if found:
                return found[0]
            time.sleep(0.15)
        return None

    def _find_any_msgbox(self, timeout=2.0):
        """Returns hwnd of first visible #32770 MessageBox, or None."""
        deadline = time.time() + timeout
        while time.time() < deadline:
            found = []
            def cb(h, _):
                if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) == "#32770":
                    found.append(h)
                return True
            try:
                win32gui.EnumWindows(cb, None)
            except Exception:
                pass
            if found:
                return found[0]
            time.sleep(0.15)
        return None

    def _is_task_dialog(self, dlg_hwnd) -> bool:
        """True, если #32770 — современный TaskDialog (внутри DirectUIHWND).

        У такого окна текст живёт в DirectUI и недоступен через WM_GETTEXT,
        поэтому узнаём его по структуре, а не по тексту.
        """
        if not dlg_hwnd:
            return False
        marks = []

        def cb(h, _):
            cls = win32gui.GetClassName(h)
            if cls in ("DirectUIHWND", "CtrlNotifySink") or cls.startswith("ScrollBar"):
                marks.append(cls)
            return True

        try:
            win32gui.EnumChildWindows(dlg_hwnd, cb, None)
        except Exception:
            pass
        return "DirectUIHWND" in marks

    def _click_abs(self, x: int, y: int, settle: float = 0.15):
        """Клик по абсолютным экранным координатам без активации главного окна.

        Активация через SetForegroundWindow здесь запрещена: она закрывает
        уже открытое выпадающее меню #32768.
        """
        try:
            win32api.SetCursorPos((int(x), int(y)))
            time.sleep(0.05)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
            time.sleep(0.04)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            time.sleep(settle)
        except Exception as e:
            _log(f"клик по ({x},{y}) не удался: {e}")

    def _dialog_body_text(self, dlg_hwnd) -> str:
        """Собирает заголовок и текст всех дочерних контролов диалога в нижнем регистре."""
        if not dlg_hwnd:
            return ""
        parts = []
        try:
            parts.append(win32gui.GetWindowText(dlg_hwnd).lower())
        except Exception:
            pass

        def cb(h, _):
            t = _msg_text(h) or win32gui.GetWindowText(h)
            if t:
                parts.append(t.lower())
            return True

        try:
            win32gui.EnumChildWindows(dlg_hwnd, cb, None)
        except Exception:
            pass
        return " ".join(parts)

    def _visible_popup_menu(self, timeout: float = 0.8):
        """Возвращает hwnd открытого системного меню (#32768), если оно есть."""
        deadline = time.time() + timeout
        while time.time() < deadline:
            found = []

            def cb(h, _):
                if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) == "#32768":
                    found.append(h)
                return True

            try:
                win32gui.EnumWindows(cb, None)
            except Exception:
                pass
            if found:
                return found[0]
            time.sleep(0.05)
        return None

    def _consume_no_order_recovery(self) -> bool:
        """True, если при последнем разборе диалогов CCalc потребовал создать заказ."""
        flag = self._no_order_recovered
        self._no_order_recovered = False
        return flag

    def dismiss_all_blocking_dialogs(self) -> bool:
        """
        Разбирает блокирующие окна CCalc: #32770 (включая «Перед тем как работать
        с конструкцией, создайте сначала заказ» и «Сохранить заказ?»),
        TKonstrNamesEnterDlg, THintDlg, TShowPrint и диалоги Access Violation.

        Возвращает True, если CCalc потребовал новый заказ: вызывающий шаг должен
        быть перезапущен с построения рамы (см. CCalcNoOrderError).
        """
        for _ in range(8):
            dismissed = False

            # 1. #32770 — стандартные MessageBox
            msgbox = self._find_any_msgbox(timeout=0.4)
            if msgbox:
                body = self._dialog_body_text(msgbox)
                known = any(k in body for k in
                            ("перед тем", "создайте сначала", "создать заказ",
                             "сохранить", "желаете"))
                if not known and self._is_task_dialog(msgbox):
                    # TaskDialog (DirectUIHWND) не отдаёт текст ни через WM_GETTEXT,
                    # ни через дочерние окна. В CCalc такая форма без читаемого
                    # текста — это «Перед тем как работать с конструкцией, создайте заказ».
                    _log("таскдиалог CCalc без читаемого текста — трактую как «создайте сначала заказ»")
                    body += " создать заказ"
                if "перед тем" in body or "создайте сначала" in body or "создать заказ" in body:
                    # «Перед тем как работать с конструкцией, создайте сначала заказ» → ОК + новый заказ
                    _log("CCalc требует создать заказ — нажимаю ОК и создаю заказ")
                    self._find_and_click_button(msgbox, ("ok", "ок"))
                    time.sleep(0.4)
                    if not self._recovering:
                        self._recovering = True
                        try:
                            self.create_new_order()
                        finally:
                            self._recovering = False
                    # Флаг ставим после create_new_order: он сам сбрасывает его в начале
                    self._no_order_recovered = True
                    dismissed = True
                elif "не сохранен" in body or "сохранить" in body or "желаете" in body:
                    # «Сохранить заказ?» → Нет
                    _log("диалог сохранения заказа — нажимаю «Нет»")
                    self._find_and_click_button(msgbox, ("нет", "no", "не сохран"))
                    time.sleep(0.4)
                    dismissed = True
                else:
                    # Текст у TLabel не читается. «Сохранить заказ?» разбирается
                    # отдельно (_handle_save_prompt) сразу после «Создать новый»,
                    # а остальные вопросы — это «Продолжить?»-подтверждения своего
                    # же действия, поэтому по умолчанию соглашаемся.
                    _log(f"неизвестный диалог #32770: {body[:160]!r} — подтверждаю ОК")
                    self._click_dialog_button(msgbox, ("ok", "ок", "да", "yes"))
                    time.sleep(0.4)
                    dismissed = True

            # 2. TChangeCfgDlg — «Выберите конфигурацию» (модальный, выключает окно)
            if self._find_dialog_by_class("TChangeCfgDlg", timeout=0.05):
                self._handle_config_dialog(timeout=0.1)
                dismissed = True

            # 2. TKonstrNamesEnterDlg — имя конструкции
            kdlg = self._find_dialog_by_class("TKonstrNamesEnterDlg", timeout=0.3)
            if kdlg:
                _log("диалог имени конструкции — подтверждаю ОК")
                self._find_and_click_button(kdlg, ("ok", "ок"))
                time.sleep(0.4)
                dismissed = True

            # 3. THintDlg — «совет дня» / «Обратите внимание»
            hint = self._find_dialog_by_class("THintDlg", timeout=0.2)
            if hint:
                self._find_and_click_button(hint, ("ok", "ок", "закрыть", "close"))
                time.sleep(0.3)
                dismissed = True

            # 3b. Зависшие модальные окна вспомогательных элементов: они выключают
            # главное окно, и тогда не открывается даже меню «Заказ».
            # TWHDialog и TStvorParDlg сюда не входят: это рабочие диалоги шагов,
            # их закрывает сам шаг после заполнения.
            for stray in ("TDobElemDlg", "TListElemDlg", "TDobElemEditDlg"):
                stuck = self._find_dialog_by_class(stray, timeout=0.1)
                if stuck:
                    _log(f"закрываю зависшее модальное окно {stray}")
                    win32gui.PostMessage(stuck, win32con.WM_CLOSE, 0, 0)
                    time.sleep(0.5)
                    if self._find_dialog_by_class(stray, timeout=0.3):
                        _log(f"{stray} не закрылся по WM_CLOSE — оставляю как есть")
                    else:
                        dismissed = True

            # 4. TShowPrint — случайно открытое окно «Печать»
            prn = self._find_dialog_by_class("TShowPrint", timeout=0.2)
            if prn:
                _log("закрываю случайно открытое окно «Печать»")
                win32gui.PostMessage(prn, win32con.WM_CLOSE, 0, 0)
                time.sleep(0.4)
                dismissed = True

            if not dismissed:
                break

        # 5. Диалоги Access Violation
        self.dismiss_error_dialog()
        return self._consume_no_order_recovery()

    def _menu_bar_item_rect(self, index: int = 0):
        """Экранный прямоугольник пункта главного меню (0 — «Заказ»)."""
        try:
            menu = win32gui.GetMenu(self.hwnd)
            if not menu:
                return None
            if index >= win32gui.GetMenuItemCount(menu):
                return None
            rect = win32gui.GetMenuItemRect(self.hwnd, menu, index)
            # pywin32 может вернуть (BOOL, (left, top, right, bottom))
            if isinstance(rect, (list, tuple)) and len(rect) == 2 \
                    and isinstance(rect[1], (list, tuple)) and len(rect[1]) == 4:
                rect = rect[1]
            if isinstance(rect, (list, tuple)) and len(rect) == 4 and rect[2] > rect[0]:
                return tuple(int(v) for v in rect)
        except Exception as e:
            _log(f"не удалось вычислить прямоугольник пункта меню {index}: {e}")
        return None

    def _click_menu_bar_item(self, index: int = 0) -> bool:
        """Кликает по пункту главного меню по его реальному прямоугольнику.

        Жёсткие координаты из трека не используются: высота полосы меню зависит
        от того, развёрнуто ли окно и какая у него рамка (в треке клик (41,44)
        попадал на 2 px ниже полосы меню и меню не открывалось).
        """
        rect = self._menu_bar_item_rect(index)
        if rect:
            x = (rect[0] + rect[2]) // 2
            y = (rect[1] + rect[3]) // 2
            _log(f"пункт меню {index} в прямоугольнике {rect} — клик по ({x},{y})")
            self._click_abs(x, y, settle=0.0)
            return True
        _log(f"прямоугольник пункта меню {index} неизвестен — клик по треку (41,33)")
        self._click_abs(41, 33, settle=0.0)
        return False

    def _menu_create_new_once(self) -> bool:
        """Один проход «Заказ → Создать новый» через системное меню #32768."""
        self.activate_window()
        time.sleep(0.2)

        self._click_menu_bar_item(0)
        popup = self._visible_popup_menu(timeout=1.0)
        if not popup:
            _log("меню «Заказ» не открылось — повторяю клик")
            self._click_menu_bar_item(0)
            popup = self._visible_popup_menu(timeout=1.0)
        if not popup:
            # Клавиатурный резерв: активное окно, Alt не нужен — Down/Enter по меню
            _log("меню «Заказ» не открылось кликом — пробую клавиатуру")
            self._click_menu_bar_item(0)
            self._key(win32con.VK_DOWN, 0.4)
            if self._visible_popup_menu(timeout=0.6):
                self._key(win32con.VK_RETURN, 1.2)
                if not self._visible_popup_menu(timeout=0.4):
                    return True
                self._key(win32con.VK_ESCAPE, 0.3)
            popup = self._visible_popup_menu(timeout=0.3)
        if not popup:
            blocked = self._main_window_blocked_by()
            if blocked:
                _log(f"меню недоступно: главное окно выключено диалогом "
                     f"{win32gui.GetClassName(blocked)}")
            return False

        try:
            left, top, right, bottom = win32gui.GetWindowRect(popup)
        except Exception:
            return False

        # Первый пункт «Создать новый»: в треке клик был в экранные (53, 52)
        # при попапе с углом (0, 42) — то есть +53/+10 от угла попапа.
        item_x = left + 53 if right - left > 70 else 61
        item_y = top + 10 if bottom - top > 20 else 60
        _log(f"меню «Заказ» открыто ({left},{top})-({right},{bottom}), выбираю пункт в ({item_x},{item_y})")
        self._click_abs(item_x, item_y, settle=0.9)

        # Если попап остался открыт — пункт не выбран: закрываем меню и сообщаем о неудаче
        if self._visible_popup_menu(timeout=0.3):
            _log("пункт «Создать новый» не выбран — закрываю меню Escape")
            self._key(win32con.VK_ESCAPE, 0.3)
            return False
        return True

    def _handle_save_prompt(self, timeout: float = 3.0) -> bool:
        """Отвечает «Нет» в диалоге «Сохранить заказ?».

        У этого #32770 заголовок «Вопрос», а сам текст нарисован TLabel —
        у TLabel нет дочернего окна, поэтому текст не читается ни GetWindowText,
        ни WM_GETTEXT. Опознаём диалог по набору кнопок Да/Нет(+Отмена) сразу
        после пункта меню «Создать новый» и жмём именно «Нет».
        """
        deadline = time.time() + timeout
        while time.time() < deadline:
            dlg = self._find_any_msgbox(timeout=0.3)
            if dlg:
                if self._click_dialog_button(dlg, ("нет", "no")):
                    _log("диалог «Сохранить заказ?» — ответ «Нет»")
                    time.sleep(0.5)
                    return True
                _log("в диалоге после «Создать новый» нет кнопки «Нет» — оставляю общему разбору")
                return False
            time.sleep(0.15)
        return False

    def _main_window_blocked_by(self):
        """Возвращает hwnd модального диалога, который выключил главное окно CCalc."""
        try:
            if win32gui.IsWindowEnabled(self.hwnd):
                return None
        except Exception:
            return None
        found = []

        def cb(h, _):
            if h == self.hwnd or not win32gui.IsWindowVisible(h):
                return True
            try:
                if win32gui.GetWindow(h, win32con.GW_OWNER) == self.hwnd:
                    found.append(h)
            except Exception:
                pass
            return True

        try:
            win32gui.EnumWindows(cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def _combo_items(self, combo_hwnd) -> list:
        """Тексты пунктов ComboBox (без раскрытия списка)."""
        try:
            count = int(win32gui.SendMessage(combo_hwnd, win32con.CB_GETCOUNT, 0, 0) or 0)
        except Exception:
            count = 0
        items = []
        for i in range(count):
            buf = ctypes.create_unicode_buffer(512)
            try:
                _user32.SendMessageW(combo_hwnd, win32con.CB_GETLBTEXT, i,
                                     ctypes.cast(buf, ctypes.c_void_p).value)
            except Exception:
                pass
            items.append(buf.value)
        return items

    def _handle_config_dialog(self, prefer: str = "deceuninck", timeout: float = 4.0) -> bool:
        """Разбирает «Выберите конфигурацию» (TChangeCfgDlg).

        CCalc открывает его сразу после «Заказ → Создать новый», и пока он открыт
        главное окно выключено (IsWindowEnabled == False): меню и палитра не
        реагируют. Без выбора конфигурации заказ не создаётся.
        """
        dlg = self._find_dialog_by_class("TChangeCfgDlg", timeout=timeout)
        if not dlg:
            return False

        combos = []

        def cb(h, _):
            if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) == "TComboBox":
                combos.append(h)
            return True

        try:
            win32gui.EnumChildWindows(dlg, cb, None)
        except Exception:
            pass

        for combo in combos:
            items = self._combo_items(combo)
            if not items:
                continue
            current = win32gui.SendMessage(combo, win32con.CB_GETCURSEL, 0, 0)
            if isinstance(current, int) and 0 <= current < len(items):
                index = current
            else:
                index = next((i for i, t in enumerate(items)
                              if prefer and prefer in t.lower()), 0)
            try:
                win32gui.SendMessage(combo, win32con.CB_SETCURSEL, index, 0)
                win32gui.SendMessage(win32gui.GetParent(combo), win32con.WM_COMMAND,
                                     win32api.MAKELONG(win32gui.GetDlgCtrlID(combo),
                                                       win32con.CBN_SELCHANGE), combo)
            except Exception as e:
                _log(f"выбор конфигурации в списке не удался: {e}")
            _log(f"«Выберите конфигурацию»: пункт {index + 1}/{len(items)} = {items[index]!r}")
            break

        self._click_dialog_button(dlg, ("ok", "ок"))
        time.sleep(0.7)
        if self._find_dialog_by_class("TChangeCfgDlg", timeout=0.5):
            raise CCalcStageError(
                "Диалог «Выберите конфигурацию» не закрылся — заказ не создан."
            )
        return True

    def _clear_modal_blockers(self):
        """Снимает модальный диалог, который выключил главное окно, если он есть."""
        blocked = self._main_window_blocked_by()
        if not blocked:
            return
        cls = win32gui.GetClassName(blocked)
        _log(f"главное окно выключено модальным диалогом {cls} — закрываю его")
        if cls == "TChangeCfgDlg":
            self._handle_config_dialog(timeout=0.2)
            return
        if self._click_dialog_button(blocked, ("ok", "ок", "otmena", "отмена", "cancel")):
            time.sleep(0.4)
            return
        win32gui.PostMessage(blocked, win32con.WM_CLOSE, 0, 0)
        time.sleep(0.4)

    def _ensure_order_exists(self):
        """Создаёт новый заказ через реальное меню «Заказ → Создать новый»."""
        self.activate_window()
        self.dismiss_error_dialog()
        self._clear_modal_blockers()

        opened = self._menu_create_new_once()
        if not opened:
            raise CCalcStageError("Меню «Заказ» не открылось — создать заказ невозможно.")

        # «Сохранить заказ?» → «Нет» (именно этот диалог, а не общий ОК)
        self._handle_save_prompt()
        # «Выберите конфигурацию» — обязательный шаг создания нового заказа.
        # Появляется с задержкой в 1-2 с, поэтому ждём до 4 с: без ответа на него
        # заказ не создаётся, а главное окно остаётся выключенным.
        self._handle_config_dialog(timeout=4.0)

        # «Сохранить заказ?» → Нет, имя конструкции → ОК (до 8 окон подряд)
        self.dismiss_all_blocking_dialogs()
        self._no_order_recovered = False
        time.sleep(0.4)

    def create_new_order(self) -> bool:
        """
        Гарантированно создаёт новый заказ через «Заказ → Создать новый».

        Предыдущий заказ не сохраняется («Нет» в диалоге), чтобы гарантировать
        чистый лист для нового расчёта.
        """
        if self.dry_run:
            return False

        self._no_order_recovered = False
        last_error = None
        for attempt in range(3):
            try:
                self._ensure_order_exists()
            except Exception as e:
                last_error = e
                _log(f"попытка создать заказ {attempt + 1}/3 не удалась: {e}")
                time.sleep(0.6)
                continue

            # Диалоги должны быть разобраны до конца
            leftovers = self._find_any_msgbox(timeout=0.3) or \
                self._find_dialog_by_class("TKonstrNamesEnterDlg", timeout=0.2) or \
                self._find_dialog_by_class("TChangeCfgDlg", timeout=0.2)
            if not leftovers:
                _log("новый заказ создан, лист чистый")
                # Без галочки на «Конструкции 1» CCalc считает заказ пустым
                self.ensure_construction_checked()
                return True
            _log(f"после создания заказа остались диалоги (попытка {attempt + 1}/3)")

        raise CCalcStageError(f"Не удалось создать новый заказ в CCalc: {last_error or 'диалоги не закрываются'}")

    def _construction_row_point(self):
        """Точка чекбокса первой строки списка конструкций («Лист заказа»)."""
        rects = []

        def cb(h, _):
            if win32gui.GetClassName(h) == "TADGListView" and win32gui.IsWindowVisible(h):
                r = win32gui.GetWindowRect(h)
                if r[2] - r[0] < 400:  # слева узкий список конструкций, справа — спецификация
                    rects.append(r)
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, cb, None)
        except Exception:
            pass
        if not rects:
            return None
        r = sorted(rects)[0]
        return r[0] + 8, r[1] + 26

    def _checkbox_ink(self, pt) -> int:
        """Сколько тёмных пикселей в квадратике чекбокса (галочка = больше)."""
        try:
            import numpy as np
            from PIL import ImageGrab
            x, y = pt
            img = ImageGrab.grab(bbox=(x - 7, y - 7, x + 8, y + 8),
                                 all_screens=True).convert("L")
            return int((np.asarray(img) < 128).sum())
        except Exception as e:
            _log(f"проверка чекбокса недоступна: {e}")
            return -1

    def ensure_construction_checked(self) -> bool:
        """Автопроверка: конструкция в заказе должна быть отмечена галочкой.

        Без отметки CCalc рисует пустоту: на любой клик по палитре отвечает
        «Перед тем как работать с конструкцией, создайте сначала заказ».
        """
        self.switch_tab(1)  # Лист заказа
        time.sleep(0.8)
        pt = self._construction_row_point()
        if not pt:
            _log("список конструкций на «Листе заказа» не найден — пропускаю отметку")
            self.switch_tab(0)
            return False

        ink_before = self._checkbox_ink(pt)
        if ink_before >= 20:
            _log(f"конструкция отмечена в заказе (галочка, тёмных пикселей {ink_before})")
            self.switch_tab(0)
            return True

        self._click_abs(pt[0], pt[1], settle=0.7)
        ink_after = self._checkbox_ink(pt)
        _log(f"отметил конструкцию в заказе: тёмных пикселей в чекбоксе {ink_before} → {ink_after}")
        self.switch_tab(0)
        time.sleep(0.5)
        if ink_after > ink_before:
            return True
        _log("галочка в чекбоксе не появилась — проверю заказ при рисовании")
        return False

    def switch_tab(self, tab_index: int):
        """
        Switches tabs in CCalc TPageControl:
        0: Чертёж (X=35, Y=61)
        1: Лист заказа (X=93, Y=61)
        """
        if self.dry_run:
            return
        if tab_index == 0:
            self.click_rel(35, 61)
        else:
            self.click_rel(93, 61)
        time.sleep(0.5)

    def activate_window(self):
        """Brings CCalc TForm1 window reliably to the foreground."""
        if not self._ensure_window():
            return
        self._attach_desktop()
        try:
            fg = win32gui.GetForegroundWindow()
            if fg != self.hwnd:
                cur_thread = win32api.GetCurrentThreadId()
                fg_thread = win32process.GetWindowThreadProcessId(fg)[0]
                target_thread = win32process.GetWindowThreadProcessId(self.hwnd)[0]
                ctypes.windll.user32.AttachThreadInput(cur_thread, fg_thread, True)
                ctypes.windll.user32.AttachThreadInput(cur_thread, target_thread, True)
                win32gui.ShowWindow(self.hwnd, win32con.SW_RESTORE)
                win32gui.ShowWindow(self.hwnd, win32con.SW_MAXIMIZE)
                win32gui.SetForegroundWindow(self.hwnd)
                time.sleep(0.1)
                ctypes.windll.user32.AttachThreadInput(cur_thread, fg_thread, False)
                ctypes.windll.user32.AttachThreadInput(cur_thread, target_thread, False)
        except Exception:
            try:
                win32gui.ShowWindow(self.hwnd, win32con.SW_MAXIMIZE)
                win32gui.SetForegroundWindow(self.hwnd)
            except Exception:
                pass

    def _window_origin(self):
        """Экранный угол окна CCalc — начало системы координат трека."""
        try:
            left, top = win32gui.GetWindowRect(self.hwnd)[:2]
            return int(left), int(top)
        except Exception:
            return 0, 0

    def _status_mm(self):
        """Координаты под курсором (мм) из строки состояния CCalc — два поля X/Y."""
        if not self.hwnd:
            return None
        ox, oy = self._window_origin()
        fields = []

        def cb(h, _):
            if win32gui.GetClassName(h) != "TStaticText" or not win32gui.IsWindowVisible(h):
                return True
            try:
                left, top = win32gui.GetWindowRect(h)[:2]
            except Exception:
                return True
            if left > ox + 200 or top < oy + 400:
                return True
            fields.append((left, top, _msg_text(h).strip()))
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, cb, None)
        except Exception:
            pass
        if not fields:
            return None
        fields.sort()
        min_left = fields[0][0]
        values = []
        for left, top, txt in sorted((f[1], f[2][0], f[2][1]) for f in fields if f[0] == min_left):
            try:
                values.append(float(txt.replace("\xa0", "").replace(" ", "").replace(",", ".")))
            except ValueError:
                continue
        if len(values) < 2:
            return None
        return values[0], values[1]

    def _canvas_scale(self, probes=((760, 300), (1240, 760))):
        """Масштаб чертежа (px на мм) и экранное положение (0,0) по строке состояния.

        CCalc показывает координаты в мм под курсором, поэтому клики по чертежу
        можно считать от реальной геометрии, а не от жёстких «координат трека»:
        после создания рамы масштаб и положение рисунка меняются.
        """
        readings = []
        for (x, y) in probes:
            win32api.SetCursorPos((int(x), int(y)))
            time.sleep(0.45)  # строка состояния обновляется с задержкой
            mm = self._status_mm()
            if not mm:
                _log("калибровка чертежа: строка состояния с координатами не найдена")
                return None
            readings.append((x, y, mm[0], mm[1]))
        (x1, y1, mx1, my1), (x2, y2, mx2, my2) = readings
        if mx1 == mx2 or my1 == my2:
            _log(f"калибровка чертежа: координаты не меняются ({readings})")
            return None
        scale = ((x2 - x1) / (mx2 - mx1) + (y2 - y1) / (my2 - my1)) / 2.0
        # Здоровый масштаб чертежа — доли пикселя на миллиметр: 0.05..5 px/мм.
        # Вне этого диапазона показания строки состояния устарели — не доверяем.
        if not 0.05 <= scale <= 5.0:
            _log(f"калибровка чертежа: неправдоподобный масштаб {scale:.3f} px/мм ({readings}) — игнорирую")
            self._canvas_geom = None
            return None
        origin_x = x1 - mx1 * scale
        origin_y = y1 + my1 * scale
        self._canvas_geom = (origin_x, origin_y, scale)
        _log(f"масштаб чертежа: {scale:.3f} px/мм, (0,0) в ({origin_x:.0f},{origin_y:.0f})")
        return self._canvas_geom

    def _mm_to_screen(self, x_mm: float, y_mm: float):
        """Переводит миллиметры чертежа в экранные координаты (окно CCalc)."""
        geom = getattr(self, "_canvas_geom", None) or self._canvas_scale()
        if not geom:
            return None
        ox, oy, scale = geom
        x = int(round(ox + x_mm * scale))
        y = int(round(oy - y_mm * scale))
        # Точка обязана попадать в окно CCalc, иначе клик улетит по рабочему столу
        try:
            left, top, right, bottom = win32gui.GetWindowRect(self.hwnd)
        except Exception:
            return None
        if not (left < x < right and top < y < bottom):
            _log(f"точка ({x},{y}) вне окна CCalc — отказываюсь кликать")
            return None
        return x, y

    def _ensure_window(self) -> bool:
        """Переоткрывает главное окно, если CCalc закрылся или перезапустился."""
        try:
            if self.hwnd and win32gui.IsWindow(self.hwnd):
                return True
        except Exception:
            pass
        if self.dry_run:
            return False
        _log("главное окно CCalc недоступно — ищу или запускаю заново")
        self.hwnd = None
        self._canvas_geom = None
        try:
            self.ensure_running()
        except Exception as e:
            _log(f"переоткрыть CCalc не удалось: {e}")
        return bool(self.hwnd)

    def _drag_abs(self, x1, y1, x2, y2, settle: float = 0.9):
        """Протяжка мышью — так CCalc рисует створку в проёме (клик не работает)."""
        if None in (x1, y1, x2, y2):
            return False
        try:
            win32api.SetCursorPos((int(x1), int(y1)))
            time.sleep(0.12)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
            time.sleep(0.15)
            steps = 12
            for i in range(1, steps + 1):
                win32api.SetCursorPos((int(x1 + (x2 - x1) * i / steps),
                                       int(y1 + (y2 - y1) * i / steps)))
                time.sleep(0.03)
            time.sleep(0.1)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            time.sleep(settle)
            _log(f"протяжка ({int(x1)},{int(y1)}) → ({int(x2)},{int(y2)})")
            return True
        except Exception as e:
            _log(f"протяжка не удалась: {e}")
            return False

    # ---------- автопроверка чертежа (разбор скриншота) ----------

    def _analyze_canvas(self):
        """Снимает область чертежа и ищет на ней линии элементов.

        Возвращает dict: маска тёмных пикселей, габариты рамки (l, t, r, b) и
        счётчики тёмных пикселей по столбцам/строкам. Размерные линии и сетка
        холста светлее линий профиля, поэтому порог 128 оставляет только элементы.
        """
        try:
            import numpy as np
            from PIL import ImageGrab
        except Exception as e:
            _log(f"автопроверка чертежа недоступна (нет numpy/PIL): {e}")
            return None
        try:
            left, top, right, bottom = win32gui.GetWindowRect(self.hwnd)
        except Exception:
            return None
        # Палитра слева, разделители панелей сверху и статусбар снизу в разбор
        # не входят: их линии тянутся через весь снимок и ломают поиск рамки.
        img = ImageGrab.grab(bbox=(left + 135, top + 120, right - 10, bottom - 70),
                             all_screens=True).convert("L")
        dark = np.asarray(img) < 128
        # Отрезаем по краям: там сплошные линии панелей (палитра, граница
        # области), которые тянутся через весь снимок.
        dark = dark[12:-12, 12:-12]
        if not dark.any():
            return None
        cols = dark.sum(axis=0)
        rows = dark.sum(axis=1)
        # Вертикальные линии рамки — длиной в высоту всего снимка (рамка занимает
        # большую его часть); горизонтальные ищем уже внутри найденных вертикалей,
        # иначе порог считается от ширины всего снимка и не срабатывает никогда.
        height, width = dark.shape
        strong_cols = np.where(cols >= 0.6 * height)[0]
        # Края снимка (границы панелей) — не линии чертежа
        strong_cols = strong_cols[(strong_cols > 10) & (strong_cols < width - 11)]
        if strong_cols.size < 2:
            return {"dark": dark, "cols": cols, "rows": rows,
                    "frame": None, "strong_cols": strong_cols, "strong_rows": []}
        left_col, right_col = int(strong_cols.min()), int(strong_cols.max())
        inner = dark[:, left_col:right_col + 1].sum(axis=1)
        strong_rows = np.where(inner >= 0.6 * (right_col - left_col))[0]
        if strong_rows.size < 2:
            return {"dark": dark, "cols": cols, "rows": rows,
                    "frame": None, "strong_cols": strong_cols, "strong_rows": strong_rows}
        frame = (left_col, int(strong_rows.min()), right_col, int(strong_rows.max()))
        return {"dark": dark, "cols": cols, "rows": rows,
                "frame": frame, "strong_cols": strong_cols, "strong_rows": strong_rows}

    def _frame_px(self):
        """Габариты нарисованной рамки в пикселях (l, t, r, b) или None."""
        info = self._analyze_canvas()
        return info and info.get("frame")

    def _frame_screen_box(self):
        """Экранный прямоугольник (L, T, R, B) нарисованной рамки или None.

        Считается из снимка чертежа, поэтому не зависит от того, что показывает
        строка состояния (её показания бывают устаревшими и давали ложную
        калибровку). Именно от этого прямоугольника считаются все клики по
        элементам: центр для импоста и правая секция для створки.
        """
        frame = self._frame_px()
        if not frame:
            return None
        try:
            left, top = win32gui.GetWindowRect(self.hwnd)[:2]
        except Exception:
            return None
        # Смещение области снимка внутри окна: 135 px от левого края (палитра)
        # и 120 px сверху (меню/панель), плюс отрезанные 12 px по краям.
        ox, oy = left + 135 + 12, top + 120 + 12
        return (frame[0] + ox, frame[1] + oy, frame[2] + ox, frame[3] + oy)

    def _close_message_window(self) -> bool:
        """Закрывает окно «Сообщения» (TTestDlg).

        Оно плавающее и часто ложится прямо на чертёж: пиксели текста попадают
        в снимок и ломают автопроверки (ложное «створка слева»).
        """
        found = []

        def cb(h, _):
            try:
                if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) == "TTestDlg":
                    found.append(h)
            except Exception:
                pass
            return True

        try:
            win32gui.EnumWindows(cb, None)
        except Exception:
            pass
        if not found:
            return False
        for h in found:
            try:
                win32gui.PostMessage(h, win32con.WM_CLOSE, 0, 0)
            except Exception:
                pass
        time.sleep(0.4)
        _log("окно «Сообщения» закрыто — оно перекрывало чертёж")
        return True

    def verify_frame(self, width_mm: int, height_mm: int) -> bool:
        """Проверяет, что на чертеже есть рамка с нужным соотношением сторон."""
        self._close_message_window()
        info = self._analyze_canvas()
        frame = info and info.get("frame")
        if not frame:
            raise CCalcStageError("автопроверка рамы: на чертеже не найдены линии рамки")
        l, t, r, b = frame
        px_w, px_h = r - l, b - t
        if px_w <= 20 or px_h <= 20:
            raise CCalcStageError(f"автопроверка рамы: рамка слишком мала ({px_w}x{px_h} px)")
        want = width_mm / float(height_mm)
        got = px_w / float(px_h)
        _log(f"автопроверка рамы: {px_w}x{px_h} px, соотношение {got:.3f} (нужно {want:.3f})")
        if abs(got - want) / want > 0.06:
            raise CCalcStageError(
                f"автопроверка рамы: соотношение сторон {got:.3f} не совпадает с {want:.3f} "
                f"({width_mm}x{height_mm} мм нарисованы неверно)")
        return True

    def verify_impost(self, width_mm: int) -> bool:
        """Проверяет, что внутри рамки есть ровно один разделитель по центру."""
        self._close_message_window()
        info = self._analyze_canvas()
        frame = info and info.get("frame")
        if not frame:
            raise CCalcStageError("автопроверка импоста: рамка на чертеже не найдена")
        l, t, r, b = frame
        inner_h = b - t
        cols = info["cols"]
        # Вертикальный импост идёт от верха до низа рамки — почти полная высота.
        # Края рамки и её внутренние линии профиля (перчатка двойной линией)
        # отступают на ~20 px, поэтому ближайшие 30 px от краёв — не разделитель.
        candidates = [int(x) for x in range(l + 30, r - 29)
                      if cols[int(x)] >= 0.7 * inner_h]
        if not candidates:
            raise CCalcStageError("автопроверка импоста: вертикального разделителя на чертеже нет")
        clusters = []
        for x in candidates:
            # Профиль рисуется двойной линией (замерено ~22 px): это один разделитель
            if clusters and x - clusters[-1][-1] <= 26:
                clusters[-1].append(x)
            else:
                clusters.append([x])
        centers = [sum(c) / len(c) for c in clusters]
        if len(centers) != 1:
            raise CCalcStageError(
                f"автопроверка импоста: найдено разделителей {len(centers)} вместо 1")
        center = centers[0]
        middle = (l + r) / 2.0
        offset = (center - middle) / float(r - l)
        x_mm = (center - l) * width_mm / float(r - l)
        _log(f"автопроверка импоста: разделитель в {center:.0f} px ({(center - l):.0f} px от левого края), "
             f"≈ {x_mm:.0f} мм, смещение от центра {offset * 100:+.1f}% ширины")
        if abs(offset) > 0.04:
            raise CCalcStageError(
                f"автопроверка импоста: разделитель не по центру — смещение {offset * 100:+.1f}% ширины")
        return True

    def verify_sash(self, side: str = "right", baseline=None) -> bool:
        """Проверяет, что створка стоит в правой (или левой) секции."""
        self._close_message_window()
        info = self._analyze_canvas()
        frame = info and info.get("frame")
        if not frame:
            raise CCalcStageError("автопроверка створки: рамка на чертеже не найдена")
        l, t, r, b = frame
        dark = info["dark"]
        mid = (l + r) // 2
        rows = info["rows"]
        top, bottom = t, b
        left_density = int(dark[top:bottom, l + 3:mid - 2].sum())
        right_density = int(dark[top:bottom, mid + 2:r - 3].sum())
        _log(f"автопроверка створки: тёмных пикселей слева {left_density}, справа {right_density}")
        if side == "right" and right_density <= left_density * 1.05:
            raise CCalcStageError(
                f"автопроверка створки: в правой секции линий не больше, чем в левой "
                f"({right_density} против {left_density}) — створка не встала справа")
        if side == "left" and left_density <= right_density * 1.05:
            raise CCalcStageError(
                f"автопроверка створки: в левой секции линий не больше, чем в правой "
                f"({left_density} против {right_density})")
        return True

    def click_rel(self, x: int, y: int, hwnd=None):
        """Клик по координатам, записанным рекордером относительно угла окна CCalc."""
        target = hwnd or self.hwnd
        if target == self.hwnd:
            self.activate_window()
            ox, oy = self._window_origin()
            x, y = x + ox, y + oy
            win32api.SetCursorPos((x, y))
            time.sleep(0.05)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
            time.sleep(0.04)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            time.sleep(0.2)
        else:
            lParam = win32api.MAKELONG(x, y)
            win32gui.PostMessage(target, win32con.WM_LBUTTONDOWN, win32con.MK_LBUTTON, lParam)
            time.sleep(0.06)
            win32gui.PostMessage(target, win32con.WM_LBUTTONUP, 0, lParam)
            time.sleep(0.2)

    def dblclick_rel(self, x: int, y: int, hwnd=None):
        """Двойной клик по координатам, записанным рекордером относительно угла окна."""
        target = hwnd or self.hwnd
        if target == self.hwnd:
            self.activate_window()
            ox, oy = self._window_origin()
            x, y = x + ox, y + oy
            win32api.SetCursorPos((x, y))
            time.sleep(0.05)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            time.sleep(0.04)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            time.sleep(0.3)
        else:
            lParam = win32api.MAKELONG(x, y)
            win32gui.PostMessage(target, win32con.WM_LBUTTONDOWN, win32con.MK_LBUTTON, lParam)
            time.sleep(0.04)
            win32gui.PostMessage(target, win32con.WM_LBUTTONUP, 0, lParam)
            time.sleep(0.04)
            win32gui.PostMessage(target, win32con.WM_LBUTTONDBLCLK, win32con.MK_LBUTTON, lParam)
            time.sleep(0.04)
            win32gui.PostMessage(target, win32con.WM_LBUTTONUP, 0, lParam)
            time.sleep(0.3)

    def drag_rel(self, x1: int, y1: int, x2: int, y2: int, hwnd=None):
        """Drags mouse from (x1, y1) to (x2, y2)."""
        target = hwnd or self.hwnd
        if target == self.hwnd:
            self.activate_window()
            win32api.SetCursorPos((x1, y1))
            time.sleep(0.05)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
            time.sleep(0.1)
            for s in range(1, 6):
                cx = int(x1 + (x2 - x1) * (s / 5.0))
                cy = int(y1 + (y2 - y1) * (s / 5.0))
                win32api.SetCursorPos((cx, cy))
                time.sleep(0.03)
            win32api.SetCursorPos((x2, y2))
            time.sleep(0.1)
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            time.sleep(0.3)
        else:
            lParam1 = win32api.MAKELONG(x1, y1)
            lParam2 = win32api.MAKELONG(x2, y2)
            win32gui.PostMessage(target, win32con.WM_LBUTTONDOWN, win32con.MK_LBUTTON, lParam1)
            time.sleep(0.1)
            for s in range(1, 6):
                cx = int(x1 + (x2 - x1) * (s / 5.0))
                cy = int(y1 + (y2 - y1) * (s / 5.0))
                win32gui.PostMessage(target, win32con.WM_MOUSEMOVE, win32con.MK_LBUTTON, win32api.MAKELONG(cx, cy))
                time.sleep(0.03)
            win32gui.PostMessage(target, win32con.WM_MOUSEMOVE, win32con.MK_LBUTTON, lParam2)
            time.sleep(0.1)
            win32gui.PostMessage(target, win32con.WM_LBUTTONUP, 0, lParam2)
            time.sleep(0.3)

    def ensure_profile_deceuninck(self):
        """Ensures Deceuninck configuration is selected (с автопроверкой)."""
        if self.dry_run:
            return
        title = win32gui.GetWindowText(self.hwnd)
        # Обе стороны нормализуются: _norm_text меняет латинские двойники на кириллицу
        if _norm_text("deceuninck") in _norm_text(title):
            _log(f"профиль подтверждён по заголовку: {title!r}")
            return
        # Menu Настройка (267, 32) -> Смена конфигурации (304, 92)
        self.click_rel(267, 32)
        time.sleep(0.3)
        self.click_rel(304, 92)
        time.sleep(0.8)

        # Handle TChangeCfgDlg
        def enum_cb(h, _):
            if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) == "TChangeCfgDlg":
                # Click ComboBox at (52, 40)
                self.click_rel(52, 40, hwnd=h)
                time.sleep(0.3)
                # Click ComboLBox at (73, 74)
                self.click_rel(73, 74, hwnd=h)
                time.sleep(0.3)
                # Click OK button at (308, 41)
                self.click_rel(308, 41, hwnd=h)
            return True
        try:
            win32gui.EnumWindows(enum_cb, None)
        except Exception:
            pass
        time.sleep(1.0)

        title = win32gui.GetWindowText(self.hwnd)
        if _norm_text("deceuninck") not in _norm_text(title):
            raise CCalcStageError(
                f"не удалось переключиться на профиль Deceuninck: заголовок окна {title!r}")
        _log(f"профиль Deceuninck включён: {title!r}")

    # ---------- работа с полями и кнопками диалогов ----------

    def _focus_control(self, hwnd):
        try:
            win32gui.SendMessage(hwnd, win32con.WM_SETFOCUS, 0, 0)
        except Exception:
            pass

    def _dialog_edits(self, dlg_hwnd):
        """Список hwnd полей ввода диалога, сверху вниз (и слева направо)."""
        if not dlg_hwnd:
            return []
        edits = []

        def cb(h, _):
            if "edit" in win32gui.GetClassName(h).lower():
                try:
                    rect = win32gui.GetWindowRect(h)
                except Exception:
                    rect = (0, 0, 0, 0)
                edits.append((h, rect))
            return True

        try:
            win32gui.EnumChildWindows(dlg_hwnd, cb, None)
        except Exception:
            pass
        edits.sort(key=lambda e: (e[1][1], e[1][0]))
        return [e[0] for e in edits]

    def _fill_dialog(self, dlg_hwnd, values) -> list:
        """Заполняет поля диалога по порядку; возвращает фактически записанные значения."""
        written = []
        for hwnd, value in zip(self._dialog_edits(dlg_hwnd), values):
            if value is None:
                continue
            actual = self._type_into_edit(hwnd, value)
            written.append(actual)
            if actual.strip() != str(value).strip():
                _log(f"поле ввода прочитано как {actual!r}, ожидалось {value!r}")
            time.sleep(0.08)
        return written

    def _type_into_edit(self, hwnd, value) -> str:
        """Записывает значение в TEdit по-настоящему (WM_CHAR), а не WM_SETTEXT.

        WM_SETTEXT меняет текст контрола, но Delphi-форма хранит своё значение
        и возвращает его при показе, а поле, уже заполненное до MaxLength,
        новые символы не принимает — поэтому сначала EM_SETSEL(0,-1) + WM_CLEAR.
        """
        win32gui.SendMessage(hwnd, win32con.WM_SETFOCUS, 0, 0)
        win32gui.SendMessage(hwnd, win32con.EM_SETSEL, 0, -1)
        time.sleep(0.1)
        win32gui.SendMessage(hwnd, win32con.WM_CLEAR, 0, 0)
        for ch in str(value):
            win32gui.PostMessage(hwnd, win32con.WM_CHAR, ord(ch), 0)
            time.sleep(0.05)
        time.sleep(0.25)
        return _msg_text(hwnd)

    def _click_dialog_button(self, dlg_hwnd, keywords=("ok", "ок"), fallback_rel=None) -> bool:
        """Нажимает кнопку диалога по тексту (Delphi TButton/TBitBtn)."""
        buttons = []

        def cb(h, _):
            cls = win32gui.GetClassName(h).lower()
            if win32gui.IsWindowVisible(h) and ("button" in cls or cls == "tbitbtn"):
                buttons.append((h, _msg_text(h).lower()))
            return True

        try:
            win32gui.EnumChildWindows(dlg_hwnd, cb, None)
        except Exception:
            pass

        for h, txt in buttons:
            if any(k in txt for k in keywords):
                win32gui.SendMessage(h, win32con.BM_CLICK, 0, 0)
                return True

        if fallback_rel:
            left, top = win32gui.GetWindowRect(dlg_hwnd)[:2]
            self._click_abs(left + fallback_rel[0], top + fallback_rel[1])
            return True

        win32gui.PostMessage(dlg_hwnd, win32con.WM_KEYDOWN, win32con.VK_RETURN, 0)
        win32gui.PostMessage(dlg_hwnd, win32con.WM_KEYUP, win32con.VK_RETURN, 0)
        return False

    def _click_dialog_abs(self, dlg_hwnd, x: int, y: int, double: bool = False):
        """Реальный клик по координатам, заданным относительно угла диалога."""
        try:
            left, top = win32gui.GetWindowRect(dlg_hwnd)[:2]
        except Exception:
            left = top = 0
        sx, sy = left + x, top + y
        self._click_abs(sx, sy, settle=0.07 if double else 0.15)
        if double:
            self._click_abs(sx, sy, settle=0.25)

    def _confirm_kolvo(self, expected: int = 1, timeout: float = 1.5) -> bool:
        """Подтверждает TKolvoDlg, проверяя и при необходимости исправляя количество."""
        dlg = self._find_dialog_by_class("TKolvoDlg", timeout=timeout)
        if not dlg:
            return False

        edits = self._dialog_edits(dlg)
        if edits:
            actual = _msg_text(edits[0]).strip()
            if actual != str(expected):
                _log(f"TKolvoDlg: количество {actual!r} != {expected} — исправляю")
                self._focus_control(edits[0])
                _msg_set_text(edits[0], expected)
                time.sleep(0.1)
                actual = _msg_text(edits[0]).strip()
                if actual != str(expected):
                    raise CCalcStageError(
                        f"TKolvoDlg: не удалось установить количество {expected} (в поле {actual!r})")
            else:
                _log(f"TKolvoDlg: количество {actual!r} подтверждено")

        self._click_dialog_button(dlg, ("ok", "ок"))
        time.sleep(0.5)
        if self._find_dialog_by_class("TKolvoDlg", timeout=0.8):
            raise CCalcStageError("TKolvoDlg не закрылся после подтверждения")
        return True

    def select_arrow_tool(self):
        """Принудительно включает «Выделение объекта» (снятие активного инструмента).

        Без этого следующий клик по холсту продолжает работу активного инструмента
        (рамы/импоста/створки) и повторно делит проём. Раньше здесь кликали (83,162),
        но по подсказке это кнопка «Створка» — инструмент не снимался вообще.
        """
        if self.dry_run:
            return
        # Escape снимает активный инструмент в CCalc без лишних кликов по
        # канвасу (клик по канвасу активным инструментом создаёт элемент).
        self._key(win32con.VK_ESCAPE, 0.45)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ при снятии инструмента.")

    def _find_dimension_dialog(self, timeout: float = 4.0):
        """Ищет TWHDialog («Габариты») или похожий диалог с двумя полями ввода."""
        dlg = self._find_dialog_by_class("TWHDialog", timeout=timeout)
        if dlg:
            return dlg

        skip = {"TLoginDlg", "THintDlg", "TShowPrint", "TDobElemEditDlg", "TKolvoDlg",
                "TStvorParDlg", "TChangeCfgDlg", "TListElemDlg", "TDobElemDlg",
                "TKonstrNamesEnterDlg"}
        deadline = time.time() + 2.0
        while time.time() < deadline:
            found = []

            def enum_dlg(h, _):
                if not win32gui.IsWindowVisible(h) or h == self.hwnd or found:
                    return True
                cls = win32gui.GetClassName(h)
                if cls in skip or not ("Dlg" in cls or "Dialog" in cls or cls == "TWHDialog"):
                    return True
                if len(self._dialog_edits(h)) >= 2:
                    found.append(h)
                return True

            try:
                win32gui.EnumWindows(enum_dlg, None)
            except Exception:
                pass
            if found:
                _log(f"диалог габаритов найден по резервному признаку: {win32gui.GetClassName(found[0])}")
                return found[0]
            time.sleep(0.2)
        return None

    def build_frame(self, width_mm: int, height_mm: int):
        """Строит раму width_mm x height_mm во вкладке «Чертёж»."""
        if self.dry_run:
            return
        self.switch_tab(0)  # Чертёж
        time.sleep(0.4)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ перед построением рамы.")

        # 1. Инструмент «Коробка (рама)»
        self.click_rel(*TOOL_FRAME)
        time.sleep(0.3)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (выбор инструмента «Рама»).")

        # 2. Клик по холсту — открывает диалог «Габариты»
        self.click_rel(*CANVAS_FRAME_POINT)
        time.sleep(0.9)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (клик по холсту).")

        # 3. Диалог «Габариты» (TWHDialog): ширина и высота
        dim_dlg = self._find_dimension_dialog(timeout=4.0)
        if not dim_dlg:
            raise CCalcStageError(
                "build_frame: TWHDialog (Габариты) не появился после клика по холсту. "
                "Возможно, заказ/конструкция не созданы или CCalc завис."
            )

        written = self._fill_dialog(dim_dlg, (width_mm, height_mm))
        _log(f"габариты рамы: {written} (ожидалось [{width_mm}, {height_mm}])")
        self._click_dialog_button(dim_dlg, ("ok", "ок"))
        time.sleep(0.9)
        if self._find_dialog_by_class("TWHDialog", timeout=1.0):
            raise CCalcStageError("build_frame: диалог «Габариты» не закрылся — размеры не приняты.")

        # 4. TKolvoDlg после отрисовки рамы (если появился)
        self._confirm_kolvo(expected=1, timeout=1.0)

        # 5. Снимаем инструмент «Рама», чтобы следующий клик не нарисовал вторую раму
        self.select_arrow_tool()

    def add_impost(self, width_mm: int = 1060, height_mm: int = 1340):
        """Ставит один вертикальный импост в центр рамы и снимает инструмент."""
        if self.dry_run:
            return
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ перед добавлением импоста.")

        # Инструмент «Импост вертикальный»
        self.click_rel(*TOOL_IMPOST_V)
        time.sleep(0.4)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (инструмент «Импост»).")

        # Клик в геометрический центр уже нарисованной рамы (из снимка чертежа),
        # а не по жёсткой координате и не по строке состояния.
        box = self._frame_screen_box()
        if box:
            center = ((box[0] + box[2]) // 2, (box[1] + box[3]) // 2)
            _log(f"импост: клик в центр рамы {center} (рамка {box})")
            self._click_abs(*center, settle=0.8)
        else:
            self.click_rel(*CANVAS_CENTER_POINT)
        time.sleep(0.7)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (клик по раме для импоста).")

        # TKolvoDlg: ровно один импост
        if not self._confirm_kolvo(expected=1, timeout=3.0):
            _log("TKolvoDlg не появился — импост добавлен без диалога количества")

        # Обязательно снимаем инструмент импоста: иначе следующий клик делит проём ещё раз
        self.select_arrow_tool()
        self.dismiss_error_dialog()

    # ---------- ComboBox (параметры створки) ----------

    def _key(self, vk: int, settle: float = 0.06):
        """Глобальное нажатие клавиши (для управления раскрытым списком ComboBox)."""
        try:
            win32api.keybd_event(vk, 0, 0, 0)
            time.sleep(0.03)
            win32api.keybd_event(vk, 0, win32con.KEYEVENTF_KEYUP, 0)
            time.sleep(settle)
        except Exception as e:
            _log(f"клавиша {vk} не отправлена: {e}")

    def _find_combo_listbox(self, parent_hwnd, timeout: float = 1.5):
        """Ищет раскрытый список ComboBox (VCL ComboLBox) — сначала среди детей диалога."""
        deadline = time.time() + timeout
        while time.time() < deadline:
            found = []

            def cb(h, _):
                if not win32gui.IsWindowVisible(h):
                    return True
                cls = win32gui.GetClassName(h)
                if "combolbox" in cls.lower() or "listbox" in cls.lower():
                    found.append(h)
                return True

            for scope in (parent_hwnd, None):
                if found:
                    break
                try:
                    if scope:
                        win32gui.EnumChildWindows(scope, cb, None)
                    else:
                        win32gui.EnumWindows(cb, None)
                except Exception:
                    pass
            if found:
                # Приоритет — настоящий список ComboBox (ComboLBox), а не TListBox диалога
                found.sort(key=lambda w: 0 if "combolbox" in win32gui.GetClassName(w).lower() else 1)
                return found[0]
            time.sleep(0.1)
        return None

    def _combo_list_items(self, lb_hwnd) -> list:
        """Тексты пунктов раскрытого списка."""
        try:
            count = int(win32gui.SendMessage(lb_hwnd, win32con.LB_GETCOUNT, 0, 0) or 0)
        except Exception:
            count = 0
        items = []
        for i in range(count):
            buf = ctypes.create_unicode_buffer(512)
            try:
                _user32.SendMessageW(lb_hwnd, win32con.LB_GETTEXT, i,
                                     ctypes.cast(buf, ctypes.c_void_p).value)
            except Exception:
                pass
            items.append(buf.value)
        return items

    def _click_dropdown_item(self, lb_hwnd, index: int, total: int) -> bool:
        """Реальный клик по пункту index раскрытого списка."""
        try:
            left, top, right, bottom = win32gui.GetWindowRect(lb_hwnd)
        except Exception:
            return False
        if total <= 0 or bottom <= top:
            return False
        item_h = max(8, (bottom - top) // total)
        x = left + 20
        y = top + index * item_h + item_h // 2
        _log(f"клик по пункту списка {index + 1}/{total} в ({x},{y})")
        self._click_abs(x, y, settle=0.35)
        return True

    def _select_combo_item(self, combo_hwnd, matcher, label: str = "", fallback_index=None) -> bool:
        """Открывает ComboBox, кликает по нужному пункту и проверяет результат."""
        try:
            rect = win32gui.GetWindowRect(combo_hwnd)
        except Exception:
            return False

        cx = (rect[0] + rect[2]) // 2
        cy = (rect[1] + rect[3]) // 2
        self._click_abs(cx, cy, settle=0.4)

        def hit(text: str) -> bool:
            return matcher(text) or matcher(_norm_text(text))

        lb = self._find_combo_listbox(win32gui.GetParent(combo_hwnd), timeout=1.5)
        if lb:
            items = self._combo_list_items(lb)
            index = next((i for i, t in enumerate(items) if hit(t)), None)
            if index is None and fallback_index is not None:
                index = fallback_index
                _log(f"{label}: подходящего пункта нет, беру {index} из {len(items)}")
            if index is not None:
                _log(f"{label}: найден пункт {index} из {len(items)} — {items[index]!r}")
                self._click_dropdown_item(lb, index, len(items))
            else:
                _log(f"{label}: пункт не найден среди {items} — закрываю список")
                self._key(win32con.VK_ESCAPE)
        else:
            # Список не читается — навигация клавиатурой от начала списка
            _log(f"{label}: раскрытый список не найден, перебираю клавиатурой")
            self._key(win32con.VK_HOME, 0.1)
            for _ in range(fallback_index or 0):
                self._key(win32con.VK_DOWN, 0.05)
            self._key(win32con.VK_RETURN, 0.3)

        actual = _msg_text(combo_hwnd).strip()
        if hit(actual):
            _log(f"{label}: выбрано {actual!r}")
            return True

        # Резервная попытка: перебор от первого пункта (индекс из fallback_index)
        _log(f"{label}: после клика в поле {actual!r} — резервный выбор с начала списка")
        self._click_abs(cx, cy, settle=0.35)
        self._key(win32con.VK_HOME, 0.1)
        for _ in range(fallback_index or 0):
            self._key(win32con.VK_DOWN, 0.05)
        self._key(win32con.VK_RETURN, 0.35)
        actual = _msg_text(combo_hwnd).strip()
        _log(f"{label}: после резервного выбора {actual!r}")
        return hit(actual)

    def _sash_net_combo(self, dlg_hwnd):
        """ComboBox москитной сетки — нижний в диалоге TStvorParDlg."""
        combos = []

        def cb(h, _):
            if "combo" in win32gui.GetClassName(h).lower():
                try:
                    combos.append((h, win32gui.GetWindowRect(h)[1]))
                except Exception:
                    pass
            return True

        try:
            win32gui.EnumChildWindows(dlg_hwnd, cb, None)
        except Exception:
            pass
        if not combos:
            return None
        combos.sort(key=lambda x: x[1])
        return combos[-1][0]

    def _select_sash_net(self, dlg_hwnd, want: str = "anwis") -> bool:
        """Выбирает сетку в TStvorParDlg: 'anwis' — москитная сетка, 'none' — без сетки."""
        combo = self._sash_net_combo(dlg_hwnd)
        if not combo:
            raise CCalcStageError("в TStvorParDlg не найден ComboBox москитной сетки")

        if want == "none":
            matcher = lambda t: (not t.strip()) or t.strip().lower() in (
                "нет", "-", "—", "без сетки", "отсутствует", "без")
            return self._select_combo_item(combo, matcher, "сетка (снять)", fallback_index=0)

        matcher = lambda t: "anwis" in t.lower()
        if not self._select_combo_item(combo, matcher, "сетка Anwis"):
            raise CCalcStageError("не удалось выбрать москитную сетку Anwis (ПП) в TStvorParDlg")
        return True

    def _open_sash_params(self, points):
        """Двойной клик по створке открывает TStvorParDlg; возвращает hwnd или None.

        points — экранные координаты (после калибровки чертежа).
        """
        for (x, y) in points:
            if x is None or y is None:
                continue
            self._click_abs(x, y, settle=0.08)
            self._click_abs(x, y, settle=0.9)
            dlg = self._find_dialog_by_class("TStvorParDlg", timeout=1.2)
            if dlg:
                _log(f"TStvorParDlg открыт двойным кликом в ({int(x)},{int(y)})")
                return dlg
        return None

    def _sash_type_combo(self):
        """ComboBox типа створки в верхней панели (список содержит «Створка …»)."""
        found = []

        def cb(h, _):
            if win32gui.GetClassName(h) != "TComboBox" or not win32gui.IsWindowVisible(h):
                return True
            items = self._combo_items(h)
            if any("120022" in t or _norm_text(t).startswith("створка ") for t in items):
                found.append(h)
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def _select_sash_type(self, side: str = "right") -> bool:
        """Выбирает реальную оконную створку нужной стороны петель.

        С «фиктивной» створкой вставленный элемент не имеет профиля, фурнитуры
        и сетки, а её параметры (TStvorParDlg) не открываются вообще.
        Если в списке есть вариант с явной стороной («правая створка»), он и берётся.
        """
        # Список типа створки заполняется только после включения инструмента
        # «Створка», поэтому берём ComboBox по геометрии панели, а не по содержимому.
        combo = self._element_combo() or self._sash_type_combo()
        if not combo:
            _log("список типа створки не найден — остаётся текущий тип")
            return False
        items = [_norm_text(t) for t in self._combo_items(combo)]
        _log(f"типы створок: {items}")
        side_word = "прав" if side == "right" else "лев"
        if any(side_word in t and "створка" in t for t in items):
            return self._set_element_type(
                lambda t: side_word in t and "створка" in t,
                "тип створки (правая)" if side == "right" else "тип створки (левая)",
                hint_note="фиктивн")
        _log(f"отдельного пункта со стороной «{side_word}» в списке нет — беру типовую оконную створку")
        return self._set_element_type(
            lambda t: "120022" in t or ("створка" in t and "оконн" in t),
            "тип створки (оконная 76 мм)",
            hint_note="фиктивн",
        )

    def _element_combo(self):
        """ComboBox типа элемента в верхней панели (створки, сетки, доп. элементы)."""
        cached = getattr(self, "_element_combo_hwnd", None)
        try:
            if cached and win32gui.IsWindow(cached):
                return cached
        except Exception:
            pass
        found = []
        try:
            left, top, right, bottom = win32gui.GetWindowRect(self.hwnd)
        except Exception:
            return self._sash_type_combo()

        def cb(h, _):
            if win32gui.GetClassName(h) != "TComboBox" or not win32gui.IsWindowVisible(h):
                return True
            r = win32gui.GetWindowRect(h)
            # Верхняя строка панели: широкий список типов элементов
            if r[1] <= top + 120 and (r[2] - r[0]) > 200 and (r[0] - left) < 400:
                found.append((r[0], h))
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, cb, None)
        except Exception:
            pass
        if found:
            found.sort()
            self._element_combo_hwnd = found[0][1]
            return self._element_combo_hwnd
        return self._sash_type_combo()

    def _set_element_type(self, match, label: str, hint_note: str = "") -> bool:
        """Выбирает тип вставляемого элемента (створка, сетка, доп. элемент).

        Список раскрывается ненадёжно (владелец — главное окно), поэтому выбор
        меняется сообщениями CB_SETCURSEL + CBN_SELCHANGE, через соседний пункт:
        без реальной смены пункта OnSelect не срабатывает.
        """
        combo = self._element_combo()
        if not combo:
            _log(f"{label}: список типов не найден — остаётся текущий тип")
            return False
        current = _msg_text(combo).strip()
        if hint_note and hint_note in _norm_text(current):
            _log(f"{label}: было {current!r} — переключаю")
        items = self._combo_items(combo)
        index = next((i for i, t in enumerate(items) if match(_norm_text(t))), None)
        if index is None:
            _log(f"{label}: в списке нет подходящего пункта: {items}")
            return False

        def notify(value):
            try:
                win32gui.SendMessage(combo, win32con.CB_SETCURSEL, value, 0)
                win32gui.SendMessage(win32gui.GetParent(combo), win32con.WM_COMMAND,
                                     win32api.MAKELONG(win32gui.GetDlgCtrlID(combo),
                                                       win32con.CBN_SELCHANGE), combo)
                time.sleep(0.2)
            except Exception as e:
                _log(f"{label}: смена не удалась: {e}")

        if len(items) > 1:
            notify(0 if index else 1)
        notify(index)
        actual = _msg_text(combo).strip()
        ok = _norm_text(actual) == _norm_text(items[index])
        _log(f"{label}: {actual!r} (пункт {index + 1}/{len(items)}, успех: {ok})")
        return ok

    def add_sash_and_net(self, width_mm: int = 1060, height_mm: int = 1340, side: str = "right"):
        """Ставит поворотно-откидную створку в правый проём и выбирает сетку Anwis."""
        if self.dry_run:
            return
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ перед добавлением створки.")

        # Инструмент «Створка»
        self.click_rel(*TOOL_SASH)
        time.sleep(0.4)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (инструмент «Створка»).")

        # Тип элемента — после включения инструмента, иначе список ещё пуст
        self._select_sash_type(side)

        # Створка рисуется протяжкой внутри правого проёма (клик не создаёт элемент).
        # Границы проёма берём из снимка рамки: 50%…100% ширины — правая секция.
        box = self._frame_screen_box()
        if box:
            L, T, R, B = box
            w, h = R - L, B - T
            # Направление протяжки задаёт сторону открывания: сверху-справа
            # вниз-влево даёт створку с петлями справа (проверено вживую).
            start = (L + int(w * 0.96), T + int(h * 0.15))
            end = (L + int(w * 0.58), T + int(h * 0.85))
            _log(f"створка: правая секция {start} → {end} (рамка {box})")
        else:
            start, end = (1260, 330), (1160, 800)
        self._drag_abs(start[0], start[1], end[0], end[1], settle=1.0)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (протяжка створки).")

        # Снимаем инструмент створки, иначе следующий клик поставит вторую створку
        self.select_arrow_tool()

        # Москитная сетка: своего диалога параметров у неё нет — сначала включаем
        # инструмент «Москитная сетка», затем выбираем тип в верхнем ComboBox
        # и ставим сетку протяжкой по створке.
        # Сетка уходит отдельным товаром в A.R.C. Frame — в конструкции CCalc
        # её быть не должно (завод её не изготавливает в составе окна).
        if getattr(self, "_skip_net", False):
            _log("москитная сетка: не ставлю в конструкцию CCalc — она уходит отдельным товаром в A.R.C. Frame")
            return

        self.click_rel(*TOOL_NET)
        time.sleep(0.5)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (инструмент «Сетка»).")
        self._element_combo_hwnd = None
        net_matcher = lambda t: ("anwis" in t.lower() or "анвис" in t.lower()) and "имп" not in t.lower()
        if not self._set_element_type(net_matcher, "москитная сетка (Anwis)"):
            if not self._set_element_type(lambda t: "anwis" in t.lower() or "анвис" in t.lower(), "москитная сетка (Anwis)"):
                if not self._set_element_type(lambda t: "пмс" in t.lower() and "оконн" not in t.lower(), "москитная сетка"):
                    if not self._set_element_type(lambda t: "пмс" in t.lower(), "москитная сетка"):
                        raise CCalcStageError("add_sash_and_net: в списке нет москитной сетки.")

        # Сетка ставится ОДИНОЧНЫМ КЛИКОМ по створке. Протяжка здесь создавала
        # ещё одну вложенную створку — именно так получались две створки друг в друге.
        if box:
            L, T, R, B = box
            point = (L + int((R - L) * 0.75), T + int((B - T) * 0.5))
        else:
            point = (1180, 550)
        self._click_abs(point[0], point[1], settle=1.2)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (клик по створке для сетки).")
        _log(f"москитная сетка поставлена на створку (клик {point})")

        # Возвращаем «Стрелку», чтобы следующие клики не добавляли элементы
        self.select_arrow_tool()

    # Размер сетки ПМС Anwis: по эталону оператора для окна 1060×1340 —
    # 445×1235, то есть «ширина секции − 85» и «высота окна − 105».
    NET_WIDTH_OFFSET = 85
    NET_HEIGHT_OFFSET = 105

    def net_size(self, width_mm: int, height_mm: int) -> tuple:
        """Размер москитной сетки, который заказывает завод (ПМС Anwis)."""
        return (int(round(width_mm / 2.0)) - self.NET_WIDTH_OFFSET,
                int(height_mm) - self.NET_HEIGHT_OFFSET)

    def _order_cost(self) -> float:
        """Стоимость конструкции по «Листу заказа» (пустая строка → 0)."""
        self.switch_tab(1)
        time.sleep(0.7)
        raw = self._order_value("cost").replace("\xa0", "").replace(" ", "").replace(",", ".")
        self.switch_tab(0)
        time.sleep(0.5)
        try:
            return float(raw)
        except ValueError:
            return 0.0

    def add_handle_and_glass(self, side: str = "right"):
        """Ставит ручку HOPPE и стеклопакет в ОБЕ секции (глухая + створка).

        Тип стеклопакета — параметр заказа (`order_data['glass']['name']`),
        по умолчанию как в эталонной записи оператора: «24 4-16-4».
        """
        if self.dry_run:
            return
        self._cost_before_extras = self._order_cost()
        _log(f"стоимость до ручки/стеклопакета: {self._cost_before_extras:.2f} ₽")

        box = self._frame_screen_box()
        if not box:
            raise CCalcStageError("ручка/стеклопакет: рамка на чертеже не найдена")
        L, T, R, B = box
        sash_point = (L + int((R - L) * 0.76), T + int((B - T) * 0.5))
        fixed_point = (L + int((R - L) * 0.25), T + int((B - T) * 0.5))

        # 0. Ручка HOPPE — инструмент, затем клик по створке
        self.click_rel(*TOOL_HANDLE)
        time.sleep(0.5)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (инструмент «Ручка»).")
        self._click_abs(*sash_point, settle=0.9)
        _log(f"ручка HOPPE поставлена в точку {sash_point}")

        # 1. Стеклопакет со штапиками (как у оператора) или обычный
        glass = self.order_data.get("glass") or {}
        spec = (glass.get("name") or "24 4-16-4").strip()
        with_bead = glass.get("with_bead", True)
        self.click_rel(*(TOOL_GLASS_BEAD if with_bead else TOOL_GLASS))
        time.sleep(0.6)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (инструмент «Стеклопакет»).")

        # «24 4-16-4» → ищем по формуле «4-16-4» (в списке это «СПО-24 (4-16-4)»)
        tokens = spec.split()
        key = (tokens[-1] if len(tokens) > 1 else spec).replace(" ", "")
        if not self._set_element_type(lambda t: key in t.replace(" ", ""),
                                      f"стеклопакет {spec}"):
            _log(f"стеклопакет: в списке нет {key!r} — остаётся текущий тип")

        # 2. Стекло в обе секции: сначала глухая часть, потом створка
        for label, pt in (("глухая секция", fixed_point), ("створка", sash_point)):
            self._click_abs(pt[0], pt[1], settle=0.9)
            if self.dismiss_all_blocking_dialogs():
                raise CCalcNoOrderError("CCalc требует создать заказ (клик по секции для стеклопакета).")
            _log(f"стеклопакет {spec} поставлен: {label} {pt}")
        self.select_arrow_tool()

    def verify_handle_and_glass(self) -> bool:
        """Автопроверка: ручка и стеклопакет должны увеличить стоимость."""
        before = getattr(self, "_cost_before_extras", 0.0)
        self.switch_tab(1)
        time.sleep(1.0)
        self._click_recalc()
        after = float(self._order_value("cost").replace("\xa0", "").replace(" ", "").replace(",", ".") or 0)
        self.switch_tab(0)
        time.sleep(0.4)
        _log(f"автопроверка ручки/стеклопакета: стоимость {before:.2f} → {after:.2f} ₽")
        if after <= before + 1.0:
            raise CCalcStageError(
                f"ручка/стеклопакет не учтены в расчёте: стоимость не выросла ({before:.2f} → {after:.2f} ₽)")
        return True

    def add_hardware(self, brand: str = "vorne"):
        """Фурнитура на створку правой секции: VORNE (с микропров.) или другая.

        Порядок как у оператора: инструмент «Фурнитура» → выбор марки в верхнем
        ComboBox → для VORNE галочка «микропров.» → клик по створке.
        """
        if self.dry_run:
            return
        self._cost_before_hardware = self._order_cost()
        box = self._frame_screen_box()
        if not box:
            raise CCalcStageError("фурнитура: рамка на чертеже не найдена")
        L, T, R, B = box
        point = (L + int((R - L) * 0.76), T + int((B - T) * 0.5))

        self.click_rel(*TOOL_HARDWARE)
        time.sleep(0.6)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (инструмент «Фурнитура»).")

        # Марка фурнитуры в верхнем ComboBox. Для VORNE нужна именно
        # поворотно-откидная (Variat), а не «откидн.» — иначе створка получит не ту фурнитуру.
        want = _norm_text(brand)

        def hw_match(t):
            if want not in t:
                return False
            return ("пов-отк" in t) or ("variat" in t) or ("поворот" in t)

        if not self._set_element_type(hw_match, f"фурнитура {brand.upper()} пов-отк",
                                      hint_note=want):
            if not self._set_element_type(lambda t: want in t, f"фурнитура {brand.upper()}"):
                _log(f"фурнитура: не удалось выбрать {brand!r} — остаётся текущая марка")

        # Микропроветривание есть только у VORNE — оператор включает его всегда.
        # Важно: _norm_text() заменяет латиницу на кириллические гомоглифы, поэтому
        # сравниваем по исходной строке бренда, а не по нормализованной.
        brand_l = (brand or "").strip().lower()
        if "vorne" in brand_l or "ворне" in brand_l:
            self._enable_microvent()

        self._click_abs(*point, settle=1.2)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ (клик по створке для фурнитуры).")
        _log(f"фурнитура {brand.upper()} поставлена в точку {point}")
        self.select_arrow_tool()

    def _enable_microvent(self) -> bool:
        """Включает галочку «микропров.» в свойствах фурнитуры (если выключена)."""
        found = []

        def cb(h, _):
            try:
                if win32gui.GetClassName(h) != "TCheckBox":
                    return True
                text = _norm_text(_msg_text(h) or win32gui.GetWindowText(h))
                if "микропров" in text or "microp" in text:
                    found.append(h)
            except Exception:
                pass
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, cb, None)
        except Exception:
            pass
        if not found:
            _log("микропроветривание: чекбокс не найден — пропускаю")
            return False

        h = found[0]
        # У VCL-чекбокса работает BM_GETCHECK/BM_CLICK — надёжнее анализа пикселей
        state = win32gui.SendMessage(h, win32con.BM_GETCHECK, 0, 0)
        if state == 1:
            _log("микропроветривание уже включено (BM_GETCHECK=1)")
            return True
        win32gui.PostMessage(h, win32con.BM_CLICK, 0, 0)
        time.sleep(0.4)
        state = win32gui.SendMessage(h, win32con.BM_GETCHECK, 0, 0)
        ok = state == 1
        _log(f"микропроветривание: BM_GETCHECK 0 → {state} — {'включено' if ok else 'НЕ подтверждено'}")
        return ok

    def verify_hardware(self) -> bool:
        """Автопроверка: фурнитура должна увеличить стоимость."""
        before = getattr(self, "_cost_before_hardware", 0.0)
        self.switch_tab(1)
        time.sleep(1.0)
        self._click_recalc()
        time.sleep(1.2)
        after = float(self._order_value("cost").replace("\xa0", "").replace(" ", "").replace(",", ".") or 0)
        self.switch_tab(0)
        time.sleep(0.4)
        _log(f"автопроверка фурнитуры: стоимость {before:.2f} → {after:.2f} ₽")
        if after <= before + 1.0:
            raise CCalcStageError(f"фурнитура не учтена: стоимость не выросла ({before:.2f} → {after:.2f} ₽)")
        return True

    def _close_stale_dialogs(self) -> int:
        """Закрывает зависшие окна доп. элементов перед новым заказом.

        Если прошлый прогон оставил TDobElemDlg/TListElemDlg открытыми,
        главное окно заблокировано и меню «Заказ» не открывается.
        """
        stale = []

        def cb(h, _):
            try:
                if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) in (
                        "TDobElemDlg", "TListElemDlg", "TDobElemEditDlg",
                        # «Печать» (TShowPrint) открывается на весь экран и
                        # блокирует главное окно — без её закрытия меню «Заказ»
                        # не открывается вообще.
                        "TShowPrint", "TTestDlg", "THelpFurnDlg"):
                    stale.append(h)
            except Exception:
                pass
            return True

        try:
            win32gui.EnumWindows(cb, None)
        except Exception:
            pass
        for h in stale:
            try:
                win32gui.PostMessage(h, win32con.WM_CLOSE, 0, 0)
            except Exception:
                pass
        if stale:
            time.sleep(0.8)
            _log(f"закрыто зависших модальных окон: {len(stale)}")
        try:
            if not win32gui.IsWindowEnabled(self.hwnd):
                _log("внимание: главное окно CCalc заблокировано модальным окном — "
                     "меню «Заказ» не откроется, пока его не закрыть")
        except Exception:
            pass
        return len(stale)

    def _dialog_child(self, parent, cls_name: str):
        """Первый дочерний контрол указанного класса у диалога."""
        found = []

        def cb(h, _):
            if win32gui.GetClassName(h) == cls_name:
                found.append(h)
            return True

        try:
            win32gui.EnumChildWindows(parent, cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def _add_single_extra_element(self, dob, match_fn, param_val: int, elem_name: str) -> None:
        """Добавляет один элемент через «+» в TDobElemDlg и задаёт его параметр в TDobElemEditDlg."""
        # 1. Нажать первый «+» на панели инструментов
        self._click_dialog_abs(dob, 27, 54)
        time.sleep(1.1)

        lst = self._find_dialog_by_class("TListElemDlg", timeout=4.0)
        if not lst:
            raise CCalcStageError(f"{elem_name}: список элементов (TListElemDlg) не открылся — «+» не сработал")

        box = self._dialog_child(lst, "TListBox")
        items = self._combo_list_items(box) if box else []
        if not items:
            raise CCalcStageError(f"{elem_name}: каталог элементов пуст или не найден")

        idx = match_fn(items)
        _log(f"{elem_name}: выбор «{items[idx]}» (пункт {idx + 1}/{len(items)})")

        bx, by, br, bb = win32gui.GetWindowRect(box)
        top = max(0, idx - 2)
        win32gui.SendMessage(box, 0x0197, top, 0)  # LB_SETTOPINDEX
        time.sleep(0.3)
        px, py = bx + 40, by + 10 + (idx - top) * 19
        self._click_abs(px, py)
        time.sleep(0.4)
        sel = int(win32gui.SendMessage(box, 0x0188, 0, 0))  # LB_GETCURSEL
        if sel != idx:
            raise CCalcStageError(
                f"{elem_name}: в каталоге выбран пункт {sel}, а нужен {idx} — клик не попал в строку")
        if not self._click_dialog_button(lst, ("ok", "ок"), fallback_rel=(496, 10)):
            _log(f"{elem_name}: кнопка «ОК» в каталоге не найдена — жму по координате")
        time.sleep(1.0)
        if self._find_dialog_by_class("TListElemDlg", timeout=1.5):
            raise CCalcStageError(f"{elem_name}: каталог элементов не закрылся после «ОК»")

        # После «ОК» окно могло быть пересоздано — обновляем хэндл
        dob = self._find_dialog_by_class("TDobElemDlg", timeout=2.0) or dob

        # 2. В окне доп. элементов — двойной клик по добавленной строке
        lv = self._dialog_child(dob, "TListView")
        if not lv:
            raise CCalcStageError(f"{elem_name}: список строк в окне доп. элементов не найден")
        try:
            rows = int(win32gui.SendMessage(lv, 0x1004, 0, 0))  # LVM_GETITEMCOUNT
        except Exception:
            rows = 0
        if rows <= 0:
            raise CCalcStageError(f"{elem_name}: после «ОК» в окне доп. элементов нет ни одной строки")
        lx, ly, lr, lb = win32gui.GetWindowRect(lv)
        row_y = ly + 25 + max(0, rows - 1) * 19
        _log(f"{elem_name}: строк {rows}, двойной клик по последней строке в ({lx + 120}, {row_y})")
        win32api.SetCursorPos((lx + 120, row_y))
        time.sleep(0.06)
        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        time.sleep(0.04)
        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        time.sleep(0.05)
        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        time.sleep(0.04)
        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        time.sleep(0.6)

        # Fallback: если диалог не открылся от клика, нажимаем Enter на выбранной строке
        edlg = self._find_dialog_by_class("TDobElemEditDlg", timeout=1.0)
        if not edlg:
            win32gui.SendMessage(lv, win32con.WM_KEYDOWN, win32con.VK_RETURN, 0)
            time.sleep(0.05)
            win32gui.SendMessage(lv, win32con.WM_KEYUP, win32con.VK_RETURN, 0)
            time.sleep(0.8)

        # 3. Окно редактирования параметра (количество или длина)
        edlg = self._find_dialog_by_class("TDobElemEditDlg", timeout=4.0)
        if not edlg:
            raise CCalcStageError(f"{elem_name}: окно параметров элемента (TDobElemEditDlg) не открылось")

        written = self._fill_dialog(edlg, (param_val,))
        _log(f"{elem_name}: введено {written} (ожидалось {param_val})")
        if not written or str(written[0]).strip() != str(param_val):
            raise CCalcStageError(f"{elem_name}: значение не записалось (прочитано {written!r})")
        self._click_dialog_button(edlg, ("ok", "ок"), fallback_rel=(272, 50))
        time.sleep(0.8)
        if self._find_dialog_by_class("TDobElemEditDlg", timeout=1.0):
            raise CCalcStageError(f"{elem_name}: окно параметров не закрылось — значение не принято")

    def add_additional_elements(self, sill_length: int = None, sill_width: int = None,
                                anchor_count: int = None):
        """Добавляет установочные и доп. элементы (анкерные пластины, подоконник) точно по треку.

        Последовательность снята с эталонной записи (recorded_trace.json lines 1-210):
        1) «...» на вкладке «Чертёж» (TOOL_EXTRA) -> TDobElemDlg;
        2) ОДИН клик по строке конструкции (62, 102) — иначе «+» ничего не делает;
        3) Если указаны анкерные пластины:
           - Первый «+» на панели (27, 54) -> TListElemDlg;
           - Выбор «Анкерные пластины» в TListBox -> ОК;
           - Двойной клик по добавленной строке в TDobElemDlg -> TDobElemEditDlg;
           - Ввод количества (например, 10) -> ОК;
        4) Если указан подоконник:
           - Первый «+» на панели (27, 54) -> TListElemDlg;
           - Выбор «Подоконник» (с учётом ширины, напр. 350/400) -> ОК;
           - Двойной клик по строке подоконника -> TDobElemEditDlg;
           - Ввод длины (например, 1350) -> ОК;
        5) Закрытие TDobElemDlg (WM_CLOSE).
        """
        if self.dry_run:
            return
        if not anchor_count and not sill_length:
            return
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ перед добавлением доп. элементов.")
        self._cost_before_extra = self._order_cost()
        self._cost_before_windowsill = self._cost_before_extra

        self.click_rel(*TOOL_EXTRA)
        time.sleep(1.0)
        dob = self._find_dialog_by_class("TDobElemDlg", timeout=4.0)
        if not dob:
            raise CCalcStageError("доп. элементы: окно «Дополнительные элементы» не открылось.")

        try:
            # 1) выбрать конструкцию — иначе «+» не открывает каталог (из трека rel (62, 102))
            self._click_dialog_abs(dob, 62, 102)
            time.sleep(0.4)

            # 2) Анкерные пластины
            if anchor_count is not None and anchor_count > 0:
                def match_anchors(items):
                    cands = [i for i, t in enumerate(items)
                             if "анкер" in _norm_text(t) or "пластин" in _norm_text(t)]
                    if not cands:
                        raise CCalcStageError(
                            f"анкерные пластины: в списке элементов нет подходящей позиции — {items[:10]}")
                    return cands[0]

                self._add_single_extra_element(dob, match_anchors, anchor_count, "анкерные пластины")
                dob = self._find_dialog_by_class("TDobElemDlg", timeout=2.0) or dob
                time.sleep(0.5)

            # 3) Подоконник
            if sill_length is not None and sill_length > 0:
                width = sill_width or 350
                def match_sill(items):
                    w_str = str(width)
                    # 1. Приоритет: обычный «Подоконник ПВХ <ширина> мм» (без LUX, глянец, danke и т.п.)
                    pure_cands = [
                        i for i, t in enumerate(items)
                        if "подокон" in _norm_text(t)
                        and w_str in t
                        and not any(bad in _norm_text(t) for bad in ("lux", "люкс", "глянец", "danke", "данке", "moeller", "мёллер", "кристаллит", "crystallit"))
                    ]
                    if pure_cands:
                        return pure_cands[0]

                    # 2. Любой обычный подоконник без LUX
                    pure_any = [
                        i for i, t in enumerate(items)
                        if "подокон" in _norm_text(t)
                        and not any(bad in _norm_text(t) for bad in ("lux", "люкс", "глянец", "danke", "данке", "moeller", "мёллер"))
                    ]
                    if pure_any:
                        return pure_any[0]

                    # 3. Любой подоконник с указанной шириной
                    w_cands = [i for i, t in enumerate(items) if "подокон" in _norm_text(t) and w_str in t]
                    if w_cands:
                        return w_cands[0]

                    # 4. Любой подоконник
                    all_sills = [i for i, t in enumerate(items) if "подокон" in _norm_text(t)]
                    if all_sills:
                        return all_sills[0]
                    raise CCalcStageError(f"подоконник: в списке элементов нет ни одного подоконника — {items[:10]}")

                self._add_single_extra_element(dob, match_sill, sill_length, "подоконник")
                dob = self._find_dialog_by_class("TDobElemDlg", timeout=2.0) or dob
                time.sleep(0.5)
        finally:
            try:
                win32gui.PostMessage(dob, win32con.WM_CLOSE, 0, 0)
            except Exception:
                pass
            time.sleep(0.6)

    def add_windowsill(self, length_mm: int, width_mm: int):
        """Добавляет подоконник через «...» → доп. элементы."""
        return self.add_additional_elements(sill_length=length_mm, sill_width=width_mm)

    def add_anchor_plates(self, count: int = 10):
        """Добавляет анкерные пластины через «...» → доп. элементы."""
        return self.add_additional_elements(anchor_count=count)

    def verify_additional_elements(self) -> bool:
        """Автопроверка: доп. элементы должны увеличить стоимость заказа."""
        before = getattr(self, "_cost_before_extra", getattr(self, "_cost_before_windowsill", 0.0))
        self.switch_tab(1)
        time.sleep(1.0)
        self._click_recalc()
        time.sleep(1.2)
        after = float(self._order_value("cost").replace("\xa0", "").replace(" ", "").replace(",", ".") or 0)
        self.switch_tab(0)
        time.sleep(0.4)
        _log(f"автопроверка доп. элементов: стоимость {before:.2f} → {after:.2f} ₽")
        if after <= before + 0.5:
            raise CCalcStageError(
                f"доп. элементы не учтены в расчёте: стоимость не выросла ({before:.2f} → {after:.2f} ₽)")
        return True

    def verify_windowsill(self) -> bool:
        """Автопроверка: подоконник должен увеличить стоимость заказа."""
        return self.verify_additional_elements()

    def verify_anchor_plates(self) -> bool:
        """Автопроверка: анкерные пластины должны увеличить стоимость заказа."""
        return self.verify_additional_elements()

    def _find_recalc_button(self):
        """Finds 'Пересчитать заказ' button on Лист заказа tab by scanning child controls."""
        recalc_btn = None

        def enum_child(h, _):
            nonlocal recalc_btn
            if not win32gui.IsWindowVisible(h):
                return True
            cls = win32gui.GetClassName(h)
            txt = win32gui.GetWindowText(h)
            # Look for TButton or TBitBtn with "Пересчит" text
            if "button" in cls.lower() and ("пересчит" in txt.lower() or "recalc" in txt.lower()):
                recalc_btn = h
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, enum_child, None)
        except Exception:
            pass

        return recalc_btn

    # ---------- «Лист заказа»: скидка, тарифы, пересчёт ----------

    # Поля «Листа заказа» задаются якорем (смещение dx, dy от левого верхнего
    # угла окна в развёрнутом виде), а не номером в списке: у строк «Стоимость»
    # и «Стоимость со скидкой» первая колонка пустая, из-за чего индексы плавали.
    # Значения сняты с реальной формы: окно в (-8,-8), поле «Скидка №1» на (309,470).
    ORDER_ROWS = {
        "cost": (392, 461),
        "discount": (317, 478),
        "discounted": (392, 497),
        "demontage": (317, 514),
        "montage": (317, 531),
        # Шапка «Листа заказа»: адрес объекта и номер заказа
        "address": (256, 119),
        "order_number": (336, 99),
    }

    # Комбобоксы единиц измерения: Демонтаж (1м.пер.), Монтаж (1м.кв.)
    ORDER_UNIT_COMBOS = {
        "demontage": (260, 514),
        "montage": (260, 531),
    }

    def _order_unit_combo(self, key: str):
        """hwnd комбобокса единицы измерения («Лист заказа») по якорю."""
        if key not in self.ORDER_UNIT_COMBOS:
            return None
        dx, dy = self.ORDER_UNIT_COMBOS[key]
        try:
            left, top = win32gui.GetWindowRect(self.hwnd)[:2]
        except Exception:
            return None
        tx, ty = left + dx, top + dy
        found = []

        def cb(h, _):
            if win32gui.GetClassName(h) in ("TComboBox", "ComboBox") and win32gui.IsWindowVisible(h):
                l, t, r, b = win32gui.GetWindowRect(h)
                if abs(l - tx) <= 18 and abs(t - ty) <= 12:
                    found.append(h)
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def _set_order_unit(self, key: str, target_unit: str) -> bool:
        """Устанавливает единицу измерения в строке Листа заказа (напр. '1м.пер.', '1м.кв.')."""
        combo = self._order_unit_combo(key)
        if not combo:
            _log(f"единица измерения {key}: комбобокс не найден")
            return False
        count = win32gui.SendMessage(combo, win32con.CB_GETCOUNT, 0, 0)
        target_idx = -1
        items = []
        for i in range(count):
            buf = ctypes.create_unicode_buffer(256)
            try:
                _user32.SendMessageW(combo, win32con.CB_GETLBTEXT, i,
                                     ctypes.cast(buf, ctypes.c_void_p).value)
            except Exception:
                pass
            val = buf.value.strip().lower()
            items.append(val)
            if target_unit.lower() in val or val in target_unit.lower():
                target_idx = i

        if target_idx < 0:
            _log(f"единица измерения {key}: {target_unit!r} не найдена среди {items}")
            return False

        cur = win32gui.SendMessage(combo, win32con.CB_GETCURSEL, 0, 0)
        if cur == target_idx:
            _log(f"единица измерения {key} уже {items[cur]!r}")
            return True

        win32gui.SendMessage(combo, win32con.CB_SETCURSEL, target_idx, 0)
        parent = win32gui.GetParent(combo)
        ctrl_id = win32gui.GetDlgCtrlID(combo)
        win32gui.SendMessage(parent, win32con.WM_COMMAND,
                             win32api.MAKELONG(ctrl_id, win32con.CBN_SELCHANGE), combo)
        time.sleep(0.3)
        _log(f"единица измерения {key} переключена на {items[target_idx]!r} (пункт {target_idx + 1}/{count})")
        return True

    def _order_field(self, key: str):
        """hwnd поля «Листа заказа» по якорю (dx, dy от угла окна)."""
        dx, dy = self.ORDER_ROWS[key]
        try:
            left, top = win32gui.GetWindowRect(self.hwnd)[:2]
        except Exception:
            return None
        tx, ty = left + dx, top + dy
        found = []

        def cb(h, _):
            if win32gui.GetClassName(h) not in ("TEdit", "TLabeledEdit"):
                return True
            l, t, r, b = win32gui.GetWindowRect(h)
            if abs(l - tx) <= 12 and abs(t - ty) <= 8:
                found.append(h)
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, cb, None)
        except Exception:
            pass
        return found[0] if found else None

    def _order_value(self, key: str) -> str:
        """Текущее значение поля «Листа заказа»."""
        hwnd = self._order_field(key)
        return _msg_text(hwnd).strip() if hwnd else ""

    def _set_order_field(self, key: str, value, label: str) -> bool:
        """Записывает значение в поле листа заказа и проверяет чтением.

        Поле ищется по якорю (а не по жёсткой координате из старой трассы):
        из-за такой координаты «20» уезжало в чужой контрол, а проверка читала
        этот же чужой контрол и писала «ок».
        """
        hwnd = self._order_field(key)
        if not hwnd:
            _log(f"{label}: поле не найдено по якорю {self.ORDER_ROWS[key]}")
            return False
        l, t, r, b = win32gui.GetWindowRect(hwnd)
        self._click_abs((l + r) // 2, (t + b) // 2, settle=0.25)
        self._type_into_edit(hwnd, value)
        # Tab — чтобы Delphi применил значение (OnChange/OnExit)
        win32gui.PostMessage(hwnd, win32con.WM_KEYDOWN, win32con.VK_TAB, 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYUP, win32con.VK_TAB, 0)
        time.sleep(0.3)
        actual = _msg_text(hwnd).strip()
        ok = actual.replace(" ", "") == str(value)
        _log(f"{label}: в поле {actual!r} (ожидалось {value!r}) — {'ок' if ok else 'не принято'}")
        return ok

    def _set_order_text(self, key: str, value: str, label: str) -> bool:
        """Записывает текст (адрес объекта) в поле шапки «Листа заказа» и читает обратно."""
        hwnd = self._order_field(key)
        if not hwnd:
            _log(f"{label}: поле не найдено по якорю {self.ORDER_ROWS[key]}")
            return False
        cur = _msg_text(hwnd).strip()
        if cur == value.strip():
            _log(f"{label}: уже {cur!r}")
            return True
        l, t, r, b = win32gui.GetWindowRect(hwnd)
        self._click_abs((l + r) // 2, (t + b) // 2, settle=0.25)
        self._type_into_edit(hwnd, value)
        win32gui.PostMessage(hwnd, win32con.WM_KEYDOWN, win32con.VK_TAB, 0)
        win32gui.PostMessage(hwnd, win32con.WM_KEYUP, win32con.VK_TAB, 0)
        time.sleep(0.3)
        actual = _msg_text(hwnd).strip()
        ok = actual.replace(" ", "") == value.strip().replace(" ", "")
        _log(f"{label}: в поле {actual!r} (ожидалось {value!r}) — {'ок' if ok else 'не принято'}")
        return ok

    def _fill_edit_at(self, x: int, y: int, value, label: str = "") -> bool:
        """Кликает по полю ввода (X, Y в координатах TForm1) и вводит значение."""
        self.click_rel(x, y)
        time.sleep(0.2)

        ctrl = None
        try:
            # WindowFromPoint ждёт ЭКРАННЫЕ координаты, а x/y — координаты окна
            ox, oy = self._window_origin()
            ctrl = win32gui.WindowFromPoint((x + ox, y + oy))
        except Exception:
            pass
        if ctrl and "edit" not in win32gui.GetClassName(ctrl).lower():
            try:
                parent = win32gui.GetParent(ctrl)
                if parent and "edit" in win32gui.GetClassName(parent).lower():
                    ctrl = parent
            except Exception:
                pass
        if not ctrl:
            _log(f"{label}: поле ввода в ({x},{y}) не найдено")
            return False

        self._focus_control(ctrl)
        try:
            win32gui.SendMessage(ctrl, win32con.EM_SETSEL, 0, -1)
        except Exception:
            pass
        _msg_set_text(ctrl, value)
        try:
            parent = win32gui.GetParent(ctrl)
            cid = win32gui.GetDlgCtrlID(ctrl)
            win32gui.SendMessage(parent, win32con.WM_COMMAND,
                                 win32api.MAKELONG(cid, win32con.EN_CHANGE), ctrl)
        except Exception:
            pass
        time.sleep(0.15)

        # Уводим фокус (Tab), чтобы Delphi применил значение по OnExit
        win32gui.PostMessage(ctrl, win32con.WM_KEYDOWN, win32con.VK_TAB, 0)
        win32gui.PostMessage(ctrl, win32con.WM_KEYUP, win32con.VK_TAB, 0)
        time.sleep(0.25)

        actual = _msg_text(ctrl).strip()
        ok = actual.replace(" ", "") == str(value)
        _log(f"{label}: в поле {actual!r} (ожидалось {value!r}) — {'ок' if ok else 'проверьте вручную'}")
        return ok

    def _click_recalc(self, nudge: int = 0):
        """Нажимает «Пересчитать заказ» (X=150, Y=573 из записанного трека)."""
        btn = self._find_recalc_button()
        if btn:
            try:
                rect = win32gui.GetWindowRect(btn)
                self._click_abs((rect[0] + rect[2]) // 2, (rect[1] + rect[3]) // 2, settle=0.05)
                time.sleep(1.6)
            except Exception:
                btn = None
        if not btn:
            self.click_rel(150 + nudge, 573 + nudge)
            time.sleep(1.6)

        # Если вместо пересчёта открылась «Печать» — закрываем и сообщаем
        prn = self._find_dialog_by_class("TShowPrint", timeout=0.4)
        if prn:
            _log("вместо пересчёта открылась «Печать» — закрываю")
            win32gui.PostMessage(prn, win32con.WM_CLOSE, 0, 0)
            time.sleep(0.4)
        self.dismiss_error_dialog()

    def _recalculate_with_retries(self, attempts: int = 3, min_total: float = 1000.0) -> dict:
        """Пересчитывает заказ и проверяет, что сумма действительно появилась."""
        last = None
        for i in range(attempts):
            self._click_recalc(nudge=i)
            try:
                info = self.read_order_info()
            except CCalcStageError as e:
                last = e
                _log(f"пересчёт {i + 1}/{attempts}: сумма не прочиталась — {e}")
                time.sleep(0.8)
                continue
            if info["total_amount"] >= min_total:
                _log(f"пересчёт {i + 1}/{attempts}: сумма {info['total_amount']:.2f} ₽")
                return info
            last = info
            _log(f"пересчёт {i + 1}/{attempts}: сумма слишком мала — {info['total_amount']:.2f} ₽")
            time.sleep(0.8)
        raise CCalcStageError(
            f"«Пересчитать заказ» не дал суммы ≥ {min_total:.0f} ₽ после {attempts} попыток: {last}")

    def fill_order_sheet_and_recalc(self, address: str = "", discount: int = 20,
                                    montage_rate: int = 2365, demontage_rate: int = 100) -> dict:
        """Заполняет «Лист заказа» (скидка, монтаж, демонтаж) и пересчитывает заказ."""
        if self.dry_run:
            return {}
        self.switch_tab(1)  # Лист заказа
        time.sleep(0.8)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ перед заполнением листа заказа.")

        _log("лист заказа до правки: " + ", ".join(
            f"{k}={self._order_value(k)!r}" for k in ("cost", "discount", "discounted",
                                                      "demontage", "montage")))

        # Адрес объекта — в шапку листа заказа (раньше он только писался в лог)
        if address:
            if not self._set_order_text("address", address, "адрес объекта"):
                raise CCalcStageError(f"адрес «{address}» не записался в «Лист заказа»")

        # Единицы измерения: демонтаж — 1м.пер., монтаж — 1м.кв.
        self._set_order_unit("demontage", "1м.пер.")
        self._set_order_unit("montage", "1м.кв.")

        cost_before = self._order_value("cost")
        self._set_order_field("discount", discount, "скидка %")
        self._set_order_field("demontage", demontage_rate, "демонтаж")
        self._set_order_field("montage", montage_rate, "монтаж")

        _log(f"лист заказа заполнен для «{address}»: скидка {discount}%, "
             f"монтаж {montage_rate}, демонтаж {demontage_rate}")
        info = self._recalculate_with_retries()

        # Автопроверка скидки: «Стоимость со скидкой» должна стать меньше «Стоимости»
        def _num(text):
            try:
                return float(text.replace("\xa0", "").replace(" ", "").replace(",", "."))
            except ValueError:
                return 0.0

        cost = _num(self._order_value("cost"))
        discounted = _num(self._order_value("discounted"))
        if cost > 0 and discounted > 0:
            ratio = discounted / cost
            _log(f"автопроверка скидки: {cost:.2f} → {discounted:.2f} "
                 f"(коэффициент {ratio:.3f}, ожидался {1 - discount / 100:.3f})")
            if abs(ratio - (1 - discount / 100.0)) > 0.02:
                raise CCalcStageError(
                    f"скидка не применилась: «Стоимость со скидкой» {discounted:.2f} "
                    f"при «Стоимости» {cost:.2f} (коэффициент {ratio:.3f}); "
                    f"стоимость до правки {cost_before!r}")
        return info

    def save_order(self) -> str:
        """Сохраняет заказ (Ctrl+S) и возвращает его номер из поля «№ заказа».

        CCalc на повторное сохранение спрашивает «сохранить под другим номером?»
        (это создаёт копию заказа) — отвечаем «Да» только если номер ещё не
        появился, иначе номер просто перечитываем.
        """
        if self.dry_run:
            return "Д-DRYRUN"
        self.switch_tab(1)
        time.sleep(0.6)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ перед сохранением.")
        try:
            win32gui.SetForegroundWindow(self.hwnd)
        except Exception:
            pass
        time.sleep(0.3)

        before = self._order_value("order_number")
        win32api.keybd_event(win32con.VK_CONTROL, 0, 0, 0)
        win32api.keybd_event(0x53, 0, 0, 0)  # 'S'
        time.sleep(0.05)
        win32api.keybd_event(0x53, 0, win32con.KEYEVENTF_KEYUP, 0)
        win32api.keybd_event(win32con.VK_CONTROL, 0, win32con.KEYEVENTF_KEYUP, 0)
        time.sleep(1.8)

        q = self._find_dialog_by_class("#32770", timeout=1.5)
        if q:
            _log(f"при сохранении открылся диалог {win32gui.GetWindowText(q)!r} — подтверждаю «Да»")
            self._click_dialog_button(q, ("да", "yes"))
            time.sleep(1.2)
        self.dismiss_all_blocking_dialogs()

        number = self._order_value("order_number").strip()
        if not number:
            time.sleep(1.5)
            number = self._order_value("order_number").strip()
        _log(f"заказ сохранён (было {before!r}), номер заказа: {number or 'не прочитан'}")
        return number

    # Реестр заказов для A.R.C. Frame: по нему приложение импортирует заказ
    # из CCalc по номеру, без повторного прогона и без фото записки.
    ORDER_REGISTRY_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "orders")

    def register_order(self, number: str, total: float = 0.0, address: str = "",
                       net: dict = None, flashing: dict = None,
                       windowsill: dict = None) -> str:
        """Записывает сведения о сохранённом заказе в реестр tools/pvc_bridge/orders."""
        if not number:
            return ""
        frame = self.order_data.get("frame", {}) if isinstance(self.order_data, dict) else {}
        data = {
            "order_number": str(number),
            "address": address or (self.order_data.get("address", "")
                                   if isinstance(self.order_data, dict) else ""),
            "total_amount": round(float(total or 0), 2),
            "frame": {
                "width_mm": int(frame.get("width_mm", 0) or 0),
                "height_mm": int(frame.get("height_mm", 0) or 0),
            },
            "windowsill": windowsill or {},
            "flashing": flashing or {},
            "mosquito_net": net or {},
            "saved_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        }
        try:
            os.makedirs(self.ORDER_REGISTRY_DIR, exist_ok=True)
            path = os.path.join(self.ORDER_REGISTRY_DIR, f"{number}.json")
            with open(path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            _log(f"заказ №{number} записан в реестр для A.R.C. Frame: {path}")
            return path
        except Exception as e:
            _log(f"реестр заказов: записать №{number} не удалось: {e}")
            return ""

    def remove_net_from_sash(self):
        """Снимает москитную сетку со створки (завод её не изготавливает) и пересчитывает заказ."""
        if self.dry_run:
            return None
        self.switch_tab(0)  # Чертёж
        time.sleep(0.4)
        if self.dismiss_all_blocking_dialogs():
            raise CCalcNoOrderError("CCalc требует создать заказ перед снятием сетки.")

        stvor_dlg = self._open_sash_params([(1068, 325), (1102, 325), (1241, 350)])
        if not stvor_dlg:
            raise CCalcStageError("remove_net_from_sash: TStvorParDlg не открылся — сетку снять не удалось.")

        self._select_sash_net(stvor_dlg, "none")
        self._click_dialog_button(stvor_dlg, ("ok", "ок", "закр", "close"))
        time.sleep(0.6)
        if self._find_dialog_by_class("TStvorParDlg", timeout=1.0):
            raise CCalcStageError("remove_net_from_sash: TStvorParDlg не закрылся.")
        self.select_arrow_tool()

        self.switch_tab(1)  # Лист заказа
        time.sleep(0.6)
        return self._recalculate_with_retries()

    def read_order_info(self):
        """Read Order Number and Total Amount from 'Лист заказа' controls."""
        if self.dry_run:
            # Deterministic mock calculation for test/fallback
            w = self.order_data.get("frame", {}).get("width_mm", 1060)
            h = self.order_data.get("frame", {}).get("height_mm", 1340)
            area = (w * h) / 1_000_000.0
            perimeter = 2 * (w + h) / 1000.0

            # Base PVC approx formula
            pvc_base = area * 8500 + 3500  # sash hardware
            montage = area * 2365
            demontage = perimeter * 100
            sill_cost = (1350 / 1000.0) * 1200  # approx sill per meter
            subtotal = (pvc_base + sill_cost) * 0.8 + montage + demontage

            # Net dimension approx: sash width = half frame - 80mm
            net_w, net_h = self.net_size(w, h)

            return {
                "order_number": f"Д-{time.strftime('%Y%m%d')}-01",
                "total_amount": round(subtotal, 2),
                "mosquito_net": {
                    "brand": "Anwis (ПП)",
                    "width_mm": net_w,
                    "height_mm": net_h
                }
            }

        import re

        # Номер заказа — из заголовка «[Заказ: X]» или из верхнего поля ввода
        order_num = ""
        title = win32gui.GetWindowText(self.hwnd)
        m = re.search(r'\[Заказ:\s*([^\]]+)\]', title)
        if m:
            order_num = m.group(1).strip()

        # Сумма: CCalc выводит итог и в TEdit, и в TStaticText / TLabel
        all_values = []
        try:
            wnd_top = win32gui.GetWindowRect(self.hwnd)[1]
        except Exception:
            wnd_top = 0

        def enum_child(h, _):
            nonlocal order_num
            if not win32gui.IsWindowVisible(h):
                return True
            cls = win32gui.GetClassName(h)
            if cls not in ("TEdit", "TLabeledEdit", "TStaticText", "TLabel"):
                return True

            txt = _msg_text(h).strip()
            if not txt:
                return True

            # Номер заказа из верхнего поля ввода
            if not order_num and cls in ("TEdit", "TLabeledEdit"):
                try:
                    if win32gui.GetWindowRect(h)[1] < wnd_top + 200 and txt.replace(" ", "").isdigit():
                        order_num = txt
                except Exception:
                    pass

            # Русские числа: «9 559» / «9559,00» / «9 559.71»
            normalized = txt.replace("\xa0", "").replace(" ", "").replace(",", ".")
            try:
                val = float(normalized)
            except ValueError:
                return True
            if val > 100.0:  # Отсекаем мусорные числа (площадь, количество и т.п.)
                all_values.append(val)
            return True

        try:
            win32gui.EnumChildWindows(self.hwnd, enum_child, None)
        except Exception:
            pass

        # Итог заказа — максимальное осмысленное число на «Листе заказа»
        total_amount = max(all_values) if all_values else 0.0

        if total_amount <= 10.0:
            raise CCalcStageError(
                f"CCalc 9.12 не рассчитал заказ: номер '{order_num or 'не найден'}', "
                f"сумма {total_amount:.2f} ₽ (числовых полей найдено: {len(all_values)}). "
                "Конструкция не нарисована или не пересчитана во вкладке 'Лист заказа'."
            )

        if not order_num:
            _log("номер заказа в заголовке не найден — верну пустую строку")

        w = self.order_data.get("frame", {}).get("width_mm", 1060)
        h = self.order_data.get("frame", {}).get("height_mm", 1340)
        net_w, net_h = self.net_size(w, h)

        return {
            "order_number": order_num,
            "total_amount": round(total_amount, 2),
            "mosquito_net": {
                "brand": "Anwis (ПП)",
                "width_mm": net_w,
                "height_mm": net_h
            }
        }

    def _draw_construction(self):
        """Строит раму, импост, створку с сеткой и доп. элементы (анкерные пластины, подоконник) с самовосстановлением.

        На каждый шаг — до 3 попыток. Если CCalc потребовал новый заказ —
        создаём его заново и начинаем с построения рамы.
        """
        frame = self.order_data.get("frame", {})
        w = frame.get("width_mm", 1060)
        h = frame.get("height_mm", 1340)
        sill = self.order_data.get("accessories", {}).get("windowsill", {})
        sill_w = sill.get("width_mm", 350) if isinstance(sill, dict) else 350
        sill_l = sill.get("length_mm", 1350) if isinstance(sill, dict) else 1350
        anchors = self.order_data.get("accessories", {}).get("anchor_plates", {})
        anchor_cnt = anchors.get("count", 10) if isinstance(anchors, dict) else 10

        has_sill = not getattr(self, "_skip_windowsill", False) and bool(sill)
        has_anchors = not getattr(self, "_skip_anchor_plates", False) and bool(anchors)

        hardware = (self.order_data.get("hardware") or "vorne").strip().lower()
        self._step_log = []
        stages = [
            ("рама", lambda: self.build_frame(w, h),
             lambda: self.verify_frame(w, h), True),
            ("импост", lambda: self.add_impost(w, h),
             lambda: self.verify_impost(w), True),
            ("створка" if getattr(self, "_skip_net", False) else "створка + сетка",
             lambda: self.add_sash_and_net(w, h),
             lambda: self.verify_sash("right"), True),
            ("ручка + стеклопакет", lambda: self.add_handle_and_glass(),
             lambda: self.verify_handle_and_glass(), True),
            ("фурнитура", lambda: self.add_hardware(hardware),
             lambda: self.verify_hardware(), True),
        ]

        if has_anchors or has_sill:
            if has_anchors and has_sill:
                step_name = "доп. элементы (анкерные пластины + подоконник)"
            elif has_anchors:
                step_name = "анкерные пластины"
            else:
                step_name = "подоконник"

            stages.append((
                step_name,
                lambda: self.add_additional_elements(
                    sill_length=sill_l if has_sill else None,
                    sill_width=sill_w if has_sill else None,
                    anchor_count=anchor_cnt if has_anchors else None
                ),
                lambda: self.verify_additional_elements(),
                False
            ))

        retries = {}
        restarts = 0
        index = 0
        while index < len(stages):
            name, action, verify, critical = stages[index]
            try:
                if not self._ensure_window():
                    raise CCalcStageError("главное окно CCalc недоступно (закрыто или упало)")
                _log(f"шаг: {name}")
                action()
                if verify:
                    verify()
                    _log(f"шаг «{name}»: автопроверка пройдена")
                    self._step_log.append({"step": name, "ok": True, "detail": "автопроверка пройдена"})
                else:
                    self._step_log.append({"step": name, "ok": True, "detail": "без автопроверки"})
                index += 1
            except CCalcNoOrderError as e:
                restarts += 1
                if restarts > 3:
                    raise CCalcStageError(f"CCalc не удерживает заказ после 3 перезапусков: {e}")
                _log(f"шаг «{name}»: {e} — создаю новый заказ и начинаю с рамы")
                self.create_new_order()
                index = 0
            except CCalcStageError as e:
                retries[name] = retries.get(name, 0) + 1
                if retries[name] >= 3:
                    if critical:
                        self._step_log.append({"step": name, "ok": False, "detail": str(e)})
                        raise CCalcStageError(f"шаг «{name}» не удался {retries[name]} раза: {e}")
                    _log(f"шаг «{name}» пропущен (некритично): {e}")
                    self._step_log.append({"step": name, "ok": False, "detail": f"пропущен: {e}"})
                    self.dismiss_all_blocking_dialogs()
                    index += 1
                    continue
                _log(f"шаг «{name}»: {e} — повтор {retries[name] + 1}/3")
                self.dismiss_all_blocking_dialogs()
                time.sleep(0.8)

    def run_build(self, width_mm: int = 1060, height_mm: int = 1340,
                  glass: str = "24 4-16-4", hardware: str = "vorne",
                  windowsill: bool = False, sill_width: int = 350, sill_length: int = 1350,
                  anchor_plates: bool = False, anchor_count: int = 10,
                  address: str = "", flashing_width: int = 130,
                  with_net: bool = False, net_width: int = 0, net_height: int = 0) -> dict:
        """Строит окно в CCalc по эталонной записи оператора — с проверкой каждого шага.

        Что может пойти не так и чем это ловится:
        - заказ не создан → CCalc пишет «сначала создайте заказ» — проверяем
          модальные окна после создания и галочку конструкции в «Листе заказа»;
        - размеры не приняты → диалог «Габариты» не закрылся — проверяем, что окно исчезло;
        - рамка не та → отношение сторон по снимку чертежа (verify_frame);
        - импост не по центру → ширина секций и смещение импоста (verify_impost);
        - две створки / не та сторона → распределение тёмных пикселей (verify_sash);
        - ручка/стекло не встали → стоимость не выросла (verify_handle_and_glass);
        - фурнитура не встала → стоимость не выросла (verify_hardware).
        """
        self.order_data = {
            "frame": {"width_mm": width_mm, "height_mm": height_mm},
            "glass": {"name": glass, "with_bead": True},
            "hardware": hardware,
            "accessories": {
                "windowsill": {"width_mm": sill_width, "length_mm": sill_length} if windowsill else {},
                "anchor_plates": {"count": anchor_count} if anchor_plates else {},
            },
        }
        self._step_log = []
        self._skip_windowsill = not windowsill
        self._skip_anchor_plates = not anchor_plates
        self._skip_net = not with_net

        if self.dry_run:
            steps = [
                {"step": "новый заказ", "ok": True, "detail": "dry-run: заказ создан, конструкция отмечена"},
                {"step": "рама", "ok": True, "detail": f"dry-run: {width_mm}x{height_mm}"},
                {"step": "импост", "ok": True, "detail": "dry-run: импост по центру"},
                {"step": "створка + сетка" if with_net else "створка", "ok": True,
                 "detail": "dry-run: правая створка" + (" с сеткой Anwis" if with_net else "")},
                {"step": "ручка + стеклопакет", "ok": True, "detail": "dry-run: ручка и стеклопакет"},
                {"step": "фурнитура", "ok": True, "detail": f"dry-run: {hardware}"},
            ]
            if anchor_plates or windowsill:
                extras = []
                if anchor_plates:
                    extras.append(f"анкерные пластины ({anchor_count} шт)")
                if windowsill:
                    extras.append(f"подоконник ({sill_width}x{sill_length})")
                steps.append({"step": "доп. элементы", "ok": True, "detail": f"dry-run: {', '.join(extras)}"})

            net_w_dry, net_h_dry = self.net_size(width_mm, height_mm)
            steps.append({"step": "сетка/отлив (в A.R.C. Frame)", "ok": True,
                          "detail": f"dry-run: ПМС Anwis {net_w_dry}×{net_h_dry}, "
                                    f"отлив {flashing_width}×{max(0, (sill_length if windowsill else width_mm) - 100)}, "
                                    f"адрес {address or 'не указан'}"})

            return {"success": True, "width_mm": width_mm, "height_mm": height_mm,
                    "glass": glass, "hardware": hardware,
                    "anchor_plates": {"count": anchor_count} if anchor_plates else {},
                    "windowsill": {"width_mm": sill_width, "length_mm": sill_length} if windowsill else {},
                    "steps": steps}

        self.ensure_running()
        self._close_stale_dialogs()
        # Окно CCalc должно быть сверху: иначе клики по menu/палитре попадают
        # в чужое окно (симптом — «меню «Заказ» не открылось» три раза подряд).
        self.activate_window()
        time.sleep(0.5)

        # 1. Новый заказ — без него CCalc не даёт строить
        try:
            self.switch_tab(0)
            time.sleep(0.4)
            self.create_new_order()
            blocked = self._main_window_blocked_by()
            if blocked:
                raise CCalcStageError(
                    f"после создания заказа остался модальный диалог "
                    f"{win32gui.GetClassName(blocked)}")
            self._step_log.append({"step": "новый заказ", "ok": True,
                                   "detail": "заказ создан, конструкция отмечена"})
        except Exception as e:
            self._step_log.append({"step": "новый заказ", "ok": False, "detail": str(e)})
            return {"success": False, "stage": "новый заказ", "error": str(e),
                    "steps": self._step_log}

        # 2. Профиль и построение окна
        try:
            self.switch_tab(0)
            time.sleep(0.5)
            self.ensure_profile_deceuninck()
            self._draw_construction()
        except Exception as e:
            return {"success": False, "stage": "построение", "error": str(e),
                    "steps": self._step_log}

        # Сетка и отлив — то, что дальше уезжает в A.R.C. Frame:
        # сетка — по эталону оператора (445×1235 для окна 1060×1340),
        # отлив — на 100 мм короче подоконника.
        net_w, net_h = self.net_size(width_mm, height_mm)
        if net_width > 0:
            net_w = net_width
        if net_height > 0:
            net_h = net_height
        _log(f"сетка ПМС Anwis: {net_w}×{net_h} мм, отлив: {flashing_width}×"
             f"{max(0, (sill_length if windowsill else width_mm) - 100)} мм")
        flashing_len = max(0, (sill_length if windowsill else width_mm) - 100)
        return {
            "success": True,
            "width_mm": width_mm,
            "height_mm": height_mm,
            "glass": glass,
            "hardware": hardware,
            "address": address,
            "order_number": "",
            "total_amount": 0.0,
            "steps": self._step_log,
            "recognized_data": {
                "address": address,
                "frame": {"width_mm": width_mm, "height_mm": height_mm},
                "accessories": {
                    "windowsill": ({"width_mm": sill_width, "length_mm": sill_length}
                                    if windowsill else {}),
                    "flashing": {"width_mm": flashing_width, "length_mm": flashing_len},
                    "anchor_plates": {"count": anchor_count} if anchor_plates else {},
                },
            },
            "calculation": {
                "order_number": "",
                "total_amount": 0.0,
                "address": address,
                "mosquito_net": {"brand": "Anwis (ПП)", "width_mm": net_w, "height_mm": net_h},
                "flashing": {"width_mm": flashing_width, "length_mm": flashing_len},
                "windowsill": ({"width_mm": sill_width, "length_mm": sill_length}
                               if windowsill else {}),
            },
        }

    def run_calculation(self, order_dict: dict) -> dict:
        self.order_data = order_dict
        try:
            self.ensure_running()
            self.activate_window()
            time.sleep(0.4)
            self.create_new_order()
            self.dismiss_error_dialog()

            net_removed = False
            if not self.dry_run:
                # 1. Вкладка «Чертёж» и профиль Deceuninck
                self.switch_tab(0)
                time.sleep(0.5)
                self.ensure_profile_deceuninck()

                # 2. Рама → импост → створка → подоконник.
                # Сетку в конструкцию CCalc не ставим: она уходит отдельным
                # товаром в A.R.C. Frame (завод её не изготавливает в составе окна).
                self._skip_net = True
                self._draw_construction()

                # 3. «Лист заказа»: скидка, монтаж, демонтаж и пересчёт
                serv = order_dict.get("services_and_discounts", {})
                montage = serv.get("installation_rate_rub_m2", 2365)
                demontage = serv.get("demolition_rate_rub_m_p", 100)
                addr = order_dict.get("address", "Новый объект")
                info = self.fill_order_sheet_and_recalc(
                    addr, discount=20, montage_rate=montage, demontage_rate=demontage)
                total_with_net = info["total_amount"]

                # 4. Сетки в конструкции нет (см. _skip_net) — снимать нечего
                if self._skip_net:
                    info["total_amount"] = total_with_net
                else:
                    try:
                        final_info = self.remove_net_from_sash()
                        if final_info and final_info["total_amount"] > 10.0:
                            info = final_info
                            net_removed = True
                        else:
                            _log("после снятия сетки сумму прочитать не удалось — оставляю сумму с сеткой")
                            info["total_amount"] = total_with_net
                    except Exception as e:
                        _log(f"сетку снять не удалось ({e}) — оставляю сумму с сеткой")
                        info["total_amount"] = total_with_net

                # 5. Сохраняем заказ: без сохранения нет номера, а A.R.C. Frame
                # без номера подставляет выдуманный «CCalc-MMdd-HHmm».
                try:
                    saved_number = self.save_order()
                    if saved_number:
                        info["order_number"] = saved_number
                        sill = order_dict.get("accessories", {}).get("windowsill", {})
                        flash = order_dict.get("accessories", {}).get("flashing", {})
                        self.register_order(
                            saved_number,
                            total=info.get("total_amount", 0) or 0,
                            address=order_dict.get("address", ""),
                            net=info.get("mosquito_net"),
                            flashing=flash if isinstance(flash, dict) else {},
                            windowsill=sill if isinstance(sill, dict) else {})
                except Exception as e:
                    _log(f"сохранить заказ не удалось: {e}")
            else:
                info = self.read_order_info()

            addr = order_dict.get("address", "Новый объект")
            flashing = order_dict.get("accessories", {}).get("flashing", {})
            sill = order_dict.get("accessories", {}).get("windowsill", {})
            anchors = order_dict.get("accessories", {}).get("anchor_plates", {})

            return {
                "success": True,
                "address": addr,
                "order_number": info["order_number"],
                "total_amount": info["total_amount"],
                "mosquito_net": info["mosquito_net"],
                "net_removed": net_removed,
                "flashing": {
                    "width_mm": flashing.get("width_mm", 130) if isinstance(flashing, dict) else 130,
                    "length_mm": flashing.get("length_mm", 1200) if isinstance(flashing, dict) else 1200
                },
                "windowsill": sill,
                "anchor_plates": anchors,
                "discount": order_dict.get("services_and_discounts", {}).get("discount_1", "20%"),
                "error": None
            }
        except Exception as e:
            return {
                "success": False,
                "error": str(e)
            }

def main():
    parser = argparse.ArgumentParser(description="CCalc Window Calculation Bot")
    parser.add_argument("--json", type=str, help="JSON input string or path to JSON file")
    parser.add_argument("--test", action="store_true", help="Run with default test order data")
    parser.add_argument("--build", action="store_true",
                        help="Только построить окно (новый заказ + чертёж), без листа заказа и цен")
    parser.add_argument("--width", type=int, default=1060, help="Ширина окна, мм (для --build)")
    parser.add_argument("--height", type=int, default=1340, help="Высота окна, мм (для --build)")
    parser.add_argument("--glass", type=str, default="24 4-16-4",
                        help="Стеклопакет для --build, например '24 4-16-4'")
    parser.add_argument("--hardware", type=str, default="vorne",
                        help="Фурнитура для --build: vorne / roto")
    parser.add_argument("--windowsill", action="store_true",
                        help="Добавить подоконник в --build")
    parser.add_argument("--sill-width", type=int, default=350,
                        help="Ширина подоконника, мм (для --build, по умолчанию 350)")
    parser.add_argument("--sill-length", type=int, default=1350,
                        help="Длина подоконника, мм (для --build, по умолчанию 1350)")
    parser.add_argument("--anchor-plates", action="store_true",
                        help="Добавить анкерные пластины (10 шт.) в --build")
    parser.add_argument("--anchor-count", type=int, default=10,
                        help="Количество анкерных пластин для --build (по умолчанию 10)")
    parser.add_argument("--address", type=str, default="Димитрова 45/46",
                        help="Адрес объекта (пишется в «Лист заказа»)")
    parser.add_argument("--with-net", action="store_true",
                        help="Поставить москитную сетку и в конструкцию CCalc (по умолчанию — только в A.R.C. Frame)")
    parser.add_argument("--net-width", type=int, default=0,
                        help="Ширина сетки, мм (по умолчанию — ширина секции − 85)")
    parser.add_argument("--net-height", type=int, default=0,
                        help="Высота сетки, мм (по умолчанию — высота окна − 105)")
    parser.add_argument("--flashing-width", type=int, default=130,
                        help="Глубина отлива для A.R.C. Frame, мм (по умолчанию 130)")
    parser.add_argument("--save", action="store_true",
                        help="Сохранить заказ в CCalc и прочитать его номер")
    parser.add_argument("--services", action="store_true",
                        help="Также заполнить услуги (скидка 20%%, монтаж 2365, демонтаж 100) на листе заказа")
    parser.add_argument("--discount", type=int, default=20, help="Скидка, %% (по умолчанию 20)")
    parser.add_argument("--montage", type=int, default=2365, help="Монтаж, руб (по умолчанию 2365)")
    parser.add_argument("--demontage", type=int, default=100, help="Демонтаж, руб (по умолчанию 100)")
    parser.add_argument("--dry-run", action="store_true", help="Simulate without touching CCalc")
    parser.add_argument("--prog-path", type=str, default="", help="Path to CCalc executable")
    parser.add_argument("--user", type=str, default="окно", help="CCalc login user")
    parser.add_argument("--password", type=str, default="1234", help="CCalc login password")
    args = parser.parse_args()

    order = {
        "address": "Димитрова 45/46",
        "frame": {"width_mm": 1060, "height_mm": 1340},
        "configuration": {
            "left_part": "fixed",
            "right_part": "tilt_and_turn",
            "mosquito_net": "Anwis"
        },
        "accessories": {
            "windowsill": {"width_mm": 350, "length_mm": 1350},
            "flashing": {"width_mm": 130, "length_mm": 1200},
            "anchor_plates": {"count": 10}
        },
        "services_and_discounts": {
            "installation_rate_rub_m2": 2365,
            "demolition_rate_rub_m_p": 100,
            "discount_1": "20%"
        }
    }

    if args.json:
        if os.path.exists(args.json):
            with open(args.json, "r", encoding="utf-8") as f:
                order = json.load(f)
        else:
            order = json.loads(args.json)

    bot = CCalcBot(dry_run=args.dry_run, prog_path=args.prog_path, username=args.user, password=args.password)
    if args.build:
        result = bot.run_build(args.width, args.height, args.glass, args.hardware,
                               windowsill=args.windowsill,
                               sill_width=args.sill_width,
                               sill_length=args.sill_length,
                               anchor_plates=args.anchor_plates,
                               anchor_count=args.anchor_count,
                               address=args.address,
                               flashing_width=args.flashing_width,
                               with_net=args.with_net,
                               net_width=args.net_width,
                               net_height=args.net_height)
        if args.services and not args.dry_run and result.get("success"):
            try:
                info = bot.fill_order_sheet_and_recalc(
                    address=order.get("address", "Новый объект"),
                    discount=args.discount,
                    montage_rate=args.montage,
                    demontage_rate=args.demontage
                )
                result["order_sheet"] = info
                result["total_amount"] = info.get("total_amount")
                result.setdefault("calculation", {})["total_amount"] = info.get("total_amount")
                if info.get("order_number"):
                    result["order_number"] = info["order_number"]
                    result["calculation"]["order_number"] = info["order_number"]
                result["steps"].append({
                    "step": "лист заказа",
                    "ok": True,
                    "detail": f"адрес {args.address}, скидка {args.discount}%, монтаж {args.montage}, демонтаж {args.demontage}"
                })
            except Exception as e:
                result["steps"].append({
                    "step": "лист заказа",
                    "ok": False,
                    "detail": str(e)
                })

        if args.save and not args.dry_run and result.get("success"):
            try:
                number = bot.save_order()
                result["order_number"] = number
                result.setdefault("calculation", {})["order_number"] = number
                calc = result.get("calculation", {}) or {}
                bot.register_order(
                    number,
                    total=result.get("total_amount") or 0,
                    address=args.address,
                    net=calc.get("mosquito_net"),
                    flashing=calc.get("flashing"),
                    windowsill=calc.get("windowsill"))
                result["steps"].append({
                    "step": "сохранение",
                    "ok": bool(number),
                    "detail": f"заказ сохранён, номер: {number or 'не прочитан'}"
                })
            except Exception as e:
                result["steps"].append({"step": "сохранение", "ok": False, "detail": str(e)})

        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    result = bot.run_calculation(order)
    print(json.dumps(result, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
