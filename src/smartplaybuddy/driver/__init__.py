from .keyboarddrv import KeyboardDrv
from .mousedrv import MouseDrv
from .screendrv import ScreenDrv
from . import xbox

keyboard = KeyboardDrv()
def keyboard_operate(operate: dict):
    if operate["operate"] == "tap":
        keyboard.tap(operate["key"])
    elif operate["operate"] == "combo":
        keyboard.combo(operate["keys"])
    elif operate["operate"] == "press":
        keyboard.press(operate["key"])
    elif operate["operate"] == "release":
        keyboard.release(operate["key"])

mouse = MouseDrv()
def mouse_operate(operate: dict):
    if operate["operate"] == "move":
        mouse.move(float(operate["x"]), float(operate["y"]), duration=float(operate["duration"]))
    elif operate["operate"] == "move_to":
        mouse.move_to(float(operate["x"]), float(operate["y"]), duration=float(operate["duration"]))
    elif operate["operate"] == "tap":
        mouse.tap(operate["button"])
    elif operate["operate"] == "press":
        mouse.press(operate["button"])
    elif operate["operate"] == "release":
        mouse.release(operate["button"])
    elif operate["operate"] == "scroll":
        mouse.scroll(float(operate["y"]))

screen = ScreenDrv()
def screen_operate(operate: dict):
    op = operate["operate"]
    if op == "screenshot":
        return screen.screenshot(
            monitor=operate.get("monitor"),
            window=operate.get("window"),
            hwnd=operate.get("hwnd"),
            save=operate.get("save"),
            return_image=operate.get("return_image", True),
        )
    elif op == "capture_start":
        return screen.start_capture(
            name=operate.get("name", "default"),
            monitor=operate.get("monitor"),
            window=operate.get("window"),
            hwnd=operate.get("hwnd"),
            minimum_update_interval=operate.get("minimum_update_interval"),
            cursor_capture=operate.get("cursor_capture", True),
        )
    elif op == "capture_stop":
        return screen.stop_capture(name=operate.get("name", "default"))
    elif op == "capture_frame":
        return screen.latest_frame(
            name=operate.get("name", "default"),
            save=operate.get("save"),
            return_image=operate.get("return_image", True),
        )
    elif op == "capture_list":
        return screen.list_captures()
    return {"status": "error", "message": f"unknown operate: {op}"}

drivers = {
    "keyboard": keyboard_operate,
    "mouse": mouse_operate,
    "screen": screen_operate,
}
