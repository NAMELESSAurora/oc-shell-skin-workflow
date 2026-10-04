"""Voice workflow regression tests; synthesized PCM only, no real voices/API calls."""
import importlib.util
import json
import math
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / "scripts"
sys.path.insert(0, str(SCRIPTS))
from voice_workflow_lib import WorkflowError, read_json, write_json


def module(filename, name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


PREPARE = module("Prepare-VoiceLibrary.py", "prepare_voice")
VALIDATE = module("Validate-VoiceLibrary.py", "validate_voice")


def synthetic_wave(rate=24000, silent=False, extra=False):
    pcm = b"".join(struct.pack("<h", 0 if silent else int(4000 * math.sin(2 * math.pi * 220 * i / rate))) for i in range(int(rate * .4)))
    body = b"WAVEfmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate*2, 2, 16) + b"data" + struct.pack("<I", len(pcm)) + pcm
    if extra:
        private = b"private-test-sentinel: not-a-real-key"
        body += b"LIST" + struct.pack("<I", len(private)) + private + (b"\0" if len(private) % 2 else b"")
    return b"RIFF" + struct.pack("<I", len(body)) + body


class VoiceWorkflow(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="offline-voice-workflow-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.wav = self.root / "input-wavs"
        self.wav.mkdir()
        self.lines = [
            {"Id":"sample_touch_01","Context":"touch","Japanese":"ここにいますよ。","Chinese":"我就在这里哦。","Delivery":"自然成年日语，完整句末。"},
            {"Id":"sample_release_01","Context":"release","Japanese":"ゆっくりで大丈夫です。","Chinese":"慢慢来就好。","Delivery":"自然从容，不喘息。"},
        ]
        self.linefile = self.root / "lines.json"
        self.out = self.root / "output"
        self.write_lines()
        for line in self.lines:
            (self.wav / (line["Id"] + ".wav")).write_bytes(synthetic_wave())

    def write_lines(self):
        write_json(self.linefile, {"Version":1,"Language":"ja","Characters":{"sample":self.lines}})

    def prepare(self, source=None):
        # Socket guard makes any accidental connection in offline functions fail this test.
        with patch("socket.socket.connect", side_effect=AssertionError("Offline workflow attempted a network connection")):
            return PREPARE.prepare("sample", self.linefile, self.wav, self.out, source)

    def validate(self):
        with patch("socket.socket.connect", side_effect=AssertionError("Validator attempted a network connection")):
            return VALIDATE.validate(self.out)

    def test_roundtrip_real_runtime_layout(self):
        self.assertEqual(self.prepare()["cloudRequests"], 0)
        report = self.validate()
        self.assertEqual((report["clips"], report["frames"], report["cloudRequests"]), (2, 19200, 0))
        self.assertAlmostEqual(report["seconds"], .8)
        self.assertTrue((self.out / "interaction-lines-ja.json").is_file())
        self.assertTrue((self.out / "voice-library/interaction-ja/sample/sample_touch_01.wav.json").is_file())
        meta = read_json(self.out / "voice-library/interaction-ja/sample/sample_touch_01.wav.json")
        self.assertEqual(set(meta), {"role","id","ja","zh","delivery","model","sha256"})

    def test_preserves_pcm_and_strips_embedded_metadata(self):
        (self.wav / "sample_touch_01.wav").write_bytes(synthetic_wave(extra=True))
        result = self.prepare()
        self.assertEqual(result["audio"][0]["removedNonAudioChunks"], ["LIST"])
        output = self.out / "voice-library/interaction-ja/sample/sample_touch_01.wav"
        self.assertEqual(output.read_bytes(), synthetic_wave())
        for file in self.out.rglob("*"):
            if file.is_file(): self.assertNotIn(b"private-test-sentinel", file.read_bytes())
        self.validate()

    def test_source_metadata_whitelist_removes_bindings(self):
        source = self.root / "sources.json"
        write_json(source, {"ApprovedForPreparation":True,"RightsReview":"Approved own demo recording.","Key":"DO-NOT-COPY","WorkspaceId":"DO-NOT-COPY","Sources":[{"Title":"Local demonstration metadata","PublicUrl":"https://example.org/voice-demo","Speaker":"Example","Language":"ja","PermissionNote":"Own test sample metadata only.","VoiceId":"DO-NOT-COPY","cache":"C:/private/cache"}]})
        self.prepare(source)
        meta = (self.out / "voice-library/interaction-ja/sources.json").read_text(encoding="utf-8")
        self.assertNotIn("DO-NOT-COPY", meta)
        self.assertNotIn("cache", meta)
        self.validate()

    def test_unreviewed_sources_fail_before_writing(self):
        source = self.root / "unreviewed.json"
        write_json(source, {"ApprovedForPreparation":False,"Sources":[]})
        with self.assertRaises(WorkflowError): self.prepare(source)
        self.assertFalse(self.out.exists())

    def test_signed_source_url_is_rejected_without_echo(self):
        source = self.root / "signed.json"
        write_json(source, {"ApprovedForPreparation":True,"RightsReview":"Reviewed","Sources":[{"Title":"Demo","PublicUrl":"https://example.org/a?Signature=PRIVATE-TOKEN","PermissionNote":"Demo"}]})
        with self.assertRaises(WorkflowError) as result: self.prepare(source)
        self.assertNotIn("PRIVATE-TOKEN", str(result.exception))
        self.assertFalse(self.out.exists())

    def test_private_source_ip_rejected(self):
        source = self.root / "private-source.json"
        write_json(source, {"ApprovedForPreparation":True,"RightsReview":"Reviewed","Sources":[{"Title":"Demo","PublicUrl":"https://10.0.0.1/audio","PermissionNote":"Demo"}]})
        with self.assertRaises(WorkflowError): self.prepare(source)
        self.assertFalse(self.out.exists())

    def test_wrong_format_truncated_or_silent_rejected(self):
        for raw in (synthetic_wave(rate=16000), synthetic_wave()[:-1], synthetic_wave(silent=True), b"not a WAV"):
            with self.subTest(raw_size=len(raw)):
                (self.wav / "sample_touch_01.wav").write_bytes(raw)
                with self.assertRaises(WorkflowError): self.prepare()
                self.assertFalse(self.out.exists())

    def test_ids_cannot_escape_or_repeat(self):
        for ident in ("../escape", "other_touch_01", "sample_touch_01"):
            with self.subTest(ident=ident):
                self.lines[1]["Id"] = ident
                self.write_lines()
                with self.assertRaises(WorkflowError): self.prepare()
                self.assertFalse(self.out.exists())

    def test_no_existing_library_overwrite(self):
        self.out.mkdir()
        sentinel = self.out / "keep.txt"
        sentinel.write_text("existing user work", encoding="utf-8")
        with self.assertRaises(WorkflowError): self.prepare()
        self.assertEqual(sentinel.read_text(), "existing user work")

    def test_missing_recording_has_no_partial_output(self):
        (self.wav / "sample_release_01.wav").unlink()
        with self.assertRaises(WorkflowError): self.prepare()
        self.assertFalse(self.out.exists())

    def test_wav_tamper_detected(self):
        self.prepare()
        path = self.out / "voice-library/interaction-ja/sample/sample_touch_01.wav"
        raw = bytearray(path.read_bytes());raw[-2] ^= 1;path.write_bytes(raw)
        with self.assertRaises(WorkflowError): self.validate()

    def test_sidecar_literal_and_private_binding_rejected(self):
        self.prepare()
        path = self.out / "voice-library/interaction-ja/sample/sample_touch_01.wav.json"
        original = read_json(path)
        for bad in ({**original,"ja":"違う文です。"}, {**original,"voiceId":"not-for-export"}):
            write_json(path,bad)
            with self.assertRaises(WorkflowError): self.validate()

    def test_manifest_absolute_path_and_extra_clip_rejected(self):
        self.prepare()
        path = self.out / "voice-library/interaction-ja/manifest.json"
        original = read_json(path)
        bad = json.loads(json.dumps(original));bad["clips"][0]["wav"] = "C:/private/cache.wav"
        write_json(path,bad)
        with self.assertRaises(WorkflowError): self.validate()
        bad = json.loads(json.dumps(original));bad["clips"].append(bad["clips"][0])
        write_json(path,bad)
        with self.assertRaises(WorkflowError): self.validate()

    def test_listen_escapes_dialogue_and_blocks_remote_resource(self):
        self.lines[0]["Japanese"] = '<img src=x onerror="alert(1)"><script>fetch("example")</script>という例です。'
        self.write_lines();self.prepare();self.validate()
        path = self.out / "voice-library/interaction-ja/listen.html"
        page = path.read_text(encoding="utf-8")
        self.assertIn("&lt;img",page)
        path.write_text(page.replace('src="sample/sample_touch_01.wav"','src="https://example.org/private.wav"'),encoding="utf-8")
        with self.assertRaises(WorkflowError): self.validate()

    def test_cli_roundtrip_and_failure_exit_codes(self):
        cmd = [sys.executable,str(SCRIPTS / "Prepare-VoiceLibrary.py"),"--role","sample","--lines",str(self.linefile),"--wav-dir",str(self.wav),"--output",str(self.out)]
        run = subprocess.run(cmd,capture_output=True,text=True,encoding="utf-8")
        self.assertEqual(run.returncode,0,run.stderr)
        check = subprocess.run([sys.executable,str(SCRIPTS / "Validate-VoiceLibrary.py"),"--root",str(self.out)],capture_output=True,text=True,encoding="utf-8")
        self.assertEqual(check.returncode,0,check.stderr)
        self.assertEqual(json.loads(check.stdout)["cloudRequests"],0)
        (self.out / "qwen-key.bin").write_bytes(b"not a real key")
        check = subprocess.run([sys.executable,str(SCRIPTS / "Validate-VoiceLibrary.py"),"--root",str(self.out)],capture_output=True,text=True,encoding="utf-8")
        self.assertEqual(check.returncode,2)

    @unittest.skipUnless(shutil.which("powershell.exe"), "Windows PowerShell unavailable")
    def test_cloud_wrapper_default_dry_and_fail_closed(self):
        cmd = ["powershell.exe","-NoProfile","-ExecutionPolicy","Bypass","-File",str(SCRIPTS / "Invoke-VoiceJob.ps1"),"-RuntimeRoot",str(self.root),"-Role","sample"]
        run = subprocess.run(cmd,capture_output=True,text=True)
        self.assertEqual(run.returncode,0,run.stderr)
        self.assertEqual(json.loads(run.stdout)["actualCloudRequests"],0)
        blocked = subprocess.run(cmd + ["-EnableCloud"],capture_output=True,text=True)
        self.assertNotEqual(blocked.returncode,0)
        blocked = subprocess.run(cmd + ["-EnableCloud","-ReferenceReviewed"],capture_output=True,text=True)
        self.assertNotEqual(blocked.returncode,0)  # No binary; cannot launch the paid job.


if __name__ == "__main__":
    unittest.main()
