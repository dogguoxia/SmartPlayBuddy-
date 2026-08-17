import importlib
import os
import subprocess
import sys

if sys.stdout is not None:
    reconfigure = getattr(sys.stdout, "reconfigure", None)
    if reconfigure:
        try:
            reconfigure(encoding="utf-8")
            if sys.stderr is not None:
                getattr(sys.stderr, "reconfigure", lambda **_: None)(encoding="utf-8")
        except Exception:
            pass

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "src"))

# 依赖自检:模块导入名 -> pip 包名
DEPENDENCIES = {
    "websockets": "websockets",
    "pyautogui": "pyautogui",
    "keyring": "keyring",
    "windows_capture": "windows-capture",
    "webview": "pywebview",
    "PyQt6": "PyQt6",
    "dxcam": "dxcam",
    "cv2": "opencv-python",
    "numpy": "numpy",
    "PIL": "Pillow",
}

PIP_MIRROR = "https://mirrors.aliyun.com/pypi/simple/"


def ensure_dependencies() -> None:
    """启动前检查依赖,缺失时自动安装。"""
    missing = []
    for module, pkg in DEPENDENCIES.items():
        try:
            importlib.import_module(module)
        except ImportError:
            missing.append(pkg)
    if not missing:
        return
    print(f"[deps] 检测到缺失依赖: {', '.join(missing)},开始安装...")
    cmd = [
        sys.executable,
        "-m",
        "pip",
        "install",
        "-i",
        PIP_MIRROR,
        *missing,
    ]
    result = subprocess.run(cmd, cwd=os.path.dirname(os.path.abspath(__file__)))
    if result.returncode != 0:
        sys.exit(f"[deps] 依赖安装失败,请手动执行: python -m pip install {' '.join(missing)}")
    print("[deps] 依赖安装完成。")


def _run_driver_host(driver_file: str, packages_dir: str):
    from smartplaybuddy.drivers.host import run_driver
    run_driver(driver_file, packages_dir)


def main():
    ensure_dependencies()

    from smartplaybuddy import cli
    args = cli.parse_args()

    if args.driver_host:
        _run_driver_host(args.driver_file or "", args.packages_dir or "")
        return

    if args.ui_only:
        from smartplaybuddy.ui.debugger import main as ui_main
        ok = ui_main()
        if not ok:
            sys.exit(1)
        return

    if args.headless:
        from smartplaybuddy.client import run_client_sync
        run_client_sync(skip_login=args.local)
        return

    # 默认：同时启动 WebSocket 客户端和 UI 调试面板
    import threading
    from smartplaybuddy.client import run_client_sync
    from smartplaybuddy.ui.debugger import main as ui_main

    client_thread = threading.Thread(
        target=run_client_sync,
        kwargs={"skip_login": args.local},
        daemon=True,
    )
    client_thread.start()

    ok = ui_main()
    if not ok:
        print("[main] UI 启动失败，WebSocket 客户端仍在后台运行。按 Ctrl+C 退出。")
        try:
            client_thread.join()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
