#!/usr/bin/env python3
"""Prüft, dass jeder Test einen `// Testzweck:`-Kommentar trägt."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[2]

CS_TEST_DIRECTORIES = [
    ROOT / 'src' / 'core-engine-tests',
    ROOT / 'src' / 'WebApiEngine.Tests',
    ROOT / 'src' / 'FlowzerFrontend.Tests',
]
JS_TEST_DIRECTORIES = [
    ROOT / 'tests' / 'ui-smoke' / 'tests',
]

CS_TEST_ATTRIBUTE = re.compile(r'^\s*\[(Test|TestCase|TestCaseSource|Theory|Fact)\b')
CS_ATTRIBUTE = re.compile(r'^\s*\[[^\]]+\]\s*$')
JS_TEST_CALL = re.compile(r'^\s*test(?:\.(?:only|skip|fixme))?\s*\(')
PURPOSE_COMMENT = re.compile(r'^\s*//\s*Testzweck:')
IGNORED_DIRECTORIES = {
    '.git',
    'bin',
    'obj',
    'node_modules',
    'playwright-report',
    'test-results',
}


class Finding:
    def __init__(self, path: Path, line_number: int, source_line: str):
        self.path = path
        self.line_number = line_number
        self.source_line = source_line.strip()

    def __str__(self) -> str:
        relative = self.path.relative_to(ROOT)
        return f'{relative}:{self.line_number}: fehlender // Testzweck:-Kommentar vor `{self.source_line}`'


def iter_files(directories: Iterable[Path], suffixes: tuple[str, ...]) -> Iterable[Path]:
    for directory in directories:
        if not directory.exists():
            continue

        for path in sorted(directory.rglob('*')):
            if any(part in IGNORED_DIRECTORIES for part in path.parts):
                continue

            if path.is_file() and path.suffix in suffixes:
                yield path


def has_purpose_comment_before_csharp_test(lines: list[str], index: int) -> bool:
    cursor = index - 1
    while cursor >= 0:
        line = lines[cursor]
        stripped = line.strip()

        if PURPOSE_COMMENT.match(stripped):
            return True

        if not stripped or CS_ATTRIBUTE.match(line):
            cursor -= 1
            continue

        return False

    return False


def has_purpose_comment_before_js_test(lines: list[str], index: int) -> bool:
    cursor = index - 1
    while cursor >= 0:
        stripped = lines[cursor].strip()

        if PURPOSE_COMMENT.match(stripped):
            return True

        if not stripped:
            cursor -= 1
            continue

        return False

    return False


def check_csharp_tests() -> list[Finding]:
    findings: list[Finding] = []

    for path in iter_files(CS_TEST_DIRECTORIES, ('.cs',)):
        lines = path.read_text(encoding='utf-8').splitlines()
        for index, line in enumerate(lines):
            if CS_TEST_ATTRIBUTE.match(line) and not has_purpose_comment_before_csharp_test(lines, index):
                findings.append(Finding(path, index + 1, line))

    return findings


def check_js_tests() -> list[Finding]:
    findings: list[Finding] = []

    for path in iter_files(JS_TEST_DIRECTORIES, ('.js', '.ts')):
        lines = path.read_text(encoding='utf-8').splitlines()
        for index, line in enumerate(lines):
            if JS_TEST_CALL.match(line) and not has_purpose_comment_before_js_test(lines, index):
                findings.append(Finding(path, index + 1, line))

    return findings


def main() -> int:
    findings = [*check_csharp_tests(), *check_js_tests()]

    if findings:
        print('Fehlende Testzweck-Kommentare gefunden:')
        for finding in findings:
            print(f'- {finding}')
        return 1

    print('Alle gefundenen NUnit- und Playwright-Tests tragen einen // Testzweck:-Kommentar.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
