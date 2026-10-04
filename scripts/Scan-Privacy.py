"""Fail closed on private state or credential literals; never print matched values."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SKIP_DIRS = {".git", "build", "dist", "workspaces", "local", "node_modules", ".venv", "__pycache__"}
FORBIDDEN_NAMES = {"qwen-voice.json", "qwen-usage.jsonl", "deepseek-settings.json", "chat-memory.json", "preferences.json"}
SECRET_FIELDS = {"apikey", "accesstoken", "authtoken", "password", "secret", "voiceid", "businessworkspaceid", "workspaceid"}
SECRET_PATTERNS = [
    re.compile(r"\bsk-(?:ant-|proj-)?[A-Za-z0-9_-]{24,}\b"),
    re.compile(r"\b(?:ghp_|github_pat_|gho_)[A-Za-z0-9_]{20,}\b"),
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
]
PLACEHOLDER = re.compile(r"^(?:YOUR_[A-Z0-9_]+|REPLACE_[A-Z0-9_]+|<[^>]+>|\$\{[^}]+\})$")
TEXT_SUFFIXES = {".py", ".ps1", ".psm1", ".cs", ".json", ".jsonl", ".md", ".txt", ".html", ".mjs", ".js", ".yml", ".yaml", ".cmd", ".toml"}


def files_to_scan(root: Path):
    for path in sorted(root.rglob("*")):
        rel = path.relative_to(root)
        if any(part in SKIP_DIRS for part in rel.parts):
            continue
        if path.is_symlink():
            yield path, "symlink"
        elif path.is_file():
            yield path, None


def json_secrets(value, pointer=""):
    if isinstance(value, dict):
        for key, item in value.items():
            where = f"{pointer}/{key}"
            if key.lower().replace("_", "") in SECRET_FIELDS and isinstance(item, str):
                if item and not PLACEHOLDER.fullmatch(item):
                    yield where
            yield from json_secrets(item, where)
    elif isinstance(value, list):
        for i, item in enumerate(value):
            yield from json_secrets(item, f"{pointer}/{i}")


def scan(root: Path):
    findings = []
    count = 0
    for path, issue in files_to_scan(root):
        count += 1
        rel = path.relative_to(root).as_posix()
        if issue:
            findings.append({"path": rel, "reason": issue})
            continue
        lower = path.name.lower()
        if lower in FORBIDDEN_NAMES or lower.endswith("-key.bin") or lower.startswith(".env") and lower != ".env.example":
            findings.append({"path": rel, "reason": "private-state-filename"})
        if path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        try:
            content = path.read_text(encoding="utf-8-sig")
        except (UnicodeError, OSError):
            findings.append({"path": rel, "reason": "unreadable-text"})
            continue
        for i, line in enumerate(content.splitlines(), 1):
            if any(pattern.search(line) for pattern in SECRET_PATTERNS):
                findings.append({"path": rel, "line": i, "reason": "credential-literal"})
        if path.suffix.lower() == ".json":
            try:
                value = json.loads(content)
            except json.JSONDecodeError:
                findings.append({"path": rel, "reason": "invalid-json"})
            else:
                for pointer in json_secrets(value):
                    findings.append({"path": rel, "pointer": pointer, "reason": "account-binding-or-secret"})
    return {"status": "passed" if not findings else "failed", "scannedFiles": count, "findings": findings}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    report = scan(args.root.resolve(strict=True))
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))
    raise SystemExit(0 if report["status"] == "passed" else 1)


if __name__ == "__main__":
    main()
