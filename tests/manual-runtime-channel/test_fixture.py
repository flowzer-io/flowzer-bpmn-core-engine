"""Testzweck: reine kombinierte Fixture-Verträge; keine Docker-/SSH-/PG-Ausführung."""
import contextlib
import importlib.util,json,sys,unittest,subprocess,dataclasses,http.server,socketserver,threading,tempfile,shutil,os,socket,tarfile,io,hashlib
from urllib.parse import urlsplit,parse_qs
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
                if args==['--version']:return b'Docker version 27.5.1, build 9f9e405\n'
                if args[:2]==['image','inspect']:
                    api=args[2]==m.API_IMAGE
                    return json.dumps(dict(id=m.API_ID if api else m.old.IMAGE_ID,os='linux',arch='amd64',revision=m.API_SOURCE if api else None,labels=labels if api else None)).encode()
                if args[:2]==['network','create']:return b'a'*64+b'\n'
                return b''
            port.checked=checked
            fake_client=m.ClientBinding(Path('/synthetic'),{})
            with mock.patch.object(m,'load_client',return_value=fake_client),mock.patch.dict(m.os.environ,{'RUNNER_TEMP':'/synthetic'}):
                if collision:
                    with self.assertRaises(m.old.FixtureFailure):port.prepare()
                    self.assertFalse(any(a[:2]==['network','create'] for a in calls));self.assertIsNone(port.image_labels)
                else:
                    port.prepare();self.assertEqual(port.image_labels,labels);self.assertEqual(port.network,'a'*64);self.assertEqual(port.cli_version,'27.5.1')

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
                def prepare(self):self.network='a'*64;self.cli_version='27.5.1'
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
            self.assertTrue(proof['migrationVerified']);self.assertEqual(proof['actualHead'],22)
            if drift:
                self.assertFalse(proof['activeApiBlocked']);self.assertFalse(stopped);continue
            self.assertTrue(all(proof[k] for k in ['initial','activeApiBlocked','restart','feel','anonymous401']))
            self.assertEqual(events,['deliver-initial','initialize','pg-initial','bootstrap','migrate-initial','helpers','deliver-restart','pg-restart','migrate-restart'])
            self.assertEqual(stopped,['c'*64,'b'*64])
    def test_unknown_local_writer_blocks_cleanup_before_any_command(self):
        # Testzweck: unbekannter eigener Transportwriter erlaubt keine konkurrierende Ressourcenlöschung.
        port=m.CombinedPipe(lambda:None,self.plan());port.local_pid=12345
        with mock.patch.object(port,'plain') as plain:self.assertEqual(port.cleanup(),'UNKNOWN');plain.assert_not_called()
    def test_pass_through_transport_failure_preserves_only_fixed_code(self):
        # Testzweck: null-Exit wird nicht als Timeout geraten; exakter Originalcode ohne Rohtext bleibt erhalten.
        port=m.CombinedPipe(lambda:None,self.plan())
        port.client=m.ClientBinding(Path('/synthetic'),{})
        with mock.patch.object(m.ClientBinding,'open_verified',side_effect=lambda:os.open(__file__,os.O_RDONLY)),mock.patch.object(m.old.transport.BoundedPipe,'exchange',side_effect=m.old.transport.TransportFailure('TIMEOUT')):
            with self.assertRaises(m.old.FixtureFailure):port.plain(['image','inspect',m.API_IMAGE])
        self.assertEqual(port.transport_code,'TIMEOUT');self.assertIsNone(port.command_exit)

    def test_numeric_unknown_handle_survives_failure_with_no_false_success(self):
        # Testzweck: lokaler Handle geht nicht in generischem Fehler/ABSENT verloren.
        class Port(m.CombinedPipe):
            def prepare(self):raise m.old.FixtureFailure('DOCKER',12345)
            def cleanup(self):return 'ABSENT'
        with mock.patch.object(m,'CombinedPipe',Port):proof=m.run(self.plan(),lambda:None)
        self.assertEqual(proof['result'],'FAIL');self.assertEqual(proof['ownHandles'],'UNKNOWN');self.assertEqual(proof['ownLocalPid'],12345);self.assertIsNone(proof['actualHead'])

    def synthetic_package(self,binary=b'\x7fELF'+bytes([2,1,1])+b'\0'*9+b'\x02\0\x3e\0'+b'\0'*44,link=False):
        data=io.BytesIO()
        with tarfile.open(fileobj=data,mode='w:xz') as tar:
            item=tarfile.TarInfo('./usr/bin/docker');item.size=0 if link else len(binary)
            if link:item.type=tarfile.SYMTYPE;item.linkname='/foreign'
            tar.addfile(item,None if link else io.BytesIO(binary))
        members=[('debian-binary',b'2.0\n'),('control.tar.xz',b'fixed'),('data.tar.xz',data.getvalue())];raw=bytearray(b'!<arch>\n')
        for name,content in members:
            raw.extend((name+'/').ljust(16).encode()+b'0'.ljust(12)+b'0'.ljust(6)+b'0'.ljust(6)+b'100644'.ljust(8)+str(len(content)).ljust(10).encode()+b'`\n'+content)
            if len(content)%2:raw.extend(b'\n')
        return bytes(raw),binary
    def test_authenticated_package_extraction_only_client_and_no_system_install(self):
        # Testzweck: Auth-SHA muss VOR Entpacken gelten; Links/keinELF/Fremddateien bleiben gesperrt.
        raw,binary=self.synthetic_package();pin={'sha256':hashlib.sha256(raw).hexdigest(),'size':len(raw),'binarySha256':hashlib.sha256(binary).hexdigest(),'binarySize':len(binary)}
        self.assertEqual(m.client_from_package(raw,pin),binary)
        for bad,p in [(raw,dict(pin,sha256='0'*64)),(raw,dict(pin,binarySha256='0'*64)),(raw[:-1],pin),(self.synthetic_package(binary=b'not-elf')[0],None),(self.synthetic_package(link=True)[0],None)]:
            if p is None:p=dict(sha256=hashlib.sha256(bad).hexdigest(),size=len(bad),binarySha256=hashlib.sha256(binary).hexdigest(),binarySize=len(binary))
            with self.assertRaises(m.old.FixtureFailure):m.client_from_package(bad,p)
    def test_known_official_fourth_signature_member_is_bounded_last_and_not_extracted(self):
        # Testzweck: offizieller vierter ar-Signaturmember blockiert nicht die gepinnte ELF; keine Formatlockerung.
        raw,binary=self.synthetic_package()
        def member(name,content):
            header=(name+'/').ljust(16).encode()+b'0'.ljust(12)+b'0'.ljust(6)+b'0'.ljust(6)+b'100644'.ljust(8)+str(len(content)).ljust(10).encode()+b'`\n'
            return header+content+(b'\n' if len(content)%2 else b'')
        signature=member('_gpgbuilder',b's'*1248);valid=raw+signature
        def pin(data):return dict(size=len(data),sha256=hashlib.sha256(data).hexdigest(),binarySize=len(binary),binarySha256=hashlib.sha256(binary).hexdigest())
        self.assertEqual(m.client_from_package(valid,pin(valid)),binary)
        # Unbekannt, doppelt, zu groß oder vor den drei Datenmembern: immer gesperrt.
        for bad in [raw+member('_unknown',b'x'),valid+signature,raw+member('_gpgbuilder',b's'*2049),raw[:8]+signature+raw[8:]]:
            with self.subTest(bytes=len(bad)),self.assertRaises(m.old.FixtureFailure):m.client_from_package(bad,pin(bad))

    def test_client_binding_stable_fd_and_tamper_rejected_without_execution(self):
        # Testzweck: Runner-Tempbindung darf weder Symlink/Modus-/Hashdrift noch Systemfallback erlauben.
        raw,binary=self.synthetic_package()
        with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-client-binding-') as temp:
            root=Path(temp);os.chmod(root,0o700)
            pin=dict(sha256=hashlib.sha256(raw).hexdigest(),size=len(raw),binarySha256=hashlib.sha256(binary).hexdigest(),binarySize=len(binary))
            binding=m.store_client(root,raw,pin)
            fd=binding.open_verified()
            try:self.assertEqual(os.read(fd,128),binary)
            finally:os.close(fd)
            self.assertEqual(set(p.name for p in (root/'flowzer-cli27-verified').iterdir()),{'docker','binding.safe.json'})
            client=root/'flowzer-cli27-verified/docker';os.chmod(client,0o700)
            with self.assertRaises(m.old.FixtureFailure):binding.open_verified()
            os.chmod(client,0o500);client.unlink();client.symlink_to('/usr/bin/true')
            with self.assertRaises(m.old.FixtureFailure):binding.open_verified()

    def test_binary_and_binding_self_hash_tamper_cannot_replace_source_pin(self):
        # Testzweck: ELF plus Binding-Selbsthash dürfen den unabhängigen Sourcepin nicht ersetzen.
        raw,binary=self.synthetic_package()
        pin=dict(sha256=hashlib.sha256(raw).hexdigest(),size=len(raw),binarySha256=hashlib.sha256(binary).hexdigest(),binarySize=len(binary))
        with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-client-selfhash-') as temp:
            root=Path(temp);os.chmod(root,0o700);m.store_client(root,raw,pin)
            with mock.patch.object(m,'package_pin',return_value=pin):
                verified=m.load_client(root);fd=verified.open_verified()
                try:self.assertEqual(os.read(fd,128),binary)
                finally:os.close(fd)
            client=root/'flowzer-cli27-verified/docker';binding=root/'flowzer-cli27-verified/binding.safe.json'
            modified=binary[:-1]+bytes([binary[-1]^1]);os.chmod(client,0o700);client.write_bytes(modified);os.chmod(client,0o500)
            facts=json.loads(binding.read_text());st=client.stat()
            facts.update(binarySha256=hashlib.sha256(modified).hexdigest(),dev=st.st_dev,ino=st.st_ino,size=st.st_size)
            os.chmod(binding,0o600);binding.write_text(json.dumps(facts));os.chmod(binding,0o400)
            with mock.patch.object(m,'package_pin',return_value=pin),self.assertRaises(m.old.FixtureFailure):m.load_client(root)

    def test_prepare_client_uses_only_fixed_bounded_download_and_authenticates_before_extract(self):
        # Testzweck: Fakecurl schreibt nur eigenes synthetisches Paket; kein Netz/echtes Binary/Installer.
        raw,binary=self.synthetic_package();pin=dict(sha256=hashlib.sha256(raw).hexdigest(),size=len(raw),binarySha256=hashlib.sha256(binary).hexdigest(),binarySize=len(binary),url='https://download.docker.com/synthetic.deb')
        for corrupted in [False,True]:
            with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-client-prep-') as temp:
                root=Path(temp);os.chmod(root,0o700);guards=[];calls=[]
                def exchange(argv,payload,secrets,**kw):
                    calls.append(argv);self.assertEqual(payload,b'');self.assertEqual(secrets,[]);self.assertEqual(kw['timeout'],65)
                    self.assertEqual(argv[:2],['/usr/bin/curl','--disable']);self.assertIn('--max-time',argv);self.assertEqual(argv[-1],pin['url'])
                    target=argv[argv.index('--output')+1];self.assertTrue(target.startswith('/proc/'+str(os.getpid())+'/fd/'))
                    os.write(int(target.rsplit('/',1)[1]),raw if not corrupted else b'x'*len(raw));return m.old.transport.PipeResult(0,b'200',b'')
                with mock.patch.object(m,'package_pin',return_value=pin),mock.patch.object(m.old.transport.BoundedPipe,'exchange',side_effect=exchange):
                    if corrupted:
                        with self.assertRaises(m.old.FixtureFailure):m.prepare_client(root,lambda:guards.append(True))
                        self.assertFalse((root/'flowzer-cli27-verified').exists())
                    else:
                        binding=m.prepare_client(root,lambda:guards.append(True));fd=binding.open_verified();os.close(fd)
                self.assertEqual(len(calls),1);self.assertEqual(len(guards),2);self.assertFalse((root/'flowzer-cli27-package.download').exists())
    def test_prepare_failure_projects_fixed_stages_before_runtime_without_raw_error(self):
        # Testzweck: echte Prepare-Abbrüche vor Runtime bleiben als feste, begrenzte Diagnose unterscheidbar.
        cases=[('SOURCE','source'),('HTTP','bad-exit'),('HTTP','bad-http'),('DOWNLOAD','download'),('HTTP','http'),('SIZE','size'),('HASH','hash'),('ARCHIVE','archive'),('STORE','store'),('DOWNLOAD','handle')]
        for expected,case in cases:
            with self.subTest(case=case),tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-prepare-diagnostic-') as temp:
                raw,binary=self.synthetic_package();pin=dict(sha256=hashlib.sha256(raw).hexdigest(),size=len(raw),binarySha256=hashlib.sha256(binary).hexdigest(),binarySize=len(binary),url='https://download.docker.com/synthetic.deb')
                if case=='archive':raw=b'x'*len(raw);pin['sha256']=hashlib.sha256(raw).hexdigest()
                calls=[];output=io.StringIO();root=Path(temp);os.chmod(root,0o700)
                def exchange(argv,payload,secrets,**kw):
                    calls.append(True)
                    if case in ('download','handle'):raise m.old.transport.TransportFailure('HANDLE_UNKNOWN' if case=='handle' else 'OS_ERROR',12345 if case=='handle' else None)
                    fd=int(argv[argv.index('--output')+1].rsplit('/',1)[1]);data=raw[:-1] if case=='size' else b'x'*len(raw) if case=='hash' else raw;os.write(fd,data)
                    return m.old.transport.PipeResult(999 if case=='bad-exit' else 22 if case=='http' else 0,b'PRIVATE_RAW_ERROR_SENTINEL' if case=='bad-http' else b'403' if case=='http' else b'200',b'PRIVATE_RAW_ERROR_SENTINEL' if case=='http' else b'')
                real_mkdir=m.os.mkdir
                def mkdir(name,*args,**kw):
                    if name=='flowzer-cli27-verified' and case=='store':raise PermissionError('PRIVATE_RAW_ERROR_SENTINEL')
                    return real_mkdir(name,*args,**kw)
                with mock.patch.object(m,'source_guard',side_effect=m.old.FixtureFailure('SOURCE') if case=='source' else None,return_value={'mode':'runtime-channel-v1'}),mock.patch.object(m,'package_pin',return_value=pin),mock.patch.object(m.old.transport.BoundedPipe,'exchange',side_effect=exchange),mock.patch.object(m.os,'mkdir',side_effect=mkdir),mock.patch.dict(m.os.environ,{'RUNNER_TEMP':str(root)}),mock.patch.object(m.sys,'argv',['fixture','--prepare-client-only']),contextlib.redirect_stdout(output):
                    self.assertEqual(m.main(),2)
                self.assertEqual(len(calls),0 if case=='source' else 1);self.assertNotIn('PRIVATE_RAW_ERROR_SENTINEL',output.getvalue())
                lines=[line.removeprefix('CLI27_PREPARE_SAFE ') for line in output.getvalue().splitlines() if line.startswith('CLI27_PREPARE_SAFE ')]
                self.assertEqual(len(lines),1);d=json.loads(lines[0])
                self.assertEqual(set(d),{'stage','exit','httpStatus','transportCode','ownPid'})
                self.assertEqual(d['stage'],expected)
                self.assertEqual(d['exit'],None if case in ('source','download','handle','bad-exit') else 22 if case=='http' else 0)
                self.assertEqual(d['httpStatus'],None if case in ('source','download','handle','bad-http') else 403 if case=='http' else 200)
                self.assertEqual(d['transportCode'],'HANDLE_UNKNOWN' if case=='handle' else 'OS_ERROR' if case=='download' else None)
                self.assertEqual(d['ownPid'],12345 if case=='handle' else None)
                # Unbekannter eigener Writer darf keinen neuen Dateilöschschritt auslösen.
                self.assertEqual((root/'flowzer-cli27-package.download').exists(),case=='handle')

    def test_client_fd_remaps_only_binary_and_never_falls_back_to_system(self):
        # Testzweck: authentifizierter FD bleibt bis Originalreturn offen; alle übrigen Argumente unverändert.
        port=m.CombinedPipe(lambda:None,self.plan());argv=['/usr/bin/docker','--host','unix:///var/run/docker.sock','image','inspect',m.API_IMAGE]
        with mock.patch.object(m.old.transport.BoundedPipe,'exchange') as native:
            with self.assertRaises(m.old.FixtureFailure):port.pipe.exchange(argv,b'',[],timeout=2)
            native.assert_not_called()
        port.client=m.ClientBinding(Path('/synthetic'),{});fd=os.open(__file__,os.O_RDONLY)
        def exchange(mapped,payload,secrets,**kw):
            self.assertEqual(mapped[0],'/proc/'+str(os.getpid())+'/fd/'+str(fd));self.assertEqual(mapped[1:],argv[1:]);self.assertEqual(payload,b'fixed');self.assertEqual(secrets,[]);self.assertEqual(kw,{'timeout':2});os.fstat(fd)
            return m.old.transport.PipeResult(0,b'fixed-return',b'')
        with mock.patch.object(m.ClientBinding,'open_verified',return_value=fd),mock.patch.object(m.old.transport.BoundedPipe,'exchange',side_effect=exchange):
            self.assertEqual(port.pipe.exchange(argv,b'fixed',[],timeout=2).stdout,b'fixed-return')
        with self.assertRaises(OSError):os.fstat(fd)

    def test_pg_prepare_mode_is_inert_and_runtime29_rejected_before_image_work(self):
        # Testzweck: alter PGmodus bleibt ohne Clientdownload; unbekannter Client kein Binaryfallback.
        with mock.patch.object(m,'source_guard',return_value={'mode':'pg-channel-v1'}),mock.patch.object(m,'prepare_client') as prepare,mock.patch.object(m.sys,'argv',['fixture','--prepare-client-only']):
            self.assertEqual(m.main(),0);prepare.assert_not_called()
        port=m.CombinedPipe(lambda:None,self.plan());calls=[]
        def checked(args,**kw):calls.append(args);return b'Docker version 29.8.2, build 7fc2dff\n'
        port.checked=checked
        with mock.patch.object(m,'load_client',return_value=m.ClientBinding(Path('/synthetic'),{})),mock.patch.dict(m.os.environ,{'RUNNER_TEMP':'/synthetic'}):
            with self.assertRaises(m.old.FixtureFailure):port.prepare()
        self.assertEqual(calls,[['--version']])

    def test_actual_cli_matrix_supported27_positive_unsupported29_negative(self):
        # Testzweck: echte CLI/Attach-HTTP-Hijack-/Waitsemantik; ausschließlich eigener AF_UNIX-Fake.
        docker=shutil.which('docker');self.assertIsNotNone(docker)
        detected=m.old.transport.BoundedPipe().exchange([docker,'--version'],b'',[],timeout=3)
        match=m.re.fullmatch(rb'Docker version ([0-9]+\.[0-9]+\.[0-9]+)[^\n]*\n?',detected.stdout);self.assertIsNotNone(match)
        version=match[1].decode();variants=[];pinned_fd=None
        if version=='27.5.1' or version.startswith('29.'):variants.append((docker,version))
        runtime_ci=False
        if os.environ.get('GITHUB_EVENT_NAME')=='workflow_dispatch':
            runtime_ci=json.loads(os.environ.get('PG_CHANNEL_OPENING','{}')).get('mode')=='runtime-channel-v1'
        if runtime_ci:
            m.source_guard(os.environ)
            pinned=m.load_client(Path(os.environ['RUNNER_TEMP']));pinned_fd=pinned.open_verified()
            variants.append(('/proc/'+str(os.getpid())+'/fd/'+str(pinned_fd),'27.5.1'))
        # Andere Systemversionen werden nicht als27-Beleg interpretiert. Im
        # Runtime-Hostedmodus ist der authentifizierte27-Positivfall zwingend.
        if not variants:
            self.assertFalse(runtime_ci);self.assertNotEqual(version,'27.5.1');return
        release=threading.Event();eof=threading.Event();waited=threading.Event();started=threading.Event();state={}
        cid='a'*64
        class Server(socketserver.ThreadingMixIn,socketserver.UnixStreamServer):
            daemon_threads=False;block_on_close=True
            def handle_error(self,*args):state['handlerError']=True
        class Handler(http.server.BaseHTTPRequestHandler):
            protocol_version='HTTP/1.1';rbufsize=0
            def log_message(self,*args):pass
            def reply(self,code,value=None,head=False):
                raw=b'' if value is None else value if type(value) is bytes else json.dumps(value).encode()
                self.send_response(code);self.send_header('API-Version','1.47');self.send_header('OSType','linux');self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(raw)));self.end_headers()
                if not head:self.wfile.write(raw);self.wfile.flush()
            def do_HEAD(self):self.get(True)
            def do_GET(self):self.get(False)
            def get(self,head):
                path=urlsplit(self.path).path.removeprefix('/v1.47');state['requests'].append(('GET',path))
                if path=='/_ping':self.reply(200,b'OK',head)
                elif path=='/info':self.reply(200,dict(OSType='linux',ServerVersion='27.5.1'),head)
                elif path=='/version':self.reply(200,dict(Version='27.5.1',ApiVersion='1.47',MinAPIVersion='1.24',Os='linux',Arch='amd64'),head)
                elif path.startswith('/images/'):
                    self.reply(200,dict(Id='sha256:'+'e'*64,Os='linux',Architecture='amd64',Config=dict(Entrypoint=['synthetic-running-app'],Cmd=[])),head)
                else:self.reply(404,dict(message='UNEXPECTED'))
            def do_POST(self):
                parsed=urlsplit(self.path);path=parsed.path.removeprefix('/v1.47');state['requests'].append(('POST',path))
                length=int(self.headers.get('Content-Length','0'));self.connection.settimeout(2)
                if path=='/containers/create':
                    self.assertion(length<=8192);state['config']=json.loads(self.rfile.read(length));self.reply(201,dict(Id=cid,Warnings=[]))
                elif path=='/containers/'+cid+'/attach':
                    self.assertion(parse_qs(parsed.query).get('stdin')==['1'])
                    self.send_response(101);self.send_header('Connection','Upgrade');self.send_header('Upgrade','tcp');self.end_headers();self.wfile.flush()
                    while len(state['input'])<=16384:
                        chunk=self.connection.recv(4096)
                        if not chunk:eof.set();break
                        state['input'].extend(chunk)
                    release.wait(12);self.close_connection=True
                elif path=='/containers/'+cid+'/wait':
                    waited.set();self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Connection','close');self.end_headers();self.wfile.flush()
                    self.assertion(release.wait(12));state['simulatedExited']=True
                    try:self.wfile.write(b'{"StatusCode":0}\n');self.wfile.flush()
                    except (BrokenPipeError,ConnectionResetError):pass
                    self.close_connection=True
                elif path=='/containers/'+cid+'/start':started.set();self.reply(204)
                else:self.reply(404,dict(message='UNEXPECTED'))
            def assertion(self,value):
                if not value:raise AssertionError('FAKE_PROTOCOL')
        try:
            for executable,client_version in variants:
                release=threading.Event();eof=threading.Event();waited=threading.Event();started=threading.Event()
                state={'requests':[],'input':bytearray(),'config':None,'simulatedExited':False,'handlerError':False}
                with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-run-eof-') as temp:
                    os.chmod(temp,0o700);sock=str(Path(temp)/'api.sock');config=Path(temp)/'empty-config';config.mkdir(mode=0o700)
                    server=Server(sock,Handler);os.chmod(sock,0o600);worker=threading.Thread(target=server.serve_forever,daemon=False);worker.start()
                    try:
                        # BoundedPipe erbt keine Credentials/Contexts; --config bindet nur
                        # unser leeres Verzeichnis, --host ausschließlich den eigenen Socket.
                        result=None
                        try:result=m.old.transport.BoundedPipe().exchange([executable,'--host','unix://'+sock,'--config',str(config),'container','run','--pull=never','--interactive','--attach=stdin','--sig-proxy=false','sha256:'+'e'*64],b'synthetic-stdin-eof-123\n',[b'synthetic-stdin-eof-123'],timeout=4)
                        except m.old.transport.TransportFailure as error:
                            self.assertTrue(client_version.startswith('29.'));self.assertEqual(error.code,'TIMEOUT')
                        if client_version.startswith('29.'):
                            self.assertIsNone(result);self.assertTrue(eof.is_set());self.assertTrue(waited.is_set());self.assertTrue(started.is_set());self.assertFalse(state['handlerError']);self.assertFalse(state['simulatedExited'])
                            self.assertEqual(bytes(state['input']),b'synthetic-stdin-eof-123\n')
                            c=state['config'];self.assertTrue(c['AttachStdin']);self.assertFalse(c['AttachStdout']);self.assertFalse(c['AttachStderr'])
                            continue
                        self.assertEqual(result.exit_code,0);self.assertEqual(result.stdout,(cid+'\n').encode());self.assertFalse(result.stderr)
                        self.assertTrue(eof.wait(.5));self.assertTrue(waited.is_set());self.assertTrue(started.is_set());self.assertFalse(state['simulatedExited'])
                        self.assertEqual(bytes(state['input']),b'synthetic-stdin-eof-123\n')
                        c=state['config'];self.assertTrue(c['AttachStdin']);self.assertFalse(c['AttachStdout']);self.assertFalse(c['AttachStderr']);self.assertFalse(c['Tty']);self.assertTrue(c['OpenStdin'])
                        self.assertFalse(state['handlerError'])
                    except m.old.transport.TransportFailure as error:
                        raise AssertionError(json.dumps(dict(code=error.code,eof=eof.is_set(),waited=waited.is_set(),started=started.is_set(),inputBytes=len(state['input']),handlerError=state['handlerError'],attachStdin=state['config'].get('AttachStdin') if state['config'] else None,attachStdout=state['config'].get('AttachStdout') if state['config'] else None,attachStderr=state['config'].get('AttachStderr') if state['config'] else None))) from None
                    finally:
                        release.set();server.shutdown();server.server_close();worker.join(timeout=2);self.assertFalse(worker.is_alive());state['input'].clear()
        finally:
            if pinned_fd is not None:os.close(pinned_fd)

    def test_ci_selects_same_guarded_job_and_preserves_normal_job_steps(self):
        # Testzweck: kein zusätzlicher deploy-/Image-/Secretjob, nur bekannter Opt-in-Aufrufpfad.
        text=(m.ROOT/m.old.WF).read_text();job=text.split('  pg_channel:\n',1)[1]
        self.assertIn('tests/manual-runtime-channel/run_fixture.py --preflight-only',job)
        self.assertIn('tests/manual-runtime-channel/run_fixture.py --once',job)
        self.assertIn('timeout-minutes: 12',job)
        for forbidden in ['secrets.','environment:','docker build','release.yml']:self.assertNotIn(forbidden,job)

if __name__=='__main__':unittest.main()
