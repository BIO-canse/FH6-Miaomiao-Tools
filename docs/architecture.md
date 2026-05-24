# 脚本结构规划

## 核心模块

- `ScreenCapture`：C# / WinForms 截图，支持全屏和区域截图。
- `OcrReader`：调用本地 PaddleOCR PP-OCRv5，统一返回文本候选坐标。
- `InputController`：C# P/Invoke `SendInput` 键鼠动作。
- `StateMachine`：主流程状态机。
- `GridGeometry`：车辆格子的固定像素几何，来自首次手动框选的完整可见车辆格子整体区域。
- `MouseCellCalibrator`：首次运行时显示框选层，记录完整可见车辆格子整体区域，并按行列数切分单格。
- `VirtualVehicleList`：运行期全局车辆列表，第一轮定表后跨轮使用，记录目标车、全新、可删、开蓝图车和当前 offset，并实时覆盖保存到本地 JSON。
- `OverlayRenderer`：透明置顶叠加层，用蓝色边框和线条显示虚拟表格，用左上角状态面板显示脚本状态。
- `DebugRecorder`：截图、框选图、日志。
- `shared-cs/FH6AutomationConstants.cs`：主程序和独立小脚本共用的默认常量，负责键码、等待时间、状态码、技术点上限、子程序文件名和 safe-stop 文件名。

## 状态机草案

```text
StartupDelay
OpenManufacturerList
ScrollManufacturerListToBottom
OcrFindSubaru
BuildInitialVehicleTable
FindPendingState3FromTable
MoveSelectionToCell
FixedKeySequence
Loop
Paused
Exit
```

## 实现原则

- 主流程只调用抽象动作，不直接写识别算法细节。
- 识别链路只使用 OCR；普通模式截图在内存中传给 OCR。调试模式保存截图链路到 `debug/screenshots/` 便于排查。
- 立即急停由独立监控进程负责。主程序启动时拉起 `FH6EmergencyStopWatcher.exe`，监控进程检测到 `Space+C` 后直接结束同一工具包内的所有自动化进程和打包 Python/OCR 子进程；业务线程即使正在等待 OCR 返回，也不影响立即急停。
- `Space+V` 仍由业务进程处理，因为安全退出需要知道当前流程是否已经跑完本轮并复位。
- 所有坐标都用截图绝对像素坐标。
- 可由用户调节的运行参数放 `config/default.json`；跨程序共用的默认值和状态码放 `shared-cs/FH6AutomationConstants.cs`。
- 调试输出默认打开，稳定后再允许关闭。
- `GridGeometry` 启动时从 `config/user-settings.json` 读取并锁定，不通过 OCR 推断格子大小。
- 全自动流程第一轮先执行定表阶段：从车库标准位进入斯巴鲁车辆列表，OCR 扫出完整虚拟表；后续点技术点、删车、买车追加和找 `900` 分开蓝图车辆直接读写虚拟表，不再每轮车辆格 OCR。
- 制造商定位使用 OCR 和运行期坐标缓存：先执行 `Enter -> 等待 1 秒 -> Backspace -> 等待 0.5 秒` 打开制造商页面，鼠标移到屏幕中心，向下滚动 `10` 格，再优先使用本进程缓存坐标；没有缓存才整屏 OCR 找 `斯巴鲁` 并点击。车库制造商页和买车前置制造商页使用不同 UI 坐标缓存标签，不能共用缓存。
- 目标格选择只用键盘移动。左右键会触发车辆列表横向滚动，选中框仍停在屏幕第一列；执行器根据虚拟表全局列和当前 offset 生成 `Right/Left` 次数，再按目标行生成 `Down` 次数。
- 叠加层只做显示，不参与识别；截图识别前必须隐藏叠加层或使用不会捕获叠加层的截图方式。
