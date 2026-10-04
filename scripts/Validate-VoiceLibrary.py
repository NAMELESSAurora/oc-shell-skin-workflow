#!/usr/bin/env python3
"""Read-only offline validation of WAV/lines/metadata/manifest/listen links."""
from __future__ import annotations
import argparse
import json
import re
import sys
from html.parser import HTMLParser
from pathlib import Path
from voice_workflow_lib import (WorkflowError, SIDECAR_FIELDS, approved_sources, load_lines,
                               model_name, pcm_wave, read_json, role_id, safe_relative, sha256, sidecar, write_json)


class PageLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.resources, self.audio, self.blocked = [], [], []
        self.scripts, self.inside_script = [], False
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        for name in ("src", "href"):
            if name in attrs:
                self.resources.append(attrs[name])
        if tag == "audio" and "src" in attrs:
            self.audio.append(attrs["src"])
        if tag in ("iframe", "object", "embed", "base"):
            self.blocked.append(tag)
        if tag == "script" and "src" in attrs:
            self.blocked.append("external-script")
        if tag == "script":
            self.inside_script = True
    def handle_endtag(self, tag):
        if tag == "script":
            self.inside_script = False
    def handle_data(self, data):
        if self.inside_script:
            self.scripts.append(data)


def validate(root, requested_role=None):
    root = root.resolve()
    base = root / "voice-library" / "interaction-ja"
    data = read_json(base / "manifest.json")
    if not isinstance(data, dict) or set(data) - {"Version", "role", "language", "model", "audioFormat", "cloudCredentialsIncluded", "clips", "updated"}:
        raise WorkflowError("Manifest contains unknown fields or account bindings.")
    role = role_id(requested_role or data.get("role"))
    if "role" in data and data["role"] != role:
        raise WorkflowError("Manifest role mismatch.")
    if data.get("cloudCredentialsIncluded", False) is not False:
        raise WorkflowError("Manifest includes cloud credentials.")
    lines = load_lines(root / "interaction-lines-ja.json", role)
    script = read_json(root / "interaction-lines-ja.json")
    if set(script.get("Characters", {})) != {role} or script["Characters"][role] != lines:
        raise WorkflowError("Expected one role and only safe interaction line fields.")
    clips = data.get("clips")
    if not isinstance(clips, list) or len(clips) != len(lines):
        raise WorkflowError("Manifest/line counts differ.")
    clip_by_id = {}
    for clip in clips:
        if not isinstance(clip, dict) or set(clip) - (SIDECAR_FIELDS | {"context", "status", "wav"}):
            raise WorkflowError("Manifest clip contains unsupported private fields.")
        ident = clip.get("id")
        if not isinstance(ident, str) or ident in clip_by_id:
            raise WorkflowError("Duplicate/missing manifest Id.")
        clip_by_id[ident] = clip
    checks, frames = [], 0
    for line in lines:
        ident = line["Id"]
        expected = f"{role}/{ident}.wav"
        clip = clip_by_id.get(ident)
        if clip is None or clip.get("wav") != expected or clip.get("context") != line["Context"] or clip.get("status") != "ready":
            raise WorkflowError("Manifest filename/event does not match interaction line.")
        file = safe_relative(clip["wav"], base, "manifest WAV")
        clean, info = pcm_wave(file)
        if file.read_bytes() != clean:
            raise WorkflowError("Output WAV retains non-audio/private chunks; prepare a sanitized library.")
        meta = read_json(Path(str(file) + ".json"))
        if not isinstance(meta, dict) or set(meta) != SIDECAR_FIELDS:
            raise WorkflowError("Sidecar does not satisfy the safe runtime whitelist.")
        model = model_name(meta.get("model"))
        required = sidecar(role, line, model, info["sha256"])
        if meta != required:
            raise WorkflowError("WAV SHA, role or literal/delivery differs from sidecar.")
        if clip.get("sha256") != info["sha256"] or clip.get("role") != role or clip.get("ja") != line["Japanese"] or clip.get("zh") != line["Chinese"] or clip.get("delivery") != line["Delivery"]:
            raise WorkflowError("Manifest contents differ from the validated line/WAV.")
        model_name(clip.get("model"))
        frames += info["frames"]
        checks.append({"id": ident, "wav": expected, "sha256": info["sha256"], "seconds": info["seconds"], "warnings": info["warnings"]})
    actual = {p.name for p in (base / role).iterdir() if p.is_file()}
    expected_files = {line["Id"] + ext for line in lines for ext in (".wav", ".wav.json")}
    if actual != expected_files:
        raise WorkflowError("Role folder has missing or unlisted recordings/private files.")
    source_path = base / "sources.json"
    if source_path.exists():
        raw = read_json(source_path)
        if raw != approved_sources(source_path, role):
            raise WorkflowError("Source metadata retains unsupported/private fields.")
    player = (base / "listen.html").read_text(encoding="utf-8-sig")
    page = PageLinks()
    page.feed(player)
    if page.blocked or len(page.audio) != len(lines) or set(page.audio) != {f'{role}/{line["Id"]}.wav' for line in lines}:
        raise WorkflowError("Listen page has wrong audio items or unsupported embeds.")
    for link in page.resources:
        if not link or "\\" in link or ":" in link or link.startswith("/"):
            raise WorkflowError("Listen page resource must be a relative POSIX path.")
        resolved = (base / link).resolve()
        if not resolved.is_relative_to(root) or not resolved.is_file():
            raise WorkflowError("Listen page resource is absent, remote or outside the library root.")
    if "connect-src &#39;none&#39;" not in player and "connect-src 'none'" not in player:
        raise WorkflowError("Listen page must block network connections with CSP.")
    if re.search(r"\b(?:fetch|XMLHttpRequest|WebSocket)\s*\(", "\n".join(page.scripts)):
        raise WorkflowError("Listen page contains a network API.")
    forbidden_names = {"qwen-key.bin", "qwen-voice.json", "deepseek-key.bin", "api-key.bin"}
    if any(p.name.lower() in forbidden_names for p in root.rglob("*")):
        raise WorkflowError("Library root contains account configuration/key files.")
    return {"state": "passed", "role": role, "clips": len(lines), "frames": frames, "seconds": frames / 24000,
            "runtimeSidecarsSafe": True, "manifestPathsRelative": True, "listenAudioItems": len(page.audio),
            "listenResourcesExisting": True, "accountBindingsIncluded": False, "cloudRequests": 0,
            "audio": checks, "humanListeningRequired": True,
            "limits": "Format/hash checks do not prove clean speech, identity, performer consent or natural clone delivery."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--role")
    parser.add_argument("--report", type=Path, help="Optional JSON report; validation otherwise writes nothing.")
    args = parser.parse_args()
    try:
        result = validate(args.root, args.role)
        if args.report:
            write_json(args.report, result)
        print(json.dumps({k: v for k, v in result.items() if k != "audio"}, ensure_ascii=False))
        return 0
    except (WorkflowError, OSError) as exc:
        print("error: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
