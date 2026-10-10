"""Testzweck: Runnergrenzen prüfen ohne Docker/Images/Prozesse/Netz/Secrets."""
import copy
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

CONTEXT=dict(repository='flowzer-io/flowzer-bpmn-core-engine',ref='refs/heads/codex/flowzer-runtime-pilot-diagnostics',
    event='workflow_dispatch',attempt='1',run_id='123456',sha='a'*40,confirmed_sha='a'*40,
    runner_environment='github-hosted',runner_os='Linux',runner_arch='X64')
PROJECT='flowzer-runtime-123456-a1'
CONFIGS={service:'sha256:'+'c'*64 for service in BUDGETS}

def container(service='api'):
    mib,cpu=BUDGETS[service]
    return {'id':'b'*64,'project':PROJECT,'owner':PROJECT,'service':service,'oneoff':'False',
        'image':IMAGES['api' if service=='migrate' else service],'image_id':CONFIGS[service],
        'memory':mib*1024**2,'swap':mib*1024**2,'nano_cpus':int(float(cpu)*10**9),
        'oom':False,'running':True,'exit_code':0,'restarts':0}

class RunnerTests(unittest.TestCase):
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

if __name__=='__main__':unittest.main()
