import sys, time
sys.path.insert(0, "tools/pvc_bridge")
import recorder_overlay as ro
ro.ccalc_hwnd = 394244
ro.running = True
import threading
def runner():
    ro.build_overlay()
t = threading.Thread(target=runner, daemon=True)
t.start()
time.sleep(4)
ro.running = False
time.sleep(1)
print("overlay_hwnd =", ro.overlay_hwnd)
print("OK")