"""Baut ausschließlich den paketfreien stdin-Runner und prüft alle 17 reinen Verträge."""
import argparse
from contextlib import contextmanager
import importlib.util
import json
import os
from pathlib import Path
import signal
import selectors
import shutil
import subprocess
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'tests/startup-secret-input/StartupSecretInput.ContractTests.csproj'
EXPECTED_TEST_COUNT = 17


def isolated_environment(work):
    """Nur eigene Pfade und feste Werte: keine geerbten Credentials, Loader- oder NuGet-Hooks."""
    paths = {key: work / name for key, name in [
        ('HOME', 'home'), ('DOTNET_CLI_HOME', 'cli'), ('TMPDIR', 'tmp'), ('NUGET_PACKAGES', 'packages')]}
    for path in paths.values():
        path.mkdir()
    return dict({key: str(path) for key, path in paths.items()}, PATH='/usr/bin:/bin',
                DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
                DOTNET_SKIP_FIRST_TIME_EXPERIENCE='true', DOTNET_NOLOGO='true',
                DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', DOTNET_MULTILEVEL_LOOKUP='0')


class ToolCleanupUnknown(RuntimeError):
    """Kein Gruppenabschlussbeleg: ausschließlich eigene numerische PID, keine Prozessdaten."""
    def __init__(self, pid):
        super().__init__(f'Own CLI cleanup UNKNOWN (pid={pid}); workspace preserved')


@contextmanager
def owned_workspace(temp_root):
    """Nur bei belegtem Kinderabschluss entfernen; bei UNKNOWN den eigenen Bereich erhalten."""
    work = Path(tempfile.mkdtemp(prefix='flowzer-stdin-ci-', dir=temp_root))
    removable = True
    try:
        yield work
    except ToolCleanupUnknown:
        removable = False
        print(f'Own CLI workspace preserved: {work}', file=sys.stderr)
        raise
    finally:
        if removable:
            shutil.rmtree(work)


def read_bounded_output(process, timeout=180, limit=65536):
    """Nur die kleine Runtime-Liste erfassen: feste Zeit-/Bytegrenze, keine Readerthreads."""
    deadline = time.monotonic() + timeout
    output = bytearray()
    with selectors.DefaultSelector() as selector:
        selector.register(process.stdout, selectors.EVENT_READ)
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0 or not selector.select(remaining):
                raise subprocess.TimeoutExpired('runtime-list', timeout)
            chunk = os.read(process.stdout.fileno(), min(4096, limit + 1 - len(output)))
            if not chunk:
                break
            output.extend(chunk)
            if len(output) > limit:
                raise ValueError('Runtime list exceeded byte limit')
        process.wait(timeout=max(0, deadline - time.monotonic()))
    return output.decode('utf-8')


def require_empty_group(pid):
    """Nach reaped Leader niemals signalisieren/adoptieren; nur ESRCH beweist Gruppenleere."""
    try:
        os.killpg(pid, 0)
    except ProcessLookupError:
        return
    except OSError:
        pass
    raise ToolCleanupUnknown(pid)


def run_tool(command, environment, capture=False, *, merge_stderr=False, timeout=180):
    """Eigene CLI-Gruppe; optional beide begrenzten Kanäle und explizite Frist für den Pilotenträger."""
    if not 1 <= timeout <= 600 or merge_stderr and not capture:
        raise ValueError('Bounded explicit tool deadline/capture required')
    process = subprocess.Popen(command, cwd=ROOT, env=environment, stdin=subprocess.DEVNULL,
                               stdout=subprocess.PIPE if capture else None,
                               stderr=subprocess.STDOUT if merge_stderr else None,
                               close_fds=True, start_new_session=True)
    try:
        try:
            output = read_bounded_output(process, timeout=timeout) if capture else ''
            if not capture:
                process.wait(timeout=timeout)
        except BaseException:
            try:
                # Nur der eigene noch lebende Leader legitimiert ein Gruppensignal.
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGTERM)
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        if process.poll() is None:
                            os.killpg(process.pid, signal.SIGKILL)
                        process.wait(timeout=5)
            except BaseException:
                raise ToolCleanupUnknown(process.pid) from None
            require_empty_group(process.pid)
            raise
        require_empty_group(process.pid)
        if process.returncode:
            raise subprocess.CalledProcessError(process.returncode, command, output=output)
        return output
    finally:
        if process.stdout is not None:
            process.stdout.close()


