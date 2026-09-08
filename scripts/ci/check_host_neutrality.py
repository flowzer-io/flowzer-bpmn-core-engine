#!/usr/bin/env python3
"""Verhindert konkrete Host-Anwendungsnamen im ausführbaren Flowzer-Produktcode."""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE_ROOTS = [ROOT / "src", ROOT / "packages" / "flowzer-sdk" / "src", ROOT / "examples"]
SUFFIXES = {
    ".bpmn", ".cs", ".csproj", ".js", ".json", ".jsx", ".mjs", ".ts",
    ".tsx", ".xml", ".yaml", ".yml",
}
IGNORED = {"bin", "obj", "dist", "node_modules"}
# Die konkrete Integration bleibt Eigentum des Hosts. Diese Sperre ist absichtlich
# nur eine Architekturgrenze des Produktcodes; Roadmap und Integrationsdokumente dürfen
# die Entscheidung weiterhin erklären.
FORBIDDEN_NAMES = {"tickytask"}


def main() -> int:
    findings: list[str] = []
    for source_root in SOURCE_ROOTS:
        for path in source_root.rglob("*"):
            if not path.is_file() or path.suffix not in SUFFIXES:
                continue
            if any(part in IGNORED for part in path.parts):
                continue
            content = path.read_text(encoding="utf-8", errors="ignore").lower()
            for name in FORBIDDEN_NAMES:
                if name in content:
                    findings.append(f"{path.relative_to(ROOT)}: konkrete Host-Referenz '{name}'")

    if findings:
        print("Flowzer-Produktcode ist nicht hostneutral:", file=sys.stderr)
        print("\n".join(findings), file=sys.stderr)
        return 1
    print("Flowzer-Produktcode enthält keine konkrete Host-Anwendungsreferenz.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
