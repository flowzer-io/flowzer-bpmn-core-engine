"""Testzweck: Runnergrenzen prüfen ohne Docker/Images/Prozesse/Netz/Secrets."""
import copy
from contextlib import contextmanager
import os
import signal
import subprocess
import tempfile
from types import SimpleNamespace
from unittest.mock import Mock
from pathlib import Path
import unittest
from unittest.mock import patch
import runner
from prepare import BUDGETS,IMAGES

CONTEXT=dict(repository='flowzer-io/flowzer-bpmn-core-engine',ref='refs/heads/codex/flowzer-runtime-calibration-pilot',
    event='workflow_dispatch',attempt='1',run_id='123456',sha='a'*40,confirmed_sha='a'*40,
    runner_environment='github-hosted',runner_os='Linux',runner_arch='X64')
PROJECT='flowzer-runtime-123456-a1'
CONFIGS={service:'sha256:'+'c'*64 for service in BUDGETS}

# Unabhängige feste Projektion: nicht aus runner.COLUMNS ableiten, sonst könnte
# ein fehlerhafter oder erweiterter Ausdruck gleichzeitig Test und Quelle ändern.
EXPECTED_INSPECT_COLUMNS={
    'id':'.Id',
    'project':'index .Config.Labels "com.docker.compose.project"',
    'owner':'index .Config.Labels "io.flowzer.runtime.owner"',
    'service':'index .Config.Labels "com.docker.compose.service"',
    'oneoff':'index .Config.Labels "com.docker.compose.oneoff"',
    'image':'.Config.Image','image_id':'.Image','memory':'.HostConfig.Memory',
    'swap':'.HostConfig.MemorySwap','nano_cpus':'.HostConfig.NanoCpus',
    'oom':'.State.OOMKilled','running':'.State.Running',
    'exit_code':'.State.ExitCode','restarts':'.RestartCount'}
EXPECTED_INSPECT='{'+','.join('"'+key+'":{{json ('+expression+')}}'
    for key,expression in EXPECTED_INSPECT_COLUMNS.items())+'}'

def container(service='api'):
    mib,cpu=BUDGETS[service]
    return {'id':'b'*64,'project':PROJECT,'owner':PROJECT,'service':service,'oneoff':'False',
        'image':IMAGES['api' if service=='migrate' else service],'image_id':CONFIGS[service],
        'memory':mib*1024**2,'swap':mib*1024**2,'nano_cpus':int(float(cpu)*10**9),
        'oom':False,'running':True,'exit_code':0,'restarts':0}

