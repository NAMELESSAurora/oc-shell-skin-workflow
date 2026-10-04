"""Package only the documented source allowlist after privacy and SHA checks."""
from __future__ import annotations

import argparse
import datetime
import hashlib
import importlib.util
import json
import zipfile
from pathlib import Path

TOP_DIRS = {".github", "ci", "docs", "templates", "scripts", "tests", "runtime", "materials", "examples", "verification"}
TOP_FILES = {"README.md", "NOTICE.md", "requirements.txt", "pyproject.toml", ".gitignore", ".gitattributes", "RELEASE-NOTES.md"}
EXCLUDED_PARTS = {"bin", "build", "dist", "__pycache__", "node_modules", ".venv", "backups"}


def digest(data: bytes):
    return hashlib.sha256(data).hexdigest().upper()


def package(root: Path, output: Path, name: str):
    if output.exists():
        raise ValueError("Refusing to replace an existing archive")
    if not name or any(c in name for c in "/\\:") or name in {".", ".."}:
        raise ValueError("Archive name must be a simple folder name")
    spec = importlib.util.spec_from_file_location("privacy_scan", Path(__file__).with_name("Scan-Privacy.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    privacy = mod.scan(root)
    if privacy["status"] != "passed":
        raise ValueError("Privacy check failed; run Scan-Privacy.py for redacted diagnostics")
    records, blobs = [], {}
    for path in sorted(root.rglob("*")):
        rel = path.relative_to(root)
        if rel.parts[0] not in TOP_DIRS and rel.as_posix() not in TOP_FILES:
            continue
        if any(part in EXCLUDED_PARTS for part in rel.parts):
            continue
        if len(rel.parts) > 1 and rel.parts[0] == "runtime" and rel.parts[1] == "runtime":
            continue
        if path.is_symlink():
            raise ValueError("No symlink payloads")
        if not path.is_file():
            continue
        data = path.read_bytes()
        if len(data) >= 90 * 1024 * 1024:
            raise ValueError("Large assets belong in a separate release attachment")
        key = rel.as_posix()
        records.append({"path": key, "bytes": len(data), "sha256": digest(data)})
        blobs[key] = data
    if not records or "README.md" not in blobs:
        raise ValueError("Expected source repository with README.md")
    manifest = {"formatVersion": 1, "package": name, "createdUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "apiKeysIncluded": False, "accountVoiceBindingsIncluded": False, "chatMemoryIncluded": False, "files": records}
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "x", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for key, data in blobs.items():
            archive.writestr(f"{name}/{key}", data)
        archive.writestr(f"{name}/PACKAGE-MANIFEST.json", json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    with zipfile.ZipFile(output) as archive:
        if archive.testzip():
            raise ValueError("Archive CRC validation failed")
        for item in records:
            if digest(archive.read(f"{name}/{item['path']}")) != item["sha256"]:
                raise ValueError("Archive SHA validation failed")
    sha = digest(output.read_bytes())
    output.with_name(output.name + ".sha256").write_text(sha + "  " + output.name + "\n", encoding="utf-8")
    return {"status": "passed", "files": len(records) + 1, "bytes": output.stat().st_size, "sha256": sha, "zipCrcPassed": True, "zipFileShaPassed": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--name", required=True)
    args = parser.parse_args()
    print(json.dumps(package(args.root.resolve(strict=True), args.output.resolve(), args.name)))


if __name__ == "__main__":
    main()
