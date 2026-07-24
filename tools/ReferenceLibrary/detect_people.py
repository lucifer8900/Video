#!/usr/bin/env python3
"""Pre-screen architecture references for people and build visual review sheets.

This tool never deletes or edits catalog assets. Detector hits are candidates for
human review, not proof that a person is present or absent.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--packages", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--contact-dir", required=True)
    return parser.parse_args()


def load_dependencies(packages: Path):
    sys.path.insert(0, str(packages))
    try:
        import cv2  # type: ignore
    except ModuleNotFoundError:
        cv2 = None
    from PIL import Image, ImageDraw, ImageFont

    return cv2, Image, ImageDraw, ImageFont


def detect(cv2, image_path: Path, hog, face_cascade) -> dict:
    image = cv2.imread(str(image_path))
    if image is None:
        return {"readError": True, "faces": [], "people": []}

    height, width = image.shape[:2]
    scale = min(1.0, 960.0 / max(width, height))
    resized = cv2.resize(image, (max(1, round(width * scale)), max(1, round(height * scale))))
    gray = cv2.cvtColor(resized, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(
        gray,
        scaleFactor=1.1,
        minNeighbors=5,
        minSize=(20, 20),
    )
    boxes, weights = hog.detectMultiScale(
        resized,
        winStride=(8, 8),
        padding=(8, 8),
        scale=1.08,
    )
    people = [
        {"x": int(box[0]), "y": int(box[1]), "width": int(box[2]), "height": int(box[3]), "confidence": round(float(weight), 4)}
        for box, weight in zip(boxes, weights)
        if float(weight) >= 0.35
    ]
    return {
        "readError": False,
        "faces": [
            {"x": int(x), "y": int(y), "width": int(w), "height": int(h)}
            for x, y, w, h in faces
        ],
        "people": people,
    }


def build_contact_sheets(Image, ImageDraw, ImageFont, entries: list[dict], output_dir: Path, prefix: str) -> list[str]:
    output_dir.mkdir(parents=True, exist_ok=True)
    columns, rows = 4, 4
    tile_width, tile_height, label_height = 480, 300, 44
    font = ImageFont.load_default()
    outputs: list[str] = []
    for page in range(math.ceil(len(entries) / (columns * rows))):
        chunk = entries[page * columns * rows:(page + 1) * columns * rows]
        sheet = Image.new("RGB", (columns * tile_width, rows * (tile_height + label_height)), "#151515")
        draw = ImageDraw.Draw(sheet)
        for index, entry in enumerate(chunk):
            column, row = index % columns, index // columns
            x, y = column * tile_width, row * (tile_height + label_height)
            try:
                with Image.open(entry["absolutePath"]) as source:
                    source = source.convert("RGB")
                    source.thumbnail((tile_width - 8, tile_height - 8))
                    offset_x = x + (tile_width - source.width) // 2
                    offset_y = y + (tile_height - source.height) // 2
                    sheet.paste(source, (offset_x, offset_y))
            except Exception:
                draw.rectangle((x + 4, y + 4, x + tile_width - 4, y + tile_height - 4), outline="#ff5050", width=3)
            label = f"{entry['index']:03d} {entry['id']}"
            draw.text((x + 6, y + tile_height + 4), label[:68], fill="white", font=font)
            if entry.get("suspected"):
                draw.rectangle((x + 2, y + 2, x + tile_width - 2, y + tile_height - 2), outline="#ff3b30", width=5)
        output = output_dir / f"{prefix}-{page + 1:02d}.jpg"
        sheet.save(output, quality=90, optimize=True)
        outputs.append(str(output))
    return outputs


def main() -> int:
    args = parse_args()
    repo_root = Path(args.repo_root).resolve()
    catalog_path = Path(args.catalog).resolve()
    packages = Path(args.packages).resolve()
    report_path = Path(args.report).resolve()
    contact_dir = Path(args.contact_dir).resolve()
    cv2, Image, ImageDraw, ImageFont = load_dependencies(packages)

    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    assets = [asset for asset in catalog["assets"] if asset["category"] == "architecture"]
    detector_available = cv2 is not None
    hog = None
    face_cascade = None
    if detector_available:
        hog = cv2.HOGDescriptor()
        hog.setSVMDetector(cv2.HOGDescriptor_getDefaultPeopleDetector())
        face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")

    entries: list[dict] = []
    for index, asset in enumerate(assets, start=1):
        absolute_path = (repo_root / asset["localRelativePath"]).resolve()
        result = (detect(cv2, absolute_path, hog, face_cascade) if detector_available else
                  {"readError": False, "faces": [], "people": [], "detectorUnavailable": True})
        suspected = result["readError"] or bool(result["faces"]) or bool(result["people"])
        entries.append({
            "index": index,
            "id": asset["id"],
            "title": asset["title"],
            "localRelativePath": asset["localRelativePath"],
            "absolutePath": str(absolute_path),
            "suspected": suspected,
            "detector": result,
        })

    all_sheets = build_contact_sheets(Image, ImageDraw, ImageFont, entries, contact_dir / "all", "architecture-all")
    suspected_entries = [entry for entry in entries if entry["suspected"]]
    suspected_sheets = build_contact_sheets(Image, ImageDraw, ImageFont, suspected_entries, contact_dir / "suspected", "architecture-suspected")
    report = {
        "schemaVersion": "1.0.0",
        "policy": "Detector hits require human review; non-hits or an unavailable detector are not automatic confirmation.",
        "detectorAvailable": detector_available,
        "architectureAssets": len(entries),
        "suspectedAssets": len(suspected_entries),
        "allContactSheets": all_sheets,
        "suspectedContactSheets": suspected_sheets,
        "assets": entries,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"architectureAssets": len(entries), "suspectedAssets": len(suspected_entries), "allSheets": len(all_sheets), "suspectedSheets": len(suspected_sheets)}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
