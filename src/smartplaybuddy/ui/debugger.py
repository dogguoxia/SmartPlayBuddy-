import json
import os
import threading
import time
import logging
import importlib.resources
from collections import deque

import webview

from .. import log as logmod
from ..config import WS_URL
from ..driver import drivers, screen, mouse

logger = logmod.logger.getChild("ui").getChild("debugger")

LOG_MAX = 500


class LogBufferHandler(logging.Handler):
    """缓存最近日志并推送到调试面板。"""

    def __init__(self, push_cb=None):
        super().__init__()
        self.buffer = deque(maxlen=LOG_MAX)
        self.push_cb = push_cb
        self._lock = threading.Lock()

    def emit(self, record):
        try:
            entry = {
                "time": time.strftime("%H:%M:%S", time.localtime(record.created)),
                "level": record.levelname,
                "name": record.name,
                "message": record.getMessage(),
            }
            with self._lock:
                self.buffer.append(entry)
            if self.push_cb:
                self.push_cb(entry)
        except Exception:
            pass

    def snapshot(self):
        with self._lock:
            return list(self.buffer)


class DebuggerApi:
    """暴露给前端（pywebview JS）的调试 API。"""

    def __init__(self, handler: "LogBufferHandler | None" = None):
        self.handler = handler
        self.ready = False

    def on_ready(self):
        self.ready = True
        return True

    def get_meta(self):
        return {
            "ws_url": WS_URL,
            "drivers": list(drivers.keys()),
            "screen_size": [mouse.width, mouse.height],
            "save_dir": screen.save_dir,
        }

    def get_logs(self):
        if self.handler is None:
            return []
        return self.handler.snapshot()

    def run_command(self, action: str, data: str) -> dict:
        """执行一条驱动指令，data 为 JSON 字符串。"""
        try:
            operate = json.loads(data) if data and data.strip() else {}
            if not isinstance(operate, dict):
                return {"status": "error", "message": "data 必须是 JSON 对象"}
            if action not in drivers:
                return {"status": "error", "message": f"未知 action: {action}"}
            result = drivers[action](operate)
            return {"status": "ok", "action": action, "result": result}
        except Exception as e:
            logger.exception("run_command failed")
            return {"status": "error", "message": str(e)}

    def screenshot(self, monitor, window, save) -> dict:
        """截图并返回 base64 预览数据。"""
        try:
            result = screen.screenshot(
                monitor=monitor if monitor not in (None, "") else None,
                window=window if window else None,
                save=save if save else None,
                return_image=True,
            )
            return result
        except Exception as e:
            logger.exception("screenshot failed")
            return {"status": "error", "message": str(e)}

    def list_screenshots(self) -> dict:
        d = screen.save_dir
        if not os.path.isdir(d):
            return {"status": "ok", "files": []}
        files = []
        for f in sorted(os.listdir(d), reverse=True):
            p = os.path.join(d, f)
            if os.path.isfile(p):
                files.append({
                    "name": f,
                    "size": os.path.getsize(p),
                    "mtime": os.path.getmtime(p),
                })
        return {"status": "ok", "dir": d, "files": files}

    def delete_screenshot(self, name: str) -> dict:
        p = os.path.join(screen.save_dir, name)
        try:
            os.remove(p)
            logger.info(f"screenshot deleted: {name}")
            return {"status": "ok"}
        except Exception as e:
            return {"status": "error", "message": str(e)}

    def open_screenshot(self, name: str) -> dict:
        p = os.path.join(screen.save_dir, name)
        if not os.path.isfile(p):
            return {"status": "error", "message": f"文件不存在: {name}"}
        try:
            os.startfile(p)
            return {"status": "ok"}
        except Exception as e:
            return {"status": "error", "message": str(e)}

    def open_save_dir(self) -> dict:
        try:
            os.makedirs(screen.save_dir, exist_ok=True)
            os.startfile(screen.save_dir)
            return {"status": "ok"}
        except Exception as e:
            return {"status": "error", "message": str(e)}


def _load_html() -> str:
    pkg = importlib.resources.files("smartplaybuddy.ui")
    return pkg.joinpath("debugger.html").read_text(encoding="utf-8")


class DebuggerApp:
    def __init__(self):
        self.api = DebuggerApi()
        self.window = None

    def run(self):
        def _push(entry):
            if self.window is not None and self.api.ready:
                try:
                    self.window.evaluate_js(
                        f"window.__pushLog({json.dumps(entry, ensure_ascii=False)})"
                    )
                except Exception:
                    pass

        handler = LogBufferHandler(push_cb=_push)
        handler.setLevel(logging.DEBUG)
        logmod.logger.addHandler(handler)
        self.api = DebuggerApi(handler)

        self.window = webview.create_window(
            "SmartPlayBuddy 调试面板",
            html=_load_html(),
            js_api=self.api,
            width=1280,
            height=800,
            min_size=(960, 640),
        )
        webview.start(debug=False)


def main():
    DebuggerApp().run()
