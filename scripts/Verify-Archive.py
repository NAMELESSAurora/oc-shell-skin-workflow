"""Validate a workflow/skin ZIP before extraction using its embedded manifest."""
from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path, PurePosixPath


def verify(path: Path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate archive entries")
        for name in names:
            parts = PurePosixPath(name).parts
            if "\\" in name or name.startswith("/") or ".." in parts or any(":" in p for p in parts):
                raise ValueError("Unsafe archive path")
        manifests = [n for n in names if n.endswith("/PACKAGE-MANIFEST.json")]
        if len(manifests) != 1:
            raise ValueError("Expected one package manifest")
        manifest_name = manifests[0]
        prefix = manifest_name[: -len("PACKAGE-MANIFEST.json")]
        manifest = json.loads(archive.read(manifest_name).decode("utf-8-sig"))
        expected = {manifest_name}
        records = manifest["files"]
        if len(records) != len({r["path"] for r in records}):
            raise ValueError("Duplicate manifest paths")
        for item in records:
            name = prefix + item["path"]
            expected.add(name)
            data = archive.read(name)
            if len(data) != item["bytes"] or hashlib.sha256(data).hexdigest().upper() != item["sha256"].upper():
                raise ValueError("Archive payload hash/size mismatch")
        if set(names) != expected:
            raise ValueError("Unmanifested archive payload")
        if archive.testzip():
            raise ValueError("CRC check failed")
    return {"status": "passed", "files": len(names), "manifestedFiles": len(records), "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper(), "zipCrcPassed": True, "zipFileShaPassed": True}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.archive)))
