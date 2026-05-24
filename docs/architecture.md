# 脚本结构规划

## 核心模块

- `ScreenCapture`：C# / WinForms 截图，支持全屏和区域截图。
- `OcrReader`：调用本地 PaddleOCR PP-OCRv5，统一返回文本候选坐标。
- `InputController`：C# P/Invoke `SendInput` 键鼠动作。
- `StateMachine`：主流程状态机。
- `GridGeometry`：车辆格子的固定像素几何，来自首次手动框选的左上角格子。
- `MouseCellCalibrator`：首次运行时显示框选层，记录最左上角完整车辆格子。
- `CellOccupancy`：当前屏幕内每个格子是否为目标车型的二维布尔表。
- `VirtualGrid`：组合 `GridGeometry` 和 `CellOccupancy`，负责坐标到格子的映射。
- `VirtualVehicleList`：运行期全局车辆列表，随着滚动扩展，记录目标车、全新状态和上次命中偏移量，并实时覆盖保存到本地 JSON。
- `OverlayRenderer`：透明置顶叠加层，用蓝色边框显示虚拟表格，用状态色填充格子并显示脚本状态。
- `DebugRecorder`：截图、框选图、日志。
- `shared-cs/FH6AutomationConstants.cs`：主程序和独立小脚本共用的默认常量，负责键码、等待时间、状态码、技术点上限、子程序文件名和 safe-stop 文件名。

## 状态机草案

```text
StartupDelay
OpenManufacturerList
ScrollManufacturerListToBottom
OcrFindSubaru
FindFirstImpreza
BuildVirtualGrid
UpdateCellOccupancy
FindValidNewBadge
MoveSelectionToCell
FixedKeySequence
Loop
Paused
Exit
```

## 实现原则

- 主流程只调用抽象动作，不直接写识别算法细节。
- 识别链路只使用 OCR；普通模式截图在内存中传给 OCR。调试模式保存截图链路到 `debug/screenshots/` 便于排查。
- 所有坐标都用截图绝对像素坐标。
- 可由用户调节的运行参数放 `config/default.json`；跨程序共用的默认值和状态码放 `shared-cs/FH6AutomationConstants.cs`。
- 调试输出默认打开，稳定后再允许关闭。
- `GridGeometry` 启动时从 `config/user-settings.json` 读取并锁定，不通过 OCR 推断格子大小。
- 后续滚动后进入 `UpdateCellOccupancy`，用当前屏幕内的 `IMPREZA 22B-STI` 坐标更新二维布尔表，不再改变格子几何。
- 制造商定位使用 OCR：按 `Backspace` 打开制造商页面后，鼠标移到屏幕中心，滚动到底，整屏 OCR 找 `斯巴鲁` 并点击。车库制造商页和买车前置制造商页使用不同 UI 坐标缓存标签，不能共用缓存。
- 目标格选择使用三行 N 列模型：从默认第 0 行第 0 列出发，先按 `Right` 到目标列，再按 `Down` 到目标行。
- 叠加层只做显示，不参与识别；截图识别前必须隐藏叠加层或使用不会捕获叠加层的截图方式。
