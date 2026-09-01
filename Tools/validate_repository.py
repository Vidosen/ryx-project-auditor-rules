# SPDX-FileCopyrightText: 2026 Ryx Interactive
# SPDX-License-Identifier: GPL-3.0-only

"""Validate the distributable Ryx Project Auditor Rules repository."""

import argparse
from dataclasses import dataclass
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ElementTree


PACKAGE_ID = "com.ryxinteractive.project-auditor-rules"
PACKAGE_PATH = Path("Packages") / PACKAGE_ID
ANALYZER_DLL = PACKAGE_PATH / "RoslynAnalyzers" / "RyxInteractive.ProjectAuditorRules.CodeSizeAnalyzer.dll"
ANALYZER_META = ANALYZER_DLL.with_suffix(ANALYZER_DLL.suffix + ".meta")
SPDX_HEADERS = {
    ".cs": (
        "// SPDX-FileCopyrightText: 2026 Ryx Interactive\n"
        "// SPDX-License-Identifier: GPL-3.0-only\n"
    ),
    ".py": (
        "# SPDX-FileCopyrightText: 2026 Ryx Interactive\n"
        "# SPDX-License-Identifier: GPL-3.0-only\n"
    ),
    ".yml": (
        "# SPDX-FileCopyrightText: 2026 Ryx Interactive\n"
        "# SPDX-License-Identifier: GPL-3.0-only\n"
    ),
    ".yaml": (
        "# SPDX-FileCopyrightText: 2026 Ryx Interactive\n"
        "# SPDX-License-Identifier: GPL-3.0-only\n"
    ),
}


@dataclass(frozen=True)
class Finding:
    code: str
    path: Path
    message: str


