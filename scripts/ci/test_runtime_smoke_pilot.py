"""Testzweck: Synthetische Fakes und eigener Bash-Dateitest; kein echter Docker/Git/SDK/Container."""
import base64
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]

def runner():
    spec=importlib.util.spec_from_file_location('pilot_under_test',ROOT/'scripts/ci/run_runtime_smoke_pilot.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module

def lease():
    return dict(version=1,owner='01a11f64-c814-7f51-abdb-1b7462e0491e',nonce='a'*32,
                sourceSha='b'*40,sourceTree='c'*40,workflowSha256='d'*64,expiresUnix=1500,p3Accepted=True)

def hosted():
    return dict(GITHUB_REPOSITORY='flowzer-io/flowzer-bpmn-core-engine',GITHUB_EVENT_NAME='workflow_dispatch',
                GITHUB_RUN_ATTEMPT='1',GITHUB_SHA='b'*40,GITHUB_REF='refs/heads/codex/flowzer-stdin-bootstrap-prototype-20261020',
                RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',RUNNER_ARCH='X64')

def pins_output():
    def config(names):
        f=[dict(name=n,version='10.0.0') for n in names]
        return base64.b64encode(json.dumps(dict(runtimeOptions=dict(
            **({'framework':f[0]} if len(f)==1 else {'frameworks':f})))).encode()).decode()
    values=dict(API='1'*64,FEEDER='2'*64,NATIVE='3'*64,INVENTORY='4'*64,
                API_CONFIG=config(['Microsoft.NETCore.App','Microsoft.AspNetCore.App']),
                FEEDER_CONFIG=config(['Microsoft.NETCore.App']))
    return '\n'.join('#9 0.123 FLOWZER_PILOT_'+k+'='+v for k,v in values.items())+'\n'

class LeaseTests(unittest.TestCase):
    def test_hosted_dispatch_attempt_and_exact_lease(self):
        # Testzweck: Kein PR-/Selfhosted-/Retry-/falscher Source-/abgelaufener oder impliziter P3-Lauf.
        r=runner();self.assertEqual(r.validate_lease(json.dumps(lease()),100,hosted()),lease())
        for change in [dict(owner='foreign'),dict(version=True),dict(p3Accepted=False),dict(expiresUnix=100),
                       dict(expiresUnix=1901),dict(nonce='x'),dict(sourceSha='a'*40),dict(extra=True)]:
            with self.assertRaises(ValueError):r.validate_lease(json.dumps(dict(lease(),**change)),100,hosted())
        for change in [dict(GITHUB_EVENT_NAME='pull_request'),dict(GITHUB_RUN_ATTEMPT='2'),
                       dict(RUNNER_ENVIRONMENT='self-hosted'),dict(RUNNER_OS='Windows'),dict(RUNNER_ARCH='ARM64'),
                       dict(GITHUB_REPOSITORY='MaassIT/TickyTask'),dict(GITHUB_REF='refs/heads/main')]:
            with self.assertRaises(ValueError):r.validate_lease(json.dumps(lease()),100,dict(hosted(),**change))

    def test_duplicate_keys_and_oversized_opening_rejected(self):
        # Testzweck: Keine mehrdeutigen JSON-Bindungen oder beliebig großen CI-Inputs.
        r=runner()
        for raw in [json.dumps(lease()).replace('"version": 1','"version": 1, "version": 1'),'x'*16385,'null']:
            with self.assertRaises(ValueError):r.validate_lease(raw,100,hosted())

class PinTests(unittest.TestCase):
    def test_real_merge_shell_accepts_identical_files_and_rejects_collision(self):
        # Testzweck: Die wirkliche Recipe-Dateisequenz gegen eigene Bytes ausführen, ohne Image/SDK/Netz.
        r=runner();text=(ROOT/'tests/runtime-bootstrap-smoke/Dockerfile.pilot').read_text()
        shell=text.split('RUN test -z ',1)[1].split('\nARG PILOT_NONCE',1)[0]
        # Docker entfernt seine Fortsetzungs-Newlines vor dem Shellaufruf, nicht als neue Befehlsgrenzen.
        shell='test -z '+shell.replace('\\\n',' ')
        with tempfile.TemporaryDirectory(prefix='pilot-merge-synthetic-') as directory:
            root=Path(directory);(root/'api').mkdir();(root/'feeder').mkdir()
            (root/'api/shared.dll').write_bytes(b'PUBLIC_SAME');(root/'feeder/shared.dll').write_bytes(b'PUBLIC_SAME')
            (root/'feeder/SmokeFeeder.dll').write_bytes(b'PUBLIC_FEEDER')
            command=shell.replace('/out/',str(root)+'/')
            r.shared.run_tool(['/bin/bash','-euo','pipefail','-c',command],{'PATH':'/usr/bin:/bin'},capture=True,merge_stderr=True,timeout=5)
            self.assertEqual((root/'bundle/SmokeFeeder.dll').read_bytes(),b'PUBLIC_FEEDER')
            import shutil;shutil.rmtree(root/'bundle');(root/'feeder/shared.dll').write_bytes(b'PUBLIC_DIFFERENT')
            with self.assertRaises(subprocess.CalledProcessError):
                r.shared.run_tool(['/bin/bash','-euo','pipefail','-c',command],{'PATH':'/usr/bin:/bin'},capture=True,merge_stderr=True,timeout=5)

    def test_actual_build_records_unique_and_framework_resolution_unchanged(self):
        # Testzweck: Buildausgabe ist keine freie Behauptung: alle sechs Pins, eindeutige Namen und erlaubte Runtimes.
        r=runner();pins=r.parse_pins(pins_output());self.assertEqual(pins['API'],'1'*64)
        for value in ['',pins_output()+pins_output(),pins_output().replace('FLOWZER_PILOT_NATIVE=','FOREIGN='),
                      pins_output().replace('FLOWZER_PILOT_API='+'1'*64,'FLOWZER_PILOT_API=bad')]:
            with self.assertRaises(ValueError):r.parse_pins(value)
        unsupported=base64.b64encode(json.dumps(dict(runtimeOptions=dict(framework=dict(
            name='Microsoft.NETCore.App',version='11.0.0')))).encode()).decode()
        with self.assertRaises(ValueError):r.parse_pins(pins_output().replace(
            next(line.split('=',1)[1] for line in pins_output().splitlines() if 'FEEDER_CONFIG=' in line),unsupported))

    def test_dockerfile_fixed_bases_collision_guard_and_no_secret_configuration(self):
        # Testzweck: Kein alter APIimage-ENV-Fallback, keine Kollision überschreiben oder native ABI weglassen.
        r=runner();s=(ROOT/'tests/runtime-bootstrap-smoke/Dockerfile.pilot').read_text()
        self.assertIn(r.SDK,s);self.assertIn(r.ASP,s);self.assertIn('cmp --',s)
        self.assertIn('/p:FlowzerV8Native=linux-x64',s);self.assertIn('PILOT_NONCE',s)
        self.assertNotIn('ASPNETCORE_URLS=',s);self.assertNotIn('FLOWZER_STORAGE_ROOT=',s)
        self.assertNotIn('ROLL_FORWARD',s);self.assertNotIn('NuGetAudit=false',s)

class CarrierTests(unittest.TestCase):
    def invoke(self, failure=None, diagnostic=False):
        r=runner();calls=[];state={}
        report=r.PilotDiagnostics() if diagnostic else None
        def tool(command,environment,**options):
            calls.append((command,environment,options))
            if command[0]=='git':
                if command[1:]==['rev-parse','HEAD']:return ('e' if failure=='source' else 'b')*40+'\n'
                if command[1:]==['rev-parse','HEAD^{tree}']:return 'c'*40+'\n'
                return ''
            if 'info' in command:return json.dumps(dict(ID='PUBLIC_DAEMON',OSType='linux',Architecture='x86_64',
                NCPU=1 if failure=='cpu' else 2,MemTotal=4*1024**3))
            if 'pull' in command:
                if failure=='pull':raise subprocess.CalledProcessError(12,command,output='PRIVATE_SYNTHETIC')
                return ''
            if 'build' in command:
                if failure=='build':raise subprocess.CalledProcessError(17,command,output='PRIVATE_SYNTHETIC')
                if failure=='timeout':raise subprocess.TimeoutExpired('PRIVATE_SYNTHETIC',300,output='PRIVATE_SYNTHETIC')
                if failure=='limit':raise ValueError('Runtime list exceeded byte limit')
                Path(command[command.index('--iidfile')+1]).write_text('sha256:'+'5'*64+'\n')
                return 'PRIVATE_SYNTHETIC' if failure=='pins' else pins_output()
            if 'image' in command and 'inspect' in command:
                if failure=='inspect':raise subprocess.CalledProcessError(18,command,output='PRIVATE_SYNTHETIC')
                return json.dumps([dict(Id='sha256:'+'5'*64,Os='linux',Architecture='amd64',
                    Config=dict(Labels={'org.opencontainers.image.revision':'b'*40,'flowzer-smoke.nonce':'a'*32}))])
            if '--bound-run' in command:
                state['parentInvoked']=True
                state['binding']=json.loads(Path(command[command.index('--bound-run')+1]).read_text())
                if failure in ['parent','parent_readback','parent_valid']:
                    output=json.dumps(ParentProjectionTests.document()) if failure=='parent_valid' else 'PRIVATE_SYNTHETIC'
                    raise subprocess.CalledProcessError(2,command,output=output)
                if failure=='cli_unknown':raise r.shared.ToolCleanupUnknown(123456)
                return 'FLOWZER_SMOKE_PARENT_OK\n'
            if 'ps' in command or 'volume' in command:
                if failure=='parent_readback' and state.get('parentInvoked'):
                    raise subprocess.TimeoutExpired('PRIVATE_SYNTHETIC',10)
                return 'PUBLIC_REMAINING' if failure=='remaining' and state.get('parentInvoked') else ''
            if command[0]=='tar':return ''
            raise AssertionError('UNEXPECTED_COMMAND')
        with tempfile.TemporaryDirectory() as directory:
            work=Path(directory)
            # Der Workflowhash ist an tatsächliche, im Test bewusst injizierte Repositorybytes gebunden.
            l=lease();import hashlib;l['workflowSha256']=hashlib.sha256((ROOT/'.github/workflows/ci.yml').read_bytes()).hexdigest()
            with mock.patch.object(r.shared,'run_tool',side_effect=tool), \
                    mock.patch.object(r,'preflight_resources',side_effect=ValueError('PRIVATE_SYNTHETIC') if failure=='preflight' else None) as resources, \
                    mock.patch.object(r,'file_digest',return_value='6'*64), \
                    contextlib.redirect_stdout(io.StringIO()) as captured:
                if failure:
                    with self.assertRaises((ValueError,subprocess.CalledProcessError,subprocess.TimeoutExpired,r.shared.ToolCleanupUnknown)):
                        r.execute(l,work,Path('/PUBLIC/docker'),lambda:100,report)
                else:self.assertEqual(r.execute(l,work,Path('/PUBLIC/docker'),lambda:100,report)['expectedMigration'],22)
            state['stdout']=captured.getvalue()
            if failure=='source':resources.assert_not_called()
            else:resources.assert_called()
        if report is not None:state['diagnostic']=report.snapshot()
        return calls,state

    def test_exact_public_preparation_and_existing_bound_run(self):
        # Testzweck: Ein Vorbereiten und genau ein vorhandener Parentaufruf; keine neue Runtimeabstraktion.
        # Testzweck: Der komponierte Source enthält 001..022; kein stilles Beibehalten
        # des erfolgreichen, aber nur main-basierten x64-Pilotheads20.
        versions=sorted(int(p.name.split('_')[0]) for p in
                        (ROOT/'src/PostgreSqlStorageSystem/Migrations').glob('*.sql'))
        self.assertEqual(versions,list(range(1,23)))
        calls,state=self.invoke()
        self.assertEqual(sum('pull' in c[0] for c in calls),3);self.assertEqual(sum('build' in c[0] for c in calls),1)
        self.assertEqual(sum('--bound-run' in c[0] for c in calls),1)
        self.assertFalse(any('push' in c[0] or 'login' in c[0] or 'run' in c[0] for c in calls))
        b=state['binding'];self.assertEqual(b['expectedMigration'],22);self.assertEqual(b['runtime'],'10.0.13')
        self.assertEqual(b['apiImage'],'sha256:'+'5'*64);self.assertEqual(len(b),19)
        self.assertTrue(all('GITHUB_TOKEN' not in env and 'LD_PRELOAD' not in env for _,env,_ in calls))

    def test_parent_failure_does_not_retry_or_force_cleanup(self):
        # Testzweck: Exit!=0 bleibt rot, kein Wiederanlauf oder Stop fremder/unklarer Ressourcen.
        calls,_=self.invoke('parent');self.assertEqual(sum('--bound-run' in c[0] for c in calls),1)
        self.assertFalse(any('rm' in c[0] or 'stop' in c[0] for c in calls))

    def test_remaining_resources_are_not_success(self):
        # Testzweck: Selbst Exit0 des Parents ersetzt niemals unabhängigen Abwesenheitsreadback.
        self.invoke('remaining')

    def test_source_or_cpu_mismatch_fails_before_any_docker_write(self):
        # Testzweck: Selbst gültige Dispatch-Metadaten ersetzen weder Checkout- noch Ressourcenbindung.
        for reason in ['source','cpu']:
            calls,_=self.invoke(reason)
            self.assertFalse(any('pull' in c[0] or 'build' in c[0] or '--bound-run' in c[0] for c in calls))

    def test_cli_shared_optional_capture_and_deadline_keep_old_defaults(self):
        # Testzweck: Kleine bestehende CLI-Funktion statt zweiter Prozesssteuerung, beide Kanäle begrenzt.
        r=runner();process=mock.Mock(pid=123456,returncode=0)
        with mock.patch.object(r.shared.subprocess,'Popen',return_value=process) as popen, \
                mock.patch.object(r.shared,'read_bounded_output',return_value='PUBLIC') as read, \
                mock.patch.object(r.shared,'require_empty_group'):
            self.assertEqual(r.shared.run_tool(['PUBLIC'],{},capture=True,merge_stderr=True,timeout=420),'PUBLIC')
            self.assertEqual(popen.call_args.kwargs['stderr'],subprocess.STDOUT)
            self.assertEqual(read.call_args.kwargs['timeout'],420)

class DiagnosticTests(unittest.TestCase):
    def test_fixed_failure_phases_and_numeric_tool_outcomes(self):
        # Testzweck: Zuvor blinde reale Phasen unterscheiden, ohne Rohlogs/Commands/Exceptions zu veröffentlichen.
        expected={'source':('SOURCE','NOT_STARTED'),'preflight':('RESOURCES','NOT_STARTED'),
                  'cpu':('DAEMON','NOT_STARTED'),'pull':('PULL_SDK','NOT_STARTED'),
                  'build':('BUILD','NOT_STARTED'),'pins':('PINS','NOT_STARTED'),
                  'inspect':('IMAGE_INSPECT','NOT_STARTED'),'parent':('PARENT','MAY_HAVE_STARTED'),
                  'remaining':('OWN_NAMES_FINAL','PARENT_SUCCEEDED')}
        for reason,(phase,runtime) in expected.items():
            with self.subTest(reason=reason):
                _,state=CarrierTests().invoke(reason,diagnostic=True);d=state['diagnostic']
                self.assertEqual(d['failurePhase'],phase);self.assertEqual(d['runtimeState'],runtime)
                self.assertNotIn('PRIVATE_SYNTHETIC',json.dumps(d))
                if reason in ['pull','build','inspect','parent']:
                    self.assertEqual(d['tool']['state'],'EXIT_NONZERO')
                    self.assertEqual(d['tool']['exitCode'],{'pull':12,'build':17,'inspect':18,'parent':2}[reason])
                if reason=='parent':
                    self.assertEqual(d['ownNames'],'ABSENT');self.assertIsNone(d['readbackTool'])
                if reason=='remaining':self.assertEqual(d['ownNames'],'UNKNOWN')

    def test_timeout_and_byte_limit_are_not_error_texts(self):
        # Testzweck: Harte Ausgabelimits nicht lockern; numerische Frist und festes Limitcode statt Payload.
        for reason,code in [('timeout','TIMEOUT'),('limit','OUTPUT_LIMIT')]:
            _,state=CarrierTests().invoke(reason,diagnostic=True);d=state['diagnostic']
            self.assertEqual(d['failurePhase'],'BUILD');self.assertEqual(d['tool']['state'],code)
            if reason=='timeout':self.assertEqual(d['tool']['timeoutSeconds'],300)
            self.assertNotIn('PRIVATE_SYNTHETIC',json.dumps(d))

    def test_unknown_cli_or_failed_readback_never_becomes_cleanup_proof(self):
        # Testzweck: Primärfehler nicht überschreiben; Namensabwesenheit ersetzt unbekannten CLIabschluss nicht.
        _,state=CarrierTests().invoke('parent_readback',diagnostic=True);d=state['diagnostic']
        self.assertEqual(d['failurePhase'],'PARENT');self.assertEqual(d['tool']['exitCode'],2)
        self.assertEqual(d['ownNames'],'UNKNOWN');self.assertEqual(d['readbackTool']['state'],'TIMEOUT')
        _,state=CarrierTests().invoke('cli_unknown',diagnostic=True);d=state['diagnostic']
        self.assertEqual(d['tool']['state'],'CLI_CLEANUP_UNKNOWN');self.assertEqual(d['ownNames'],'ABSENT')
        self.assertEqual(d['runtimeState'],'MAY_HAVE_STARTED')

    def test_build_classifier_canaries_ambiguous_and_truncated_outputs(self):
        # Testzweck: Geheimnisähnliche Nebeninhalte nie ausgeben; gemischte/abgeschnittene Diagnose bleibt unklar.
        r=runner();canary='Password=DO_NOT_PRINT;Bearer_CANARY;PRIVATE_KEY_CANARY'
        c=r.classify_build(canary+' error NU1301 '+canary)
        self.assertEqual(c,{'state':'MATCHED','counts':{'NUGET_SOURCE':1}})
        self.assertNotIn(canary,json.dumps(c))
        self.assertEqual(r.classify_build('error NU1301 error CS0246 '+canary)['state'],'AMBIGUOUS')
        for output in [canary,'error NU13',b'error NU1301',('error NU1301 '+canary)*3000]:
            self.assertEqual(r.classify_build(output),{'state':'UNKNOWN','counts':{}})
        # Nichtnumerische Exceptionattribute dürfen auch bei irregulären Test-/Adapterobjekten nicht durchreichen.
        d=r.PilotDiagnostics();d.enter('BUILD')
        d.failed(subprocess.CalledProcessError(canary,[canary],output=canary))
        self.assertNotIn(canary,json.dumps(d.snapshot()))

    def test_execute_never_emits_pilot_success_before_workspace_removal(self):
        # Testzweck: Echter synthetischer Executepfad darf noch keinen finalen Gesamtabschluss behaupten.
        _,state=CarrierTests().invoke(diagnostic=True)
        self.assertEqual(state['stdout'],'')

    def test_main_workspace_removal_fault_is_only_rejection(self):
        # Testzweck: Der tatsächliche Main-Fehlerpfad nach erfolgreichem Parent muss Exit2 ohne PilotOK liefern.
        r=runner()
        with tempfile.TemporaryDirectory() as directory:
            def prepared(lease,work,docker,now,report):
                report.runtime_state='PARENT_SUCCEEDED';report.own_names='ABSENT'
                return {'sourceSha':lease['sourceSha'],'nonce':lease['nonce']}
            args=['pilot','--opening',json.dumps(lease()),'--docker','/PUBLIC/docker','--temp-root',directory]
            output=io.StringIO()
            with mock.patch.object(r.sys,'argv',args),mock.patch.object(r.os,'environ',hosted()), \
                    mock.patch.object(r.time,'time',return_value=100),mock.patch.object(r,'file_digest'), \
                    mock.patch.object(r,'execute',side_effect=prepared), \
                    mock.patch.object(r.shutil,'rmtree',side_effect=OSError(13,'PRIVATE_SYNTHETIC')), \
                    contextlib.redirect_stdout(output):
                self.assertEqual(r.main(),2)
            result=json.loads(output.getvalue())
            self.assertEqual(result['result'],'PILOT_REJECTED')
            self.assertEqual(result['diagnostic']['failurePhase'],'WORKSPACE_REMOVE')
            self.assertNotIn('SYNTHETIC_PILOT_OK',output.getvalue());self.assertNotIn('PRIVATE_SYNTHETIC',output.getvalue())
            self.assertTrue(Path(result['work']).is_dir())

    def test_main_emits_single_success_only_after_real_workspace_removal(self):
        # Testzweck: Auch der tatsächliche Main-Positivpfad liefert erst nach echtem eigenen Verzeichnisabschluss OK.
        r=runner();output=io.StringIO();prepared_work=[];remove=r.shutil.rmtree
        with tempfile.TemporaryDirectory() as directory:
            def prepared(lease,work,docker,now,report):
                prepared_work.append(work);report.runtime_state='PARENT_SUCCEEDED';report.own_names='ABSENT'
                return {'sourceSha':lease['sourceSha'],'nonce':lease['nonce']}
            def removing(path):
                self.assertEqual(output.getvalue(),'');remove(path)
            args=['pilot','--opening',json.dumps(lease()),'--docker','/PUBLIC/docker','--temp-root',directory]
            with mock.patch.object(r.sys,'argv',args),mock.patch.object(r.os,'environ',hosted()), \
                    mock.patch.object(r.time,'time',return_value=100),mock.patch.object(r,'file_digest'), \
                    mock.patch.object(r,'execute',side_effect=prepared),mock.patch.object(r.shutil,'rmtree',side_effect=removing), \
                    contextlib.redirect_stdout(output):
                self.assertEqual(r.main(),0)
            self.assertFalse(prepared_work[0].exists())
            result=json.loads(output.getvalue());self.assertEqual(result['result'],'SYNTHETIC_PILOT_OK')
            self.assertEqual(result['diagnostic']['phase'],'COMPLETE')

    def test_main_never_exposes_exception_or_child_output(self):
        # Testzweck: Auch der reale CLI-Fehlerabschluss bleibt fest/sicher und behauptet keinen Runtimecleanup.
        r=runner()
        with tempfile.TemporaryDirectory() as directory:
            def fail(lease,work,docker,now,report):
                report.enter('BUILD')
                report.failed(ValueError('PRIVATE_SYNTHETIC'))
                raise ValueError('PRIVATE_SYNTHETIC')
            args=['pilot','--opening',json.dumps(lease()),'--docker','/PUBLIC/docker','--temp-root',directory]
            output=io.StringIO()
            with mock.patch.object(r.sys,'argv',args),mock.patch.object(r.os,'environ',hosted()), \
                    mock.patch.object(r.time,'time',return_value=100),mock.patch.object(r,'file_digest'), \
                    mock.patch.object(r,'execute',side_effect=fail),contextlib.redirect_stdout(output):
                self.assertEqual(r.main(),2)
            self.assertNotIn('PRIVATE_SYNTHETIC',output.getvalue())
            d=json.loads(output.getvalue())['diagnostic']
            self.assertEqual(d['failurePhase'],'BUILD');self.assertEqual(d['runtimeState'],'NOT_STARTED')

class ParentProjectionTests(unittest.TestCase):
    @staticmethod
    def document():
        return dict(result='REJECTED',nonce='a'*32,cids=['3'*64],home='/PUBLIC/parent',cleanup='CHECK_REQUIRED',
                    diagnostic=dict(phase='CLEANUP_INSPECT',failurePhase='PG_INSPECT',failureCode='OWNERSHIP',
                        cleanup='UNKNOWN',runtimeState='MAY_HAVE_STARTED',violation='TMPFS',
                        candidates=[dict(cid='3'*64,role='pg',ownership='UNKNOWN')],fixture=None,tool=None,cleanupTool=None))

    def test_safe_parent_failure_projection_and_provenance(self):
        # Testzweck: Zuvor verworfene sichere Kindphase/CIDdaten nur nach vollständiger strenger Prüfung projizieren.
        r=runner();value=self.document();p=r.project_parent_failure(json.dumps(value),'a'*32)
        self.assertEqual(p['state'],'VALIDATED');self.assertEqual(p['diagnostic']['failurePhase'],'PG_INSPECT')
        self.assertNotIn('home',p);self.assertEqual(p['diagnostic']['candidates'][0]['ownership'],'UNKNOWN')
        d=r.PilotDiagnostics();d.bind_artifacts(dict(sourceSha='b'*40,sourceTree='c'*40,nonce='a'*32,
            image='sha256:'+'5'*64,inventorySHA256='4'*64,apiSHA256='1'*64,feederSHA256='2'*64,
            nativeSHA256='3'*64,dockerSHA256='6'*64,daemonIdSHA256='7'*64,runtime='10.0.13',expectedMigration=22))
        self.assertEqual(d.snapshot()['artifacts']['nativeSHA256'],'3'*64)
        # Testzweck: Der alte Head20 oder bool sind kein gültiger aktueller Provenienzbeleg.
        for stale in [20,True,23]:
            with self.assertRaises(ValueError):
                d.bind_artifacts(dict(d.snapshot()['artifacts'],expectedMigration=stale))
        _,state=CarrierTests().invoke('parent_valid',diagnostic=True);d=state['diagnostic']
        self.assertEqual(d['parent']['state'],'VALIDATED');self.assertEqual(d['artifacts']['nativeSHA256'],'3'*64)
        self.assertEqual(d['failurePhase'],'PARENT');self.assertEqual(d['ownNames'],'ABSENT')
        value=self.document();value['diagnostic']['tool']=dict(phase='PG_CREATE',state='EXIT_NONZERO',exitCode=1,operation='CONTAINER_CREATE')
        value['diagnostic']['cleanupTool']=dict(phase='CLEANUP_INSPECT',state='EXIT_NONZERO',exitCode=2,operation='CONTAINER_INSPECT')
        projected=r.project_parent_failure(json.dumps(value),'a'*32)
        self.assertEqual(projected['diagnostic']['tool']['exitCode'],1)
        self.assertEqual(projected['diagnostic']['cleanupTool']['exitCode'],2)
        self.assertEqual(projected['diagnostic']['cleanupTool']['operation'],'CONTAINER_INSPECT')

    def test_unknown_ambiguous_truncated_or_canary_parent_output_stays_unknown(self):
        # Testzweck: Keine Freitexte/Secrets, Duplikate, Fremdnonce, falsche Ownership oder fremde CIDs übernehmen.
        r=runner();canary='Password=DO_NOT_PRINT;Bearer_CANARY'
        value=self.document()
        bad=[canary,json.dumps(value)[:-2],json.dumps(value)+'\nEXTRA',json.dumps(value).replace('"result":','"result":"REJECTED","result":',1),'x'*65537]
        for change in [dict(nonce='b'*32),dict(cids=['3'*64,'4'*64,'5'*64]),dict(extra=canary)]:
            bad.append(json.dumps(dict(value,**change)))
        for change in [dict(failureCode=canary),dict(violation=canary),dict(candidates=[dict(cid=canary,role='pg',ownership='VERIFIED')]),dict(runtimeState='NOT_STARTED')]:
            bad.append(json.dumps(dict(value,diagnostic=dict(value['diagnostic'],**change))))
        for raw in bad:self.assertEqual(r.project_parent_failure(raw,'a'*32),{'state':'UNKNOWN'})
        value['home']=canary
        self.assertNotIn(canary,json.dumps(r.project_parent_failure(json.dumps(value),'a'*32)))
        d=r.PilotDiagnostics()
        with self.assertRaises(ValueError):d.bind_artifacts({'nativeSHA256':canary})
        self.assertIsNone(d.snapshot()['artifacts'])

class WorkflowTests(unittest.TestCase):
    def test_closed_dispatch_only_carrier_in_existing_job_and_mock_tests_always(self):
        # Testzweck: Routine-PRs dürfen nur Fakes, nie den neuen realen Pilotenträger aktivieren.
        s=(ROOT/'.github/workflows/ci.yml').read_text()
        self.assertIn('stdin_pilot_opening:',s);self.assertIn('python3 -I -B scripts/ci/test_runtime_smoke_pilot.py',s)
        self.assertIn("github.event_name == 'workflow_dispatch' && inputs.stdin_pilot_opening != ''",s)
        self.assertIn('python3 -I -B scripts/ci/run_runtime_smoke_pilot.py',s)
        self.assertEqual(s.count('  dotnet:'),1)

if __name__=='__main__':unittest.main(verbosity=2)
