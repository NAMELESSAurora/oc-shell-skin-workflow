import importlib.util
import json
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from PIL import Image

SPEC = importlib.util.spec_from_file_location("upscale", Path(__file__).resolve().parents[1] / "scripts/Upscale-Layer.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class UpscaleWorkflowTests(unittest.TestCase):
    def test_rgba_inference_uses_rgb_and_restores_original_matte(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source_path = root / "mother image.png"
            output = root / "new layer.png"
            engine = root / "engine.exe"
            engine.write_bytes(b"test fixture, never executed")
            model = root / "models-cunet"
            model.mkdir()
            source = Image.new("RGBA", (20, 12), (250, 210, 190, 0))
            source.paste((80, 140, 160, 255), (4, 3, 16, 10))
            source.save(source_path)
            original = source_path.read_bytes()

            def infer(command, **kwargs):
                with Image.open(command[command.index("-i") + 1]) as rgb:
                    self.assertEqual(rgb.mode, "RGB")
                    rgb.resize((40, 24)).save(command[command.index("-o") + 1])
                return types.SimpleNamespace(returncode=0)

            with patch.object(MODULE.subprocess, "run", side_effect=infer):
                report = MODULE.upscale(source_path, output, engine, model)
            self.assertEqual(source_path.read_bytes(), original)
            with Image.open(output) as actual:
                self.assertEqual(actual.mode, "RGBA")
                self.assertEqual(actual.size, (40, 24))
                self.assertEqual(actual.getchannel("A").tobytes(), source.getchannel("A").resize((40, 24), Image.Resampling.BICUBIC).tobytes())
            self.assertEqual(report["apiRequests"], 0)
            self.assertEqual(json.loads(output.with_suffix(".upscale.json").read_text())["status"], "processed")

    def test_check_only_does_not_execute_or_write(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "input.png"
            Image.new("RGB", (16, 16)).save(source)
            engine, model = root / "engine.exe", root / "model"
            engine.write_bytes(b"fixture")
            model.mkdir()
            output = root / "output.png"
            with patch.object(MODULE.subprocess, "run", side_effect=AssertionError("must not execute")):
                report = MODULE.upscale(source, output, engine, model, check_only=True)
            self.assertEqual(report["status"], "planned")
            self.assertFalse(output.exists())
            with self.assertRaises(ValueError):
                MODULE.upscale(source, source, engine, model)


if __name__ == "__main__":
    unittest.main()
