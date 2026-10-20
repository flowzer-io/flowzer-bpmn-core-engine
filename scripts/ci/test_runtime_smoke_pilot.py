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
    def invoke(self, failure=None):
        r=runner();calls=[];state={}
        def tool(command,environment,**options):
            calls.append((command,environment,options))
            if command[0]=='git':
                if command[1:]==['rev-parse','HEAD']:return ('e' if failure=='source' else 'b')*40+'\n'
                if command[1:]==['rev-parse','HEAD^{tree}']:return 'c'*40+'\n'
                return ''
            if 'info' in command:return json.dumps(dict(ID='PUBLIC_DAEMON',OSType='linux',Architecture='x86_64',
                NCPU=1 if failure=='cpu' else 2,MemTotal=4*1024**3))
            if 'pull' in command:return ''
            if 'build' in command:
                Path(command[command.index('--iidfile')+1]).write_text('sha256:'+'5'*64+'\n')
                return pins_output()
            if 'image' in command and 'inspect' in command:
                return json.dumps([dict(Id='sha256:'+'5'*64,Os='linux',Architecture='amd64',
                    Config=dict(Labels={'org.opencontainers.image.revision':'b'*40,'flowzer-smoke.nonce':'a'*32}))])
            if '--bound-run' in command:
                state['parentInvoked']=True
                state['binding']=json.loads(Path(command[command.index('--bound-run')+1]).read_text())
                if failure=='parent':raise subprocess.CalledProcessError(2,command,output='PUBLIC_REJECTED')
                return 'FLOWZER_SMOKE_PARENT_OK\n'
            if 'ps' in command or 'volume' in command:
                return 'PUBLIC_REMAINING' if failure=='remaining' and state.get('parentInvoked') else ''
            if command[0]=='tar':return ''
            raise AssertionError('UNEXPECTED_COMMAND')
        with tempfile.TemporaryDirectory() as directory:
            work=Path(directory)
            # Der Workflowhash ist an tatsächliche, im Test bewusst injizierte Repositorybytes gebunden.
            l=lease();import hashlib;l['workflowSha256']=hashlib.sha256((ROOT/'.github/workflows/ci.yml').read_bytes()).hexdigest()
            with mock.patch.object(r.shared,'run_tool',side_effect=tool), \
                    mock.patch.object(r,'preflight_resources') as resources, \
                    mock.patch.object(r,'file_digest',return_value='6'*64), \
                    contextlib.redirect_stdout(io.StringIO()):
                if failure:
                    with self.assertRaises((ValueError,subprocess.CalledProcessError)):
                        r.execute(l,work,Path('/PUBLIC/docker'),lambda:100)
                else:self.assertEqual(r.execute(l,work,Path('/PUBLIC/docker'),lambda:100),0)
            if failure=='source':resources.assert_not_called()
            else:resources.assert_called()
        return calls,state

    def test_exact_public_preparation_and_existing_bound_run(self):
        # Testzweck: Ein Vorbereiten und genau ein vorhandener Parentaufruf; keine neue Runtimeabstraktion.
        calls,state=self.invoke()
        self.assertEqual(sum('pull' in c[0] for c in calls),3);self.assertEqual(sum('build' in c[0] for c in calls),1)
        self.assertEqual(sum('--bound-run' in c[0] for c in calls),1)
        self.assertFalse(any('push' in c[0] or 'login' in c[0] or 'run' in c[0] for c in calls))
        b=state['binding'];self.assertEqual(b['expectedMigration'],20);self.assertEqual(b['runtime'],'10.0.13')
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

class WorkflowTests(unittest.TestCase):
    def test_closed_dispatch_only_carrier_in_existing_job_and_mock_tests_always(self):
        # Testzweck: Routine-PRs dürfen nur Fakes, nie den neuen realen Pilotenträger aktivieren.
        s=(ROOT/'.github/workflows/ci.yml').read_text()
        self.assertIn('stdin_pilot_opening:',s);self.assertIn('python3 -I -B scripts/ci/test_runtime_smoke_pilot.py',s)
        self.assertIn("github.event_name == 'workflow_dispatch' && inputs.stdin_pilot_opening != ''",s)
        self.assertIn('python3 -I -B scripts/ci/run_runtime_smoke_pilot.py',s)
        self.assertEqual(s.count('  dotnet:'),1)

if __name__=='__main__':unittest.main(verbosity=2)
