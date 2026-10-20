"""Testzweck: Reine CI-Verdrahtung, Counts und Mockaufrufe; keinerlei SDK-/Adapterstarts."""
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]


def runner():
    spec = importlib.util.spec_from_file_location('runtime_smoke_ci', ROOT / 'scripts/ci/run_runtime_smoke_contracts.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def registration(text):
    """Nur genau ein unbedingter Schritt im bestehenden dotnet-Job gilt als Anschluss."""
    if len(re.findall(r'^  dotnet:$', text, re.M)) != 1: raise ValueError('job')
    job = re.search(r'^  dotnet:\n(.*?)(?=^  [A-Za-z][\w-]*:|\Z)', text, re.M | re.S).group(1)
    steps = re.split(r'(?=^      - name:)', job, flags=re.M)
    matches = [s for s in steps if 'python3 -I -B scripts/ci/run_runtime_smoke_contracts.py ' in s]
    if len(matches) != 1: raise ValueError('step')
    step = matches[0]
    if not all(x in step for x in ['set -euo pipefail\n',
            'python3 -I -B scripts/ci/test_runtime_smoke_ci.py\n',
            '--dotnet "$(command -v dotnet)" --temp-root "$RUNNER_TEMP"\n']) \
            or re.search(r'continue-on-error:|\|\||^\s*if:|\| tee', step, re.M): raise ValueError('masked')


class WorkflowTests(unittest.TestCase):
    def test_registered_in_existing_job_and_never_masked(self):
        # Testzweck: Eine bloße README, falscher Job, Duplikat oder unterdrückter Exit genügt nicht.
        text = (ROOT / '.github/workflows/ci.yml').read_text(); registration(text)
        for bad in [text.replace('  dotnet:', '  foreign:'), text+'\n  dotnet:\n',
                    text.replace('run_runtime_smoke_contracts.py', 'missing.py'),
                    text.replace('      - name: Feeder-Fakeverträge prüfen',
                                 '      - name: Feeder-Fakeverträge prüfen\n        if: false'),
                    text.replace(' --temp-root "$RUNNER_TEMP"\n', ' --temp-root "$RUNNER_TEMP" || true\n')]:
            with self.assertRaises(ValueError): registration(bad)

    def test_old_startup_step_is_unchanged_and_registered(self):
        # Testzweck: Neuer Anschluss darf den vorhandenen 13+17-Vertrag weder ersetzen noch deaktivieren.
        spec = importlib.util.spec_from_file_location('existing_ci_test', ROOT/'scripts/ci/test_startup_secret_ci.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        module.validate_registration((ROOT/'.github/workflows/ci.yml').read_text())


class RunnerTests(unittest.TestCase):
    def test_python_count_success_skips_and_expected_failures(self):
        # Testzweck: Genau19 echte Fälle, keine Nulltests/Skips/ExpectedFailures als Erfolg.
        r = runner()
        def result(**changes):
            fields = dict(testsRun=19, skipped=[], expectedFailures=[], unexpectedSuccesses=[],
                          wasSuccessful=lambda: True); fields.update(changes)
            return SimpleNamespace(**fields)
        self.assertTrue(r.verify_python_result(result()))
        for changes in [dict(testsRun=0), dict(testsRun=18), dict(testsRun=20), dict(skipped=['x']),
                        dict(expectedFailures=['x']), dict(unexpectedSuccesses=['x']), dict(wasSuccessful=lambda:False)]:
            self.assertFalse(r.verify_python_result(result(**changes)))

    def test_csharp_requires_exact_unique_passes_and_terminal_summary(self):
        # Testzweck: Exit0 allein reicht nicht; feste15 IDs und ausschließlich vollständige NoSkip-Zusammenfassung.
        r = runner(); good = ''.join('PASS '+name+'\n' for name in r.CSHARP_IDS)+r.SUMMARY+'\n'
        r.verify_csharp_output(good)
        for bad in ['', good.replace('total=15', 'total=14'), good.replace('skipped=0', 'skipped=1'),
                    good.replace('PASS ', 'FAIL ', 1), good+good, good+'SKIP unknown\n',
                    good.replace(r.CSHARP_IDS[0], r.CSHARP_IDS[1], 1), 'UNEXPECTED\n'+good]:
            with self.assertRaises(ValueError): r.verify_csharp_output(bad)

    def test_default_framework_allows_installed_newer_patch_not_missing_runtime(self):
        # Testzweck: Tatsächlicher Standardvertrag statt künstlicher SDK-/Runtime-Patchgleichheit.
        r = runner()
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory)/'runtimeconfig.json'
            value = dict(runtimeOptions=dict(tfm='net10.0',framework=dict(name='Microsoft.NETCore.App',version='10.0.0')))
            config.write_text(json.dumps(value))
            self.assertEqual(r.verify_framework(config, 'Microsoft.NETCore.App 10.0.13 [/PUBLIC]\n'), '10.0.13')
            for installed in ['', 'Microsoft.NETCore.App 9.0.13 [/PUBLIC]\n', 'Microsoft.AspNetCore.App 10.0.13 [/PUBLIC]\n',
                              'MicrosoftxNETCorexApp 10.0.13 [/PUBLIC]\n']:
                with self.assertRaises(ValueError): r.verify_framework(config, installed)
            value['runtimeOptions']['rollForward']='LatestMajor'; config.write_text(json.dumps(value))
            with self.assertRaises(ValueError): r.verify_framework(config,'Microsoft.NETCore.App 10.0.13 [/PUBLIC]\n')

    def test_isolated_build_uses_only_small_project_public_feed_and_one_pass(self):
        # Testzweck: Kein Solution-/Container-/APIstart, kein zweiter Restore/Build, keine SecretENV oder Feedlockerung.
        r = runner(); calls = []
        with tempfile.TemporaryDirectory() as directory:
            work=Path(directory)
            def tool(command, env, capture=False):
                calls.append((command,dict(env),capture))
                if command[1]=='build':
                    output=work/'artifacts/bin/SmokeFeeder/release';output.mkdir(parents=True)
                    (output/'SmokeFeeder.dll').write_bytes(b'PUBLIC_FAKE')
                    (output/'SmokeFeeder.runtimeconfig.json').write_text(json.dumps(dict(runtimeOptions=dict(
                        framework=dict(name='Microsoft.NETCore.App',version='10.0.0')))))
                if command[1]=='--list-runtimes': return 'Microsoft.NETCore.App 10.0.13 [/PUBLIC]\n'
                if command[-1]=='--contract-test': return ''.join('PASS '+x+'\n' for x in r.CSHARP_IDS)+r.SUMMARY+'\n'
                return ''
            with mock.patch.object(r.shared,'run_tool',side_effect=tool), \
                    mock.patch.object(r,'run_python_contracts',return_value=0) as python:
                self.assertEqual(r.execute(Path('/PUBLIC/dotnet'),work),0)
            python.assert_called_once()
            self.assertEqual([c[0][1] for c in calls[:3]],['restore','build','--list-runtimes'])
            self.assertEqual(len(calls),4)
            self.assertTrue(all(c[0][2]==str(r.PROJECT) for c in calls[:2]))
            self.assertTrue(all('DOTNET_ROLL_FORWARD' not in c[1] and 'GH_TOKEN' not in c[1] for c in calls))
            config=(work/'NuGet.Config').read_text()
            self.assertIn('https://api.nuget.org/v3/index.json',config);self.assertIn('<clear />',config)
            self.assertNotIn('NuGetAudit=false',str(calls));self.assertIn('--no-restore',calls[1][0])

    def test_failed_restore_stops_before_build(self):
        # Testzweck: Echter Exit!=0 wird nicht als Count-/Artefakterfolg kaschiert oder wiederholt.
        r=runner()
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(r,'run_python_contracts',return_value=0), \
                mock.patch.object(r.shared,'run_tool',side_effect=subprocess.CalledProcessError(1,['PUBLIC'])) as tool:
            with self.assertRaises(subprocess.CalledProcessError): r.execute(Path('/PUBLIC/dotnet'),Path(directory))
            self.assertEqual(tool.call_count,1)


if __name__ == '__main__':
    unittest.main(verbosity=2)
