# SmartPlayBuddy

基于 WebSocket 的智能手柄控制客户端。手机端（服务端）将触发的指令通过 WebSocket 实时下发到本机，客户端执行键盘、鼠标等桌面操作，实现"手机遥控电脑"的玩法；同时提供 MOD 扩展能力，支持处理自定义请求并回传指令。

## 特性

- 通过 JWT 认证连接服务端，实时接收并执行指令
- 内置键盘、鼠标驱动（单击、组合键、按下/释放、移动、滚轮等）
- 支持虚拟 Xbox 手柄（`vgamepad`）
- 提供 `smtplay`（客户端）与 `smtplay-mod`（MOD）两个命令行入口
- 消息统一封装（`command` / `response` / `system`），详见 [docs/data-format.zh-CN.md](docs/data-format.zh-CN.md)

## 项目结构

```
src/smartplaybuddy/
├── client.py        # 客户端入口：接收服务端指令并调用驱动执行
├── mod.py           # MOD 入口：处理自定义请求
├── driver/          # 键盘 / 鼠标 / 虚拟手柄驱动
├── ws/              # WebSocket 连接、消息封装与逻辑处理
├── user/            # 登录与令牌刷新（keyring 存储）
├── config/          # 服务端地址等配置
├── i18n/            # 多语言支持
├── log/             # 日志
└── ui/              # 界面
```

## 环境要求

- Python >= 3.7
- Windows（键盘/鼠标/手柄驱动依赖 `pyautogui`、`vgamepad`）

## 安装

```bash
pip install -r requirements.txt
```

或通过 pyproject 安装（同时提供命令行入口）：

```bash
pip install -e .
```

## 使用

配置服务端地址（`src/smartplaybuddy/config/__init__.py`，默认 `localhost:8000` / `ws://localhost:2508/ws`），然后运行：

```bash
# 启动客户端（接收指令，执行键盘/鼠标操作）
smtplay

# 启动 MOD（处理自定义请求）
smtplay-mod
```

首次运行会提示登录，令牌通过 `keyring` 保存，之后自动刷新。

## 开发

- MOD 开发流程见 [docs/mods/mod-decelopment.zh-CN.md](docs/mods/mod-decelopment.zh-CN.md)
- 通信数据格式见 [docs/data-format.zh-CN.md](docs/data-format.zh-CN.md)
