"""
Action Recorder for CCalc 9.12 (overlay edition).

Shows a compact always-on-top window with the list of recorded clicks.
Select any row to edit/attach a comment; F9 comments the last click;
F10 stops. Clicks on the overlay itself are NOT recorded.

Run:
    python tools/pvc_bridge/recorder_overlay.py
"""

import os
import sys
import time
import json
import ctypes
import threading
from ctypes import wintypes

user32 = ctypes.windll.user32

try:
    import win32gui
    import win32con
    import win32api
except Exception as e:
    print("pywin32 required:", e)
    sys.exit(1)

import tkinter as tk
from tkinter import ttk

VK_LBUTTON = 0x01
VK_F9 = 0x78
VK_F10 = 0x79

OUT_FILE = os.path.join(os.path.dirname(__file__), "recorded_trace.json")

# ---- palette (near-black) ----
BG = "#060708"
PANEL = "#0d0e11"
PANEL_2 = "#090a0c"
LINE = "#1b1d22"
TEXT = "#e8eaee"
MUTED = "#767c88"
ACCENT = "#4f8cff"
ACCENT_HOVER = "#6ea2ff"
DANGER = "#ff6b6b"
DANGER_BG = "#2a0f12"
OK = "#3ddc97"
ROW_A = "#0b0c0f"
ROW_B = "#0f1114"

# ---- shared state ----
actions = []
actions_lock = threading.Lock()
annotate_requested = threading.Event()
revision = 0
running = True
overlay_alive = True
overlay_hwnd = None
ccalc_hwnd = None
start_time = 0.0
ui_busy = False


def find_ccalc_hwnd():
    found = []

    def cb(h, _):
        if win32gui.IsWindowVisible(h) and win32gui.GetClassName(h) == "TForm1":
            found.append(h)
        return True

    try:
        win32gui.EnumWindows(cb, None)
    except Exception:
        pass
    return found[0] if found else None


def overlay_rect():
    if overlay_hwnd and win32gui.IsWindow(overlay_hwnd):
        return win32gui.GetWindowRect(overlay_hwnd)
    return None


def in_overlay(pt):
    r = overlay_rect()
    if not r:
        return False
    return r[0] <= pt.x <= r[2] and r[1] <= pt.y <= r[3]


