"""Testzweck: Hosted-/Lease-/Prooffelder und Fehlerschlüsse nur mit Fakes, kein Docker."""
import importlib.util,sys,unittest,copy,json,time,hashlib,subprocess
from unittest import mock
from pathlib import Path
P=Path(__file__).with_name('run_fixture.py');s=importlib.util.spec_from_file_location('pg_fixture',P);m=importlib.util.module_from_spec(s);sys.modules[s.name]=m;s.loader.exec_module(m)
class Contracts(unittest.TestCase):
    def env(self):return dict(GITHUB_EVENT_NAME='workflow_dispatch',GITHUB_RUN_ATTEMPT='1',GITHUB_REPOSITORY='flowzer-io/flowzer-bpmn-core-engine',GITHUB_REF='refs/heads/codex/test',GITHUB_SHA='a'*40,RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',RUNNER_ARCH='X64',PG_CHANNEL_OPENING=json.dumps(dict(mode='pg-channel-v1',sourceSha='a'*40,sourceTree='b'*40,workflowSha256='c'*64,nonce='d'*32,expiresAt=1200)),PG_LEGACY_OPENING='',GITHUB_WORKFLOW_REF='flowzer-io/flowzer-bpmn-core-engine/.github/workflows/ci.yml@refs/heads/codex/test',GITHUB_RUN_ID='12345')
    def test_exact_hosted_public_lease_fields(self):
        # Testzweck: nur eindeutig gebundener Root-Dispatch, keine privaten Workflowparameter.
        p=m.validate_opening(self.env(),1000);self.assertEqual(p['nonce'],'d'*32)
    def test_drift_reset_attempt_private_runner_and_bad_fields_stop_before_io(self):
        # Testzweck: keine Protected-/Selfhosted-/Rerun-/abgelaufenen Aktionen.
        for changes in [dict(GITHUB_EVENT_NAME='push'),dict(GITHUB_RUN_ATTEMPT='2'),dict(RUNNER_ENVIRONMENT='self-hosted'),dict(RUNNER_ARCH='ARM64'),dict(GITHUB_SHA='e'*40),dict(GITHUB_REF='refs/heads/release'),dict(PG_LEGACY_OPENING='legacy-selected'),dict(GITHUB_WORKFLOW_REF='wrong')]:
            with self.subTest(changes=changes),self.assertRaises(m.FixtureFailure):m.validate_opening(dict(self.env(),**changes),1000)
    def test_mode_schema_duplicates_and_expiry_are_fail_closed(self):
        # Testzweck: kein freier Modus, kein zusätzlicher Befehl und keine mehrdeutige JSON-/Fristbindung.
        valid=json.loads(self.env()['PG_CHANNEL_OPENING'])
        for changes in [dict(mode='other'),dict(command='unsafe'),dict(nonce='../escape'),dict(expiresAt=900),dict(expiresAt=999999),dict(expiresAt=True)]:
            env=dict(self.env(),PG_CHANNEL_OPENING=json.dumps(dict(valid,**changes)))
            with self.subTest(changes=changes),self.assertRaises(m.FixtureFailure):m.validate_opening(env,1000)
        for raw in ['{"mode":"unsafe",'+json.dumps(valid)[1:], 'x'*4097]:
            with self.assertRaises(m.FixtureFailure):m.validate_opening(dict(self.env(),PG_CHANNEL_OPENING=raw),1000)
    def test_three_cases_and_all_resources_are_distinct_and_bounded(self):
        # Testzweck: kein beschädigter Cluster aus einem Negativfall für den nächsten Fall wiederverwenden.
        names=[m.namespace('d'*32,c) for c in m.CASES];self.assertEqual(len(set(names)),3)
        self.assertTrue(all(n.startswith('flowzer-pg-channel-') and len(n)<=64 for n in names))
        self.assertEqual(m.MAX_CONTAINERS,9);self.assertEqual(m.MAX_VOLUMES,6)
    def test_missing_second_password_mutation_is_exact_ram_only(self):
        # Testzweck: kontrollierter Negativstream, kein freier SQL-/Secretdateipfad.
        raw=b'pw-first\nSQL\n\\connect :datenbank\nsynthetic-password-123\nREST\n'
        p=m.omit_reconnect_password(raw,b'synthetic-password-123')
        self.assertEqual(p,b'pw-first\nSQL\n\\connect :datenbank\nREST\n')
        with self.assertRaises(m.FixtureFailure):m.omit_reconnect_password(b'wrong',b'synthetic-password-123')
    def test_proof_rejects_raw_or_secret_dependent_fields(self):
        # Testzweck: Artifact enthält nie Rohbytes, Passwortwerte oder daraus gebildete Hashes.
        good=dict(case='positive',expected=True,bootstrap_exit=0,prompts=2,eof_wait_observed=False,role_acl_verified=True,cleanup='ABSENT')
        self.assertEqual(m.case_proof(good),good)
        for change in [dict(raw='PRIVATE'),dict(payload_sha256='a'*64),dict(prompts=True),dict(cleanup='success-maybe')]:
            with self.assertRaises(m.FixtureFailure):m.case_proof(dict(good,**change))
    def port(self):return m.LocalDockerPipe(lambda:None,dict(nonce='d'*32,sha='a'*40,run='12345'))
    def test_only_exact_neutral_ssh_envelope_is_translated_before_any_local_command(self):
        # Testzweck: Hostedübersetzung darf keine ausgelassenen ENV-/Config-/Metadatenguards als Fallback akzeptieren.
        port=self.port();calls=[]
        port.plain=lambda args,*a,**kw:calls.append(args) or m.transport.PipeResult(0,b'',b'')
        argv=m.pg.SshTarget('192.0.2.8','synthetic.invalid','/synthetic/known_hosts').argv(['version'])
        port.exchange(argv,b'',[],timeout=2);self.assertEqual(calls,[['version']])
        raw=argv[-1]
        wrong=[raw.split(' && ',1)[-1],raw.replace('/usr/bin/env -i','/usr/bin/env',1),raw.replace('PATH=/nonexistent','PATH=/usr/bin:/bin',1),raw.replace('HOME=/nonexistent','HOME=/synthetic/foreign',1),raw.replace('DOCKER_CONFIG=/nonexistent','DOCKER_CONFIG=/synthetic/foreign',1),raw.replace('--config /nonexistent','--config /synthetic/foreign',1),raw.replace('unix:///run/docker.sock','tcp://synthetic.invalid:2375',1),raw.replace('/usr/bin/python3','python3',1)]
        for remote in wrong:
            changed=[*argv[:-1],remote]
            with self.subTest(prefix=remote[:16]),self.assertRaises(m.FixtureFailure):port.exchange(changed,b'',[],timeout=2)
        self.assertEqual(calls,[['version']])

    def test_eof_channel_with_real_own_python_fixture_not_docker(self):
        # Testzweck: echte eigene Pipes warten auf EOF; der feste Dockerpfad wird nur gemockt.
        native=subprocess.Popen;port=self.port()
        script='import sys;sys.stdin.buffer.read();sys.stdout.write("t\\n");sys.stderr.write("Password: \\nPassword for user postgres: \\n")'
        def launch(argv,**kw):
            self.assertEqual(argv[:3],['/usr/bin/docker','--host','unix:///var/run/docker.sock'])
            return native([sys.executable,'-c',script],**kw)
        begin=time.monotonic()
        with mock.patch.object(m.subprocess,'Popen',side_effect=launch):r=port.hold_eof(['synthetic'],b'synthetic-input',[b'synthetic-secret-not-echoed'])
        self.assertEqual(r.exit_code,0);self.assertTrue(port.eof);self.assertGreaterEqual(time.monotonic()-begin,1.9)
    def test_premature_exit_is_not_a_positive_eof_proof(self):
        # Testzweck: ein früh endender Prozess ersetzt niemals den EOF-/Terminalbeleg.
        native=subprocess.Popen;port=self.port()
        with mock.patch.object(m.subprocess,'Popen',side_effect=lambda argv,**kw:native([sys.executable,'-c','pass'],**kw)):
            with self.assertRaises(m.FixtureFailure):port.hold_eof(['synthetic'],b'input',[b'not-echoed'])
    def test_unknown_cleanup_is_not_automatically_retried(self):
        # Testzweck: unbekannte Ownership endet UNKNOWN, keine zweite Cleanup-/Adoptionsrunde.
        port=self.port();calls=[]
        def fake(args,**kw):calls.append(args);return m.transport.PipeResult(0,b'bad-id\n',b'')
        port.plain=fake
        self.assertEqual(port.cleanup(),'UNKNOWN');n=len(calls)
        self.assertEqual(port.cleanup(),'UNKNOWN');self.assertEqual(len(calls),n)
    def test_foreign_label_blocks_before_stop_remove(self):
        # Testzweck: Namens-/Noncefilter allein erlaubt kein Signal/Löschen fremder Ressourcen.
        port=self.port();port.names=['ownnamespace'];calls=[]
        def fake(args,**kw):
            calls.append(args)
            value=b'a'*64+b'\n' if args[0]=='ps' else json.dumps(dict(labels={'com.docker.compose.project':'FOREIGN'})).encode()
            return m.transport.PipeResult(0,value,b'')
        port.plain=fake;self.assertEqual(port.cleanup(),'UNKNOWN')
        self.assertFalse(any('rm' in a or 'stop' in a for a in calls))
    def test_three_case_orchestration_with_mocked_private_ports(self):
        # Testzweck: Workflowfolge/Negativauswertung ohne echten PG-/Docker-/SSH-Aufruf.
        plan=dict(nonce='d'*32,sha='a'*40,tree='b'*40,run='12345');created=[]
        class Port:
            def __init__(self,*args):self.names=[];self.failure=None;self.local_pid=None
            def checked(self,args,**kw):
                return json.dumps(dict(id=m.IMAGE_ID,os='linux',arch='amd64')).encode() if args[:2]==['image','inspect'] else b'psql (PostgreSQL) 17.6\n'
            def cleanup(self):return 'ABSENT'
        class Adapter:
            def __init__(self,b,t,g,p):self.p=p;self.pg_cid='c'*64;created.append(b.namespace)
            def initialize(self,**kwargs):pass
            def pg_start(self):pass
            def bootstrap(self):
                self.p.last=dict(exit=2 if self.p.mode=='missingpw' else 0,prompts=2);self.p.eof=self.p.mode=='noeof'
                if self.p.mode=='missingpw':raise m.pg.PgAdapterFailure()
            def close(self):pass
        with mock.patch.object(m,'LocalDockerPipe',Port),mock.patch.object(m.pg,'PgBootstrapAdapter',Adapter),mock.patch.object(m,'role_acl_proof',return_value=True):
            proof=m.run(plan,lambda:None)
        self.assertEqual(proof['result'],'PASS');self.assertEqual([v['case'] for v in proof['cases']],list(m.CASES))
        self.assertEqual(len(set(created)),3);self.assertEqual(proof['cleanup'],'ABSENT')
    def test_unknown_local_handle_survives_adapter_exception_projection(self):
        # Testzweck: Ressourcen-ABSENT darf einen ungeklärten lokalen Pipeprozess nicht als geschlossen ausgeben.
        plan=dict(nonce='d'*32,sha='a'*40,tree='b'*40,run='12345')
        class Port:
            def __init__(self,*args):self.names=[];self.failure=None;self.local_pid=None;self.command='INITDB';self.command_exit=None;self.stderr_empty=None
            def checked(self,args,**kw):return json.dumps(dict(id=m.IMAGE_ID,os='linux',arch='amd64')).encode()
            def cleanup(self):return 'ABSENT'
        class Adapter:
            def __init__(self,b,t,g,p):self.p=p
            def initialize(self,**kwargs):
                self.p.local_pid=12345
                raise m.pg.PgAdapterFailure()
            def close(self):pass
        with mock.patch.object(m,'LocalDockerPipe',Port),mock.patch.object(m.pg,'PgBootstrapAdapter',Adapter):proof=m.run(plan,lambda:None)
        self.assertEqual(proof['result'],'FAIL');self.assertEqual(proof['cleanup'],'ABSENT')
        self.assertEqual(proof['ownHandles'],'UNKNOWN');self.assertEqual(proof['ownLocalPid'],12345)

    def test_workflow_has_fixed_artifact_path_and_pinned_actions(self):
        # Testzweck: always-Schritt darf ungeprüfte Dispatchinputs niemals als Pfad verwenden.
        w=P.resolve().parents[2]/'.github/workflows/ci.yml';t=w.read_text()
        self.assertIn('flowzer-pg-channel-proof/proof.safe.json',t)
        self.assertNotIn('path: ${{ runner.temp }}/flowzer-pg-channel-${{ inputs.',t)
        self.assertIn('actions/checkout@11d5960a326750d5838078e36cf38b85af677262',t)
        self.assertIn('actions/upload-artifact@65c4c4a1ddee5b72f698fdd19549f0f0fb45cf08',t)
if __name__=='__main__':unittest.main()
