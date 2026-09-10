# SPDX-FileCopyrightText: 2026 Ryx Interactive
# SPDX-License-Identifier: GPL-3.0-only

import json
import tempfile
import unittest
from pathlib import Path

from Tools.validate_repository import PACKAGE_ID, main, validate_repository


class RepositoryValidationTests(unittest.TestCase):
    def test_valid_repository_has_no_findings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_valid_repository(root)
            self.assertEqual([], validate_repository(root, expected_tag="v0.1.1"))

    def test_package_tests_and_missing_headers_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_valid_repository(root)
            package_tests = root / "Packages" / PACKAGE_ID / "Tests"
            package_tests.mkdir()
            (package_tests / "Unexpected.cs").write_text("namespace Bad { }\n", encoding="utf-8")
            (root / "Assets" / "Tests" / "Editor" / "Example.cs").write_text("namespace Bad { }\n", encoding="utf-8")
            codes = {finding.code for finding in validate_repository(root)}
            self.assertIn("PACKAGE_TESTS", codes)
            self.assertIn("SPDX_HEADER", codes)

    def test_invalid_metadata_license_and_tag_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_valid_repository(root)
            manifest_path = root / "Packages" / PACKAGE_ID / "package.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            manifest["version"] = "0.2.0"
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            codes = {finding.code for finding in validate_repository(root, expected_tag="v0.1.1")}
            self.assertIn("PACKAGE_METADATA", codes)
            self.assertIn("TAG_VERSION", codes)

    def test_cyrillic_legacy_and_invalid_json_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_valid_repository(root)
            source = root / "Assets" / "Tests" / "Editor" / "Example.cs"
            source.write_text(
                "// SPDX-FileCopyrightText: 2026 Ryx Interactive\n"
                "// SPDX-License-Identifier: GPL-3.0-only\n"
                "namespace IC" + "VR.Legacy { }\n"
                "// " + "".join(chr(code) for code in (0x041F, 0x0440, 0x0438, 0x0432, 0x0435, 0x0442)) + "\n",
                encoding="utf-8",
            )
            (root / "broken.json").write_text("{", encoding="utf-8")
            codes = {finding.code for finding in validate_repository(root)}
            self.assertIn("CYRILLIC_TEXT", codes)
            self.assertIn("LEGACY_BRAND", codes)
            self.assertIn("INVALID_JSON", codes)

    def test_cli_exit_code_reflects_validation_result(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._write_valid_repository(root)
            self.assertEqual(0, main([str(root)]))
            (root / "legacy.txt").write_text("IC" + "VR", encoding="utf-8")
            self.assertEqual(1, main([str(root)]))

    @staticmethod
    def _write_valid_repository(root: Path):
        package = root / "Packages" / PACKAGE_ID
        package.mkdir(parents=True)
        editor_tests = root / "Assets" / "Tests" / "Editor"
        editor_tests.mkdir(parents=True)
        license_text = "GNU GENERAL PUBLIC LICENSE\nVersion 3, 29 June 2007\n"
        (root / "LICENSE").write_text(license_text, encoding="utf-8")
        (package / "LICENSE.md").write_text(license_text, encoding="utf-8")
        (package / "package.json").write_text(
            json.dumps(
                {
                    "name": PACKAGE_ID,
                    "displayName": "Ryx Project Auditor Rules",
                    "version": "0.1.1",
                    "unity": "6000.0",
                    "type": "tool",
                    "license": "GPL-3.0-only",
                    "documentationUrl": "https://github.com/Vidosen/ryx-project-auditor-rules/blob/main/Packages/com.ryxinteractive.project-auditor-rules/README.md",
                    "changelogUrl": "https://github.com/Vidosen/ryx-project-auditor-rules/blob/main/Packages/com.ryxinteractive.project-auditor-rules/CHANGELOG.md",
                    "licensesUrl": "https://github.com/Vidosen/ryx-project-auditor-rules/blob/main/Packages/com.ryxinteractive.project-auditor-rules/LICENSE.md",
                    "author": {"name": "Ryx Interactive", "url": "https://github.com/Vidosen"},
                    "dependencies": {"com.unity.project-auditor": "1.1.0"},
                }
            ),
            encoding="utf-8",
        )
        (root / "Packages" / "manifest.json").write_text('{"dependencies":{}}', encoding="utf-8")
        (package / "RoslynAnalyzers").mkdir()
        (package / "RoslynAnalyzers" / "RyxInteractive.ProjectAuditorRules.CodeSizeAnalyzer.dll").write_bytes(b"dll")
        (package / "RoslynAnalyzers" / "RyxInteractive.ProjectAuditorRules.CodeSizeAnalyzer.dll.meta").write_text(
            "labels:\n- RoslynAnalyzer\nPluginImporter:\n  platformData:\n    Any:\n      enabled: 0\n    Editor:\n      enabled: 0\n    Win64:\n      enabled: 0\n    Linux64:\n      enabled: 0\n    OSXUniversal:\n      enabled: 0\n",
            encoding="utf-8",
        )
        source = editor_tests / "ExampleTests.cs"
        source.write_text(
            "// SPDX-FileCopyrightText: 2026 Ryx Interactive\n"
            "// SPDX-License-Identifier: GPL-3.0-only\n"
            "namespace RyxInteractive.ProjectAuditorRules.Tests { }\n",
            encoding="utf-8",
        )


if __name__ == "__main__":
    unittest.main()