def record_loop():
    global running, start_time, revision
    prev_lbutton = False
    prev_f9 = False
    prev_f10 = False
    start_time = time.time()

    while running:
        f10 = user32.GetAsyncKeyState(VK_F10) & 0x8000
        if f10 and not prev_f10:
            prev_f10 = True
            running = False
            break
        prev_f10 = bool(f10)

        f9 = bool(user32.GetAsyncKeyState(VK_F9) & 0x8000)
        if f9 and not prev_f9 and overlay_alive and not ui_busy:
            annotate_requested.set()
        prev_f9 = f9

        lbutton = bool(user32.GetAsyncKeyState(VK_LBUTTON) & 0x8000)
        if lbutton and not prev_lbutton:
            pt = wintypes.POINT()
            user32.GetCursorPos(ctypes.byref(pt))

            fg_hwnd = user32.GetForegroundWindow()
            try:
                fg_title = win32gui.GetWindowText(fg_hwnd)
                fg_cls = win32gui.GetClassName(fg_hwnd)
            except Exception:
                fg_title, fg_cls = "", ""

            if not (fg_hwnd == overlay_hwnd or in_overlay(pt)
                    or fg_title in ("Подпись клика", "Комментарий")):
                try:
                    wrect = win32gui.GetWindowRect(fg_hwnd)
                except Exception:
                    prev_lbutton = lbutton
                    continue
                rel_x = pt.x - wrect[0]
                rel_y = pt.y - wrect[1]

                child_h = user32.WindowFromPoint(pt)
                try:
                    ch_cls = win32gui.GetClassName(child_h) if child_h else ""
                    ch_txt = (win32gui.GetWindowText(child_h) if child_h else "")[:30]
                except Exception:
                    ch_cls, ch_txt = "", ""

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
                    "control_text": ch_txt,
                    "label": None,
                }
                with actions_lock:
                    actions.append(evt)
                    revision += 1
                print(f"[{evt['time_offset_s']}s] Клик: X={rel_x}, Y={rel_y} | "
                      f"Окно: '{fg_title[:20]}' | Элемент: {ch_cls}")

        prev_lbutton = lbutton
        time.sleep(0.02)

    with actions_lock:
        data = list(actions)
    try:
        with open(OUT_FILE, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
    except Exception as e:
        print(f"\n[!] Не удалось сохранить трассу: {e}")
    else:
        print(f"\n[УСПЕХ] Записано {len(data)} действий!")
        print(f"Файл сохранён: {OUT_FILE}")


# ---- Tk overlay ----
def build_overlay():
    global overlay_hwnd, overlay_alive, revision, ui_busy

    root = tk.Tk()
    root.title("Recorder")
    root.attributes("-topmost", True)
    root.geometry("640x600+980+40")
    root.minsize(540, 420)
    root.configure(bg=BG)

    style = ttk.Style()
    try:
        style.theme_use("clam")
    except Exception:
        pass
    style.configure("Rec.Treeview",
                    background=PANEL_2, foreground=TEXT,
                    fieldbackground=PANEL_2, borderwidth=0,
                    font=("Consolas", 9), rowheight=26)
    style.configure("Rec.Treeview.Heading",
                    background=PANEL, foreground="#8b93a1",
                    font=("Segoe UI", 8, "bold"), borderwidth=0, relief="flat",
                    padding=(0, 4))
    style.map("Rec.Treeview.Heading",
              background=[("active", PANEL)],
              foreground=[("active", TEXT)])
    style.map("Rec.Treeview",
              background=[("selected", "#1b2b45")],
              foreground=[("selected", "#ffffff")])
    style.layout("Rec.Treeview", [("Treeview.treearea", {"sticky": "nswe"})])

    # slim dark scrollbar (arrows blended into the trough)
    for sb in ("Vertical.TScrollbar", "Rec.Vertical.TScrollbar"):
        style.configure(sb, gripcount=0, width=10, arrowsize=6,
                        background="#2b2f37", darkcolor="#2b2f37",
                        lightcolor="#2b2f37", troughcolor="#0a0b0d",
                        bordercolor="#0a0b0d", arrowcolor="#0a0b0d",
                        relief="flat", borderwidth=0)
        style.map(sb, background=[("active", "#3d424c"), ("pressed", "#4a4f5a")])

    drag = {"x": 0, "y": 0}

    def start_drag(e):
        drag["x"] = e.x
        drag["y"] = e.y

    def do_drag(e):
        root.geometry(f"+{root.winfo_x() + e.x - drag['x']}+{root.winfo_y() + e.y - drag['y']}")

    # ---------- header ----------
    header = tk.Frame(root, bg=BG, height=52, cursor="fleur")
    header.pack(fill="x")
    header.bind("<Button-1>", start_drag)
    header.bind("<B1-Motion>", do_drag)

    tk.Frame(header, bg=ACCENT, width=3, height=52).pack(side="left")

    title_box = tk.Frame(header, bg=BG)
    title_box.pack(side="left", padx=12, pady=8)
    title_box.bind("<Button-1>", start_drag)
    title_box.bind("<B1-Motion>", do_drag)
    tk.Label(title_box, text="CCalc Recorder", bg=BG, fg=TEXT,
             font=("Segoe UI Semibold", 12)).pack(anchor="w")
    tk.Label(title_box, text="9.12 Dealer · запись действий", bg=BG, fg=MUTED,
             font=("Segoe UI", 8)).pack(anchor="w")

    dot = tk.Label(header, text="●", bg=BG, fg=OK, font=("Segoe UI", 14))
    dot.pack(side="right", padx=(0, 10))
    tk.Label(header, text="F10 — стоп", bg=BG, fg=MUTED,
             font=("Segoe UI", 8)).pack(side="right", padx=(0, 6))

    tk.Frame(root, bg=LINE, height=1).pack(fill="x")

    # ---------- stat chips ----------
    chips = tk.Frame(root, bg=BG)
    chips.pack(fill="x", padx=12, pady=(0, 8))

    clicks_var = tk.StringVar(value="0")
    commented_var = tk.StringVar(value="0")
    elapsed_var = tk.StringVar(value="00:00")

    def chip(parent, caption, var, color):
        box = tk.Frame(parent, bg=PANEL, highlightthickness=1,
                       highlightbackground=LINE, highlightcolor=LINE)
        box.pack(side="left", padx=(0, 8))
        tk.Label(box, text=caption, bg=PANEL, fg=MUTED,
                 font=("Segoe UI", 7, "bold")).pack(anchor="w", padx=12, pady=(7, 0))
        tk.Label(box, textvariable=var, bg=PANEL, fg=color,
                 font=("Consolas", 14, "bold")).pack(anchor="w", padx=12, pady=(0, 7))

    chip(chips, "КЛИКОВ", clicks_var, TEXT)
    chip(chips, "С КОММЕНТАРИЕМ", commented_var, OK)
    chip(chips, "ВРЕМЯ", elapsed_var, ACCENT)

    # ---------- table ----------
    tree_frame = tk.Frame(root, bg=PANEL_2, highlightthickness=1,
                          highlightbackground=LINE, highlightcolor=LINE)
    tree_frame.pack(fill="both", expand=True, padx=12, pady=(0, 8))

    cols = ("#", "Время", "X", "Y", "Элемент", "Комментарий")
    tree = ttk.Treeview(tree_frame, columns=cols, show="headings",
                        style="Rec.Treeview", selectmode="extended")
    for c, w, anchor in (("#", 44, "center"), ("Время", 62, "center"),
                         ("X", 54, "center"), ("Y", 54, "center"),
                         ("Элемент", 140, "w"), ("Комментарий", 220, "w")):
        tree.heading(c, text=c)
        tree.column(c, width=w, anchor=anchor, stretch=(c == "Комментарий"))
    vsb = ttk.Scrollbar(tree_frame, orient="vertical", command=tree.yview,
                        style="Rec.Vertical.TScrollbar")
    tree.configure(yscrollcommand=vsb.set)
    tree.pack(side="left", fill="both", expand=True)
    vsb.pack(side="right", fill="y")

    tree.tag_configure("odd", background=ROW_A)
    tree.tag_configure("even", background=ROW_B)

    # ---------- inline comment editor ----------
    editor = tk.Frame(root, bg=PANEL, highlightthickness=1,
                      highlightbackground=LINE)
    edit_state = {"idx": None}

    edit_title = tk.Label(editor, text="Комментарий", bg=PANEL, fg=MUTED,
                          font=("Segoe UI", 8))
    edit_title.pack(anchor="w", padx=10, pady=(8, 2))

    row = tk.Frame(editor, bg=PANEL)
    row.pack(fill="x", padx=10, pady=(0, 8))
    entry_var = tk.StringVar()
    entry = tk.Entry(row, textvariable=entry_var, bg=PANEL_2, fg=TEXT,
                     insertbackground=TEXT, relief="flat", bd=0,
                     font=("Segoe UI", 10), highlightthickness=1,
                     highlightbackground=LINE, highlightcolor=ACCENT)
    entry.pack(side="left", fill="x", expand=True, ipady=6)

    def save_edit(_evt=None):
        global revision
        idx = edit_state["idx"]
        if idx is None:
            return
        text = entry_var.get().strip()
        with actions_lock:
            if 0 <= idx < len(actions):
                actions[idx]["label"] = text or None
                revision += 1
        hide_editor()
        refresh_list(force=True)
        return "break"

    def hide_editor():
        global ui_busy
        edit_state["idx"] = None
        editor.pack_forget()
        ui_busy = False

    def cancel_edit(_evt=None):
        hide_editor()
        return "break"

    def start_edit(idx):
        global ui_busy
        with actions_lock:
            if not (0 <= idx < len(actions)):
                return
            cur = actions[idx].get("label") or ""
            xy = (actions[idx].get("rel_x"), actions[idx].get("rel_y"))
        edit_state["idx"] = idx
        ui_busy = True
        editor.pack(fill="x", padx=12, pady=(0, 6), before=footer)
        edit_title.config(text=f"Комментарий к клику #{idx + 1}  (X={xy[0]}, Y={xy[1]})")
        entry_var.set(cur)
        entry.focus_set()
        entry.selection_range(0, "end")

    tk.Button(row, text="Сохранить", command=save_edit, bg=ACCENT, fg="#ffffff",
              activebackground=ACCENT_HOVER, activeforeground="#ffffff",
              bd=0, padx=14, pady=6, font=("Segoe UI Semibold", 9),
              cursor="hand2").pack(side="left", padx=(8, 0))
    tk.Button(row, text="Отмена", command=cancel_edit, bg=PANEL_2, fg=MUTED,
              activebackground=LINE, activeforeground=TEXT,
              bd=0, padx=12, pady=6, font=("Segoe UI", 9),
              cursor="hand2").pack(side="left", padx=(6, 0))

    entry.bind("<Return>", save_edit)
    entry.bind("<KP_Enter>", save_edit)
    entry.bind("<Escape>", cancel_edit)

    # ---------- footer ----------
    footer = tk.Frame(root, bg=BG)
    footer.pack(fill="x", padx=12, pady=(0, 10))

    HINT_TEXT = "Enter / двойной клик — комментарий   ·   Del — удалить"
    hint = tk.Label(footer, text=HINT_TEXT,
                    bg=BG, fg=MUTED, font=("Segoe UI", 8))
    hint.pack(anchor="w", pady=(0, 6))

    btns = tk.Frame(footer, bg=BG)
    btns.pack(fill="x")

    def make_btn(parent, text, cmd, bg, fg, hover, border=LINE):
        b = tk.Button(parent, text=text, command=cmd, bg=bg, fg=fg,
                      activebackground=hover, activeforeground=fg,
                      bd=0, padx=12, pady=10, font=("Segoe UI Semibold", 9),
                      cursor="hand2", relief="flat", highlightthickness=1,
                      highlightbackground=border, highlightcolor=border)
        b.bind("<Enter>", lambda _: b.config(bg=hover))
        b.bind("<Leave>", lambda _: b.config(bg=bg))
        return b

    def stop_rec():
        global running
        running = False

    btn_last = make_btn(btns, "Последний (F9)", lambda: annotate_last(), PANEL, TEXT, "#1c2947")
    btn_last.pack(side="left", padx=(0, 8), fill="x", expand=True)
    btn_edit = make_btn(btns, "Комментировать (Enter)", lambda: edit_selected(), ACCENT, "#ffffff", ACCENT_HOVER)
    btn_edit.pack(side="left", padx=(0, 8), fill="x", expand=True)
    btn_del = make_btn(btns, "Удалить (Del)", lambda: delete_selected(), PANEL, TEXT, "#1c2947")
    btn_del.pack(side="left", padx=(0, 8), fill="x", expand=True)
    btn_stop = make_btn(btns, "Стоп (F10)", stop_rec, DANGER_BG, DANGER, "#3d161b", border="#4a1a20")
    btn_stop.pack(side="left", fill="x", expand=True)

    # ---------- logic ----------
    def selected_index():
        sel = tree.selection()
        if not sel:
            return None
        try:
            return int(sel[0])
        except ValueError:
            return None

    last_revision = -1
    last_count = -1
    sched = {"on": False}

    def schedule_refresh(delay=250):
        """Единственная цепочка таймеров обновления (без дублей)."""
        if not overlay_alive or sched["on"]:
            return
        sched["on"] = True

        def tick():
            sched["on"] = False
            refresh_list()

        root.after(delay, tick)

    def refresh_list(force=False):
        nonlocal last_revision, last_count
        with actions_lock:
            rev = revision
            snapshot = list(enumerate(actions))
        if not force and rev == last_revision and len(snapshot) == last_count:
            schedule_refresh()
            return
        last_revision = rev
        last_count = len(snapshot)

        prev_sel = tree.selection()
        prev_iid = prev_sel[0] if prev_sel else None
        at_bottom = True
        try:
            at_bottom = tree.yview()[1] > 0.985
        except Exception:
            pass

        tree.delete(*tree.get_children())
        commented = 0
        for i, e in snapshot:
            label = e.get("label") or ""
            if label:
                commented += 1
            tree.insert("", "end", iid=str(i),
                        tags=("even" if i % 2 else "odd",),
                        values=(f"{i + 1:02d}",
                                f"{e.get('time_offset_s', 0):.1f}",
                                e.get("rel_x", 0), e.get("rel_y", 0),
                                (e.get("control_class") or "")[:20], label))
        clicks_var.set(str(len(snapshot)))
        commented_var.set(str(commented))
        try:
            elapsed = int(time.time() - start_time)
            elapsed_var.set(f"{elapsed // 60:02d}:{elapsed % 60:02d}")
        except Exception:
            pass

        if prev_iid and tree.exists(prev_iid):
            tree.selection_set(prev_iid)
        elif at_bottom:
            children = tree.get_children()
            if children:
                tree.see(children[-1])

        schedule_refresh()

    def edit_selected():
        idx = selected_index()
        if idx is None:
            hint.config(text="Сначала выберите строку в таблице", fg=DANGER)
            root.after(2500, lambda: hint.config(text=HINT_TEXT, fg=MUTED))
            return
        start_edit(idx)

    def delete_selected():
        sel = tree.selection()
        if not sel:
            hint.config(text="Выберите строки для удаления", fg=DANGER)
            root.after(2500, lambda: hint.config(text=HINT_TEXT, fg=MUTED))
            return
        try:
            idxs = sorted({int(s) for s in sel}, reverse=True)
        except ValueError:
            return
        with actions_lock:
            for i in idxs:
                if 0 <= i < len(actions):
                    del actions[i]
            revision += 1
        if edit_state["idx"] is not None:
            edit_state["idx"] = None
            editor.pack_forget()
        refresh_list(force=True)

    def annotate_last():
        with actions_lock:
            if not actions:
                hint.config(text="Пока нет записанных кликов", fg=DANGER)
                root.after(2500, lambda: hint.config(text=HINT_TEXT, fg=MUTED))
                return
            idx = len(actions) - 1
        try:
            tree.selection_set(str(idx))
            tree.see(str(idx))
        except Exception:
            pass
        start_edit(idx)

    tree.bind("<Double-Button-1>", lambda e: edit_selected())
    tree.bind("<Return>", lambda e: edit_selected())
    tree.bind("<Delete>", lambda e: delete_selected())
    root.bind("<Delete>", lambda e: delete_selected())
    root.bind("<F9>", lambda e: annotate_last())

    def check_annotate():
        if not overlay_alive:
            return
        if annotate_requested.is_set():
            annotate_requested.clear()
            annotate_last()
        root.after(80, check_annotate)

    def on_close():
        global overlay_alive
        overlay_alive = False
        try:
            root.withdraw()
        except Exception:
            pass

    def check_running():
        if not running:
            try:
                root.quit()
            except Exception:
                pass
            return
        root.after(200, check_running)

    def blink():
        if not overlay_alive:
            return
        dot.config(fg=MUTED if dot.cget("fg") == OK else OK)
        root.after(700, blink)

    root.protocol("WM_DELETE_WINDOW", on_close)
    root.after(200, check_running)

    root.update_idletasks()
    overlay_hwnd = root.winfo_id()

    refresh_list(force=True)
    check_annotate()
    blink()
    root.mainloop()


def main():
    global ccalc_hwnd, running

    print("=" * 60)
    print(" РЕКОРДЕР С ОВЕРЛЕЕМ (CCALC 9.12) ДЛЯ A.R.C. FRAME")
    print("=" * 60)

    ccalc_hwnd = find_ccalc_hwnd()
    if not ccalc_hwnd:
        print("\n[!] Окно CCalc не найдено! Сначала откройте CCalc 9.12.")
        input("\nНажмите Enter для выхода...")
        return
    txt = win32gui.GetWindowText(ccalc_hwnd)
    print(f"\n[OK] Найдено окно CCalc: '{txt}' (HWND: {ccalc_hwnd})")
    print("\nИнструкция:")
    print("1. Переключитесь в окно CCalc.")
    print("2. Постройте окно 1060x1340 от начала до конца.")
    print("3. F9 — комментарий к последнему клику, F10 — стоп.")
    print("4. Двойной клик по строке — комментарий к любому клику.")
    print("\nЗапись начнётся через 3 секунды...")
    time.sleep(3)
    print("\n>>> ИДЁТ ЗАПИСЬ (F9=комментарий, F10=стоп) <<<")

    rec_thread = threading.Thread(target=record_loop, daemon=True)
    rec_thread.start()

    try:
        build_overlay()
    except Exception as e:
        print("Overlay error:", e)
        running = False

    rec_thread.join(timeout=5)
    print("\nГотово.")


if __name__ == "__main__":
    main()