def verify_frameworks(runtimeconfig, installed):
    """Disable-Rollforward der bestehenden Tests nur mit tatsächlich vorhandenen exakten Runtimes nutzen."""
    frameworks = json.loads(runtimeconfig.read_text())['runtimeOptions']['frameworks']
    declared = {(entry['name'], entry['version']) for entry in frameworks}
    available = {tuple(line.split()[:2]) for line in installed.splitlines() if len(line.split()) >= 3}
    if {name for name, _ in declared} != {'Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'} \
            or not declared.issubset(available):
        raise ValueError('Exact contract framework runtime unavailable')


def verify_result(result):
    """Weder null/weniger Tests noch Skips, erwartete Fehler oder Leakassertions still grün werten."""
    return (result.testsRun == EXPECTED_TEST_COUNT and not result.skipped
            and not result.expectedFailures and not result.unexpectedSuccesses and result.wasSuccessful())


def run_contracts(dotnet, dll, environment):
    """Echte vorhandene Tests unverändert laden; nur ihre geprüfte Umgebung ist sichtbar."""
    previous = dict(os.environ)
    try:
        os.environ.clear()
        os.environ.update(environment, FLOWZER_STDIN_TEST_DOTNET=str(dotnet), FLOWZER_STDIN_TEST_DLL=str(dll))
        spec = importlib.util.spec_from_file_location('startup_secret_contracts',
                                                      ROOT / 'tests/startup-secret-input/test_contract.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        suite = unittest.defaultTestLoader.loadTestsFromModule(module)
        if suite.countTestCases() != EXPECTED_TEST_COUNT:
            raise ValueError('Exactly 17 startup contracts required')
        result = unittest.TextTestRunner(verbosity=2).run(suite)
        return 0 if verify_result(result) else 1
    finally:
        os.environ.clear()
        os.environ.update(previous)


def main():
    """Ausschließlich den explizit eingerichteten SDK-Host und einen eigenen Wegwerfbereich verwenden."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--temp-root', type=Path, required=True)
    args = parser.parse_args()
    if not args.dotnet.is_absolute() or not args.dotnet.is_file() \
            or not args.temp_root.is_absolute() or not args.temp_root.is_dir():
        raise ValueError('Explicit absolute SDK and temporary root required')
    dotnet = args.dotnet.resolve()
    with owned_workspace(args.temp_root) as work:
        environment = isolated_environment(work)
        config = work / 'NuGet.Config'
        # Keine Pakete benötigt: keine geerbte Feed-/Credentialkonfiguration und kein Netzrestore.
        config.write_text('<configuration><packageSources><clear /></packageSources></configuration>\n')
        artifacts = work / 'artifacts'
        common = ['--artifacts-path', str(artifacts), '-p:UseAppHost=false',
                  '-p:DisableImplicitLibraryPacksFolder=true', '-p:EnableTargetingPackDownload=false',
                  '-p:EnableRuntimePackDownload=false', '-p:TargetLatestRuntimePatch=true']
        run_tool([str(dotnet), 'restore', str(PROJECT), '--configfile', str(config), *common], environment)
        run_tool([str(dotnet), 'build', str(PROJECT), '--no-restore', '--configuration', 'Release',
                  '--disable-build-servers', '-p:UseSharedCompilation=false', '-m:1', '-nr:false',
                  *common], environment)
        output = artifacts / 'bin/StartupSecretInput.ContractTests/release'
        dll = output / 'StartupSecretInput.ContractTests.dll'
        verify_frameworks(output / 'StartupSecretInput.ContractTests.runtimeconfig.json',
                          run_tool([str(dotnet), '--list-runtimes'], environment, capture=True))
        return run_contracts(dotnet, dll, environment)


if __name__ == '__main__':
    raise SystemExit(main())
