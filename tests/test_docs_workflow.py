import re
import unittest
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]


class DocumentationTests(unittest.TestCase):
    def test_relative_markdown_links_are_resolvable(self):
        roots = [ROOT / "README.md", ROOT / "NOTICE.md"]
        roots += list((ROOT / "docs").rglob("*.md"))
        roots += list((ROOT / "examples").rglob("*.md"))
        failures = []
        for file in roots:
            text = file.read_text(encoding="utf-8-sig")
            for link in re.findall(r"\]\(([^)]+)\)", text):
                target = unquote(link.split("#", 1)[0].strip("<>"))
                if not target or "://" in target or target.startswith("mailto:"):
                    continue
                path = (file.parent / target).resolve()
                if not path.is_relative_to(ROOT) or not path.exists():
                    failures.append(f"{file.relative_to(ROOT)}: {target}")
        self.assertEqual(failures, [], "Broken source/documentation links: " + str(failures))


if __name__ == "__main__":
    unittest.main()