def validate_repository(root: Path, expected_tag: str | None = None) -> list[Finding]:
    findings: list[Finding] = []
    manifest_path = root / PACKAGE_PATH / "package.json"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        findings.append(Finding("PACKAGE_MANIFEST", manifest_path.relative_to(root), str(error)))
        manifest = {}
    else:
        expected_metadata = {
            "name": PACKAGE_ID,
            "displayName": "Ryx Project Auditor Rules",
            "version": "0.1.0",
            "unity": "6000.0",
            "type": "tool",
            "license": "GPL-3.0-only",
            "documentationUrl": "https://github.com/Vidosen/ryx-project-auditor-rules/blob/main/Packages/com.ryxinteractive.project-auditor-rules/README.md",
            "changelogUrl": "https://github.com/Vidosen/ryx-project-auditor-rules/blob/main/Packages/com.ryxinteractive.project-auditor-rules/CHANGELOG.md",
            "licensesUrl": "https://github.com/Vidosen/ryx-project-auditor-rules/blob/main/Packages/com.ryxinteractive.project-auditor-rules/LICENSE.md",
        }
        for key, expected_value in expected_metadata.items():
            if manifest.get(key) != expected_value:
                findings.append(Finding("PACKAGE_METADATA", manifest_path.relative_to(root), f"{key} must be {expected_value}."))
        if manifest.get("dependencies", {}).get("com.unity.project-auditor") != "1.1.0":
            findings.append(Finding("PACKAGE_DEPENDENCY", manifest_path.relative_to(root), "com.unity.project-auditor must be 1.1.0."))
        author = manifest.get("author", {})
        if author.get("name") != "Ryx Interactive" or author.get("url") != "https://github.com/Vidosen":
            findings.append(Finding("PACKAGE_AUTHOR", manifest_path.relative_to(root), "Package author must be Ryx Interactive at https://github.com/Vidosen."))
        if expected_tag and expected_tag != f"v{manifest.get('version')}":
            findings.append(Finding("TAG_VERSION", manifest_path.relative_to(root), f"Tag {expected_tag} does not match package version {manifest.get('version')}."))

    root_license_path = root / "LICENSE"
    package_license_path = root / PACKAGE_PATH / "LICENSE.md"
    try:
        root_license = root_license_path.read_text(encoding="utf-8")
        package_license = package_license_path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as error:
        findings.append(Finding("LICENSE_MISSING", Path("LICENSE"), str(error)))
    else:
        if root_license != package_license:
            findings.append(Finding("LICENSE_MISMATCH", package_license_path.relative_to(root), "Package license must match the root license."))
        if "GNU GENERAL PUBLIC LICENSE" not in root_license or "Version 3" not in root_license:
            findings.append(Finding("LICENSE_INVALID", root_license_path.relative_to(root), "Expected the GPL version 3 license text."))

    package_tests_path = root / PACKAGE_PATH / "Tests"
    if package_tests_path.exists():
        findings.append(Finding("PACKAGE_TESTS", package_tests_path.relative_to(root), "Unity tests must live under Assets/Tests, outside the distributable package."))
    if not (root / "Assets" / "Tests" / "Editor").is_dir():
        findings.append(Finding("UNITY_TESTS_MISSING", Path("Assets/Tests/Editor"), "Repository-only Unity tests must live under Assets/Tests/Editor."))
    if not (root / "Packages" / "manifest.json").is_file():
        findings.append(Finding("HOST_MANIFEST_MISSING", Path("Packages/manifest.json"), "The Unity host manifest is required."))
    if not ANALYZER_DLL.is_file():
        findings.append(Finding("ANALYZER_DLL_MISSING", ANALYZER_DLL, "The committed Release analyzer DLL is required."))
    if not ANALYZER_META.is_file():
        findings.append(Finding("ANALYZER_META_MISSING", ANALYZER_META, "The analyzer PluginImporter metadata is required."))
    else:
        meta = ANALYZER_META.read_text(encoding="utf-8")
        if "- RoslynAnalyzer" not in meta:
            findings.append(Finding("ANALYZER_LABEL", ANALYZER_META.relative_to(root), "Analyzer DLL must have the RoslynAnalyzer label."))
        for platform in ("Any", "Editor", "Win64", "Linux64", "OSXUniversal"):
            if not re.search(rf"^    {re.escape(platform)}:\s*$.*?^      enabled: 0\s*$", meta, re.MULTILINE | re.DOTALL):
                findings.append(Finding("ANALYZER_PLATFORM", ANALYZER_META.relative_to(root), f"Analyzer must be disabled for {platform}."))

    cyrillic_pattern = re.compile(r"[\u0400-\u052f\u1c80-\u1c8f\u2de0-\u2dff\ua640-\ua69f]")
    legacy_brand = "ic" + "vr"
    legacy_package = "io." + legacy_brand
    legacy_pattern = re.compile(rf"(?:{re.escape(legacy_brand)}|{re.escape(legacy_package)})", re.IGNORECASE)

    for path in _iter_text_files(root):
        relative_path = path.relative_to(root)
        relative_text = relative_path.as_posix()
        if cyrillic_pattern.search(relative_text):
            findings.append(Finding("CYRILLIC_TEXT", relative_path, "Cyrillic text is not allowed in paths."))
        if legacy_pattern.search(relative_text):
            findings.append(Finding("LEGACY_BRAND", relative_path, "Legacy product branding is not allowed in paths."))

        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue

        if cyrillic_pattern.search(text):
            findings.append(Finding("CYRILLIC_TEXT", relative_path, "Cyrillic text is not allowed."))
        if legacy_pattern.search(text):
            findings.append(Finding("LEGACY_BRAND", relative_path, "Legacy product branding is not allowed."))
        expected_header = SPDX_HEADERS.get(path.suffix.lower())
        if expected_header and not text.startswith(expected_header):
            findings.append(Finding("SPDX_HEADER", relative_path, "Authored source files require a format-appropriate Ryx Interactive GPL-3.0-only SPDX header."))
        if path.suffix.lower() == ".json":
            try:
                json.loads(text)
            except json.JSONDecodeError as error:
                findings.append(Finding("INVALID_JSON", relative_path, str(error)))
        if path.suffix.lower() in {".xml", ".uxml"}:
            try:
                ElementTree.fromstring(text)
            except ElementTree.ParseError as error:
                findings.append(Finding("INVALID_XML", relative_path, str(error)))

    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate the Ryx Project Auditor Rules repository.")
    parser.add_argument("root", nargs="?", default=Path(__file__).resolve().parents[1])
    parser.add_argument("--expected-tag")
    arguments = parser.parse_args(argv)
    root = Path(arguments.root).resolve()
    findings = validate_repository(root, arguments.expected_tag)
    for finding in findings:
        print(f"{finding.code}: {finding.path}: {finding.message}")
    if findings:
        print(f"Repository validation failed with {len(findings)} finding(s).")
        return 1
    print("Repository validation passed.")
    return 0


def _iter_text_files(root: Path):
    excluded_directories = {".git", ".idea", ".vscode", "Library", "Logs", "Temp", "UserSettings", "obj", "bin"}
    for path in root.rglob("*"):
        relative_parts = path.relative_to(root).parts
        if not path.is_file() or any(part in excluded_directories for part in relative_parts):
            continue
        yield path


if __name__ == "__main__":
    raise SystemExit(main())