class RunnerTests(unittest.TestCase):
    def test_complete_inspect_format_groups_every_json_argument(self):
        # Testzweck: Alle 14 Felder bleiben geschlossen; index-Aufrufe sind je EIN json-Argument.
        # Dies ist ein Quellenvertrag, kein behaupteter echter Docker-/Go-Parserlauf.
        self.assertEqual(EXPECTED_INSPECT_COLUMNS,runner.COLUMNS)
        self.assertEqual(EXPECTED_INSPECT,runner.INSPECT)

    def test_actual_inventory_sends_complete_grouped_inspect_format(self):
        # Testzweck: Der echte Inventorypfad nutzt den gesamten festen Formatvertrag,
        # nicht bloß einen separat korrigierten Labelstring oder einen Roh-Inspectdump.
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            rig=runner.Rig(Path('/unused'),Path('/unused'),CONTEXT);rig.configs=CONFIGS
            row=container();rig.command=Mock(side_effect=[row['id'],runner.json.dumps(row),'',''])
            self.assertEqual([row],rig.inventory())
            self.assertEqual([
                ['docker','ps','-aq','--no-trunc','--filter','label=com.docker.compose.project='+PROJECT],
                ['docker','inspect','--format',EXPECTED_INSPECT,row['id']],
                ['docker','network','ls','--filter','label=com.docker.compose.project='+PROJECT,'--format','{{.ID}}'],
                ['docker','volume','ls','--filter','label=com.docker.compose.project='+PROJECT,'--format','{{.Name}}']
            ],[entry.args[0] for entry in rig.command.call_args_list])
            self.assertEqual(0,rig.resource_count)

    def test_hard_context_and_transport_guards_before_runtime(self):
        # Testzweck: Fremdref/Retry und manipulierte Host-/Browser-/Proxyumgebung sind kein Runtimepfad.
        self.assertEqual(PROJECT,runner.environment_guard(CONTEXT,{}))
        for key in ['DOCKER_HOST','DOCKER_CONTEXT','NODE_OPTIONS','HTTP_PROXY','HTTPS_PROXY','ALL_PROXY',
            'PW_TEST_CONNECT_WS_ENDPOINT','PW_TEST_REUSE_CONTEXT','PLAYWRIGHT_DISABLE_FORCED_CHROMIUM_PROXIED_LOOPBACK']:
            with self.subTest(key=key),self.assertRaises(ValueError):runner.environment_guard(CONTEXT,{key:'synthetic-foreign'})
        for key,value in [('event','push'),('attempt','2'),('ref','refs/heads/main')]:
            with self.assertRaises(ValueError):runner.environment_guard(CONTEXT|{key:value},{})

    def test_container_projection_requires_ownership_images_and_actual_limits(self):
        # Testzweck: Nur eigene CIDs mit exakten Digest-/RAM-/CPU-/Noswapbindungen werden Messdaten.
        result=runner.container_row(container(),PROJECT,CONFIGS)
        self.assertEqual({'service':'api','memory_limit_bytes':536870912,'nano_cpus':500000000,
            'oom':False,'running':True,'exit_code':0,'restarts':0,'oneoff':False},result)
        for key,value in [('project','foreign'),('owner','foreign'),('id','short'),('service','foreign'),
            ('image','other:latest'),('image_id','sha256:'+'d'*64),('memory',1),('swap',-1),('nano_cpus',0),('oom','false')]:
            with self.subTest(key=key),self.assertRaises(ValueError):runner.container_row(container()|{key:value},PROJECT,CONFIGS)

    def test_transient_oneoff_remains_owned_and_identified(self):
        # Testzweck: --check-config darf als eigene API-Oneoff gemessen werden, nicht fremde Dienste legitimieren.
        self.assertEqual('api',runner.container_row(container()|{'oneoff':'True'},PROJECT,CONFIGS)['service'])
        with self.assertRaises(ValueError):runner.container_row(container()|{'oneoff':'unknown'},PROJECT,CONFIGS)

    def test_network_and_volume_cleanup_requires_double_label_binding(self):
        # Testzweck: Kein cleanup eines gleich benannten fremden Volumes oder eines nichtinternen Netzes.
        network={'name':PROJECT+'_default','project':PROJECT,'owner':PROJECT,'internal':True}
        volume={'name':PROJECT+'_db-data','project':PROJECT,'owner':PROJECT}
        self.assertTrue(runner.resource_row(network,PROJECT,'network'))
        self.assertTrue(runner.resource_row(volume,PROJECT,'volume'))
        for row,kind in [(network|{'internal':False},'network'),(network|{'owner':'other'},'network'),
            (volume|{'project':'other'},'volume'),(volume|{'name':'other_db-data'},'volume')]:
            with self.assertRaises(ValueError):runner.resource_row(row,PROJECT,kind)

    def test_auth_report_cannot_hide_skips_errors_or_raw_fields(self):
        # Testzweck: Erfolgreiches Prozessende allein reicht nicht, jeder Originaltest muss wirklich bestanden sein.
        report=dict(total=24,passed=24,failed=0,skipped=0,interrupted=0,errors=0,success=True)
        self.assertEqual(report,runner.auth_result(report))
        for value in [report|{'skipped':1},report|{'errors':1},report|{'passed':23},report|{'total':0},
            report|{'raw':'never-log'},report|{'success':1},report|{'failed':False}]:
            with self.assertRaises(ValueError):runner.auth_result(value)

    def test_total_ram_is_daemon_cgroup_usage_not_cli_cache_subtraction(self):
        # Testzweck: Schutzbedarf darf nicht durch eine cachebereinigte CLI-Anzeige zu klein dargestellt werden.
        value={'memory_stats':{'usage':128000000,'limit':536870912}}
        self.assertEqual(128000000,runner.ram_usage(value,'api'))
        for changed in [{'memory_stats':{'usage':'128000000','limit':536870912}},
            {'memory_stats':{'usage':128,'limit':1}}, {'memory_stats':{'usage':-1,'limit':536870912}}]:
            with self.assertRaises(ValueError):runner.ram_usage(changed,'api')

    def test_egress_report_has_exact_numeric_whitelist(self):
        # Testzweck: Ein manipulierter Bericht darf weder Rohfelder exportieren noch falsche Negativproben attestieren.
        value=dict(calibrated=False,positive_control_requests=1,allowed_page=1,redirect_ip_blocked=1,
            redirect_host_blocked=1,direct_ip_blocked=1,websocket_blocked=1,serviceworker_blocked=1,
            marker_requests=0,marker_assertion_red=0,success=True)
        self.assertEqual(value,runner.egress_result(value))
        for changed in [value|{'raw':'never-save'},value|{'marker_requests':1},value|{'redirect_ip_blocked':False}]:
            with self.assertRaises(ValueError):runner.egress_result(changed)

    def test_empty_gate_counts_networks_and_volumes_too(self):
        # Testzweck: Übrig gebliebene eigene Namen/Labels sind kein frischer isolierter Namespace.
        self.assertTrue(runner.empty_inventory([],0))
        for rows,count in [([],1),([container()],0)]:
            with self.assertRaises(ValueError):runner.empty_inventory(rows,count)

    def test_finished_zero_stats_require_retained_successful_actual_state(self):
        # Testzweck: Ein beendeter eigener Container ist keine Fake-Nullmessung; laufende/abgebrochene/OOM-Zustände verboten.
        self.assertTrue(runner.completed_container(container()|{'running':False}))
        for row in [container(),container()|{'running':False,'oom':True},container()|{'running':False,'exit_code':1}]:
            with self.assertRaises(ValueError):runner.completed_container(row)

    def test_pg_allocated_kib_are_not_claimed_as_exact_logical_bytes(self):
        # Testzweck: BusyBox-portables -sk wird klar als allozierte gerundete Bytes projiziert; fremde Pfade fehlen.
        self.assertEqual(125952,runner.directory_bytes('123 /var/lib/postgresql/data'))
        for value in ['secret /var/lib/postgresql/data','123 /foreign','-1 /var/lib/postgresql/data']:
            with self.assertRaises(ValueError):runner.directory_bytes(value)

    def test_late_oom_during_stats_is_recorded_before_failure(self):
        # Testzweck: Zwischen Probe und Stats beendeter OOM bleibt Diagnose, auch bei leeren CLI-/Daemonwerten.
        for phase in ['zero_cli','missing_cli','empty_daemon']:
            with self.subTest(phase=phase),tempfile.TemporaryDirectory() as temp,\
                patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
                rig=runner.Rig(Path('/unused'),Path('/unused'),CONTEXT);rig.configs=CONFIGS
                rig.inventory=Mock(return_value=[container()])
                stopped=container()|{'running':False,'oom':True,'exit_code':137}
                cli='' if phase=='missing_cli' else 'b'*64+'\t0%\t'+('0B / 0B' if phase=='zero_cli' else '1MiB / 512MiB')+'\t0\n'
                rig.command=Mock(side_effect=[cli,runner.json.dumps(stopped)])
                connection=Mock();connection.getresponse.return_value=Mock(status=200)
                connection.getresponse.return_value.read.return_value=b'{"memory_stats":{}}'
                with patch.object(runner,'DockerSocket',return_value=connection):
                    with self.assertRaises(ValueError):rig.sample()
                self.assertIs(rig.report['oom_observed'],True)

    def test_inventory_records_only_bound_oom_monotonically(self):
        # Testzweck: Cleanup-Inventur merkt eigenen OOM dauerhaft, übernimmt aber keine fremden/malformierten Zustände.
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            rig=runner.Rig(Path('/unused'),Path('/unused'),CONTEXT);rig.configs=CONFIGS
            for row,expected in [(container()|{'owner':'foreign','oom':True},False),
                (container()|{'oom':True},True),(container(),True)]:
                rig.command=Mock(side_effect=['b'*64,runner.json.dumps(row),'',''])
                if not expected:
                    with self.assertRaises(ValueError):rig.inventory()
                else:self.assertEqual([row],rig.inventory())
                self.assertIs(rig.report['oom_observed'],expected)

    def test_final_state_oom_is_recorded_before_rejection(self):
        # Testzweck: Auch die letzte gebundene Zustandsprüfung ohne weitere Probe erhält den OOM im Fehlerartefakt.
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            root=Path(__file__).resolve().parents[2];rig=runner.Rig(root,root,CONTEXT);rig.resource_count=0
            def command(args,**kwargs):
                if args[0]=='git':return ('a'*40 if args[-1]=='HEAD' else 'b'*40)+'\n'
                if args[:2]==['docker','context']:return 'unix:///var/run/docker.sock\n'
                if args[:2]==['docker','info']:return 'linux\tx86_64\t/var/lib/docker\n'
                return '123456 /var/lib/postgresql/data\n'
            rows=[container(service) for service in BUDGETS]
            final=[row|{'oom':row['service']=='api'} for row in rows]
            report=dict(total=16,passed=16,failed=0,skipped=0,interrupted=0,errors=0,success=True)
            def prepare(*args):
                (rig.root/'rig/tests/installation-auth').mkdir(parents=True)
                (rig.root/'auth-result.json').write_text(runner.json.dumps(report))
            rig.command=Mock(side_effect=command);rig.browser_preflight=Mock();rig.pull=Mock();rig.disk=Mock()
            rig.measured_command=Mock(side_effect=lambda *args,**kwargs:rig.report.update(sample_count=1))
            rig.inventory=Mock(side_effect=[[],rows,final,rows,[]])
            with patch.object(runner.os,'uname',return_value=SimpleNamespace(sysname='Linux',machine='x86_64')),\
                patch.object(runner.proof,'attest',return_value={'image_configs':CONFIGS}),patch.object(runner,'prepare',side_effect=prepare):
                with self.assertRaises(ValueError):rig.run()
            value=runner.json.loads((rig.root/'report/resource-result.json').read_bytes())
            self.assertIs(value['success'],False);self.assertIs(value['oom_observed'],True)

    def test_failed_start_cleanup_preserves_actual_inventory_oom(self):
        # Testzweck: Sofortiger Startfehler vor nächster Probe liest OOM bei Cleanup und speichert keine falsche Negativdiagnose.
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            root=Path(__file__).resolve().parents[2];rig=runner.Rig(root,root,CONTEXT);rig.resource_count=0
            original_inventory=rig.inventory
            # Erste Inventur ist leer; Cleanup und abschließende Leerprüfung nutzen den wirklichen Projektionscode.
            inventory_calls=iter([[],original_inventory,original_inventory])
            bound=container()|{'oom':True,'running':False,'exit_code':137}
            inventories=iter(['b'*64,''])
            def command(args,**kwargs):
                if args[0]=='git':return ('a'*40 if args[-1]=='HEAD' else 'b'*40)+'\n'
                if args[:2]==['docker','context']:return 'unix:///var/run/docker.sock\n'
                if args[:2]==['docker','info']:return 'linux\tx86_64\t/var/lib/docker\n'
                if args[:2]==['docker','ps']:return next(inventories)
                if args[:2]==['docker','inspect']:return runner.json.dumps(bound)
                return ''
            def inventory():
                value=next(inventory_calls)
                return value() if callable(value) else value
            rig.inventory=inventory
            rig.command=Mock(side_effect=command);rig.browser_preflight=Mock();rig.pull=Mock();rig.disk=Mock()
            rig.measured_command=Mock(side_effect=ValueError('synthetic-start-failure'))
            with patch.object(runner.os,'uname',return_value=SimpleNamespace(sysname='Linux',machine='x86_64')),\
                patch.object(runner.proof,'attest',return_value={'image_configs':CONFIGS}),patch.object(runner,'prepare'):
                with self.assertRaises(ValueError):rig.run()
            value=runner.json.loads((rig.root/'report/resource-result.json').read_bytes())
            self.assertIs(value['success'],False);self.assertIs(value['cleanup_complete'],True)
            self.assertIs(value['oom_observed'],True)

    def test_timeout_closes_only_own_new_process_group(self):
        # Testzweck: Browser-/Init-Unterprozesse dürfen bei Timeout nicht als eigener Ressourcenrest bleiben.
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            rig=runner.Rig(Path('/unused'),Path('/unused'),CONTEXT)
            child=Mock(pid=12345);child.communicate.side_effect=subprocess.TimeoutExpired(['synthetic'],1)
            with patch.object(runner.subprocess,'Popen',return_value=child) as spawn,patch.object(runner.os,'killpg') as kill:
                with self.assertRaises(subprocess.TimeoutExpired):rig.command(['synthetic'],timeout=1,capture=False)
                self.assertIs(spawn.call_args.kwargs['start_new_session'],True)
                kill.assert_called_once_with(12345,signal.SIGTERM);child.wait.assert_called_once_with(timeout=5)

    def test_expected_calibration_failure_is_not_normal_runtime_success(self):
        # Testzweck: Nur die eigens gekennzeichnete rote Kalibrierung darf Status1 als erwartetes Ergebnis nutzen.
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            rig=runner.Rig(Path('/unused'),Path('/unused'),CONTEXT)
            child=Mock(returncode=1);child.communicate.return_value=(None,None)
            with patch.object(runner.subprocess,'Popen',return_value=child):
                with self.assertRaises(ValueError):rig.command(['synthetic'],capture=False)
                self.assertEqual('',rig.command(['synthetic'],capture=False,accepted=1))

    def test_cleanup_failure_still_emits_false_closed_numeric_report(self):
        # Testzweck: Ein Ownership-/Cleanupfehler darf niemals stillen Erfolg oder Rohfehler als Artefakt erzeugen.
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            root=Path(__file__).resolve().parents[2];rig=runner.Rig(root,root,CONTEXT);rig.resource_count=0
            def command(args,**kwargs):
                if args[0]=='git':return ('a'*40 if args[-1]=='HEAD' else 'b'*40)+'\n'
                if args[:2]==['docker','context']:return 'unix:///var/run/docker.sock\n'
                if args[:2]==['docker','info']:return 'linux\tx86_64\t/var/lib/docker\n'
                return '123456 /var/lib/postgresql/data\n'
            rows=[container(service) for service in BUDGETS]
            report=dict(total=16,passed=16,failed=0,skipped=0,interrupted=0,errors=0,success=True)
            def prepare(*args):
                (rig.root/'rig/tests/installation-auth').mkdir(parents=True)
                (rig.root/'auth-result.json').write_text(runner.json.dumps(report))
            rig.command=Mock(side_effect=command);rig.browser_preflight=Mock();rig.pull=Mock();rig.disk=Mock()
            rig.measured_command=Mock(side_effect=lambda *args,**kwargs:rig.report.update(sample_count=1))
            rig.inventory=Mock(side_effect=[[],rows,rows,ValueError('never-save-raw-error')])
            with patch.object(runner.os,'uname',return_value=SimpleNamespace(sysname='Linux',machine='x86_64')),\
                patch.object(runner.proof,'attest',return_value={'image_configs':CONFIGS}),patch.object(runner,'prepare',side_effect=prepare):
                with self.assertRaises(ValueError):rig.run()
            raw=(rig.root/'report/resource-result.json').read_text();value=runner.json.loads(raw)
            self.assertIs(value['success'],False);self.assertIs(value['cleanup_complete'],False)
            self.assertNotIn('never-save-raw-error',raw)


