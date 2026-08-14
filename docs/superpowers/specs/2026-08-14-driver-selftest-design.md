# 调试面板驱动自检功能设计

日期：2026-08-14

## 背景

合并主仓库的插件化驱动架构后，`ui/debugger.py` 仍引用已删除的旧 `driver/` 模块，调试面板无法启动。用户需要在面板上添加"驱动自检"功能，一键验证键盘 / 鼠标 / 屏幕驱动是否正常工作。

## 目标

1. 修复调试面板，适配新的插件化 `drivers/` 架构
2. 新增"驱动自检"页面：一键自检全部驱动，逐项显示状态、耗时、错误详情，支持单项重测

## 自检方式（安全模式）

不干扰用户操作：

| 驱动 | 自检指令 | 说明 |
|---|---|---|
| keyboard | `{"operate": "tap", "key": "f13"}` | F13 为无害键，无副作用 |
| mouse | `{"operate": "move_to", "x": 0.5, "y": 0.5, "duration": 0.3}` | 鼠标移到屏幕中心，不点击 |
| screen | `{"operate": "capture", "device_idx": 0, "output_idx": 0, "scale": 0.25}` | 轻量抓帧验证成像链路，缩放减小数据量 |

判定标准：驱动调用返回 `status: ok`（screen 需返回有效 width/height）即通过；抛异常或返回 error 即失败。

## 后端改动（`src/smartplaybuddy/ui/debugger.py`）

1. 导入改为 `from ..drivers import drivers`（新注册表 Dr iversDict）
2. `get_meta`：屏幕尺寸改用 `pyautogui.size()`；驱动列表用 `drivers.keys()`；截图目录固定为 `./screenshots`
3. `screenshot` API：改走 `drivers["screen"]({"operate": "capture", ...})`，返回 PNG base64 预览（fmt=png，`__data__` 转 base64）
4. 新增 `self_test()`：遍历 `drivers.keys()` 逐个执行安全自检，收集 `{action, status, elapsed_ms, detail}`；单项失败不中断其他项
5. 新增 `self_test_one(action)`：单项重测
6. 首次加载驱动需启动子进程 / 安装依赖，单项目标超时 30 秒

## 前端改动（`src/smartplaybuddy/ui/debugger.html`）

1. 导航新增"驱动自检"页
2. 页面包含：一键"全部自检"按钮、逐项结果卡片（驱动名、状态徽标、耗时、详情、单项重测按钮）
3. 自检执行中按钮置灰防重复，结果逐项实时更新
4. 失败项显示错误详情（如驱动子进程启动失败信息）

## 错误处理

- 单项失败不中断；全部结果统一返回
- 首次加载慢：提示"首次加载驱动可能较慢"
- 驱动加载异常时返回可读错误信息而非堆栈崩溃

## 非目标

- 不做真实操作模式（打字 / 点击验证）
- 不改动驱动插件本身的实现
- 不涉及服务端连接测试
