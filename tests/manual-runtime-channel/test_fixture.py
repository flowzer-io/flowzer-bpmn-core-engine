"""Testzweck: reine kombinierte Fixture-Verträge; keine Docker-/SSH-/PG-Ausführung."""
import importlib.util,json,sys,unittest,subprocess,dataclasses
from pathlib import Path
from unittest import mock
P=Path(__file__).with_name('run_fixture.py')
s=importlib.util.spec_from_file_location('combined_fixture',P);m=importlib.util.module_from_spec(s);sys.modules[s.name]=m;s.loader.exec_module(m)

def owned_value(port,role,cid,running):
    return dict(id=cid,name='/'+port.namespace+'_'+role+'_'+port.plan['nonce'],image=m.API_ID if role in ('api','migrate') else m.old.IMAGE_ID,labels=dict((port.image_labels or {}) if role in ('api','migrate') else {},**port.labels(port.namespace)),running=running,exit=0)

class Contracts(unittest.TestCase):
    def env(self,mode='runtime-channel-v1'):
        return dict(GITHUB_EVENT_NAME='workflow_dispatch',GITHUB_RUN_ATTEMPT='1',GITHUB_REPOSITORY='flowzer-io/flowzer-bpmn-core-engine',GITHUB_REF='refs/heads/codex/test',GITHUB_SHA='a'*40,GITHUB_RUN_ID='12345',RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',RUNNER_ARCH='X64',GITHUB_WORKFLOW_REF='flowzer-io/flowzer-bpmn-core-engine/.github/workflows/ci.yml@refs/heads/codex/test',PG_CHANNEL_OPENING=json.dumps(dict(mode=mode,sourceSha='a'*40,sourceTree='b'*40,workflowSha256='c'*64,nonce='d'*32,expiresAt=1200)),PG_LEGACY_OPENING='')
    def plan(self):return m.validate_opening(self.env(),1000)
    def test_default_cli_inert_and_import_has_no_effect(self):
        # Testzweck: Standardaufruf darf nie allein durch vorhandene ENV Docker starten.
        r=subprocess.run([sys.executable,'-I','-B',str(P)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=3)
        self.assertEqual(r.returncode,0);self.assertEqual(r.stdout,b'RUNTIME_CHANNEL_SOURCE_ONLY\n');self.assertFalse(r.stderr)
    def test_exact_six_fields_and_separate_old_new_modes(self):
        # Testzweck: keine Route schaltet aus unbekanntem/mischtem/dupliziertem Input Effekte frei.
        for mode in ['pg-channel-v1','runtime-channel-v1']:self.assertEqual(m.validate_opening(self.env(mode),1000)['mode'],mode)
        for delta in [dict(PG_LEGACY_OPENING='old'),dict(GITHUB_RUN_ATTEMPT='2'),dict(RUNNER_ARCH='ARM64'),dict(GITHUB_EVENT_NAME='push')]:
            with self.subTest(delta=delta),self.assertRaises(m.old.FixtureFailure):m.validate_opening(dict(self.env(),**delta),1000)
        good=json.loads(self.env()['PG_CHANNEL_OPENING'])
        for change in [dict(mode='unknown'),dict(expiresAt=999),dict(expiresAt=True),dict(extra='x')]:
            with self.assertRaises(m.old.FixtureFailure):m.validate_opening(dict(self.env(),PG_CHANNEL_OPENING=json.dumps(dict(good,**change))),1000)
        raw=self.env()['PG_CHANNEL_OPENING'].replace('{','{"mode":"pg-channel-v1",',1)
        with self.assertRaises(m.old.FixtureFailure):m.validate_opening(dict(self.env(),PG_CHANNEL_OPENING=raw),1000)
    def test_actual_existing_ports_and_pins_not_old_csharp_or_new_build(self):
        # Testzweck: Operatorquelle bleibt separat von attestierter APIquelle und ihren nativen Digests.
        self.assertEqual(m.API_SOURCE,'a7fcde7cba2cf526e8908037b238ee6d68bdbf85')
        self.assertEqual(m.API_ID,'sha256:cd566e56e47e3b3125f8ae4734f7397c5672c804678dffd98ef8535106e1a559')
        self.assertEqual(m.MAX_CONTAINERS,8);self.assertEqual(m.MAX_VOLUMES,3)
        public=m.public_settings();self.assertEqual(public.authority,'https://127.0.0.1/realms/TickyTask')
        self.assertEqual(public.environment()['Authentication__JwtBearer__RequireHttpsMetadata'],'true')
        self.assertEqual(public.environment()['IdentityDirectory__Enabled'],'true')
        for forbidden in ['FixtureRunner.cs','docker build','PGPASSWORD','--insecure','verify=False']:self.assertNotIn(forbidden,P.read_text())
    def test_prepare_binds_complete_image_labels_and_blocks_reserved_collision_before_network_write(self):
        # Testzweck: Cleanup erwartet nur unveränderliche Imageconfig, keine später adoptierten Containerlabels.
        for collision in [False,True]:
            port=m.CombinedPipe(lambda:None,self.plan());calls=[]
            labels={'org.opencontainers.image.revision':m.API_SOURCE,'org.opencontainers.image.title':'Synthetic API'}
            if collision:labels['flowzer.tt.demo.owner']='FOREIGN'
            def checked(args,**kw):
                calls.append(args)
                if args[:2]==['image','inspect']:
                    api=args[2]==m.API_IMAGE
                    return json.dumps(dict(id=m.API_ID if api else m.old.IMAGE_ID,os='linux',arch='amd64',revision=m.API_SOURCE if api else None,labels=labels if api else None)).encode()
                if args[:2]==['network','create']:return b'a'*64+b'\n'
                return b''
            port.checked=checked
            if collision:
                with self.assertRaises(m.old.FixtureFailure):port.prepare()
                self.assertFalse(any(a[:2]==['network','create'] for a in calls));self.assertIsNone(port.image_labels)
            else:
                port.prepare();self.assertEqual(port.image_labels,labels);self.assertEqual(port.network,'a'*64)

    def test_names_nonce_restart_and_resource_labels_are_explicit(self):
        # Testzweck: Restart verwendet gleiche Ressourcennamen, aber neuen eindeutig gebundenen Appversuch.
        port=m.CombinedPipe(lambda:None,self.plan());self.assertNotEqual(port.restart_nonce,port.plan['nonce'])
        b,r=m.bindings(port);self.assertEqual(b.namespace,port.namespace);self.assertEqual(r.api_source,m.API_SOURCE)
        self.assertEqual(r.uid,1654);self.assertEqual(b.key_uid,1654);self.assertEqual(b.architecture,'amd64')
        self.assertEqual(dict(r.network_labels),port.labels(port.namespace));self.assertEqual(r.settings.proxy_cidr,m.CIDR)
    def test_ownership_projection_rejects_foreign_duplicate_or_unknown_before_stop(self):
        # Testzweck: Namensfilter allein erlaubt keine fremden Stop-/Löschaktionen.
        port=m.CombinedPipe(lambda:None,self.plan());port.image_labels={'org.opencontainers.image.revision':m.API_SOURCE,'org.opencontainers.image.title':'Synthetic API'};good=owned_value(port,'api','a'*64,True)
        self.assertEqual(port.validate_owned(good)['id'],'a'*64)
        for change in [dict(labels={}),dict(image='sha256:'+'f'*64),dict(name='/foreign'),dict(id='short'),dict(running='yes'),dict(exit=True),dict(labels=port.labels(port.namespace)),dict(labels=dict(good['labels'],extra='FOREIGN'))]:
            with self.subTest(change=change),self.assertRaises(m.old.FixtureFailure):port.validate_owned(dict(good,**change))
    def test_cleanup_unknown_never_retries_or_removes_foreign(self):
        # Testzweck: UNKNOWN kein grüner Abschluss und kein Reparatur-/Adoptionsloop.
        port=m.CombinedPipe(lambda:None,self.plan());calls=[]
        def raw(args,**kwargs):calls.append(args);return m.old.transport.PipeResult(0,b'bad\n',b'')
        port.plain=raw;self.assertEqual(port.cleanup(),'UNKNOWN');before=len(calls)
        self.assertEqual(port.cleanup(),'UNKNOWN');self.assertEqual(len(calls),before)
        self.assertFalse(any(x[:2] in [['container','stop'],['container','rm']] for x in calls))
    def test_only_current_exact_owned_helpers_removed_before_restart(self):
        # Testzweck: keine API-/PG-/Volumelöschung bei der expliziten Helper-Vorbedingung.
        port=m.CombinedPipe(lambda:None,self.plan());calls=[]
        rows=[owned_value(port,role,str(i)*64,False) for i,role in enumerate(['ownership-pg','ownership-key','initdb','migrate'],1)]
        with mock.patch.object(port,'owned',return_value=rows),mock.patch.object(port,'checked',side_effect=lambda args,**kw:calls.append(args) or b''):
            port.remove_helpers()
        self.assertEqual(calls,[['container','rm',str(i)*64] for i in range(1,5)])
        with mock.patch.object(port,'owned',return_value=[dict(rows[0],running=True)]),mock.patch.object(port,'checked') as checked:
            with self.assertRaises(m.old.FixtureFailure):port.remove_helpers()
            checked.assert_not_called()
    def test_source_guard_tracks_new_entrypoint_and_used_operator_closure(self):
        # Testzweck: geänderte Fixture oder Runtimeports dürfen nicht unter identischem HEAD laufen.
        for p in ['tests/manual-runtime-channel/run_fixture.py','deploy/tickytask-demo-coolify/supervised-operator/manual_runtime_adapter.py','tests/manual-postgres-channel/run_fixture.py','deploy/postgresql/02-laufzeitrechte.sql']:self.assertIn(p,m.FILES)
        env=self.env();plan=self.plan()
        def git(*args):
            if args==('rev-parse','HEAD'):return (plan['sha']+'\n').encode()
            if args==('rev-parse','HEAD^{tree}'):return (plan['tree']+'\n').encode()
            return (m.ROOT/args[1].removeprefix('HEAD:')).read_bytes()
        with mock.patch.object(m.old,'git',side_effect=git),mock.patch.object(m.time,'time',return_value=1000),mock.patch.object(m.old.platform,'system',return_value='Linux'),mock.patch.object(m.old.platform,'machine',return_value='x86_64'):
            # Testopening muss echte Workflowbytes binden, keine freie Musterhashzahl.
            raw=json.loads(env['PG_CHANNEL_OPENING']);raw['workflowSha256']=m.hashlib.sha256((m.ROOT/m.old.WF).read_bytes()).hexdigest();env['PG_CHANNEL_OPENING']=json.dumps(raw)
            self.assertEqual(m.source_guard(env)['mode'],'runtime-channel-v1')
            with mock.patch.object(m.old,'git',side_effect=lambda *a:b'changed'):
                with self.assertRaises(m.old.FixtureFailure):m.source_guard(env)
    def test_actual_operator_orchestration_with_fake_ports_and_cleanup_failure(self):
        # Testzweck: echter Sequenzkern muss Initial/Block/Restart durchlaufen; CleanupUNKNOWN macht rot.
        from types import SimpleNamespace
        for cleanup,drift in [('ABSENT',False),('UNKNOWN',False),('ABSENT',True)]:
            events=[];stopped=[]
            class Port(m.CombinedPipe):
                def prepare(self):self.network='a'*64
                def remove_helpers(self):events.append('helpers')
                def stop_registered(self,cids):stopped.extend(cids)
                def cleanup(self):return cleanup
            class Ports:
                def __init__(self,*args,mode,**kw):self.mode=mode;self.pg=SimpleNamespace(pg_cid='b'*64)
                def observe(self):
                    if self.mode=='restart' and not stopped:
                        if drift:raise m.op.PortFailure()
                        self._inspect_app('c'*64,'api',False,'d'*32)
                    return m.op.Observation(True,'ABSENT' if self.mode=='initial' else 'OWNED','ABSENT' if self.mode=='initial' else 'INITIALIZED','ABSENT' if self.mode=='initial' else 'EXITED')
                def _image(self):pass
                def _inspect_app(self,cid,role,running,nonce=None,**kw):
                    if not running:raise m.op.PortFailure()
                    return cid
                def accept_bundle(self,b):events.append('deliver-'+self.mode);return b
                def initialize(self,b):events.append('initialize');return 0
                def pg_start(self):events.append('pg-'+self.mode);return 0
                def bootstrap(self,b):events.append('bootstrap');return 0
                def pg_proof(self):return m.op.RoleProof(True,True,True)
                def migrate(self,packet):events.append('migrate-'+self.mode);return m.op.MigrationProof(0,22,0)
                def api_start(self,packet):return ('c' if self.mode=='initial' else 'd')*64
                def api_proof(self,cid):return m.op.ApiProof(True,22,0,'Feel',401)
                def close(self):pass
            with mock.patch.object(m,'CombinedPipe',Port),mock.patch.object(m.runtime,'RuntimePorts',Ports):proof=m.run(self.plan(),lambda:None)
            self.assertEqual(proof['result'],'PASS' if cleanup=='ABSENT' and not drift else 'FAIL')
            if drift:
                self.assertFalse(proof['activeApiBlocked']);self.assertFalse(stopped);continue
            self.assertTrue(all(proof[k] for k in ['initial','activeApiBlocked','restart','feel','anonymous401']))
            self.assertEqual(events,['deliver-initial','initialize','pg-initial','bootstrap','migrate-initial','helpers','deliver-restart','pg-restart','migrate-restart'])
            self.assertEqual(stopped,['c'*64,'b'*64])
    def test_unknown_local_writer_blocks_cleanup_before_any_command(self):
        # Testzweck: unbekannter eigener Transportwriter erlaubt keine konkurrierende Ressourcenlöschung.
        port=m.CombinedPipe(lambda:None,self.plan());port.local_pid=12345
        with mock.patch.object(port,'plain') as plain:self.assertEqual(port.cleanup(),'UNKNOWN');plain.assert_not_called()
    def test_numeric_unknown_handle_survives_failure_with_no_false_success(self):
        # Testzweck: lokaler Handle geht nicht in generischem Fehler/ABSENT verloren.
        class Port(m.CombinedPipe):
            def prepare(self):raise m.old.FixtureFailure('DOCKER',12345)
            def cleanup(self):return 'ABSENT'
        with mock.patch.object(m,'CombinedPipe',Port):proof=m.run(self.plan(),lambda:None)
        self.assertEqual(proof['result'],'FAIL');self.assertEqual(proof['ownHandles'],'UNKNOWN');self.assertEqual(proof['ownLocalPid'],12345);self.assertIsNone(proof['actualHead'])

    def test_ci_selects_same_guarded_job_and_preserves_normal_job_steps(self):
        # Testzweck: kein zusätzlicher deploy-/Image-/Secretjob, nur bekannter Opt-in-Aufrufpfad.
        text=(m.ROOT/m.old.WF).read_text();job=text.split('  pg_channel:\n',1)[1]
        self.assertIn('tests/manual-runtime-channel/run_fixture.py --preflight-only',job)
        self.assertIn('tests/manual-runtime-channel/run_fixture.py --once',job)
        self.assertIn('timeout-minutes: 12',job)
        for forbidden in ['secrets.','environment:','docker build','release.yml']:self.assertNotIn(forbidden,job)

if __name__=='__main__':unittest.main()