class BrowserPreflightTests(unittest.TestCase):
    """Echte Vorabtest-Orchestrierung, aber niemals echte Kindprozesse oder Browser."""

    @contextmanager
    def preflight(self,calibration_exit=1,launch_error=None,calibration_change=None,missing_report=None):
        """Nur eigene Zahlenfixtures; Popen wird an der tatsächlich verwendeten Grenze ersetzt."""
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            source=Path(__file__).resolve().parents[2];rig=runner.Rig(source,source,CONTEXT)
            auth=rig.root/'rig/tests/installation-auth';auth.mkdir(parents=True)
            cli=auth/'node_modules/playwright/cli.js';probe=source/'scripts/demo-runtime/egress-probe.js'
            expected=[(['npm','ci','--ignore-scripts','--no-audit','--no-fund'],300,auth),
                (['node',str(cli),'install','--with-deps','chromium'],600,auth),
                (['node',str(probe),str(rig.root/'calibration/egress-result.json'),'--calibrate-local-only'],60,None),
                (['node',str(probe),str(rig.root/'egress/egress-result.json')],60,None),
                (['node',str(cli),'test','--config',str(source/'scripts/demo-runtime/fixture-probe.config.js')],90,None)]
            normal=dict(calibrated=False,positive_control_requests=1,allowed_page=1,redirect_ip_blocked=1,
                redirect_host_blocked=1,direct_ip_blocked=1,websocket_blocked=1,serviceworker_blocked=1,
                marker_requests=0,marker_assertion_red=0,success=True)
            calibration=normal|dict(calibrated=True,redirect_ip_blocked=0,redirect_host_blocked=0,
                direct_ip_blocked=0,websocket_blocked=0,serviceworker_blocked=0,marker_requests=1,
                marker_assertion_red=1,success=False,failure_phase='redirect_ip_blocked',
                assertion_failed=True,failure_source='closed_other')
            calibration.update(calibration_change or {})
            fixture=dict(total=2,passed=2,failed=0,skipped=0,interrupted=0,errors=0,success=True)
            calls=[]

            def spawn(args,**kwargs):
                # Unbekannte oder umsortierte Aufrufe sind Assertionfehler, nie reale Prozesse.
                index=len(calls);self.assertLess(index,len(expected));self.assertEqual(expected[index][0],args)
                self.assertEqual(expected[index][2],kwargs['cwd'])
                self.assertIs(kwargs['start_new_session'],True)
                self.assertEqual(subprocess.DEVNULL,kwargs['stdout']);self.assertEqual(subprocess.DEVNULL,kwargs['stderr'])
                observation=dict(args=list(args),cwd=kwargs['cwd'],env=dict(kwargs['env']));calls.append(observation)
                if index==2 and launch_error is not None:raise launch_error
                child=Mock(returncode=calibration_exit if index==2 else 0)

                def communicate(timeout):
                    observation['timeout']=timeout;self.assertEqual(expected[index][1],timeout)
                    observation['observed_exit']=child.returncode
                    report=None
                    if index==2:report=('calibration',rig.root/'calibration/egress-result.json',calibration)
                    elif index==3:report=('egress',rig.root/'egress/egress-result.json',normal)
                    elif index==4:report=('fixture',Path(kwargs['env']['FLOWZER_RUNTIME_REPORT']),fixture)
                    if report is not None and missing_report!=report[0]:
                        report[1].write_text(runner.json.dumps(report[2]),encoding='utf-8')
                    return None,None
                child.communicate.side_effect=communicate;return child

            # Zusätzliche Tripwires: selbst eine versehentliche Folgeaktion ist nur ein Mockfehler.
            denied=AssertionError('Kein Runtime-/Netzschritt im hermetischen Vorabtest.')
            rig.inventory=Mock(side_effect=denied);rig.pull=Mock(side_effect=denied);rig.measured_command=Mock(side_effect=denied)
            with patch.object(runner.subprocess,'Popen',side_effect=spawn),\
                patch.object(runner,'DockerSocket',side_effect=denied),patch.object(runner.proof,'attest',side_effect=denied):
                yield rig,auth,calls,expected,normal,fixture
                rig.inventory.assert_not_called();rig.pull.assert_not_called();rig.measured_command.assert_not_called()

    def assert_failure(self,rig,calls,phase,error,exit_code=None,call_count=3):
        """Geschlossene Einzeldiagnose und Abbruch vor Folgeprobe/Fixture/Runtime sichern."""
        self.assertEqual([dict(phase=phase,error=error,exit_code=exit_code)],rig.report['failures'])
        self.assertEqual(call_count,len(calls));self.assertEqual('preflight',rig.current_phase)
        self.assertIs(rig.report['success'],False);self.assertIs(rig.report['runtime_started'],False)
        self.assertEqual(0,rig.report['sample_count'])
        for field in ['fixture','egress','calibration_marker_assertion_red']:self.assertNotIn(field,rig.report)

    def test_calibration_launch_io_is_distinct_before_child_exit(self):
        # Testzweck: Popen-/CWD-I/O ohne beobachteten Kindstatus darf nicht als fehlender Bericht erscheinen.
        failure=FileNotFoundError('synthetic-private-launch-error')
        with self.preflight(launch_error=failure) as (rig,auth,calls,*_):
            with self.assertRaises(FileNotFoundError) as caught:rig.browser_preflight(auth)
            self.assertIs(failure,caught.exception);self.assertNotIn('observed_exit',calls[-1])
            self.assert_failure(rig,calls,'browser_calibration_process','io')

    def test_calibration_exit_one_requires_report_in_separate_phase(self):
        # Testzweck: Akzeptierter wirklicher Mock-Exit1 ersetzt nie den Markerbeleg; fehlende Datei bleibt I/O/null.
        with self.preflight(missing_report='calibration') as (rig,auth,calls,*_):
            with self.assertRaises(FileNotFoundError):rig.browser_preflight(auth)
            self.assertEqual(1,calls[-1]['observed_exit'])
            self.assert_failure(rig,calls,'browser_calibration_report','io')

    def test_calibration_unexpected_exit_zero_stops_before_report(self):
        # Testzweck: Unerwarteter Exit0 bleibt tatsächlicher Status0 und darf keinen scheinbaren Markererfolg liefern.
        with self.preflight(calibration_exit=0) as (rig,auth,calls,*_):
            with self.assertRaises(runner.ProcessExitError) as caught:rig.browser_preflight(auth)
            self.assertEqual(0,caught.exception.exit_code);self.assertEqual(0,calls[-1]['observed_exit'])
            self.assert_failure(rig,calls,'browser_calibration_process','process_exit',exit_code=0)

    def test_calibration_setup_error_is_not_marker_red(self):
        # Testzweck: Setup-/Browserfehler nach Exit1 bleibt ungültig, auch wenn ein sicherer Zahlenbericht existiert.
        change=dict(marker_assertion_red=0,marker_requests=0,positive_control_requests=0,allowed_page=0,failure_phase='fixture')
        with self.preflight(calibration_change=change) as (rig,auth,calls,*_):
            with self.assertRaises(ValueError):rig.browser_preflight(auth)
            self.assertEqual(1,calls[-1]['observed_exit'])
            self.assert_failure(rig,calls,'browser_calibration_report','validation')

    def test_calibration_inconsistent_markers_stop_before_followups(self):
        # Testzweck: Jede bisher verlangte Kalibrierungszahl bleibt verbindlich; kein Exit1-Fallback zur Folgeprobe.
        for change in [dict(marker_assertion_red=0),dict(marker_requests=0),dict(marker_requests=2),
            dict(positive_control_requests=0),dict(allowed_page=0),dict(success=True)]:
            with self.subTest(change=change),self.preflight(calibration_change=change) as (rig,auth,calls,*_):
                with self.assertRaises(ValueError):rig.browser_preflight(auth)
                self.assert_failure(rig,calls,'browser_calibration_report','validation')

    def test_valid_calibration_preserves_complete_preflight_contract(self):
        # Testzweck: Neue Diagnose verändert weder Befehle/Timeouts/Umgebungsgrenze noch den strengen gültigen Ablauf.
        with self.preflight() as (rig,auth,calls,expected,normal,fixture):
            rig.browser_preflight(auth)
            self.assertEqual([row[0] for row in expected],[row['args'] for row in calls])
            self.assertEqual([row[1] for row in expected],[row['timeout'] for row in calls])
            self.assertEqual([0,0,1,0,0],[row['observed_exit'] for row in calls])
            self.assertEqual([],rig.report['failures']);self.assertEqual('preflight',rig.current_phase)
            self.assertEqual(normal,rig.report['egress']);self.assertEqual(fixture,rig.report['fixture'])
            self.assertEqual(1,rig.report['calibration_marker_assertion_red'])
            self.assertIs(rig.report['success'],False);self.assertIs(rig.report['runtime_started'],False)
            self.assertEqual(0,rig.report['sample_count'])
            for row in calls:
                self.assertEqual(str(auth/'node_modules'),row['env']['NODE_PATH'])
                self.assertEqual({'PATH','HOME','DOCKER_CONFIG','PLAYWRIGHT_BROWSERS_PATH','CI','NODE_PATH'}|
                    ({'FLOWZER_RUNTIME_REPORT'} if row is calls[-1] else set()),set(row['env']))

    def test_calibration_closed_substage_failures_are_projected(self):
        # Testzweck: Akzeptierter Exit1 erhält nur geschlossene echte JS-Vorstufen, nie Rohfehler oder Marker-RED.
        cases=[('modules','io',None),('contract','validation',None),('certificate','process_exit',2)]
        for phase,error,exit_code in cases:
            with self.subTest(phase=phase),self.preflight(calibration_change=dict(marker_assertion_red=0,
                    marker_requests=0,positive_control_requests=0,allowed_page=0,
                    failures=[dict(phase=phase,error=error,exit_code=exit_code)])) as (rig,auth,calls,*_):
                with self.assertRaises(ValueError):rig.browser_preflight(auth)
                self.assert_failure(rig,calls,'browser_probe_'+phase,error,exit_code)

    def test_calibration_marker_red_never_hides_cleanup_failure(self):
        # Testzweck: Erwarteter Marker-Assert ist kein Cleanup-Go; zusätzlicher Schließfehler bleibt echte Ursache.
        failures=[dict(phase='redirect_ip_blocked',error='validation',exit_code=None),
            dict(phase='browser_close',error='io',exit_code=None)]
        with self.preflight(calibration_change=dict(failures=failures)) as (rig,auth,calls,*_):
            with self.assertRaises(ValueError):rig.browser_preflight(auth)
            self.assert_failure(rig,calls,'browser_probe_browser_close','io')

    def test_probe_failure_fields_remain_closed_and_bounded(self):
        # Testzweck: Freie Phasen, Texte, Bool-/erfundene Exitwerte und mehr als drei Fehler öffnen keine Folgeprobe.
        valid=dict(phase='modules',error='io',exit_code=None)
        for failures in [[valid|dict(phase='never-save-private')],[valid|dict(error='never-save-private')],
                [valid|dict(raw='never-save-private')],[valid|dict(exit_code=True)],
                [valid|dict(error='process_exit',exit_code=None)],[valid]*4]:
            with self.subTest(failures=failures),self.preflight(calibration_change=dict(marker_assertion_red=0,
                    marker_requests=0,failures=failures)) as (rig,auth,calls,*_):
                with self.assertRaises(ValueError):rig.browser_preflight(auth)
                self.assert_failure(rig,calls,'browser_calibration_report','validation')
                self.assertNotIn('never-save-private',runner.json.dumps(rig.report))

    def test_later_report_io_keeps_outer_browser_phase(self):
        # Testzweck: Kalibrierungsunterphasen enden nach ihrer Operation und etikettieren spätere I/O nicht um.
        for missing,count in [('egress',4),('fixture',5)]:
            with self.subTest(missing=missing),self.preflight(missing_report=missing) as (rig,auth,calls,*_):
                with self.assertRaises(FileNotFoundError):rig.browser_preflight(auth)
                self.assertEqual(0,calls[-1]['observed_exit'])
                self.assert_failure(rig,calls,'browser_preflight','io',call_count=count)

if __name__=='__main__':unittest.main()
