"""Reine Source-/CI-Verträge: keine SDK-, Host-, Netzwerk- oder Datenbankstarts."""
import importlib.util
import json
import re
import tempfile
import subprocess
from unittest import mock
from types import SimpleNamespace
import unittest
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def bootstrap_step(workflow):
    """Nur den einzelnen registrierten Schritt im bestehenden dotnet-Job akzeptieren."""
    if len(re.findall(r'^  dotnet:$', workflow, re.M)) != 1:
        raise ValueError('Exactly one existing dotnet job required')
    job = re.search(r'^  dotnet:\n(.*?)(?=^  [A-Za-z][\w-]*:|\Z)', workflow, re.M | re.S)
    if not job:
        raise ValueError('dotnet job missing')
    steps = re.split(r'(?=^      - name:)', job.group(1), flags=re.M)
    matches = [step for step in steps if re.search(
        r'^          python3 -I -B scripts/ci/run_startup_secret_contracts\.py ', step, re.M)]
    if len(matches) != 1:
        raise ValueError('Exactly one pure startup contract step required')
    return matches[0]


def validate_registration(workflow):
    """Der echte Schritt darf weder ausgelagert, deaktiviert noch fehlerverdeckend verkettet sein."""
    step = bootstrap_step(workflow)
    required = ['set -euo pipefail\n', 'python3 -I -B scripts/ci/test_startup_secret_ci.py\n',
                '--dotnet "$(command -v dotnet)" --temp-root "$RUNNER_TEMP"\n']
    if not all(part in step for part in required) or re.search(
            r'continue-on-error:|\|\|\s*true|^\s*if:|\| tee', step, re.M):
        raise ValueError('Pure startup contracts must run fail-closed')


