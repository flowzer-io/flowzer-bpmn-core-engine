#!/usr/bin/env python3
"""Wertet trx-Testergebnisse aus und schlaegt fehl, sobald ein Test uebersprungen wurde.

Zweck: In der CI darf kein Test still ausfallen. Assert.Ignore (fehlendes Docker fuer die
PostgreSQL-Container, fehlende native V8-Bibliothek, kein freier Port) landet im trx als
"NotExecuted"; ohne diese Auswertung bliebe der Lauf gruen, obwohl ganze Fixtures nicht
geprueft wurden. Das Skript nennt jeden uebersprungenen Test samt Begruendung.

Ausnahme: Tests mit [Explicit] (z. B. der Generatorlauf der MIWG-Erwartungen) meldet der
NUnit-Adapter ebenfalls als "NotExecuted". Sie sind absichtlich nicht Teil des normalen
Laufs und werden deshalb toleriert. Welche Tests das sind, liest das Skript aus den
Testquellen unter src/ (Attribut [Explicit ...] vor einer Testmethode), damit keine
Liste im Skript gepflegt werden muss.

Aufruf: python3 scripts/ci/check_skipped_tests.py [--source <Quellverzeichnis>] <Verzeichnis-oder-trx-Datei> [...]
"""

from __future__ import annotations

import pathlib
import re
import sys
import xml.etree.ElementTree as ET

TRX_NAMESPACE = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
COUNTED_AS_RUN = {"Passed", "Failed"}
DEFAULT_SOURCE = pathlib.Path(__file__).resolve().parents[2] / "src"

# [Explicit] bzw. [Explicit("...")], danach (ueber weitere Attribute und Kommentare hinweg)
# die naechste Methodendeklaration; deren Name ist der Testname im trx.
EXPLICIT_PATTERN = re.compile(
    r"\[Explicit(?:\([^)]*\))?\][\s\S]*?"
    r"(?:public|internal|protected|private)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\]?,. ]+?\s+(\w+)\s*\(",
)


def explicit_tests(source: pathlib.Path) -> set[str]:
    """Namen aller Testmethoden mit [Explicit]-Attribut unterhalb von source."""
    names: set[str] = set()
    if not source.is_dir():
        return names
    for path in source.rglob("*.cs"):
        if "node_modules" in path.parts or "bin" in path.parts or "obj" in path.parts:
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        if "[Explicit" not in text:
            continue
        names.update(EXPLICIT_PATTERN.findall(text))
    return names


def find_trx_files(arguments: list[str]) -> list[pathlib.Path]:
    files: list[pathlib.Path] = []
    for argument in arguments:
        path = pathlib.Path(argument)
        if path.is_dir():
            files.extend(sorted(path.rglob("*.trx")))
        elif path.suffix == ".trx" and path.is_file():
            files.append(path)
        else:
            raise SystemExit(f"Kein trx-Verzeichnis oder keine trx-Datei: {argument}")
    return files


def skipped_results(
    trx: pathlib.Path, tolerated: set[str]
) -> tuple[int, list[tuple[str, str, str]], list[str]]:
    """Liefert (Gesamtzahl, [(Testname, Ergebnis, Begruendung), ...], tolerierte Explicit-Tests)."""
    root = ET.parse(trx).getroot()
    namespace = {"t": TRX_NAMESPACE}
    total = 0
    skipped: list[tuple[str, str, str]] = []
    explicit: list[str] = []
    for result in root.iterfind(".//t:Results/t:UnitTestResult", namespace):
        total += 1
        outcome = result.attrib.get("outcome", "")
        if outcome in COUNTED_AS_RUN:
            continue
        name = result.attrib.get("testName", "?")
        if name.split("(")[0] in tolerated:
            explicit.append(name)
            continue
        message = result.find("./t:Output/t:ErrorInfo/t:Message", namespace)
        reason = (message.text or "").strip() if message is not None else ""
        skipped.append((name, outcome, reason))
    return total, skipped, explicit


def main(arguments: list[str]) -> int:
    source = DEFAULT_SOURCE
    if arguments[:1] == ["--source"]:
        if len(arguments) < 2:
            raise SystemExit("--source verlangt ein Verzeichnis.")
        source = pathlib.Path(arguments[1])
        arguments = arguments[2:]

    if not arguments:
        print(__doc__)
        return 2

    tolerated = explicit_tests(source)
    files = find_trx_files(arguments)
    if not files:
        print("Keine trx-Dateien gefunden; ohne Testergebnisse ist keine Aussage moeglich.")
        return 1

    failures = 0
    for trx in files:
        total, skipped, explicit = skipped_results(trx, tolerated)
        state = "OK" if not skipped else "FEHLER"
        print(f"{state}: {trx} - {total} Ergebnisse, {len(skipped)} uebersprungen")
        for name in explicit:
            print(f"  - {name}: [Explicit], absichtlich nicht Teil des Laufs (toleriert)")
        for name, outcome, reason in skipped:
            print(f"  - {name} [{outcome}]: {reason or '(keine Begruendung)'}")
        failures += len(skipped)

    if failures:
        print(
            f"\n{failures} Test(s) wurden uebersprungen. In der CI darf kein Test still "
            "ausfallen; Ursache auf dem Runner beheben (Docker, V8-Bibliothek, Ports)."
        )
        return 1

    print("\nKein Test uebersprungen.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
