#!/usr/bin/env python3
"""Create a fresh, metadata-safe offline library. Never calls an API."""
from __future__ import annotations
import argparse
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
from voice_workflow_lib import (WorkflowError, approved_sources, canonical_script, listen_html,
                               load_lines, manifest, model_name, pcm_wave, role_id, sidecar, write_json)


def prepare(role, lines_path, wav_dir, output, source_meta=None, model="offline-import", portrait=None):
    role_id(role)
    model_name(model)
    lines = load_lines(lines_path, role)
    sources = approved_sources(source_meta, role)
    if output.is_symlink() or (output.exists() and (not output.is_dir() or any(output.iterdir()))):
        raise WorkflowError("Output must be a new or empty folder; existing libraries/configuration are never overwritten.")
    prepared, records = [], []
    for line in lines:
        candidate = wav_dir / (line["Id"] + ".wav")
        if not candidate.is_file():
            raise WorkflowError("Missing WAV for an interaction Id; filenames must match Id exactly.")
        if not candidate.resolve().is_relative_to(wav_dir.resolve()):
            raise WorkflowError("A source WAV symlink escapes the supplied WAV folder.")
        clean, info = pcm_wave(candidate)
        prepared.append(clean)
        records.append({"id": line["Id"], "sourceFile": line["Id"] + ".wav", **info})
    avatar = None
    if portrait is not None:
        if portrait.stat().st_size > 20 * 1024 * 1024:
            raise WorkflowError("Portrait exceeds 20 MiB.")
        avatar = portrait.read_bytes()
        if not avatar.startswith(b"\x89PNG\r\n\x1a\n"):
            raise WorkflowError("Optional portrait must be an approved PNG; this tool does not edit images.")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".offline-voice-build-", dir=output.parent) as temporary:
        stage = Path(temporary) / "library"
        base = stage / "voice-library" / "interaction-ja"
        (base / role).mkdir(parents=True)
        write_json(stage / "interaction-lines-ja.json", canonical_script(role, lines))
        for line, clean, record in zip(lines, prepared, records):
            file = base / role / (line["Id"] + ".wav")
            file.write_bytes(clean)
            write_json(Path(str(file) + ".json"), sidecar(role, line, model, record["sha256"]))
        write_json(base / "manifest.json", manifest(role, lines, records, model))
        if sources is not None:
            write_json(base / "sources.json", sources)
        if avatar is not None:
            (stage / "assets").mkdir()
            (stage / "assets" / "voice-avatar.png").write_bytes(avatar)
        (base / "listen.html").write_text(listen_html(role, lines, avatar is not None), encoding="utf-8")
        report = {"state": "prepared", "role": role, "clips": len(lines), "frames": sum(r["frames"] for r in records),
                  "seconds": sum(r["frames"] for r in records) / 24000, "audio": records,
                  "sourceReviewRecorded": sources is not None, "pcmSamplesUnchanged": True,
                  "nonAudioWavMetadataRemoved": True, "cloudRequests": 0,
                  "cloudBindingsIncluded": False, "humanListeningRequired": True,
                  "limits": "No denoising, speaker verification, rights adjudication or cloud clone evaluation performed."}
        write_json(stage / "verification" / "voice-preparation.json", report)
        if output.exists():
            output.rmdir()  # Only an empty folder established above; never recursive deletion.
        os.replace(stage, output)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--role", required=True)
    parser.add_argument("--lines", type=Path, required=True)
    parser.add_argument("--wav-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--approved-source-meta", type=Path)
    parser.add_argument("--model", default="offline-import", help="Metadata label only; does not select or invoke an API.")
    parser.add_argument("--portrait", type=Path, help="Optional approved PNG, copied without editing.")
    args = parser.parse_args()
    try:
        report = prepare(args.role, args.lines.resolve(), args.wav_dir.resolve(), args.output.absolute(),
                         args.approved_source_meta, args.model, args.portrait)
        print(json.dumps({"state": report["state"], "role": report["role"], "clips": report["clips"],
                          "seconds": report["seconds"], "cloudRequests": 0, "output": str(args.output.absolute())}))
        return 0
    except (WorkflowError, OSError) as exc:
        print("error: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