def load_runner():
    """Nur Definitionen laden; der CLI-Einstieg startet bei Import keine SDK-Prozesse."""
    spec = importlib.util.spec_from_file_location('startup_ci_runner',
                                                  ROOT / 'scripts/ci/run_startup_secret_contracts.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class WorkflowContractTests(unittest.TestCase):
    def test_existing_dotnet_job_runs_pure_contracts_fail_closed(self):
        # Testzweck: Die vorher fehlende CI-Registrierung muss real vorhanden sein, nicht nur dokumentiert.
        validate_registration((ROOT / '.github/workflows/ci.yml').read_text())

    def test_missing_duplicate_or_wrong_job_never_satisfies_registration(self):
        # Testzweck: Dokumentation, Duplikat oder fremder Job ist kein Testanschluss im dotnet-Job.
        workflow = (ROOT / '.github/workflows/ci.yml').read_text()
        for variant in [workflow.replace('  dotnet:', '  unrelated:'),
                        workflow.replace('run_startup_secret_contracts.py', 'unregistered.py'),
                        workflow + '\n  dotnet:\n']:
            with self.subTest(size=len(variant)):
                with self.assertRaises(ValueError):
                    validate_registration(variant)

    def test_disabled_or_masked_step_is_not_green(self):
        # Testzweck: if/continue-on-error/Fehlerpipeline darf die 17er-Abnahme nicht umgehen.
        workflow = (ROOT / '.github/workflows/ci.yml').read_text()
        step = bootstrap_step(workflow)
        for extra in ['        if: false\n', '        continue-on-error: true\n']:
            with self.assertRaises(ValueError):
                validate_registration(workflow.replace(step, step.replace('        shell:', extra + '        shell:')))
        with self.assertRaises(ValueError):
            validate_registration(workflow.replace(' --temp-root "$RUNNER_TEMP"\n',
                                                     ' --temp-root "$RUNNER_TEMP" || true\n'))

    def test_project_remains_package_and_host_free(self):
        # Testzweck: Der kleine Runner darf keine Solution, API, Fremdpakete oder Tasks hereinziehen.
        project = ET.parse(ROOT / 'tests/startup-secret-input/StartupSecretInput.ContractTests.csproj')
        for tag in ['PackageReference', 'ProjectReference', 'Import', 'Target', 'UsingTask']:
            self.assertEqual(list(project.iter(tag)), [])
        self.assertEqual([x.attrib['Include'] for x in project.iter('Compile')], [
            '../../src/WebApiEngine/Configuration/RuntimeSecretInput.cs',
            '../../src/WebApiEngine/Configuration/RuntimeSecretConfiguration.cs'])
        self.assertEqual([x.attrib['Include'] for x in project.iter('FrameworkReference')],
                         ['Microsoft.AspNetCore.App'])


class RunnerContractTests(unittest.TestCase):
    def test_environment_has_only_own_paths_and_fixed_values(self):
        # Testzweck: Keine geerbten Credentials, Loader- oder NuGet-Pluginwerte an SDK/Testkinder geben.
        runner = load_runner()
        with tempfile.TemporaryDirectory(prefix='flowzer-ci-source-test-') as directory:
            root = Path(directory)
            environment = runner.isolated_environment(root)
            for key in ['HOME', 'DOTNET_CLI_HOME', 'TMPDIR', 'NUGET_PACKAGES']:
                self.assertTrue(Path(environment[key]).is_relative_to(root))
                self.assertTrue(Path(environment[key]).is_dir())
            self.assertFalse(set(environment) & {'GH_TOKEN', 'GITHUB_TOKEN', 'BW_SESSION',
                                                'PYTHONPATH', 'LD_PRELOAD', 'NUGET_PLUGIN_PATHS'})
            self.assertEqual(environment['PATH'], '/usr/bin:/bin')

    def test_exact_test_count_and_no_skips_or_expected_failures(self):
        # Testzweck: Count17, echter Rückgabestatus und jeder ausgelassene/erwartet rote Fall sind Gates.
        runner = load_runner()
        def result(**changes):
            fields = dict(testsRun=17, skipped=[], expectedFailures=[], unexpectedSuccesses=[],
                          wasSuccessful=lambda: True)
            fields.update(changes)
            return SimpleNamespace(**fields)
        self.assertTrue(runner.verify_result(result()))
        for changed in [dict(testsRun=0), dict(testsRun=16), dict(testsRun=18), dict(skipped=['PUBLIC']),
                        dict(expectedFailures=['PUBLIC']), dict(unexpectedSuccesses=['PUBLIC']),
                        dict(wasSuccessful=lambda: False)]:
            self.assertFalse(runner.verify_result(result(**changed)))

    def test_frameworks_must_exist_exactly_without_rollforward(self):
        # Testzweck: Hosted-SDK-Patchlevel ist explizit verfügbar, kein stilles Lockern von Disable.
        runner = load_runner()
        with tempfile.TemporaryDirectory(prefix='flowzer-ci-source-test-') as directory:
            config = Path(directory) / 'runtimeconfig.json'
            config.write_text(json.dumps({'runtimeOptions': {'frameworks': [
                {'name': 'Microsoft.NETCore.App', 'version': '10.0.3'},
                {'name': 'Microsoft.AspNetCore.App', 'version': '10.0.3'}]}}))
            exact = 'Microsoft.NETCore.App 10.0.3 [/PUBLIC]\nMicrosoft.AspNetCore.App 10.0.3 [/PUBLIC]\n'
            runner.verify_frameworks(config, exact)
            with self.assertRaises(ValueError):
                runner.verify_frameworks(config, exact.replace('10.0.3', '10.0.4'))


class ToolLifecycleTests(unittest.TestCase):
    def test_success_or_term_leader_exit_does_not_prove_empty_group(self):
        # Testzweck: Auch nach Exit0/TERM muss ein überlebendes eigenes Gruppenkind UNKNOWN ergeben.
        runner = load_runner()
        for timeout in [False, True]:
            process = mock.Mock(pid=424242, returncode=0, stdout=None)
            process.poll.return_value = None if timeout else 0
            process.wait.side_effect = [subprocess.TimeoutExpired('PUBLIC', 180), 0] if timeout else [0]
            with mock.patch.object(runner.subprocess, 'Popen', return_value=process), \
                    mock.patch.object(runner.os, 'killpg') as kill:
                with self.assertRaises(runner.ToolCleanupUnknown):
                    runner.run_tool(['PUBLIC'], {})
                self.assertEqual(kill.call_args_list[-1], mock.call(424242, 0))
                # Nach dem reaped Leader ausschließlich lesen, nicht erneut signalisieren.
                self.assertEqual(len(kill.call_args_list), 2 if timeout else 1)

    def test_reaped_leader_and_esrch_prove_success(self):
        # Testzweck: Nur ESRCH bestätigt die leere eigene Gruppe; PermissionError ist kein Erfolg.
        runner = load_runner()
        for error in [ProcessLookupError(), PermissionError()]:
            process = mock.Mock(pid=424242, returncode=0, stdout=None)
            with mock.patch.object(runner.subprocess, 'Popen', return_value=process), \
                    mock.patch.object(runner.os, 'killpg', side_effect=error):
                if isinstance(error, ProcessLookupError):
                    self.assertEqual(runner.run_tool(['PUBLIC'], {}), '')
                else:
                    with self.assertRaises(runner.ToolCleanupUnknown):
                        runner.run_tool(['PUBLIC'], {})

    def test_unknown_cleanup_preserves_owned_workspace(self):
        # Testzweck: Ungeklärte eigene Kinder dürfen ihren Arbeitsbereich nicht unter sich verlieren.
        runner = load_runner()
        with tempfile.TemporaryDirectory(prefix='flowzer-ci-source-test-') as directory:
            with self.assertRaises(runner.ToolCleanupUnknown):
                with runner.owned_workspace(Path(directory)) as work:
                    raise runner.ToolCleanupUnknown(424242)
            self.assertTrue(work.is_dir())
            with runner.owned_workspace(Path(directory)) as cleared:
                self.assertTrue(cleared.is_dir())
            self.assertFalse(cleared.exists())

    def test_capture_finishes_within_limit_or_times_out_without_background_reader(self):
        # Testzweck: Kleiner EOF-Erfolg und fehlende Readiness bleiben synchron und zeitbegrenzt.
        runner = load_runner()
        process = mock.Mock(stdout=mock.Mock())
        selector = mock.MagicMock()
        selector.__enter__.return_value = selector
        selector.select.return_value = [object()]
        with mock.patch.object(runner.selectors, 'DefaultSelector', return_value=selector), \
                mock.patch.object(runner.os, 'read', side_effect=[b'PUBLIC', b'']):
            self.assertEqual(runner.read_bounded_output(process), 'PUBLIC')
            process.wait.assert_called_once()
        selector.select.return_value = []
        with mock.patch.object(runner.selectors, 'DefaultSelector', return_value=selector):
            with self.assertRaises(subprocess.TimeoutExpired):
                runner.read_bounded_output(process)

    def test_signal_failure_preserves_unknown_and_closes_owned_pipe(self):
        # Testzweck: Kein unbeschränktes Popen.__exit__-Warten und kein Löschen nach Signalfehler.
        runner = load_runner()
        process = mock.Mock(pid=424242, returncode=None)
        process.poll.return_value = None
        with mock.patch.object(runner.subprocess, 'Popen', return_value=process), \
                mock.patch.object(runner, 'read_bounded_output', side_effect=ValueError('PUBLIC')), \
                mock.patch.object(runner.os, 'killpg', side_effect=PermissionError()):
            with self.assertRaises(runner.ToolCleanupUnknown):
                runner.run_tool(['PUBLIC'], {}, capture=True)
            process.stdout.close.assert_called_once()
            process.wait.assert_not_called()

    def test_captured_runtime_output_has_a_hard_byte_limit(self):
        # Testzweck: Ausschließlich gefakte CLI-Ausgabe; ein endloser Producer darf nicht RAM füllen.
        runner = load_runner()
        process = mock.Mock(stdout=mock.Mock())
        selector = mock.MagicMock()
        selector.__enter__.return_value = selector
        selector.select.return_value = [object()]
        with mock.patch.object(runner.selectors, 'DefaultSelector', return_value=selector), \
                mock.patch.object(runner.os, 'read', return_value=b'x' * 4096) as read:
            with self.assertRaises(ValueError):
                runner.read_bounded_output(process, timeout=180, limit=8192)
            self.assertEqual(read.call_count, 3)


if __name__ == '__main__':
    unittest.main(verbosity=2)
