# SmartPlayBuddy

基于 WebSocket 的智能手柄控制客户端。手机端（服务端）将触发的指令通过 WebSocket 实时下发到本机，客户端执行键盘、鼠标等桌面操作，实现"手机遥控电脑"的玩法；同时提供 MOD 扩展能力，支持处理自定义请求并回传指令。

## 特性

- 通过 JWT 认证连接服务端，实时接收并执行指令
- **插件化驱动架构**：驱动以独立子进程运行，按需加载、崩溃自动恢复，隔离互不影响
- 内置键盘、鼠标驱动（单击、组合键、按下/释放、移动、滚轮等）
- 内置屏幕 / 窗口捕获驱动（`windows-capture`）：按需截图、持续捕获指定显示器或窗口，支持截图与实时画面流式回传
- 驱动执行结果回传服务端（含批量指令列表结果、二进制数据）
- 提供 `smtplay`（客户端）、`smtplay-mod`（MOD）与 `smtplay-debug`（调试面板）三个命令行入口
- 消息统一封装（`command` / `response` / `stream` / `system`），详见 [docs/data-format.zh-CN.md](docs/data-format.zh-CN.md)

## 驱动架构

```
src/smartplaybuddy/drivers/
├── registry.py      # 驱动注册表：扫描、加载、进程管理、崩溃恢复
├── base.py          # 驱动基类与子进程通信协议
├── host.py          # 驱动宿主：子进程内运行驱动入口
├── keyboard/        # 键盘驱动（pyautogui）
│   ├── driver.py
│   ├── manifest.json
│   └── requirements.txt
├── mouse/           # 鼠标驱动（pyautogui）
└── screen/          # 屏幕捕获驱动（windows-capture）
```

- 每个驱动是一个独立目录，通过 `manifest.json` 声明元数据（`appid`、`name`、`entry`、`requirements`、`actions`）
- 客户端按 `action` 懒加载驱动：首次调用时以子进程启动（`--driver-host` 模式），按需安装该驱动的依赖
- 驱动与客户端通过 stdin/stdout 二进制协议通信，驱动崩溃后自动重启恢复
- 调用方式统一为 `drivers[action](data)`，支持 `start_stream` / `stop_stream` 实时画面回传

## 项目结构

```
src/smartplaybuddy/
├── client.py        # 客户端入口：接收服务端指令并调用驱动执行，回传结果
├── mod.py           # MOD 入口：处理自定义请求
├── drivers/         # 插件化驱动系统（键盘 / 鼠标 / 屏幕，见上文）
├── ws/              # WebSocket 连接、消息封装与逻辑处理
├── user/            # 登录与令牌刷新（keyring 存储）
├── config/          # 服务端地址等配置
├── i18n/            # 多语言支持
├── log/             # 日志
└── ui/              # 界面（调试面板等）
```

## 环境要求

- Python >= 3.9
- Windows（键盘 / 鼠标 / 屏幕捕获驱动依赖 `pyautogui`、`windows-capture`）

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
# 启动客户端（接收指令，执行键盘/鼠标等操作）
smtplay

# 启动 MOD（处理自定义请求）
smtplay-mod

# 启动调试面板（日志 / 指令测试台 / 截图预览 / 截图文件管理）
smtplay-debug
```

首次运行会提示登录，令牌通过 `keyring` 保存，之后自动刷新。

## 开发

- 数据通信格式见 [docs/data-format.zh-CN.md](docs/data-format.zh-CN.md)
- 驱动开发与插件化机制见 [docs/driver.zh-CN.md](docs/driver.zh-CN.md)
- MOD 开发流程见 [docs/mods/mod-development.zh-CN.md](docs/mods/mod-development.zh-CN.md)
- 产品需求文档见 [docs/SmartPlayBuddy-prd.md](docs/SmartPlayBuddy-prd.md)
