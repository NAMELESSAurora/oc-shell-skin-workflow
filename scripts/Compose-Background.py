#!/usr/bin/env python3
"""Offline uniform-scale composition. Does not generate images or super-resolve them."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw


def uniform_canvas(image: Image.Image, size: tuple[int, int], scale: float,
                   left: float, top: float) -> Image.Image:
    # One inverse matrix has the same scale on X/Y; no stretched cover resize.
    matrix = (1 / scale, 0, -left / scale, 0, 1 / scale, -top / scale)
    rgba = image.convert("RGBA")
    if scale == 1 and float(left).is_integer() and float(top).is_integer():
        output = Image.new("RGBA", size, (0, 0, 0, 0))
        output.paste(rgba, (int(left), int(top)))
        return output
    # Pillow premultiplies RGBA during filtered sampling to avoid a dark fringe
    # from invisible black RGB padding. The input image remains untouched.
    return rgba.transform(size, Image.Transform.AFFINE, matrix,
                          Image.Resampling.BICUBIC, fillcolor=(0, 0, 0, 0))


def compose_images(environment: Image.Image, character: Image.Image,
                   size: tuple[int, int], character_height: float,
                   left: float, top: float, bottom_fade: float = 0.08,
                   darken_right: float = 0.0) -> tuple[Image.Image, dict]:
    width, height = size
    if not all(math.isfinite(value) for value in [width, height, character_height,
                                                left, top, bottom_fade, darken_right]):
        raise ValueError("Composition parameters must be finite numbers")
    if min(width, height, character_height) <= 0:
        raise ValueError("Canvas and character dimensions must be positive")
    if character.mode != "RGBA" or character.getchannel("A").getextrema() == (255, 255):
        raise ValueError("Character must be a real nonopaque RGBA cutout")
    if not character.getchannel("A").getbbox():
        raise ValueError("Character cannot be fully transparent")
    if not 0 <= bottom_fade < 0.5 or not 0 <= darken_right <= 0.5:
        raise ValueError("bottom-fade must be [0,0.5); darken-right must be [0,0.5]")
    char_scale = character_height / character.height
    char_width = character.width * char_scale
    if left < 0 or top < 0 or left + char_width > width or top + character_height > height:
        raise ValueError("Character canvas would be cropped; change uniform height or placement")
    env_scale = max(width / environment.width, height / environment.height)
    env_left = (width - environment.width * env_scale) / 2
    env_top = (height - environment.height * env_scale) / 2
    background = uniform_canvas(environment.convert("RGB"), size, env_scale,
                                env_left, env_top)
    if darken_right:
        shade = Image.new("L", (128, 128))
        shade.putdata([round(255 * darken_right * max(0, (x / 127 - 0.40) / 0.60)
                             * max(0, 1 - y / 127)) for y in range(128) for x in range(128)])
        shade = shade.resize(size, Image.Resampling.BILINEAR)
        background = Image.composite(Image.new("RGBA", size, (0, 0, 0, 255)),
                                     background, shade)
    foreground = uniform_canvas(character, size, char_scale, left, top)
    fade_start = top + character_height * (1 - bottom_fade)
    fade_end = top + character_height
    if bottom_fade:
        mask = Image.new("L", size, 255)
        drawing = ImageDraw.Draw(mask)
        for y in range(max(0, int(fade_start)), min(height, int(fade_end) + 1)):
            t = min(1.0, max(0.0, (y - fade_start) / (fade_end - fade_start)))
            opacity = round(255 * (1 - t * t * (3 - 2 * t)))
            drawing.line((0, y, width - 1, y), fill=opacity)
        foreground.putalpha(ImageChops.multiply(foreground.getchannel("A"), mask))
    result = Image.alpha_composite(background, foreground)
    metadata = {
        "canvas": list(size),
        "character": {"scaleX": char_scale, "scaleY": char_scale,
                      "left": left, "top": top, "width": char_width,
                      "height": character_height},
        "environment": {"scaleX": env_scale, "scaleY": env_scale,
                        "left": env_left, "top": env_top},
        "bottomFade": {"fraction": bottom_fade, "startY": fade_start, "endY": fade_end},
        "environmentOnlyRightDarkening": darken_right,
        "sourceImagesModified": False, "faceBlurOrDenoise": False,
        "rgbProcessing": "Uniform bicubic sampling only; bottom fade changes alpha only",
        "superResolutionPerformed": False,
        "humanReviewRequired": ["Complete hair/horns in source", "Natural body proportions",
                                "Seam and text readability", "No residual generation noise"],
    }
    return result, metadata


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--environment", required=True, type=Path)
    parser.add_argument("--character", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--size", default="3840x2160")
    parser.add_argument("--character-height", type=float)
    parser.add_argument("--left", type=float)
    parser.add_argument("--top", type=float)
    parser.add_argument("--bottom-fade", default=0.08, type=float)
    parser.add_argument("--darken-right", default=0.0, type=float)
    parser.add_argument("--metadata", type=Path)
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args()
    try:
        width, height = map(int, args.size.lower().split("x"))
        metadata_path = args.metadata or args.output.with_suffix(".metadata.json")
        if args.output.resolve() in {args.environment.resolve(), args.character.resolve()}:
            raise ValueError("Output must not overwrite an input mother image")
        if metadata_path.resolve() in {args.environment.resolve(), args.character.resolve(),
                                      args.output.resolve()}:
            raise ValueError("Metadata must not overwrite an input image or the output PNG")
        if args.output.exists() and not args.overwrite:
            raise ValueError("Output exists; choose a new path or use --overwrite")
        if metadata_path.exists() and not args.overwrite:
            raise ValueError("Metadata exists; choose a new path or use --overwrite")
        with Image.open(args.environment) as env, Image.open(args.character) as char:
            output, metadata = compose_images(
                env, char, (width, height),
                args.character_height if args.character_height is not None else height * 0.9,
                args.left if args.left is not None else width * 0.06,
                args.top if args.top is not None else height * 0.065,
                args.bottom_fade, args.darken_right)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.output.with_name(args.output.name + ".tmp.png")
        output.save(temporary, format="PNG")
        os.replace(temporary, args.output)
        metadata["sources"] = [{"name": source.name,
                                "sha256": hashlib.sha256(source.read_bytes()).hexdigest()}
                               for source in [args.character, args.environment]]
        metadata["output"] = {"name": args.output.name,
                              "sha256": hashlib.sha256(args.output.read_bytes()).hexdigest()}
        metadata_path.parent.mkdir(parents=True, exist_ok=True)
        metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n",
                                 encoding="utf-8")
        print(json.dumps(metadata, ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError) as exc:
        print(json.dumps({"passed": False, "error": str(exc)}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
