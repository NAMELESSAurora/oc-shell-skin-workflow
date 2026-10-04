#!/usr/bin/env python3
"""Validate PNG contracts without changing the images or contacting a service."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PureWindowsPath
from typing import Any

from PIL import Image


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inspect_png(path: Path) -> dict[str, Any]:
    header = path.read_bytes()[:26]
    if header[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("File is not a PNG")
    with Image.open(path) as image:
        image.load()
        result: dict[str, Any] = {
            "width": image.width, "height": image.height, "mode": image.mode,
            "bitDepth": header[24], "pngColorType": header[25],
            "sha256": sha256(path), "bytes": path.stat().st_size,
        }
        if image.mode == "RGBA":
            alpha = image.getchannel("A")
            bbox = alpha.getbbox()
            result.update(
                alphaRange=list(alpha.getextrema()),
                visibleBounds=list(bbox) if bbox else None,
                corners=[alpha.getpixel(xy) for xy in [
                    (0, 0), (image.width - 1, 0),
                    (0, image.height - 1), (image.width - 1, image.height - 1),
                ]],
                margins={"left": bbox[0], "top": bbox[1],
                         "right": image.width - bbox[2],
                         "bottom": image.height - bbox[3]} if bbox else None,
            )
        return result


def validate_asset(spec: dict[str, Any], root: Path) -> dict[str, Any]:
    errors: list[str] = []
    relative = spec.get("path", "")
    result: dict[str, Any] = {"path": relative, "errors": errors}
    candidate = (root / relative).resolve()
    if (not relative or Path(relative).is_absolute() or PureWindowsPath(relative).drive
            or not candidate.is_relative_to(root.resolve())):
        errors.append("Asset path must be relative and remain inside --root")
        result["passed"] = False
        return result
    if not candidate.is_file():
        errors.append("Asset does not exist")
        result["passed"] = False
        return result
    try:
        actual = inspect_png(candidate)
        result["actual"] = actual
    except (OSError, ValueError) as exc:
        errors.append(str(exc))
        result["passed"] = False
        return result

    if "size" in spec and [actual["width"], actual["height"]] != spec["size"]:
        errors.append("Pixel dimensions do not match the prescribed size")
    if "minimumSize" in spec and any(a < b for a, b in zip(
            [actual["width"], actual["height"]], spec["minimumSize"])):
        errors.append("Pixel dimensions are below minimumSize")
    if "maxBytes" in spec and actual["bytes"] > spec["maxBytes"]:
        errors.append("File exceeds maxBytes")
    expected = spec.get("sha256")
    if expected:
        if len(expected) != 64 or any(c not in "0123456789abcdefABCDEF" for c in expected):
            errors.append("Expected sha256 must be the approved 64-digit hex value")
        elif actual["sha256"].lower() != expected.lower():
            errors.append("SHA256 differs from the approved asset")

    if spec.get("trueRGBA", False):
        if (actual["mode"], actual["pngColorType"], actual["bitDepth"]) != ("RGBA", 6, 8):
            errors.append("Requires true 8-bit RGBA PNG, not RGB or indexed transparency")
        elif actual["alphaRange"] == [255, 255]:
            errors.append("Fully opaque RGBA is not a transparent character cutout")
        elif actual["visibleBounds"] is None:
            errors.append("Fully transparent PNG has no character")
        else:
            if actual["alphaRange"][0] != 0:
                errors.append("Cutout must include alpha=0 transparent pixels")
            if spec.get("transparentCorners", True) and any(actual["corners"]):
                errors.append("All four sprite canvas corners must be transparent")
            margins = spec.get("minimumMargins", {})
            for side, minimum in margins.items():
                if side not in {"left", "top", "right", "bottom"}:
                    errors.append("Unknown margin side: " + side)
                elif actual["margins"][side] < minimum:
                    errors.append(side + " transparent margin is too small")
    result["passed"] = not errors
    result["humanArtworkReview"] = spec.get("humanArtworkReview", "required")
    return result


def validate_manifest(manifest_path: Path, root: Path) -> dict[str, Any]:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    results = [validate_asset(item, root) for item in manifest.get("assets", [])]
    return {
        "role": manifest.get("role"),
        "passed": bool(results) and all(item["passed"] for item in results),
        "assets": results,
        "humanReviewNotice": "Alpha and geometry checks cannot prove correct eyes, outfit, hair, style, noise or a hidden painted checkerboard. Inspect the artwork separately.",
        "networkRequests": 0, "sourceImagesModified": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    try:
        report = validate_manifest(args.manifest, args.root)
    except (OSError, ValueError, TypeError, KeyError) as exc:
        report = {"passed": False, "error": str(exc)}
    body = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(body, encoding="utf-8")
    print(body, end="")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
