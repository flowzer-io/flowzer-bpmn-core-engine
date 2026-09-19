#!/usr/bin/env python3
"""Erzeugt THIRD-PARTY-NOTICES.md deterministisch aus den csproj- und package.json-Dateien.

Quellen:
  - .NET: alle `<PackageReference>`-Einträge in den *.csproj der Solution. Die Lizenz
    wird aus dem lokalen NuGet-Cache gelesen (`~/.nuget/packages/<id>/<version>/<id>.nuspec`,
    überschreibbar über die Umgebungsvariable NUGET_PACKAGES). Das Paket muss also vorher
    per `dotnet restore core-engine.sln` aufgelöst worden sein.
  - npm: die direkten Abhängigkeiten (`dependencies` + `devDependencies`) der package.json
    in den unten gelisteten Projektverzeichnissen. Die Lizenz wird aus
    `node_modules/<paket>/package.json` gelesen. Die Verzeichnisse müssen also vorher per
    `npm ci` installiert worden sein. Eigene Workspace-Pakete (`@flowzer/*`) sind keine
    Drittanbieter-Abhängigkeiten und werden ausgelassen.

Das Skript ist rein lesend, deterministisch (keine Zeitstempel, stabile Sortierung) und
ohne Argumente aufrufbar:

    python3 scripts/ci/generate-third-party-notices.py

Die CI erzeugt die Datei neu und vergleicht sie per `git diff --exit-code` gegen den
eingecheckten Stand (Drift-Check, analog zum OpenAPI-Snapshot-Check).
"""

from __future__ import annotations

import json
import os
import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT_FILE = ROOT / 'THIRD-PARTY-NOTICES.md'

