import base64
import contextlib
import io
import json
import os
import sys
import traceback


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(ROOT, "runtime", "paddleocr-py")
if SITE not in sys.path:
    sys.path.insert(0, SITE)
LOCAL_CACHE_ROOT = os.path.join(ROOT, "runtime", "paddleocr-cache")
MODEL_ROOT = os.path.join(ROOT, "runtime", "paddleocr-models")
DEFAULT_DET_MODEL_NAME = os.environ.get("FH6_PADDLEOCR_DET_MODEL", "PP-OCRv5_mobile_det")
DEFAULT_REC_MODEL_NAME = os.environ.get("FH6_PADDLEOCR_REC_MODEL", "PP-OCRv5_mobile_rec")

os.makedirs(LOCAL_CACHE_ROOT, exist_ok=True)
os.environ.setdefault("PADDLE_PDX_CACHE_HOME", LOCAL_CACHE_ROOT)
os.environ.setdefault("PADDLE_HOME", os.path.join(LOCAL_CACHE_ROOT, "paddle"))
os.environ.setdefault("XDG_CACHE_HOME", os.path.join(LOCAL_CACHE_ROOT, "xdg"))
os.environ.setdefault("HF_HOME", os.path.join(LOCAL_CACHE_ROOT, "huggingface"))
os.environ.setdefault("HUGGINGFACE_HUB_CACHE", os.path.join(LOCAL_CACHE_ROOT, "huggingface", "hub"))
os.environ.setdefault("MODELSCOPE_CACHE", os.path.join(LOCAL_CACHE_ROOT, "modelscope"))
os.environ.setdefault("PADDLE_EXTENSION_DIR", os.path.join(LOCAL_CACHE_ROOT, "paddle_extension"))
os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")

from PIL import Image
import numpy as np

with contextlib.redirect_stdout(sys.stderr):
    from paddleocr import PaddleOCR


def box_to_rect(box):
    xs = [float(p[0]) for p in box]
    ys = [float(p[1]) for p in box]
    left = min(xs)
    top = min(ys)
    right = max(xs)
    bottom = max(ys)
    return [left, top, right - left, bottom - top]


def normalize_result(result):
    items = []
    if result is None:
        return items

    for page in result:
        data = getattr(page, "json", None)
        if isinstance(data, dict) and "res" in data:
            data = data["res"]
        elif isinstance(page, dict):
            data = page.get("res", page)
        else:
            data = {}

        texts = data.get("rec_texts") or []
        scores = data.get("rec_scores") or []
        polys = data.get("rec_polys") or data.get("dt_polys") or []
        boxes = data.get("rec_boxes") or []

        for i, text in enumerate(texts):
            rect = None
            if i < len(polys):
                rect = box_to_rect(polys[i])
            elif i < len(boxes):
                b = boxes[i]
                rect = [float(b[0]), float(b[1]), float(b[2]) - float(b[0]), float(b[3]) - float(b[1])]
            if rect is None:
                continue
            score = float(scores[i]) if i < len(scores) else -1
            items.append({"text": str(text), "rect": rect, "confidence": score})

    return items


def read_image(image_base64):
    raw = base64.b64decode(image_base64)
    image = Image.open(io.BytesIO(raw)).convert("RGB")
    return np.array(image)


def find_model_dir(model_name):
    candidates = [
        os.path.join(MODEL_ROOT, model_name),
        os.path.join(LOCAL_CACHE_ROOT, "official_models", model_name),
    ]
    for path in candidates:
        if os.path.isdir(path):
            return path
    return None


def require_model_dir(model_name):
    path = find_model_dir(model_name)
    if not path:
        raise FileNotFoundError(
            "missing local PaddleOCR model: {}. Please use the full release package and extract all files.".format(model_name)
        )
    required = ["inference.pdiparams", "inference.yml"]
    missing = [name for name in required if not os.path.isfile(os.path.join(path, name))]
    if missing:
        raise FileNotFoundError(
            "incomplete PaddleOCR model {}: missing {} under {}".format(model_name, ", ".join(missing), path)
        )
    return path


def main():
    engine_kwargs = {
        "use_doc_orientation_classify": False,
        "use_doc_unwarping": False,
        "use_textline_orientation": False,
    }
    det_model_dir = require_model_dir(DEFAULT_DET_MODEL_NAME)
    rec_model_dir = require_model_dir(DEFAULT_REC_MODEL_NAME)
    engine_kwargs.update(
        {
            "text_detection_model_name": DEFAULT_DET_MODEL_NAME,
            "text_detection_model_dir": det_model_dir,
            "text_recognition_model_name": DEFAULT_REC_MODEL_NAME,
            "text_recognition_model_dir": rec_model_dir,
        }
    )

    with contextlib.redirect_stdout(sys.stderr):
        engine = PaddleOCR(**engine_kwargs)

    sys.stdout.write(json.dumps({"code": 0, "message": "ready"}, ensure_ascii=False) + "\n")
    sys.stdout.flush()

    for line in sys.stdin:
        line = line.strip().lstrip("\ufeff")
        if not line:
            continue
        if line == "__exit__":
            break
        try:
            request = json.loads(line)
            image = read_image(request["image_base64"])
            with contextlib.redirect_stdout(sys.stderr):
                result = engine.predict(image)
            response = {"code": 100, "data": normalize_result(result)}
        except Exception as exc:
            response = {
                "code": 500,
                "error": str(exc),
                "trace": traceback.format_exc(limit=8),
            }
        sys.stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
