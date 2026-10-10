"""Testzweck: Geschlossene Fehlerdiagnose ohne echte Prozesse, Docker, Netz oder Secrets."""
import contextlib
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import runner
from test_runner import CONTEXT, CONFIGS, container
from prepare import BUDGETS

MARKER = 'synthetic-private-error-must-not-escape'


class DiagnosticsTests(unittest.TestCase):
    def test_actual_connection_refused_auth_path_captures_only_own_tls_binding(self):
        # Testzweck: Nur bestehende Discovery-Code3 nach echtem Auth-Kindprozess-Exit
        # aktiviert genau einen formatgebundenen eigenen TLS-Inspect, keinen Netzrequest.
        with self.synthetic_runtime() as rig:
            error=runner.MeasuredProcessExitError(1)
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]=='node':
                    (rig.root/'auth-result.json').write_text(json.dumps(dict(total=16,passed=1,
                        failed=1,skipped=0,interrupted=0,errors=0,success=False,failed_test_indexes=[2])))
                    (rig.root/'discovery-result.json').write_text(json.dumps(dict(status=None,
                        transport_code=3,issuer_matches=None,pkce_s256=None)));raise error
            rig.measured_command=Mock(side_effect=measured)
            original=rig.command;fields=dict(configured_binding_count=1,configured_loopback_match=True,
                published_binding_count=0,published_loopback_match=None,network_count=1,only_owned_network=True)
            def command(args,**kwargs):
                if args[:3]==['docker','inspect','--type']:return json.dumps(container('tls')|fields)
                return original(args,**kwargs)
            rig.command=Mock(side_effect=command)
            rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],[container(s) for s in BUDGETS],[]])
            with self.assertRaises(runner.MeasuredProcessExitError) as raised:rig.run()
            self.assertIs(error,raised.exception)
            actual=json.loads((rig.root/'report/resource-result.json').read_text())
            self.assertEqual(fields|dict(tls_running=True,tls_oom=False,tls_exit_code=0,tls_restarts=0),actual.get('tls_loopback'))
            self.assertEqual(1,sum(call.args[0][:3]==['docker','inspect','--type'] for call in rig.command.call_args_list))
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1)],actual['failures'])
            self.assertFalse(actual['success']);self.assertTrue(actual['cleanup_complete'])

    def test_optional_loopback_diagnosis_never_displaces_real_cleanup_and_stop(self):
        # Testzweck: Neue rein optionale Inspectdiagnose besitzt keinen Vorrang
        # vor echter Cleanup-/Stopursache; Max3 und erste reale Authursache bleiben.
        with self.synthetic_runtime() as rig:
            # Mocks halten die tatsächlich unterschiedlichen Fehleridentitäten
            # bis zum Testende lebendig; keine CPython-id-Wiederverwendung vortäuschen.
            errors=(runner.MeasuredProcessExitError(1),ValueError(MARKER),runner.ProcessExitError(29),
                subprocess.TimeoutExpired(['synthetic'],120),OSError(MARKER))
            rig.note_failure('auth',errors[0]);rig.note_failure('auth_report',errors[1])
            try:rig.note_failure('loopback_report',errors[2])
            except ValueError:self.fail('Geschlossene optionale Loopbackphase fehlt')
            rig.note_failure('cleanup',errors[3]);rig.note_failure('stop',errors[4])
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                dict(phase='cleanup',error='timeout',exit_code=None),dict(phase='stop',error='io',exit_code=None)],rig.report['failures'])
            self.assertEqual(3,len(rig.report['failures']));self.assertNotIn(MARKER,json.dumps(rig.report))

    def test_both_optional_report_errors_never_displace_actual_cleanup_failure(self):
        # Testzweck: Der echte Runpfad bleibt auf maximal drei Diagnosen und hält
        # den primären Auth-Exit sowie die spätere Ownership-/Cleanup-Ursache fest.
        with self.synthetic_runtime() as rig:
            error=runner.MeasuredProcessExitError(1)
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]=='node':
                    for name in ('auth-result.json','discovery-result.json'):
                        (rig.root/name).write_text(json.dumps({'raw':MARKER}))
                    raise error
            rig.measured_command=Mock(side_effect=measured)
            cleanup=ValueError(MARKER)
            rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],cleanup])
            with self.assertRaises(ValueError) as raised:rig.run()
            self.assertIs(cleanup,raised.exception)
            raw=(rig.root/'report/resource-result.json').read_text();actual=json.loads(raw)
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                dict(phase='auth_report',error='validation',exit_code=None),
                dict(phase='cleanup',error='validation',exit_code=None)],actual['failures'])
            self.assertEqual(3,len(actual['failures']));self.assertNotIn(MARKER,raw)
            self.assertFalse(actual['success']);self.assertFalse(actual['cleanup_complete'])
            self.assertFalse(any('down' in call.args[0] for call in rig.command.call_args_list))

    def test_optional_reports_never_displace_actual_cleanup_timeout_and_stop_io(self):
        # Testzweck: Auch der tatsächliche command→stop_process-Pfad beim Cleanup
        # bekommt innerhalb Max3 seine beiden realen Ursachen, keine geänderte Abbruchaktion.
        with self.synthetic_runtime() as rig:
            error=runner.MeasuredProcessExitError(1)
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]=='node':
                    for name in ('auth-result.json','discovery-result.json'):
                        (rig.root/name).write_text(json.dumps({'raw':MARKER}))
                    raise error
            rig.measured_command=Mock(side_effect=measured)
            rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],[container(s) for s in BUDGETS]])
            original=rig.command;timeout=subprocess.TimeoutExpired(['synthetic'],120);stop=OSError(MARKER)
            child=Mock(pid=123);child.communicate.side_effect=timeout;child.wait.side_effect=stop
            def command(args,**kwargs):
                if 'down' in args:
                    with patch.object(runner.subprocess,'Popen',return_value=child),patch.object(runner.os,'killpg') as kill:
                        try:return runner.Rig.command(rig,args,**kwargs)
                        finally:kill.assert_called_once_with(123,signal.SIGTERM);child.wait.assert_called_once_with(timeout=5)
                return original(args,**kwargs)
            rig.command=Mock(side_effect=command)
            with self.assertRaises(OSError) as raised:rig.run()
            self.assertIs(stop,raised.exception)
            raw=(rig.root/'report/resource-result.json').read_text();actual=json.loads(raw)
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                dict(phase='cleanup',error='timeout',exit_code=None),
                dict(phase='stop',error='io',exit_code=None)],actual['failures'])
            self.assertEqual(3,len(actual['failures']));self.assertNotIn(MARKER,raw)
            self.assertFalse(actual['success']);self.assertFalse(actual['cleanup_complete'])

    def test_discovery_report_accepts_only_status_transport_and_nullable_booleans(self):
        # Testzweck: Bestehende Anfrage liefert nur beobachtete Zahlen/Booleans;
        # fehlende Informationen bleiben null, kein Rohtext und keine erfundene Null.
        project=getattr(runner,'discovery_result',None)
        self.assertTrue(callable(project),'Geschlossene Discovery-Projektion fehlt')
        value=dict(status=200,transport_code=None,issuer_matches=True,pkce_s256=True)
        for accepted in [value,value|{'status':503,'issuer_matches':None,'pkce_s256':None},
            dict(status=None,transport_code=8,issuer_matches=None,pkce_s256=None),
            dict(status=None,transport_code=None,issuer_matches=None,pkce_s256=None)]:
            self.assertEqual(accepted,project(accepted))
        for rejected in [value|{'raw':MARKER},value|{'status':True},value|{'status':99},
            value|{'status':600},value|{'status':'200'},value|{'transport_code':1},
            value|{'status':None,'transport_code':0},value|{'status':None,'transport_code':14},
            value|{'status':None,'transport_code':True},value|{'issuer_matches':1},
            value|{'pkce_s256':'true'},value|{'status':None}]:
            with self.subTest(),self.assertRaises(ValueError):project(rejected)

    def test_actual_auth_exit_retains_only_existing_safe_discovery_report(self):
        # Testzweck: Tatsächlich eigener Auth-Exit behält eine vorhandene sichere
        # Discovery-Beobachtung, bleibt rot und führt unverändert eigenes Cleanup aus.
        observed=dict(status=None,transport_code=8,issuer_matches=None,pkce_s256=None)
        with self.synthetic_runtime() as rig:
            error=runner.MeasuredProcessExitError(1)
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]=='node':
                    (rig.root/'auth-result.json').write_text(json.dumps(dict(total=16,passed=1,
                        failed=1,skipped=0,interrupted=0,errors=0,success=False,failed_test_indexes=[2])))
                    (rig.root/'discovery-result.json').write_text(json.dumps(observed));raise error
            rig.measured_command=Mock(side_effect=measured)
            rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],
                [container(s) for s in BUDGETS],[]])
            with self.assertRaises(runner.MeasuredProcessExitError) as raised:rig.run()
            self.assertIs(error,raised.exception)
            actual=json.loads((rig.root/'report/resource-result.json').read_text())
            self.assertEqual(observed,actual.get('discovery'));self.assertNotIn('auth',actual)
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1)],actual['failures'])
            self.assertFalse(actual['success']);self.assertTrue(actual['cleanup_complete'])

    def test_discovery_report_problem_never_replaces_auth_exit_or_exports_raw(self):
        # Testzweck: Manipulierte/überlange/Symlink-Beobachtung bleibt feste Zusatzphase;
        # kein Rohtext, kein primärer Exitverlust und keine Abkürzung am Cleanup.
        for kind in ('invalid','oversize','symlink'):
            with self.subTest(kind=kind),self.synthetic_runtime() as rig:
                error=runner.MeasuredProcessExitError(1)
                def measured(args,**kwargs):
                    rig.report['sample_count']=1
                    if args[0]=='node':
                        (rig.root/'auth-result.json').write_text(json.dumps(dict(total=16,passed=1,
                            failed=1,skipped=0,interrupted=0,errors=0,success=False)))
                        output=rig.root/'discovery-result.json'
                        if kind=='symlink':output.symlink_to(rig.root/'auth-result.json')
                        elif kind=='oversize':output.write_text(' ' * 1025)
                        else:output.write_text(json.dumps({'raw':MARKER}))
                        raise error
                rig.measured_command=Mock(side_effect=measured)
                rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],
                    [container(s) for s in BUDGETS],[]])
                with self.assertRaises(runner.MeasuredProcessExitError) as raised:rig.run()
                self.assertIs(error,raised.exception)
                raw=(rig.root/'report/resource-result.json').read_text();actual=json.loads(raw)
                self.assertNotIn('discovery',actual);self.assertNotIn(MARKER,raw)
                self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                    dict(phase='discovery_report',error='validation',exit_code=None)],actual['failures'])
                self.assertTrue(actual['cleanup_complete']);self.assertFalse(actual['success'])

    def test_discovery_existence_io_never_replaces_actual_auth_exit(self):
        # Testzweck: Auch ein Dateisystemfehler schon bei der Existenzprüfung bleibt
        # Zusatzdiagnose, nicht Ersatz des tatsächlich beobachteten Auth-Kindprozess-Exits.
        with self.synthetic_runtime() as rig:
            rig.root.mkdir()
            (rig.root/'auth-result.json').write_text(json.dumps(dict(total=16,passed=1,
                failed=1,skipped=0,interrupted=0,errors=0,success=False)))
            error=runner.MeasuredProcessExitError(1);rig.measured_command=Mock(side_effect=error)
            original=Path.exists
            def exists(path):
                if path==rig.root/'discovery-result.json':raise OSError(MARKER)
                return original(path)
            with patch.object(Path,'exists',exists):
                try:rig.run_auth(rig.root)
                except Exception as actual:self.assertIs(error,actual)
                else:self.fail('Tatsächlicher Auth-Exit wurde verschluckt')
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                dict(phase='discovery_report',error='io',exit_code=None)],rig.report['failures'])
            self.assertNotIn('discovery',rig.report);self.assertNotIn(MARKER,json.dumps(rig.report))

    def test_sampling_exit_never_opens_existing_discovery_observation(self):
        # Testzweck: Der tatsächliche measured_command→sample-Fehler beweist keinen
        # Auth-Kindprozess-Exit und darf auch eine vorhandene Discovery-Datei nicht öffnen.
        with self.synthetic_runtime() as rig:
            error=runner.ProcessExitError(29)
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]!='node':return
                (rig.root/'discovery-result.json').write_text(json.dumps({'raw':MARKER}))
                child=Mock();child.poll.side_effect=[None,0];rig.sample=Mock(side_effect=error)
                with patch.object(runner.subprocess,'Popen',return_value=child):
                    runner.Rig.measured_command(rig,args,**kwargs)
            rig.measured_command=Mock(side_effect=measured)
            rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],
                [container(s) for s in BUDGETS],[]])
            with self.assertRaises(runner.ProcessExitError) as raised:rig.run()
            self.assertIs(error,raised.exception)
            actual=json.loads((rig.root/'report/resource-result.json').read_text())
            self.assertNotIn('discovery',actual);self.assertNotIn('auth_attempt',actual)
            self.assertEqual([dict(phase='sampling',error='process_exit',exit_code=29)],actual['failures'])
            self.assertTrue(actual['cleanup_complete'])

    def test_failed_auth_optional_ordinals_are_closed_unique_and_not_success(self):
        # Testzweck: Nur bekannte positive Suiteordinale eines echten Fehlversuchs,
        # niemals Titel/IDs, erfundene Null, Duplikate oder mehr Kennungen als Fehltests.
        value=dict(total=16,passed=1,failed=1,skipped=0,interrupted=0,errors=0,
            success=False,failed_test_indexes=[2])
        try:actual=runner.auth_attempt_result(value,16)
        except ValueError:actual=None
        self.assertEqual(value,actual,'Optionale geschlossene Fehltestordinale fehlen')
        for indexes in [[],[0],[17],[True],['2'],[2.0],[1,1],[2,1]]:
            with self.subTest(indexes=indexes),self.assertRaises(ValueError):
                runner.auth_attempt_result(value|{'failed_test_indexes':indexes},16)
        with self.assertRaises(ValueError):runner.auth_result(value|{'success':True},16)
        with self.assertRaises(ValueError):runner.auth_attempt_result(value|{'raw':MARKER},16)

    def test_actual_measured_auth_child_exit_has_distinct_provenance(self):
        # Testzweck: Nur der wirklich gelesene returncode des eigenen gemessenen
        # Auth-Kindprozesses aktiviert die Fehlversuchsprojektion, kein Sampling-Exit.
        child_exit=getattr(runner,'MeasuredProcessExitError',None)
        self.assertTrue(callable(child_exit),'Herkunft des gemessenen Kindprozess-Exits fehlt')
        value=dict(total=16,passed=1,failed=1,skipped=14,interrupted=0,errors=0,success=False)
        with self.synthetic_runtime() as rig:
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]!='node':return
                (rig.root/'auth-result.json').write_text(json.dumps(value))
                child=Mock(returncode=1);child.poll.side_effect=[1,1]
                with patch.object(runner.subprocess,'Popen',return_value=child):
                    runner.Rig.measured_command(rig,args,**kwargs)
            rig.measured_command=Mock(side_effect=measured)
            rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],
                [container(s) for s in BUDGETS],[]])
            with self.assertRaises(child_exit) as raised:rig.run()
            self.assertEqual(1,raised.exception.exit_code)
            actual=json.loads((rig.root/'report/resource-result.json').read_text())
            self.assertEqual(value,actual.get('auth_attempt'));self.assertNotIn('auth',actual)
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1)],actual['failures'])
            self.assertFalse(actual['success']);self.assertTrue(actual['cleanup_complete'])

    def test_actual_sampling_process_exit_is_not_an_auth_child_exit(self):
        # Testzweck: Der echte measured_command→sample-Pfad kann einen Docker-Exit werfen,
        # ohne den Auth-Kindprozessstatus beobachtet zu haben. Keine Auth-Zähler/-Reportphase erfinden.
        for missing in (False,True):
            with self.subTest(missing=missing),self.synthetic_runtime() as rig:
                error=runner.ProcessExitError(29)
                def measured(args,**kwargs):
                    rig.report['sample_count']=1
                    if args[0]!='node':return
                    if missing:(rig.root/'auth-result.json').unlink()
                    else:(rig.root/'auth-result.json').write_text(json.dumps(dict(
                        total=16,passed=1,failed=1,skipped=14,interrupted=0,errors=0,success=False)))
                    child=Mock();child.poll.side_effect=[None,0]
                    rig.sample=Mock(side_effect=error)
                    with patch.object(runner.subprocess,'Popen',return_value=child):
                        runner.Rig.measured_command(rig,args,**kwargs)
                rig.measured_command=Mock(side_effect=measured)
                rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],
                    [container(s) for s in BUDGETS],[]])
                with self.assertRaises(runner.ProcessExitError) as raised:rig.run()
                self.assertIs(error,raised.exception)
                actual=json.loads((rig.root/'report/resource-result.json').read_text())
                self.assertNotIn('auth_attempt',actual);self.assertNotIn('auth',actual)
                self.assertEqual([dict(phase='sampling',error='process_exit',exit_code=29)],actual['failures'])
                self.assertFalse(actual['success']);self.assertTrue(actual['cleanup_complete'])

    def test_failed_auth_attempt_accepts_only_closed_bounded_numbers(self):
        # Testzweck: Fehlversuchszähler sind Diagnose, niemals eine erfolgreiche Authabnahme;
        # freie Felder, Bool-Zähler, Überzählung und ein widersprüchlicher Erfolg bleiben geschlossen.
        project=getattr(runner,'auth_attempt_result',None)
        self.assertTrue(callable(project),'Geschlossene Auth-Fehlversuchsprojektion fehlt')
        value=dict(total=16,passed=1,failed=1,skipped=14,interrupted=0,errors=0,success=False)
        self.assertEqual(value,project(value,16))
        self.assertEqual(value|{'passed':0,'failed':0,'skipped':0,'errors':1},
            project(value|{'passed':0,'failed':0,'skipped':0,'errors':1},16))
        for changed in [value|{'raw':MARKER},value|{'success':True},value|{'failed':True},
            value|{'passed':17},value|{'failed':2},value|{'total':17},value|{'errors':10001}]:
            with self.subTest(changed=list(changed)),self.assertRaises(ValueError):project(changed,16)

    def test_actual_auth_exit_retains_failed_counts_and_still_cleans_up(self):
        # Testzweck: Ein tatsächlicher Exit1 bleibt Fehler, während vorhandene sichere
        # Reporterzahlen durch den echten Runpfad erhalten werden und Cleanup weiterläuft.
        value=dict(total=16,passed=1,failed=1,skipped=14,interrupted=0,errors=0,success=False)
        with self.synthetic_runtime() as rig:
            error=runner.MeasuredProcessExitError(1)
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]=='node':
                    (rig.root/'auth-result.json').write_text(json.dumps(value));raise error
            rig.measured_command=Mock(side_effect=measured)
            rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],
                [container(s) for s in BUDGETS],[]])
            with self.assertRaises(runner.ProcessExitError) as raised:rig.run()
            self.assertIs(error,raised.exception)
            actual=json.loads((rig.root/'report/resource-result.json').read_text())
            self.assertEqual(value,actual.get('auth_attempt'))
            self.assertNotIn('auth',actual)
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1)],actual['failures'])
            self.assertFalse(actual['success']);self.assertTrue(actual['cleanup_complete'])

    def test_failed_auth_report_problem_never_replaces_primary_or_leaks_raw(self):
        # Testzweck: Fehlender/manipulierter Zahlenreport erhält nur seine feste Zusatzphase;
        # der echte Exit und die unveränderte Besitzprüfung beim Cleanup bleiben zuerst.
        for missing in (False,True):
            with self.subTest(missing=missing),self.synthetic_runtime() as rig:
                error=runner.MeasuredProcessExitError(1)
                def measured(args,**kwargs):
                    rig.report['sample_count']=1
                    if args[0]=='node':
                        if missing:(rig.root/'auth-result.json').unlink()
                        else:(rig.root/'auth-result.json').write_text(json.dumps({'raw':MARKER}))
                        raise error
                rig.measured_command=Mock(side_effect=measured)
                rig.inventory=Mock(side_effect=[[],[container(s) for s in BUDGETS],
                    [container(s) for s in BUDGETS],[]])
                with self.assertRaises(runner.ProcessExitError) as raised:rig.run()
                self.assertIs(error,raised.exception)
                raw=(rig.root/'report/resource-result.json').read_text();actual=json.loads(raw)
                self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                    dict(phase='auth_report',error='io' if missing else 'validation',exit_code=None)],actual['failures'])
                self.assertNotIn('auth_attempt',actual);self.assertNotIn('auth',actual)
                self.assertNotIn(MARKER,raw);self.assertTrue(actual['cleanup_complete'])

    @contextlib.contextmanager
    def synthetic_runtime(self, start_error=False):
        """Nur eigene Tempdateien und Mocks; kein Docker-, Auth-, Git- oder Netzprozess."""
        with tempfile.TemporaryDirectory() as temp, \
                patch.dict(os.environ, {'RUNNER_TEMP': temp, 'PATH': '/synthetic'}, clear=True):
            root = Path(__file__).resolve().parents[2]
            rig = runner.Rig(root, root, CONTEXT)
            rig.resource_count = 0

            def command(args, **kwargs):
                if args[0] == 'git':
                    return ('a' * 40 if args[-1] == 'HEAD' else 'b' * 40) + '\n'
                if args[:2] == ['docker', 'context']:
                    return 'unix:///var/run/docker.sock\n'
                if args[:2] == ['docker', 'info']:
                    return 'linux\tx86_64\t/var/lib/docker\n'
                return '123 /var/lib/postgresql/data\n'

            def prepare(*args):
                (rig.root / 'rig/tests/installation-auth').mkdir(parents=True)
                (rig.root / 'auth-result.json').write_text(json.dumps(dict(
                    total=16, passed=16, failed=0, skipped=0, interrupted=0, errors=0, success=True)))

            rows = [container(service) for service in BUDGETS]
            rig.command = Mock(side_effect=command)
            rig.browser_preflight = Mock()
            rig.pull = Mock()
            rig.disk = Mock()
            rig.measured_command = Mock(side_effect=ValueError(MARKER) if start_error else
                lambda *args, **kwargs: rig.report.update(sample_count=1))
            rig.inventory = Mock(side_effect=[[], rows, []] if start_error else [[], rows, rows, rows, []])
            with patch.object(runner.os, 'uname', return_value=SimpleNamespace(sysname='Linux', machine='x86_64')), \
                    patch.object(runner.proof, 'attest', return_value={'image_configs': CONFIGS}), \
                    patch.object(runner, 'prepare', side_effect=prepare):
                yield rig

    def test_prepare_start_and_cleanup_are_distinct_without_raw_errors(self):
        # Testzweck: Fehler vor Start und beim Cleanup erhalten eigene Phasen, keine erfundenen Exitcodes.
        for phase in ('prepare', 'start', 'cleanup'):
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as temp, \
                    patch.dict(os.environ, {'RUNNER_TEMP': temp, 'PATH': '/synthetic'}, clear=True):
                root = Path(__file__).resolve().parents[2]
                rig = runner.Rig(root, root, CONTEXT)
                rig.resource_count = 0

                def command(args, **kwargs):
                    if args[0] == 'git':
                        return ('a' * 40 if args[-1] == 'HEAD' else 'b' * 40) + '\n'
                    if args[:2] == ['docker', 'context']:
                        return 'unix:///var/run/docker.sock\n'
                    if args[:2] == ['docker', 'info']:
                        return 'linux\tx86_64\t/var/lib/docker\n'
                    return '123 /var/lib/postgresql/data\n'

                def prepare(*args):
                    if phase == 'prepare':
                        raise ValueError(MARKER)
                    (rig.root / 'rig/tests/installation-auth').mkdir(parents=True)
                    (rig.root / 'auth-result.json').write_text(json.dumps(dict(
                        total=16, passed=16, failed=0, skipped=0, interrupted=0, errors=0, success=True)))

                rows = [container(service) for service in BUDGETS]
                rig.command = Mock(side_effect=command)
                rig.browser_preflight = Mock()
                rig.pull = Mock()
                rig.disk = Mock()
                rig.measured_command = Mock(side_effect=ValueError(MARKER) if phase == 'start'
                    else lambda *args, **kwargs: rig.report.update(sample_count=1))
                rig.inventory = Mock(side_effect=[[], rows, []] if phase == 'start' else
                    [[], rows, rows, ValueError(MARKER) if phase == 'cleanup' else rows, []])
                with patch.object(runner.os, 'uname', return_value=SimpleNamespace(sysname='Linux', machine='x86_64')), \
                        patch.object(runner.proof, 'attest', return_value={'image_configs': CONFIGS}), \
                        patch.object(runner, 'prepare', side_effect=prepare):
                    with self.assertRaises(ValueError):
                        rig.run()
                raw = (rig.root / 'report/resource-result.json').read_text()
                value = json.loads(raw)
                self.assertEqual([dict(phase=phase, error='validation', exit_code=None)], value.get('failures', []))
                self.assertFalse(value['success'])
                self.assertNotIn(MARKER, raw)
                if phase == 'prepare':
                    self.assertFalse(value['runtime_started'])
                    self.assertFalse(value['cleanup_complete'])

    def test_process_exit_is_actual_including_zero_and_signal(self):
        # Testzweck: Echter Rückgabecode wird erhalten; auch unerwartete 0 ist kein erfundener Erfolg.
        for actual, accepted in ((37, 0), (-9, 0), (0, 1)):
            with self.subTest(actual=actual), tempfile.TemporaryDirectory() as temp, \
                    patch.dict(os.environ, {'RUNNER_TEMP': temp, 'PATH': '/synthetic'}, clear=True):
                rig = runner.Rig(Path('/unused'), Path('/unused'), CONTEXT)
                child = Mock(returncode=actual)
                child.communicate.return_value = (None, None)
                with patch.object(runner.subprocess, 'Popen', return_value=child):
                    with self.assertRaises(ValueError):
                        rig.command(['synthetic'], capture=False, accepted=accepted)
                self.assertEqual([dict(phase='preflight', error='process_exit', exit_code=actual)],
                    rig.report.get('failures', []))

    def test_sampling_failure_is_not_mislabelled_as_start_or_stop(self):
        # Testzweck: Die innere Messung bleibt Ursache, selbst wenn Start-/Stoprahmen sie weiterreichen.
        with tempfile.TemporaryDirectory() as temp, \
                patch.dict(os.environ, {'RUNNER_TEMP': temp, 'PATH': '/synthetic'}, clear=True):
            rig = runner.Rig(Path('/unused'), Path('/unused'), CONTEXT)
            child = Mock()
            child.poll.side_effect = [None, 0]
            rig.sample = Mock(side_effect=ValueError(MARKER))
            with patch.object(runner.subprocess, 'Popen', return_value=child):
                with self.assertRaises(ValueError):
                    rig.measured_command(['synthetic'], 1)
            self.assertEqual([dict(phase='sampling', error='validation', exit_code=None)],
                rig.report.get('failures', []))
            self.assertNotIn(MARKER, json.dumps(rig.report))

    def test_total_deadline_is_timeout_at_the_original_strict_boundary(self):
        # Testzweck: Auch der Gesamtdeadline-Ablauf ist Timeout; die ursprüngliche < Grenze bleibt strikt.
        with tempfile.TemporaryDirectory() as temp, \
                patch.dict(os.environ, {'RUNNER_TEMP': temp, 'PATH': '/synthetic'}, clear=True):
            rig = runner.Rig(Path('/unused'), Path('/unused'), CONTEXT)
            rig.current_phase = 'start'
            child = Mock()
            child.poll.side_effect = [None, 0]
            with patch.object(runner.subprocess, 'Popen', return_value=child), \
                    patch.object(runner.time, 'monotonic', side_effect=[0, 1]):
                with self.assertRaises(ValueError):
                    rig.measured_command(['synthetic'], 1)
            self.assertEqual([dict(phase='start', error='timeout', exit_code=None)], rig.report['failures'])

    def test_cleanup_disk_failure_cannot_leave_success_true(self):
        # Testzweck: Nach erfolgreicher Auth darf ein eigener Abschlussread nicht zugleich Erfolg melden.
        with self.synthetic_runtime() as rig:
            rig.disk.side_effect = lambda key: (_ for _ in ()).throw(OSError(MARKER)) \
                if key == 'after_cleanup' else None
            with self.assertRaises(OSError):
                rig.run()
            value = json.loads((rig.root / 'report/resource-result.json').read_text())
            self.assertFalse(value['success'])
            self.assertEqual([dict(phase='cleanup', error='io', exit_code=None)], value['failures'])
            self.assertNotIn(MARKER, json.dumps(value))

    def test_report_io_failure_preserves_primary_and_emits_safe_console_diagnosis(self):
        # Testzweck: Eigene Open-/Write-/Chmodfehler sind auch ohne Artefakt sichtbar, nie als Rohtext.
        for operation in ('open', 'write', 'chmod'):
            for start_error in (False, True):
                with self.subTest(operation=operation, start_error=start_error), \
                        self.synthetic_runtime(start_error) as rig:
                    original_open, original_chmod = Path.open, Path.chmod

                    def open_file(path, *args, **kwargs):
                        if path.name != 'resource-result.json':
                            return original_open(path, *args, **kwargs)
                        if operation == 'open':
                            raise OSError(MARKER)
                        if operation == 'write':
                            stream = Mock()
                            stream.__enter__ = Mock(return_value=stream)
                            stream.__exit__ = Mock(return_value=False)
                            stream.write.side_effect = OSError(MARKER)
                            return stream
                        return original_open(path, *args, **kwargs)

                    def chmod_file(path, *args, **kwargs):
                        if path.name == 'resource-result.json' and operation == 'chmod':
                            raise OSError(MARKER)
                        return original_chmod(path, *args, **kwargs)

                    output = io.StringIO()
                    with patch.object(Path, 'open', open_file), patch.object(Path, 'chmod', chmod_file), \
                            patch.object(runner, 'Rig', return_value=rig), patch.object(runner.signal, 'signal'), \
                            patch('sys.argv', ['runner', '--source', '/unused', '--publisher', '/unused']), \
                            patch.dict(os.environ, {name: 'synthetic' for name in ('GITHUB_REPOSITORY', 'GITHUB_REF',
                                'GITHUB_EVENT_NAME', 'GITHUB_RUN_ATTEMPT', 'GITHUB_RUN_ID', 'GITHUB_SHA',
                                'CONFIRMED_WORKFLOW_SHA', 'RUNNER_ENVIRONMENT', 'RUNNER_OS', 'RUNNER_ARCH')}), \
                            contextlib.redirect_stderr(output), self.assertRaises(SystemExit):
                        runner.main()
                    expected = ([dict(phase='start', error='validation', exit_code=None)] if start_error else []) \
                        + [dict(phase='cleanup', error='io', exit_code=None)]
                    projected = json.loads(output.getvalue().splitlines()[-1])
                    self.assertEqual(expected, projected['failures'])
                    self.assertNotIn(MARKER, output.getvalue())
                    if operation == 'chmod':
                        # Vor erfolgreichem Rechte-Setzen dürfen noch keine Erfolgsbytes im Artefakt stehen.
                        self.assertEqual(b'', (rig.root / 'report/resource-result.json').read_bytes())

    def test_timeout_and_stop_failure_preserve_both_safe_causes(self):
        # Testzweck: Ein Stopfehler überschreibt nicht den Timeout; ohne Exitbeleg bleibt der Wert unbekannt.
        with tempfile.TemporaryDirectory() as temp, \
                patch.dict(os.environ, {'RUNNER_TEMP': temp, 'PATH': '/synthetic'}, clear=True):
            rig = runner.Rig(Path('/unused'), Path('/unused'), CONTEXT)
            child = Mock(pid=12345)
            child.communicate.side_effect = subprocess.TimeoutExpired([MARKER], 1)
            with patch.object(runner.subprocess, 'Popen', return_value=child), \
                    patch.object(runner.os, 'killpg', side_effect=OSError(MARKER)) as kill:
                with self.assertRaises(OSError):
                    rig.command(['synthetic'], capture=False)
            kill.assert_called_once_with(12345, signal.SIGTERM)
            self.assertEqual([dict(phase='preflight', error='timeout', exit_code=None),
                dict(phase='stop', error='io', exit_code=None)], rig.report.get('failures', []))
            self.assertNotIn(MARKER, json.dumps(rig.report))

    def test_pre_rig_failure_emits_only_closed_unknown_diagnostic(self):
        # Testzweck: Selbst vor Report-/Rig-Anlage bleiben Fehlertexte und Argumente außerhalb der Konsole.
        env = {key: 'synthetic' for key in ('GITHUB_REPOSITORY', 'GITHUB_REF', 'GITHUB_EVENT_NAME',
            'GITHUB_RUN_ATTEMPT', 'GITHUB_RUN_ID', 'GITHUB_SHA', 'CONFIRMED_WORKFLOW_SHA',
            'RUNNER_ENVIRONMENT', 'RUNNER_OS', 'RUNNER_ARCH')}
        output = io.StringIO()
        with patch.dict(os.environ, env, clear=True), patch('sys.argv', ['runner', '--source', '/unused', '--publisher', '/unused']), \
                patch.object(runner.signal, 'signal'), patch.object(runner, 'Rig', side_effect=RuntimeError(MARKER)), \
                contextlib.redirect_stderr(output), self.assertRaises(SystemExit) as stopped:
            runner.main()
        self.assertEqual(1, stopped.exception.code)
        self.assertNotIn(MARKER, output.getvalue())
        self.assertIn('"phase": "preflight"', output.getvalue())
        self.assertIn('"exit_code": null', output.getvalue())


if __name__ == '__main__':
    unittest.main()
