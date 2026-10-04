import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), SCRIPTS / (name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class ReleaseWorkflowTests(unittest.TestCase):
    def test_secret_scan_redacts_value_and_accepts_empty_binding(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "settings.example.json").write_text(json.dumps({"apiKey": "", "voiceId": "YOUR_VOICE_ID"}))
            self.assertEqual(load("Scan-Privacy").scan(root)["status"], "passed")
            value = "sk-" + "a" * 32
            (root / "settings.example.json").write_text(json.dumps({"apiKey": value}))
            result = load("Scan-Privacy").scan(root)
            self.assertEqual(result["status"], "failed")
            self.assertNotIn(value, json.dumps(result))

    def test_package_excludes_build_outputs_and_rejects_tampering(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "src"
            root.mkdir()
            (root / "README.md").write_text("example")
            (root / "build").mkdir()
            (root / "build" / "private.log").write_text("excluded")
            output = Path(temp) / "source.zip"
            load("Package-Workflow").package(root, output, "source")
            self.assertEqual(load("Verify-Archive").verify(output)["files"], 2)
            with zipfile.ZipFile(output) as archive:
                payloads = {name: archive.read(name) for name in archive.namelist()}
            payloads["source/README.md"] = b"tampered"
            bad = Path(temp) / "bad.zip"
            with zipfile.ZipFile(bad, "w") as archive:
                for name, data in payloads.items():
                    archive.writestr(name, data)
            with self.assertRaises(ValueError):
                load("Verify-Archive").verify(bad)

    def test_refuses_private_state_and_output_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "src"
            root.mkdir()
            (root / "README.md").write_text("example")
            (root / "qwen-key.bin").write_bytes(b"private")
            with self.assertRaises(ValueError):
                load("Package-Workflow").package(root, Path(temp) / "x.zip", "x")
            existing = Path(temp) / "exists.zip"
            existing.write_bytes(b"existing")
            with self.assertRaises(ValueError):
                load("Package-Workflow").package(root, existing, "x")
            self.assertEqual(existing.read_bytes(), b"existing")


if __name__ == "__main__":
    unittest.main()
