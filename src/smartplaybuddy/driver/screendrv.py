import os
import time
import base64
import threading

import cv2

from .. import log

from windows_capture import WindowsCapture, DxgiDuplicationSession

logger = log.logger.getChild("driver").getChild("screen")


def _png_b64(frame):
    """将 BGR 帧编码为 PNG 的 base64 字符串，便于经 WebSocket 消息回传。"""
    _, buf = cv2.imencode(".png", frame)
    return base64.b64encode(buf.tobytes()).decode("utf-8")


def _normalize_monitor(monitor):
    """windows-capture 的显示器索引为 1-based，0 视为主显示器。"""
    if monitor is None or monitor == 0:
        return None
    return monitor


class ScreenDrv:
    """屏幕 / 窗口捕获驱动（基于 windows-capture）"""

    def __init__(self, save_dir: str = "screenshots"):
        self.save_dir = save_dir
        self._captures: dict = {}
        self._latest: dict = {}
        self._lock = threading.Lock()

    # ---------- 按需截图 ----------

    def screenshot(self, monitor=None, window=None, hwnd=None, save=None, return_image=True):
        """截取指定显示器或窗口的一帧画面，返回结果 dict。"""
        monitor = _normalize_monitor(monitor)
        if window is not None or hwnd is not None:
            frame = self._capture_window_frame(window=window, hwnd=hwnd)
        else:
            frame = self._capture_monitor_frame(monitor=monitor)

        if frame is None:
            logger.error("screenshot failed: no frame captured")
            return {"status": "error", "message": "capture failed"}

        if save is None:
            os.makedirs(self.save_dir, exist_ok=True)
            save = os.path.join(self.save_dir, f"screenshot_{int(time.time() * 1000)}.png")
        else:
            os.makedirs(os.path.dirname(save) or ".", exist_ok=True)

        ok = cv2.imwrite(save, frame)
        result = {
            "status": "ok" if ok else "error",
            "width": frame.shape[1],
            "height": frame.shape[0],
            "file": save,
        }
        if ok and return_image:
            _, buf = cv2.imencode(".png", frame)
            result["data"] = _png_b64(frame)
        return result

    def _capture_monitor_frame(self, monitor=None):
        """截取显示器画面：优先 DXGI Desktop Duplication，失败时回退 Graphics Capture。"""
        try:
            session = DxgiDuplicationSession(monitor_index=monitor)
        except Exception as e:
            logger.warning(f"DXGI unavailable, falling back to Graphics Capture: {e}")
            return self._capture_frame(monitor=monitor)

        try:
            for _ in range(20):
                try:
                    frame = session.acquire_frame(timeout_ms=500)
                except RuntimeError:
                    session.recreate()
                    continue
                if frame is not None:
                    return frame.to_bgr()
        except Exception as e:
            logger.exception(f"monitor capture failed: {e}")
        return self._capture_frame(monitor=monitor)

    def _capture_window_frame(self, window=None, hwnd=None):
        """使用 Graphics Capture API 抓取指定窗口的一帧。"""
        return self._capture_frame(window=window, hwnd=hwnd)

    def _capture_frame(self, monitor=None, window=None, hwnd=None):
        """使用 Graphics Capture API 抓取一帧画面。"""
        result = {}

        capture = WindowsCapture(
            cursor_capture=True,
            monitor_index=monitor,
            window_name=window,
            window_hwnd=hwnd,
        )

        @capture.event
        def on_frame_arrived(frame, capture_control):
            result["frame"] = frame.convert_to_bgr().frame_buffer.copy()
            capture_control.stop()

        @capture.event
        def on_closed():
            pass

        try:
            control = capture.start_free_threaded()
            control.wait()
        except Exception as e:
            logger.exception(f"frame capture failed: {e}")
        return result.get("frame")

    # ---------- 持续捕获 ----------

    def start_capture(self, name="default", monitor=None, window=None, hwnd=None,
                      minimum_update_interval=None, cursor_capture=True):
        """后台持续捕获指定显示器 / 窗口，最新帧可通过 latest_frame 获取。"""
        monitor = _normalize_monitor(monitor)
        with self._lock:
            if name in self._captures:
                return {"status": "error", "message": f"capture '{name}' already running"}

            capture = WindowsCapture(
                cursor_capture=cursor_capture,
                minimum_update_interval=minimum_update_interval,
                monitor_index=monitor,
                window_name=window,
                window_hwnd=hwnd,
            )

            @capture.event
            def on_frame_arrived(frame, capture_control):
                with self._lock:
                    self._latest[name] = frame.convert_to_bgr().frame_buffer.copy()

            @capture.event
            def on_closed():
                with self._lock:
                    self._latest.pop(name, None)
                    self._captures.pop(name, None)

            try:
                control = capture.start_free_threaded()
            except Exception as e:
                logger.exception(f"capture start failed: {e}")
                return {"status": "error", "message": str(e)}
            self._captures[name] = (capture, control)
            return {"status": "ok", "name": name}

    def stop_capture(self, name="default"):
        """停止指定名称的持续捕获会话。"""
        with self._lock:
            item = self._captures.pop(name, None)
        if item is not None:
            _, control = item
            try:
                control.stop()
            except RuntimeError:
                pass
            logger.info(f"capture '{name}' stopped")
            return {"status": "ok", "name": name}
        return {"status": "error", "message": f"capture '{name}' not running"}

    def latest_frame(self, name="default", save=None, return_image=True):
        """获取指定捕获会话的最新帧。"""
        with self._lock:
            frame = self._latest.get(name)
            if frame is None:
                return {"status": "error", "message": f"no frame for capture '{name}'"}
            frame = frame.copy()

        if save is not None:
            os.makedirs(os.path.dirname(save) or ".", exist_ok=True)
            cv2.imwrite(save, frame)

        result = {
            "status": "ok",
            "name": name,
            "width": frame.shape[1],
            "height": frame.shape[0],
            "file": save,
        }
        if return_image:
            result["data"] = _png_b64(frame)
        return result

    def list_captures(self):
        with self._lock:
            return {"status": "ok", "captures": list(self._captures.keys())}
