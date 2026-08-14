import json
import os
import time
import base64
import threading
import logging
import importlib.resources
from collections import deque
from typing import Any

import pyautogui
import webview

from .. import log as logmod
from ..config import WS_URL
from ..drivers import drivers

logger = logmod.logger.getChild("ui").getChild("debugger")

LOG_MAX = 500
SCREENSHOT_DIR = "screenshots"

# 各驱动安全自检方案：不干扰用户正常操作
SELF_TEST_PLANS = {
    "keyboard": {"operate": "tap", "key": "f13"},
    "mouse": {"operate": "move_to", "x": 0.5, "y": 0.5, "duration": 0.3},
    "screen": {"operate": "capture", "device_idx": 0, "output_idx": 0, "scale": 0.25, "fmt": "png"},
}


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

    def __init__(self, handler: "LogBufferHandler | None" = None, push_cb=None):
        self.handler = handler
        self.push_cb = push_cb
        self.ready = False
        self.window: Any = None

    def choose_save_dir(self) -> dict:
        """弹出系统文件夹选择对话框，返回用户选择的目录。"""
        try:
            import webview
            if self.window is None:
                return {"status": "error", "message": "窗口未就绪"}
            result = self.window.create_file_dialog(
                webview.FOLDER_DIALOG,
                directory=os.path.abspath(SCREENSHOT_DIR),
            )
            if not result:
                return {"status": "ok", "path": None, "message": "已取消"}
            return {"status": "ok", "path": result[0]}
        except Exception as e:
            return {"status": "error", "message": str(e)}

    def on_ready(self):
        self.ready = True
        self._push({"type": "on_ready"})
        # 后台预热驱动注册表（首次扫描可能触发依赖安装，避免卡住 GUI 线程）
        threading.Thread(target=self._warmup, daemon=True).start()
        return True

    def _warmup(self):
        try:
            list(drivers.keys())
            self._push({"type": "meta_ready"})
        except Exception as e:
            logger.warning(f"driver scan failed: {e}")
            self._push({"type": "meta_ready", "error": str(e)})

    def get_meta(self):
        try:
            dlist = sorted(drivers.keys())
        except Exception as e:
            logger.warning(f"driver scan failed: {e}")
            dlist = []
        return {
            "ws_url": WS_URL,
            "drivers": dlist,
            "screen_size": list(pyautogui.size()),
            "save_dir": os.path.abspath(SCREENSHOT_DIR),
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
            if isinstance(result, dict) and "__data__" in result and isinstance(result["__data__"], bytes):
                result["__data__"] = base64.b64encode(result["__data__"]).decode("ascii")
            return {"status": "ok", "action": action, "result": result}
        except Exception as e:
            logger.exception("run_command failed")
            return {"status": "error", "message": str(e)}

    def screenshot(self, monitor, save) -> dict:
        """截图并返回 base64 预览数据（走新架构 screen 驱动）。"""
        try:
            output_idx = int(monitor) if str(monitor or "").isdigit() else 0
            result = drivers["screen"]({
                "operate": "capture",
                "device_idx": 0,
                "output_idx": output_idx,
                "scale": 0.5,
                "fmt": "png",
            })
            if not isinstance(result, dict) or result.get("status") == "error":
                msg = result.get("message", "截图失败") if isinstance(result, dict) else "截图失败"
                return {"status": "error", "message": msg}
            data = result.get("__data__")
            if not data:
                return {"status": "error", "message": "截图结果为空"}
            filename = f"screenshot_{int(time.time() * 1000)}.png"
            if save:
                ext = os.path.splitext(save)[1].lower()
                if ext in (".png", ".jpg", ".jpeg", ".webp", ".bmp"):
                    parent = os.path.dirname(save)
                    if parent:
                        os.makedirs(parent, exist_ok=True)
                    path = save
                else:
                    os.makedirs(save, exist_ok=True)
                    path = os.path.join(save, filename)
            else:
                os.makedirs(SCREENSHOT_DIR, exist_ok=True)
                path = os.path.join(SCREENSHOT_DIR, filename)
            with open(path, "wb") as f:
                f.write(data)
            b64 = base64.b64encode(data).decode("ascii")
            meta = result.get("result") if isinstance(result.get("result"), dict) else {}
            return {
                "status": "ok",
                "data": b64,
                "width": meta.get("width"),
                "height": meta.get("height"),
                "file": os.path.basename(path),
                "path": path,
            }
        except Exception as e:
            logger.exception("screenshot failed")
            return {"status": "error", "message": str(e)}

    def list_screenshots(self) -> dict:
        if not os.path.isdir(SCREENSHOT_DIR):
            return {"status": "ok", "files": []}
        files = []
        for f in sorted(os.listdir(SCREENSHOT_DIR), reverse=True):
            p = os.path.join(SCREENSHOT_DIR, f)
            if os.path.isfile(p):
                files.append({
                    "name": f,
                    "size": os.path.getsize(p),
                    "mtime": os.path.getmtime(p),
                })
        return {"status": "ok", "dir": os.path.abspath(SCREENSHOT_DIR), "files": files}

    def delete_screenshot(self, name: str) -> dict:
        p = os.path.join(SCREENSHOT_DIR, name)
        try:
            os.remove(p)
            logger.info(f"screenshot deleted: {name}")
            return {"status": "ok"}
        except Exception as e:
            return {"status": "error", "message": str(e)}

    def open_screenshot(self, name: str) -> dict:
        p = os.path.join(SCREENSHOT_DIR, name)
        if not os.path.isfile(p):
            return {"status": "error", "message": f"文件不存在: {name}"}
        try:
            os.startfile(p)
            return {"status": "ok"}
        except Exception as e:
            return {"status": "error", "message": str(e)}

    def open_save_dir(self) -> dict:
        try:
            os.makedirs(SCREENSHOT_DIR, exist_ok=True)
            os.startfile(os.path.abspath(SCREENSHOT_DIR))
            return {"status": "ok"}
        except Exception as e:
            return {"status": "error", "message": str(e)}

    # ===== 驱动自检 =====

    def start_self_test(self) -> dict:
        """后台线程执行全部驱动自检，结果通过 push_cb 逐项推送。"""
        threading.Thread(target=self._self_test_worker, args=(list(drivers.keys()),), daemon=True).start()
        return {"status": "started"}

    def start_self_test_one(self, action: str) -> dict:
        threading.Thread(target=self._self_test_worker, args=([action],), daemon=True).start()
        return {"status": "started"}

    def _self_test_worker(self, actions):
        try:
            logger.info(f"驱动自检开始: {', '.join(sorted(actions))}")
            for a in sorted(actions):
                item = self._run_self_test(a)
                level = logger.error if item.get("status") == "error" else logger.info
                level(f"驱动自检 [{item.get('action')}] {item.get('status')} ({item.get('elapsed_ms')}ms) - {item.get('detail')}")
                self._push({"type": "self_test_item", "item": item})
            self._push({"type": "self_test_done"})
            logger.info("驱动自检完成")
        except Exception as e:
            logger.exception("self_test failed")
            self._push({"type": "self_test_done", "error": str(e)})

    def _run_self_test(self, action: str) -> dict:
        started = time.time()
        try:
            if action not in drivers:
                return {"action": action, "status": "error", "elapsed_ms": 0, "detail": f"未知 action: {action}"}
            plan = SELF_TEST_PLANS.get(action)
            if plan is None:
                return {"action": action, "status": "error", "elapsed_ms": 0, "detail": f"没有为 {action} 定义自检方案"}
            result = drivers[action](plan)
            elapsed_ms = int((time.time() - started) * 1000)
            if isinstance(result, dict) and result.get("status") == "error":
                return {"action": action, "status": "error", "elapsed_ms": elapsed_ms,
                        "detail": result.get("message", "驱动返回错误")}
            if action == "screen":
                meta = result.get("result") if isinstance(result.get("result"), dict) else result
                if not (isinstance(meta, dict) and meta.get("width") and meta.get("height")):
                    return {"action": action, "status": "error", "elapsed_ms": elapsed_ms,
                            "detail": f"截图结果异常: {result}"}
                return {"action": action, "status": "ok", "elapsed_ms": elapsed_ms,
                        "detail": f"抓帧成功 {meta['width']}×{meta['height']}"}
            return {"action": action, "status": "ok", "elapsed_ms": elapsed_ms, "detail": "驱动响应正常"}
        except Exception as e:
            return {"action": action, "status": "error",
                    "elapsed_ms": int((time.time() - started) * 1000), "detail": str(e)}

    def _push(self, payload: dict):
        if self.push_cb:
            self.push_cb(payload)


def _load_html() -> str:
    pkg = importlib.resources.files("smartplaybuddy.ui")
    return pkg.joinpath("debugger.html").read_text(encoding="utf-8")


class DebuggerApp:
    def __init__(self):
        self.api = DebuggerApi()
        self.window = None

    def run(self):
        pending = []

        def _emit(entry):
            if self.window is None:
                return
            js = json.dumps(entry, ensure_ascii=False)
            if "type" in entry:
                self.window.evaluate_js(f"window.__onTestEvent({js})")
            else:
                self.window.evaluate_js(f"window.__pushLog({js})")

        def _push(entry):
            if entry.get("type") == "on_ready":
                for e in pending:
                    _emit(e)
                pending.clear()
                return
            if self.window is not None and self.api.ready:
                try:
                    _emit(entry)
                except Exception:
                    pass
            else:
                pending.append(entry)
                if len(pending) > 500:
                    pending.pop(0)

        handler = LogBufferHandler(push_cb=_push)
        handler.setLevel(logging.DEBUG)
        logmod.logger.addHandler(handler)
        self.api = DebuggerApi(handler, push_cb=_push)

        self.window = webview.create_window(
            "SmartPlayBuddy 调试面板",
            html=_load_html(),
            js_api=self.api,
            width=1280,
            height=800,
            min_size=(960, 640),
        )
        self.api.window = self.window
        webview.start(debug=False)


def main():
    DebuggerApp().run()
