import base64
import io
import json
import os
import sys
import traceback


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(ROOT, "runtime", "rapidocr-py")
if SITE not in sys.path:
    sys.path.insert(0, SITE)

from PIL import Image
import numpy as np
from rapidocr import RapidOCR


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

    boxes = getattr(result, "boxes", None)
    texts = getattr(result, "txts", None)
    scores = getattr(result, "scores", None)
    if boxes is not None and texts is not None:
        for i, text in enumerate(texts):
            box = boxes[i]
            score = scores[i] if scores is not None and i < len(scores) else -1
            items.append({"text": str(text), "rect": box_to_rect(box), "confidence": float(score)})
        return items

    if isinstance(result, tuple) and len(result) > 0:
        result = result[0]

    if isinstance(result, list):
        for item in result:
            if not item:
                continue
            box = item[0]
            text = item[1] if len(item) > 1 else ""
            score = item[2] if len(item) > 2 else -1
            items.append({"text": str(text), "rect": box_to_rect(box), "confidence": float(score)})

    return items


def read_image(image_base64):
    raw = base64.b64decode(image_base64)
    image = Image.open(io.BytesIO(raw)).convert("RGB")
    return np.array(image)


def main():
    engine = RapidOCR()
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
            result = engine(image)
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