NUGET_CACHE = Path(os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget' / 'packages')))

NPM_PROJECT_DIRS = [
    ROOT / 'src' / 'FlowzerConsole',
    ROOT / 'packages' / 'flowzer-sdk',
    ROOT / 'packages' / 'flowzer-react',
]

IGNORED_DIRECTORIES = {'.git', 'bin', 'obj', 'node_modules'}

# Lizenzen, die üblicherweise unproblematisch mit einer MPL-2.0-Nutzung dieses Projekts
# zusammenspielen (permissiv, oder MPL-2.0 selbst). Alles andere wird als auffällig markiert
# und muss manuell geprüft werden.
UNPROBLEMATIC_LICENSES = {
    'MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', '0BSD',
    'OFL-1.1', 'CC0-1.0', 'Unlicense', 'MPL-2.0', 'PostgreSQL', 'Python-2.0',
}

# Für Pakete, bei denen das nuspec/package.json-Feld keine eindeutige SPDX-Kennung liefert
# (Lizenzdatei statt Ausdruck, oder Sonderfall), aber die Lizenz manuell anhand der im Paket
# mitgelieferten Lizenzdatei verifiziert wurde. Quelle jeweils im Kommentar.
KNOWN_FILE_LICENSES: dict[tuple[str, str], str] = {
    # NuGet, license type="file": eingebettetes License.txt ist die MIT-Lizenz von
    # Microsoft (ClearScript selbst); mitgelieferte V8/ICU/etc.-Unterlizenzen sind
    # BSD-/MIT-artig und liegen im Paket unter licenses/.
    ('nuget', 'microsoft.clearscript.v8'): 'MIT (siehe eingebettete License.txt)',
    ('nuget', 'microsoft.clearscript.v8.native.osx-arm64'): 'MIT (siehe eingebettete License.txt)',
}

# Pakete mit einer Lizenz, die besondere Aufmerksamkeit braucht (nicht automatisch als
# "unproblematisch" einstufbar), samt Begründung für den Auffälligkeiten-Abschnitt.
FLAGGED_NOTES: dict[tuple[str, str], str] = {
    ('npm', 'bpmn-js'): (
        'Eigene "bpmn.io"-Lizenz (MIT-artig, im Paket als LICENSE hinterlegt) mit einer '
        'Zusatzbedingung: Das eingeblendete bpmn.io-Wasserzeichen im gerenderten Diagramm '
        'darf nicht entfernt oder verdeckt werden. Das ist keine Lizenzkollision, aber eine '
        'UI-Pflicht, die die Konsole einhalten muss.'
    ),
}


@dataclass(frozen=True)
class Dependency:
    ecosystem: str  # "nuget" | "npm"
    name: str
    version: str
    license: str
    flagged: bool
    note: str = ''
    sources: tuple[str, ...] = field(default_factory=tuple)


def iter_csproj_files() -> list[Path]:
    result = []
    for path in ROOT.rglob('*.csproj'):
        if any(part in IGNORED_DIRECTORIES for part in path.relative_to(ROOT).parts):
            continue
        result.append(path)
    return sorted(result)


PACKAGE_REFERENCE_VERSION_ATTR = re.compile(
    r'<PackageReference\s+[^>]*Include="(?P<id>[^"]+)"[^>]*Version="(?P<version>[^"]+)"',
)
PACKAGE_REFERENCE_VERSION_CHILD = re.compile(
    r'<PackageReference\s+[^>]*Include="(?P<id>[^"]+)"[^>]*>\s*'
    r'<Version>(?P<version>[^<]+)</Version>',
)


def parse_package_references(csproj: Path) -> list[tuple[str, str]]:
    text = csproj.read_text(encoding='utf-8')
    refs: list[tuple[str, str]] = []
    for match in PACKAGE_REFERENCE_VERSION_ATTR.finditer(text):
        refs.append((match.group('id'), match.group('version')))
    seen_ids = {r[0] for r in refs}
    for match in PACKAGE_REFERENCE_VERSION_CHILD.finditer(text):
        if match.group('id') not in seen_ids:
            refs.append((match.group('id'), match.group('version')))
    return refs


def read_nuspec_license(package_id: str, version: str) -> tuple[str, bool, str]:
    """Liefert (Lizenz-Text, auffaellig, Zusatznotiz) fuer ein NuGet-Paket."""
    key = ('nuget', package_id.lower())
    nuspec_path = NUGET_CACHE / package_id.lower() / version / f'{package_id.lower()}.nuspec'
    if not nuspec_path.exists():
        return (
            'NICHT ERMITTELBAR (Paket fehlt im lokalen NuGet-Cache; '
            '"dotnet restore core-engine.sln" vorher ausführen)',
            True,
            '',
        )

    root = ET.parse(nuspec_path).getroot()
    ns = ''
    if root.tag.startswith('{'):
        ns = root.tag.split('}')[0] + '}'
    metadata = root.find(f'{ns}metadata')
    if metadata is None:
        return ('UNBEKANNT (kein <metadata> im nuspec)', True, '')

    def find_text(tag: str) -> str | None:
        el = metadata.find(f'{ns}{tag}')
        return el.text.strip() if el is not None and el.text else None

    license_el = metadata.find(f'{ns}license')
    license_type = license_el.get('type') if license_el is not None else None
    license_value = (license_el.text or '').strip() if license_el is not None else None

    note = FLAGGED_NOTES.get(key, '')

    if license_type == 'expression' and license_value:
        flagged = license_value not in UNPROBLEMATIC_LICENSES
        return (license_value, flagged, note)

    if key in KNOWN_FILE_LICENSES:
        resolved = KNOWN_FILE_LICENSES[key]
        # Manuell verifizierte MIT/BSD-artige Datei-Lizenzen gelten als unproblematisch,
        # es sei denn es liegt zusätzlich eine explizite Auffälligkeits-Notiz vor.
        return (resolved, bool(note), note)

    if license_type == 'file' and license_value:
        return (
            f'siehe mitgelieferte Lizenzdatei "{license_value}" im Paket (nicht automatisch '
            'als SPDX-Kennung bestimmbar)',
            True,
            note,
        )

    license_url = find_text('licenseUrl')
    if license_url:
        return (f'siehe {license_url}', True, note)

    return ('UNBEKANNT', True, note)


def collect_nuget_dependencies() -> list[Dependency]:
    by_key: dict[tuple[str, str], set[str]] = {}
    for csproj in iter_csproj_files():
        rel = csproj.relative_to(ROOT).as_posix()
        for package_id, version in parse_package_references(csproj):
            by_key.setdefault((package_id, version), set()).add(rel)

    deps: list[Dependency] = []
    for (package_id, version), sources in by_key.items():
        license_text, flagged, note = read_nuspec_license(package_id, version)
        deps.append(
            Dependency(
                ecosystem='nuget',
                name=package_id,
                version=version,
                license=license_text,
                flagged=flagged,
                note=note,
                sources=tuple(sorted(sources)),
            )
        )
    return sorted(deps, key=lambda d: d.name.lower())


def read_package_json_deps(package_json: Path) -> list[str]:
    data = json.loads(package_json.read_text(encoding='utf-8'))
    names = set(data.get('dependencies', {}).keys()) | set(data.get('devDependencies', {}).keys())
    # Eigene Workspace-Pakete sind kein Drittanbieter-Code.
    return sorted(n for n in names if not n.startswith('@flowzer/'))


def read_node_modules_license(project_dir: Path, package_name: str) -> tuple[str, str, bool, str]:
    pkg_json = project_dir / 'node_modules' / package_name / 'package.json'
    if not pkg_json.exists():
        return (
            'UNBEKANNT',
            'NICHT ERMITTELBAR (node_modules fehlt; "npm ci" vorher ausführen)',
            True,
            '',
        )
    data = json.loads(pkg_json.read_text(encoding='utf-8'))
    version = data.get('version', 'UNBEKANNT')
    license_field = data.get('license')
    if not license_field:
        licenses_field = data.get('licenses')
        if licenses_field:
            license_field = ', '.join(
                lic.get('type', 'UNBEKANNT') if isinstance(lic, dict) else str(lic)
                for lic in licenses_field
            )
        else:
            license_field = 'UNBEKANNT'

    key = ('npm', package_name)
    note = FLAGGED_NOTES.get(key, '')
    flagged = license_field not in UNPROBLEMATIC_LICENSES or bool(note)
    return (version, license_field, flagged, note)


def collect_npm_dependencies() -> list[Dependency]:
    by_key: dict[tuple[str, str, str], set[str]] = {}
    versions: dict[tuple[str, str, str], str] = {}
    flags: dict[tuple[str, str, str], tuple[bool, str]] = {}

    for project_dir in NPM_PROJECT_DIRS:
        package_json = project_dir / 'package.json'
        if not package_json.exists():
            continue
        rel_project = project_dir.relative_to(ROOT).as_posix()
        for name in read_package_json_deps(package_json):
            version, license_field, flagged, note = read_node_modules_license(project_dir, name)
            key = (name, version, license_field)
            by_key.setdefault(key, set()).add(rel_project)
            versions[key] = version
            flags[key] = (flagged, note)

    deps: list[Dependency] = []
    for (name, version, license_field), sources in by_key.items():
        flagged, note = flags[(name, version, license_field)]
        deps.append(
            Dependency(
                ecosystem='npm',
                name=name,
                version=version,
                license=license_field,
                flagged=flagged,
                note=note,
                sources=tuple(sorted(sources)),
            )
        )
    return sorted(deps, key=lambda d: (d.name.lower(), d.version))


def render_table(deps: list[Dependency], source_label: str) -> str:
    lines = [
        f'| Paket | Version | Lizenz | {source_label} |',
        '|---|---|---|---|',
    ]
    for dep in deps:
        marker = ' ⚠️' if dep.flagged else ''
        sources = ', '.join(f'`{s}`' for s in dep.sources)
        lines.append(f'| {dep.name}{marker} | {dep.version} | {dep.license} | {sources} |')
    return '\n'.join(lines)


def render_notes(deps: list[Dependency]) -> str:
    notes = [d for d in deps if d.note]
    if not notes:
        return ''
    lines = ['## Auffälligkeiten im Detail', '']
    for dep in sorted(notes, key=lambda d: (d.ecosystem, d.name.lower())):
        lines.append(f'- **{dep.name}** ({dep.ecosystem}, {dep.version}): {dep.note}')
    lines.append('')
    return '\n'.join(lines)


def render(nuget_deps: list[Dependency], npm_deps: list[Dependency]) -> str:
    flagged_count = sum(1 for d in nuget_deps + npm_deps if d.flagged)
    parts = [
        '# Abhängigkeits- und Lizenzhinweise (Third-Party Notices)',
        '',
        'Diese Datei wird automatisch durch',
        '[`scripts/ci/generate-third-party-notices.py`](scripts/ci/generate-third-party-notices.py)',
        'aus den direkten Abhängigkeiten der Solution und der npm-Projekte erzeugt.',
        '**Nicht von Hand bearbeiten** — Änderungen werden beim nächsten Lauf des Skripts',
        'überschrieben. Die CI erzeugt die Datei neu und lehnt Abweichungen',
        '(`git diff --exit-code`) ab, damit sie nicht veraltet.',
        '',
        'Flowzer BPMN Core Engine selbst steht unter der Mozilla Public License 2.0',
        '(siehe [LICENSE](LICENSE)); diese Datei dokumentiert ausschließlich die Lizenzen',
        '**direkter** Drittanbieter-Abhängigkeiten. Transitive Abhängigkeiten sind hier nicht',
        'aufgeführt. Eigene Workspace-Pakete (`@flowzer/react`, `@flowzer/sdk`) sind kein',
        'Drittanbieter-Code und daher ausgenommen.',
        '',
        f'Ein ⚠️ markiert Pakete, deren Lizenz manuell geprüft werden sollte (nicht ohne',
        'Weiteres als unproblematisch für eine MPL-2.0-Nutzung eingestuft, unklar oder nicht',
        f'automatisch ermittelbar). Aktuell {flagged_count} von {len(nuget_deps) + len(npm_deps)}',
        'Einträgen.',
        '',
        '## .NET (NuGet)',
        '',
        'Direkte `<PackageReference>`-Einträge aus allen `*.csproj` der Solution, Lizenz aus',
        'den nuspec-Metadaten des lokalen NuGet-Cache.',
        '',
        render_table(nuget_deps, 'Verwendet in'),
        '',
        '## JavaScript/TypeScript (npm)',
        '',
        'Direkte `dependencies`/`devDependencies` aus `src/FlowzerConsole/package.json`,',
        '`packages/flowzer-sdk/package.json` und `packages/flowzer-react/package.json`,',
        'Lizenz aus dem jeweils installierten `node_modules/<paket>/package.json`.',
        '',
        render_table(npm_deps, 'Projekt(e)'),
        '',
    ]
    notes = render_notes(nuget_deps + npm_deps)
    if notes:
        parts.append(notes)
    parts.append(
        'Weiterer Kontext zur Meldung von Sicherheitslücken in einer dieser Abhängigkeiten '
        'oder im eigenen Code steht in [SECURITY.md](SECURITY.md).'
    )
    parts.append('')
    return '\n'.join(parts)


def main() -> int:
    nuget_deps = collect_nuget_dependencies()
    npm_deps = collect_npm_dependencies()
    content = render(nuget_deps, npm_deps)
    OUTPUT_FILE.write_text(content, encoding='utf-8')
    flagged = [d for d in nuget_deps + npm_deps if d.flagged]
    print(f'{OUTPUT_FILE.relative_to(ROOT)} geschrieben: '
          f'{len(nuget_deps)} NuGet- und {len(npm_deps)} npm-Abhängigkeiten, '
          f'{len(flagged)} davon markiert.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
