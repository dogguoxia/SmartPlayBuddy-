"""
命令行入口，统一解析启动参数。
"""
import argparse


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="SmartPlayBuddy",
        description="SmartPlayBuddy 本地客户端",
    )
    parser.add_argument(
        "--headless",
        action="store_true",
        help="只启动 WebSocket 客户端，不启动 UI 调试面板",
    )
    parser.add_argument(
        "--ui-only",
        action="store_true",
        help="只启动 UI 调试面板，不启动 WebSocket 客户端",
    )
    parser.add_argument(
        "--local",
        action="store_true",
        help="本地模式：连接 localhost 时跳过登录流程",
    )
    parser.add_argument(
        "--driver-host",
        action="store_true",
        help="以驱动子进程模式运行（内部使用）",
    )
    parser.add_argument(
        "driver_file",
        nargs="?",
        help="驱动文件路径（仅 --driver-host 模式使用）",
    )
    parser.add_argument(
        "packages_dir",
        nargs="?",
        help="驱动包目录（仅 --driver-host 模式使用）",
    )
    return parser


def parse_args(argv=None):
    return build_parser().parse_args(argv)
