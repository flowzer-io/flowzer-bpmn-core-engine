"""Nur Feeder-Fakes: ein öffentlicher Restore, ein kleiner Build, niemals gebundene Liveadapter."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'tests/runtime-bootstrap-smoke/SmokeFeeder.csproj'
spec = importlib.util.spec_from_file_location('shared_stdin_ci', ROOT/'scripts/ci/run_startup_secret_contracts.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)

CSHARP_IDS = ('packet-short-reads-eof', 'packet-eof-size-timeout', 'packet-schema', 'packet-clock',
    'sequence-once', 'roles-least-privilege', 'migration-failure', 'child-projections', 'connection-builder',
    'readiness-components', 'output-sentinel-encodings', 'output-bounds-and-terminal', 'cleanup-proof',
    'partial-start-fails-safe', 'entrypoint-closed')
SUMMARY = 'CONTRACTS total=15 passed=15 failed=0 skipped=0'


def verify_python_result(result):
    """Nur die vollständigen 19 Fälle ohne Skips oder erwartete Fehler akzeptieren."""
    return (result.testsRun == 19 and not result.skipped and not result.expectedFailures
            and not result.unexpectedSuccesses and result.wasSuccessful())


def verify_csharp_output(output):
    """Jede feste ID einmal und letzte vollständige NoSkip-Zusammenfassung, keine beliebige Exit0-Meldung."""
    if output.splitlines() != ['PASS '+name for name in CSHARP_IDS]+[SUMMARY]:
        raise ValueError('Exactly 15 successful feeder contracts required')


def verify_framework(config, installed):
    """Standard-Minor-Auflösung braucht eine passende installierte Runtime, keine SDK-Patchgleichheit."""
    options = json.loads(config.read_text())['runtimeOptions']
    framework = options['framework']
    if options.get('rollForward', 'Minor') != 'Minor' or 'applyPatches' in options \
            or 'frameworks' in options or framework['name'] != 'Microsoft.NETCore.App' \
            or not re.fullmatch(r'10\.0\.\d+', framework['version']):
        raise ValueError('Unexpected feeder framework resolution contract')
    declared = tuple(map(int, framework['version'].split('.')))
    available = [tuple(map(int, match.group(1).split('.'))) for line in installed.splitlines()
                 if (match := re.fullmatch(r'Microsoft\.NETCore\.App (10\.0\.\d+) \[.+\]', line))]
    eligible = [version for version in available if version >= declared]
    if not eligible: raise ValueError('Feeder framework runtime unavailable')
    return '.'.join(map(str, max(eligible)))


def run_python_contracts():
    """Die tatsächlichen Python-Fakes laden; keine eigenen Ersatztests und keine Adapternormalaufrufe."""
    spec = importlib.util.spec_from_file_location('feeder_fake_contracts',
                                                 ROOT/'tests/runtime-bootstrap-smoke/test_run_smoke.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    suite = unittest.defaultTestLoader.loadTestsFromModule(module)
    if suite.countTestCases() != 19: raise ValueError('Exactly 19 Python feeder contracts required')
    return 0 if verify_python_result(unittest.TextTestRunner(verbosity=2).run(suite)) else 1


def execute(dotnet, work):
    """Geprüfte bestehende Own-Workspace-/Prozessfunktionen wiederverwenden; keine zweite Laufzeitsteuerung."""
    if run_python_contracts(): return 1
    environment = shared.isolated_environment(work)
    config = work/'NuGet.Config'
    config.write_text('<configuration><packageSources><clear />'
                      '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
                      '</packageSources></configuration>\n')
    artifacts = work/'artifacts'
    common = ['--artifacts-path', str(artifacts), '-p:UseAppHost=false',
              '-p:DisableImplicitLibraryPacksFolder=true', '-p:EnableTargetingPackDownload=false',
              '-p:EnableRuntimePackDownload=false']
    shared.run_tool([str(dotnet), 'restore', str(PROJECT), '--configfile', str(config), *common], environment)
    shared.run_tool([str(dotnet), 'build', str(PROJECT), '--no-restore', '--configuration', 'Release',
                     '--disable-build-servers', '-p:UseSharedCompilation=false', '-m:1', '-nr:false', *common], environment)
    output = artifacts/'bin/SmokeFeeder/release'
    dll = output/'SmokeFeeder.dll'
    if not dll.is_file(): raise ValueError('Feeder artifact missing')
    eligible = verify_framework(output/'SmokeFeeder.runtimeconfig.json',
                               shared.run_tool([str(dotnet), '--list-runtimes'], environment, capture=True))
    verify_csharp_output(shared.run_tool([str(dotnet), str(dll), '--contract-test'], environment, capture=True))
    print('Feeder fake contracts: Python19/19, C#15/15, skipped=0; eligible NETCore '+eligible)
    return 0


def main():
    """Explizites vorhandenes SDK und eigener Tempbereich; kein Install-/Discovery-/Runtimepfad."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--temp-root', type=Path, required=True)
    args = parser.parse_args()
    if not args.dotnet.is_absolute() or not args.dotnet.is_file() \
            or not args.temp_root.is_absolute() or not args.temp_root.is_dir():
        raise ValueError('Explicit absolute SDK and temporary root required')
    with shared.owned_workspace(args.temp_root) as work:
        return execute(args.dotnet.resolve(), work)


if __name__ == '__main__':
    raise SystemExit(main())
