# 已知问题记录

最后更新：2026-05-24

## 已修：开发版找不到 PaddleOCR Python

- 现象：开发目录运行 `FH6SkillPointOcr.exe` 时直接报错 `找不到 PaddleOCR Python`。
- 原因：开发目录缺少 `runtime/python/python.exe`，代码会退回到 `python.exe`，但 `File.Exists("python.exe")` 不会搜索 PATH，所以即使系统命令行能运行 Python，也会被判定为不存在。
- 当前处理：已从 `release/FH6-Miaomiao-Tools-v1.0.3/runtime/python` 复制便携 Python 到 `complex-script/runtime/python`，并验证开发目录 OCR bridge 能加载 PP-OCRv5 模型并返回 `ready`。
- 发布包检查：`v1.0.3` 解压目录和 zip 内都包含便携 Python、PaddleOCR 依赖和 PP-OCRv5 det/rec 模型；发布包没有同类缺文件问题。

## 暂不修：合理性查验中发现的风险

1. OCR 不反复写已知格子是设计规则，不作为 bug 修。
   用户原话：“已知格子不随OCR更新是正确的，因为删除车辆等操作都是直接更新，不借助OCR的信息，这个反正列表不会保留到下一轮（如果实际代码中会保留那么是错的）”。后续检查重点应放在动作模块是否直接维护虚拟表，以及旧表是否被错误带到下一轮。

2. 车辆区域截图越界时会静默改成整屏截图。
   `ScreenCapture.Grab(Rectangle region)` 如果传入区域和当前屏幕边界没有交集，会直接把截图区域改成当前屏幕整屏。这会掩盖坐标/屏幕选择错误，导致 OCR 结果和虚拟格子映射不一致。

3. 截图屏幕选择依赖当前前台窗口。
   `ScreenCapture.GetBounds()` 会优先使用非当前进程的前台窗口所在屏幕。若前台短暂变成其他窗口、通知、任务栏等，截图和鼠标避让坐标可能跑到错误显示器。

4. 技术点不足时仍会先初始化 OCR。
   `Program.Main` 会先创建 `Runtime`，而 `Runtime` 构造函数会创建 `OcrReader`。所以即使 `--skill-points 0` 后续马上退出，也会先要求 OCR 环境完整。这次开发版缺 Python 就是在 0 技术点最小启动中暴露的。

5. 源码仓库目录不能直接当完整运行包。
   仓库的 `runtime/python/`、`runtime/paddleocr-py/`、`runtime/paddleocr-models/` 都被 `.gitignore` 排除。若直接从源码目录运行，需要手动补齐这些运行时文件；正式用户应使用 release 压缩包。
