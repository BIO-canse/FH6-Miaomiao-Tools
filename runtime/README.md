# runtime

本目录放脚本运行需要的本地环境。

- `paddleocr-py/`：PaddleOCR PP-OCRv5 运行依赖。
- `paddleocr-models/`：PP-OCRv5 server det/rec 默认模型。建表和制造商识别优先准确率，不再随包携带 mobile 模型。
- `python/`：发布包内置 Python 运行时；如果存在，程序会优先使用它。
- `paddleocr-cache/`：PaddleOCR / PaddleX / Paddle 缓存目录；缺模型时首次下载也写到这里。
- `paddleocr_bridge.py`：C# 常驻调用 PaddleOCR 的桥接脚本。
- `mediaocr_bridge.ps1`：C# 常驻调用 Windows `Media.Ocr` 的桥接脚本。MediaOCR 版只依赖系统 PowerShell 和 Windows 自带 OCR 语言能力，不需要包内 Python/PaddleOCR。
- `rapidocr-py/`：RapidOCR 运行依赖，保留为可切换后端。
- `rapidocr_bridge.py`：RapidOCR 桥接脚本。
- `tesseract/`：本地 Tesseract OCR，保留为可切换后端。

正常运行使用项目根目录的 `run.cmd`。
