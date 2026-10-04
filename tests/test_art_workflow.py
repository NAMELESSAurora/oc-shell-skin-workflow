"""Regressions for transparency false positives and aspect-preserving composition."""
import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]


def module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), ROOT / "scripts" / filename)
    instance = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(instance)
    return instance


validator = module("Validate-Assets.py")
composer = module("Compose-Background.py")


class ArtWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def cutout(self, top=18):
        image = Image.new("RGBA", (80, 100), (0, 0, 0, 0))
        ImageDraw.Draw(image).rectangle((10, top, 69, 90), fill=(210, 80, 40, 255))
        return image

    def test_real_rgba_with_margin_and_hash_passes(self):
        f = self.root / "character.png"
        self.cutout().save(f)
        result = validator.validate_asset({"path": f.name, "trueRGBA": True,
            "size": [80, 100], "minimumMargins": {"top": 16},
            "sha256": hashlib.sha256(f.read_bytes()).hexdigest()}, self.root)
        self.assertTrue(result["passed"], result)
        self.assertEqual(result["actual"]["margins"]["top"], 18)

    def test_rgb_and_fully_opaque_rgba_are_rejected(self):
        for mode in ["RGB", "RGBA"]:
            with self.subTest(mode=mode):
                f = self.root / (mode + ".png")
                Image.new(mode, (32, 32), "white").save(f)
                self.assertFalse(validator.validate_asset({"path": f.name, "trueRGBA": True}, self.root)["passed"])

    def test_indexed_alpha_is_not_claimed_true_rgba(self):
        f = self.root / "indexed.png"
        self.cutout().quantize(colors=8).save(f)
        self.assertFalse(validator.validate_asset({"path": f.name, "trueRGBA": True}, self.root)["passed"])

    def test_clipped_top_and_replaced_asset_fail(self):
        f = self.root / "character.png"
        self.cutout(top=0).save(f)
        result = validator.validate_asset({"path": f.name, "trueRGBA": True,
            "minimumMargins": {"top": 16}, "sha256": "0" * 64}, self.root)
        self.assertFalse(result["passed"])
        self.assertTrue(any("SHA256" in e for e in result["errors"]))
        self.assertTrue(any("top" in e for e in result["errors"]))

    def test_empty_sprite_and_parent_path_fail(self):
        f = self.root / "empty.png"
        Image.new("RGBA", (10, 10), (0, 0, 0, 0)).save(f)
        self.assertFalse(validator.validate_asset({"path": f.name, "trueRGBA": True}, self.root)["passed"])
        self.assertFalse(validator.validate_asset({"path": "../outside.png"}, self.root)["passed"])
        self.assertFalse(validator.validate_asset({"path": "C:/outside.png"}, self.root)["passed"])

    def test_uniform_composition_preserves_source_and_one_to_one_pixels(self):
        char = self.cutout()
        before = char.tobytes()
        output, meta = composer.compose_images(Image.new("RGB", (117, 51), (20, 40, 60)),
            char, (200, 120), 100, 10, 5, bottom_fade=0, darken_right=0)
        self.assertEqual(char.tobytes(), before)
        self.assertEqual(output.size, (200, 120))
        self.assertEqual(output.getpixel((30, 45)), (210, 80, 40, 255))
        for name in ["character", "environment"]:
            self.assertEqual(meta[name]["scaleX"], meta[name]["scaleY"])
        self.assertFalse(meta["superResolutionPerformed"])

    def test_bottom_fade_does_not_blur_head_or_modify_input(self):
        char = self.cutout()
        before = char.tobytes()
        env = Image.new("RGB", (200, 120), (0, 0, 0))
        clean, _ = composer.compose_images(env, char, (200, 120), 100, 10, 5, bottom_fade=0)
        faded, _ = composer.compose_images(env, char, (200, 120), 100, 10, 5, bottom_fade=0.2)
        self.assertEqual(clean.getpixel((30, 45)), faded.getpixel((30, 45)))
        self.assertLess(faded.getpixel((30, 94))[0], clean.getpixel((30, 94))[0])
        self.assertEqual(char.tobytes(), before)

    def test_cropping_character_or_opaque_input_is_rejected(self):
        env = Image.new("RGB", (200, 120), "black")
        with self.assertRaises(ValueError):
            composer.compose_images(env, self.cutout(), (200, 120), 100, 10, -2)
        with self.assertRaises(ValueError):
            composer.compose_images(env, Image.new("RGBA", (30, 30), "white"), (200, 120), 40, 10, 10)

    def test_filtered_alpha_edges_do_not_pick_up_black_padding(self):
        image = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
        ImageDraw.Draw(image).rectangle((2, 2, 5, 5), fill=(255, 255, 255, 255))
        output = composer.uniform_canvas(image, (20, 20), 2.0, 2.0, 2.0)
        pixels = [output.getpixel((x, y)) for y in range(output.height) for x in range(output.width)]
        fractional = [pixel for pixel in pixels if 0 < pixel[3] < 255]
        self.assertTrue(fractional)
        self.assertTrue(all(pixel[:3] == (255, 255, 255) for pixel in fractional))

    def test_cli_rejects_metadata_overwriting_mother_image(self):
        char = self.root / "character.png"
        env = self.root / "environment.png"
        self.cutout().save(char)
        Image.new("RGB", (200, 120), "black").save(env)
        before = char.read_bytes()
        process = subprocess.run([sys.executable, str(ROOT / "scripts/Compose-Background.py"),
            "--character", str(char), "--environment", str(env), "--output", str(self.root / "out.png"),
            "--size", "200x120", "--character-height", "100", "--left", "10", "--top", "5",
            "--metadata", str(char)], capture_output=True, text=True)
        self.assertEqual(process.returncode, 1)
        self.assertEqual(char.read_bytes(), before)
        self.assertFalse((self.root / "out.png").exists())

    def test_checked_in_preview_manifest(self):
        result = validator.validate_manifest(ROOT / "examples/zhuangfangyi/assets-manifest.json", ROOT)
        self.assertTrue(result["passed"], result)


if __name__ == "__main__":
    unittest.main()
