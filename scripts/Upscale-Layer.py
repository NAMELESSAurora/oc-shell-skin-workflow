"""Optional local waifu2x 2x inference with an independently restored RGBA alpha plane."""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import tempfile
from pathlib import Path

from PIL import Image


def validate(input_path: Path, output_path: Path, executable: Path, model_dir: Path):
    if input_path.resolve() == output_path.resolve():
        raise ValueError("Output must not replace the mother image")
    if output_path.exists() or output_path.with_suffix(".upscale.json").exists():
        raise ValueError("Output exists; choose a new candidate filename")
    if not executable.is_file() or not model_dir.is_dir():
        raise ValueError("Provide an installed waifu2x executable and model directory")
    with Image.open(input_path) as source:
        if source.mode not in {"RGB", "RGBA"}:
            raise ValueError("Use an RGB environment or reviewed RGBA character layer")
        return source.mode, source.size


def upscale(input_path: Path, output_path: Path, executable: Path, model_dir: Path, check_only=False):
    mode, dimensions = validate(input_path, output_path, executable, model_dir)
    report = {"status": "planned" if check_only else "processed", "scale": 2, "noiseLevel": -1, "tile": 256, "jobs": "1:1:1", "input": {"name": input_path.name, "mode": mode, "dimensions": list(dimensions), "sha256": hashlib.sha256(input_path.read_bytes()).hexdigest().upper()}, "engine": {"name": executable.name, "sha256": hashlib.sha256(executable.read_bytes()).hexdigest().upper(), "modelDirectoryName": model_dir.name}, "alphaHandling": "Original alpha plane, bicubic 2x, restored after RGB inference" if mode == "RGBA" else "RGB environment", "apiRequests": 0, "sourceModified": False}
    if check_only:
        return report
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(input_path) as source:
        rgb = source.convert("RGB")
        alpha = source.getchannel("A") if mode == "RGBA" else None
        with tempfile.TemporaryDirectory(prefix="upscale-candidate-", dir=output_path.parent) as temp:
            candidate = Path(temp)
            rgb_path, result_path = candidate / "rgb-input.png", candidate / "rgb-result.png"
            rgb.save(rgb_path)
            command = [str(executable.resolve()), "-i", str(rgb_path.resolve()), "-o", str(result_path.resolve()), "-m", str(model_dir.resolve()), "-n", "-1", "-s", "2", "-t", "256", "-j", "1:1:1", "-f", "png"]
            result = subprocess.run(command, cwd=executable.parent, capture_output=True, timeout=600)
            if result.returncode != 0 or not result_path.is_file():
                raise ValueError("Local waifu2x inference failed; check Vulkan support and the installed model")
            with Image.open(result_path) as image:
                expected = (dimensions[0] * 2, dimensions[1] * 2)
                if image.size != expected:
                    raise ValueError("Unexpected inference dimensions")
                final = image.convert("RGBA" if alpha else "RGB")
                if alpha:
                    final.putalpha(alpha.resize(expected, Image.Resampling.BICUBIC))
                final.save(output_path)
    report["output"] = {"name": output_path.name, "mode": final.mode, "dimensions": list(final.size), "sha256": hashlib.sha256(output_path.read_bytes()).hexdigest().upper()}
    output_path.with_suffix(".upscale.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--executable", type=Path, required=True)
    parser.add_argument("--model-dir", type=Path, required=True)
    parser.add_argument("--check-only", action="store_true")
    args = parser.parse_args()
    try:
        print(json.dumps(upscale(args.input, args.output, args.executable, args.model_dir, args.check_only), ensure_ascii=False))
    except (ValueError, OSError, subprocess.TimeoutExpired) as error:
        print(json.dumps({"status": "failed", "error": str(error)}, ensure_ascii=False))
        raise SystemExit(1)
