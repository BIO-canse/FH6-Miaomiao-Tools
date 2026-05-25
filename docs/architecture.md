# 脚本结构规划

## 核心模块

- `ScreenCapture`：C# / WinForms 截图，支持全屏和区域截图。
- `OcrReader`：统一返回文本候选坐标。发布包分为两个 OCR 后端版本：`MediaOCR` 版默认使用 Windows 自带 `Windows.Media.Ocr`，少依赖、优先解决用户电脑 OCR 闪退；`PaddleOCR` 版保留本地 PaddleOCR PP-OCRv5，准确率更高但依赖更重。
- OCR 候选必须保留语言来源，不能把中文和英文结果粗暴合并后交给同一个匹配器。用户原话：“车名交给英语，年份+斯巴鲁后面的斯巴鲁部分会被中文识别到，我们会拿到两个坐标，然后放到一起不就行了？”当前实现按这个边界处理：中文 UI、制造商、`全新` 只消费中文 OCR 结果；`IMPREZA 22B-STI` 只消费英文 OCR 结果；数字/性能分按格子位置和数值规则过滤。
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
- OCR 启动前必须做依赖自检：除检查 Python、bridge、模型和核心 `.pyd/.dll` 文件是否存在外，还要用同一个便携 Python 启动 bridge 的 `--self-check`，确认 `PIL`、`numpy`、`paddle`、`paddleocr` 和 Paddle 原生库能实际加载。自检 stdout/stderr、进程退出码、Python 路径、模型路径、系统位数等写入 `debug/ocr-dependency-check-last.txt`，失败时停止流程，不让自动化继续到后续错误动作。
- MediaOCR 版也必须做独立自检：列出 `Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages`，确认能创建中文和英文 OCR engine，把语言列表、最大图片尺寸和实际选中的语言写入 `debug/mediaocr-dependency-check-last.txt`。如果系统没有中文 OCR 能力，启动阶段直接提示用户改用 PaddleOCR 版或在 Windows 语言/可选功能里安装中文 OCR。
- OCR 文本候选进入业务逻辑前必须做紧凑过滤：例如同一轮同时得到 `xxx 斯巴鲁` 和 `斯巴鲁` 时，只保留精确/最紧凑候选；没有精确候选时才使用最短包含或模糊候选，避免宽泛结果和精确结果一起进入后续逻辑造成多命中或误选。
- 目标车型识别不能只依赖整行文本匹配。MediaOCR 可能把同一行多个车辆格子的 `IMPREZA 22B-STI` 拼成一个很宽的候选框；建表写格时必须优先按虚拟格子聚合 OCR 词，判断该格内部是否同时存在 `IMPREZA/MPREZA/PREZA` 和 `22B...`，生成单格紧凑候选框，再退到相邻词组、整行/模糊匹配。
- MediaOCR 版在车辆格 OCR 时走专属列切分流程：先截一次完整车辆格区域，本地第 0 列是已知保留列，不参与 OCR；从第 1 列开始按用户配置的完整可见列逐列裁切，每列启动独立 MediaOCR reader 识别，最后把识别结果按绝对坐标合并。这个处理只用于新 OCR/MediaOCR，PaddleOCR 和旧 OCR 继续使用整块车辆格 OCR。业务层不要直接判断 OCR 后端；OCR 适配层通过 `SkippedLeadingGridColumns` 告诉建表阶段本次前置列没有识别，建表只消费这个元数据。
- OCR 阶段可以直接写状态 `4`，因为车库里可能本来就有旧车。写表优先级必须保证 `目标车+600+全新` 写成状态 `3`，`目标车+600+没有全新` 才写成状态 `4`，并且状态 `3` 绝不能进入删车目标集合。
- `GridGeometry` 启动时从 `config/user-settings.json` 读取并锁定，不通过 OCR 推断格子大小。
- 全自动流程第一轮先执行定表阶段：从车库标准位进入斯巴鲁车辆列表，OCR 扫出完整虚拟表；后续点技术点、删车、买车追加和找 `900` 分开蓝图车辆直接读写虚拟表，不再每轮车辆格 OCR。
- 虚拟表快照带 `semantic_revision`。当 OCR 写表语义发生安全相关变化时必须提升版本并拒绝加载旧快照，避免旧版本生成的错误 `4` 状态被新版继续删除。
- 制造商定位使用 OCR 和运行期坐标缓存：先执行 `Enter -> 等待 1 秒 -> Backspace -> 等待 0.5 秒` 打开制造商页面，鼠标移到屏幕中心，向下滚动 `10` 格，再优先使用本进程缓存坐标；没有缓存才整屏 OCR 找 `斯巴鲁` 并点击。车库制造商页和买车前置制造商页使用不同 UI 坐标缓存标签，不能共用缓存。
- 目标格选择只用键盘移动。左右键会触发车辆列表横向滚动，选中框仍停在屏幕第一列；执行器根据虚拟表全局列和当前 offset 生成 `Right/Left` 次数，再按目标行生成 `Down` 次数。
- 叠加层只做显示，不参与识别；截图识别前必须隐藏叠加层或使用不会捕获叠加层的截图方式。
