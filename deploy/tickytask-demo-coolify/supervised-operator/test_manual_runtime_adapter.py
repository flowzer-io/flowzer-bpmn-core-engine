"""Testzweck: vollständige manuelle Ports mit Fakes, keine reale SSH-/Docker-/DBausführung."""
import hashlib
import importlib.util,sys,json,unittest,shlex,tarfile,io,subprocess,dataclasses,http.server,socketserver,threading,tempfile,shutil,os
from urllib.parse import urlsplit
from pathlib import Path
from unittest import mock
D=Path(__file__).parent
for n in ['manual_pg_adapter','manual_operator','manual_readiness','manual_runtime_adapter']:
 s=importlib.util.spec_from_file_location(n,D/(n+'.py'));m=importlib.util.module_from_spec(s);sys.modules[n]=m;s.loader.exec_module(m)
a=sys.modules['manual_runtime_adapter'];op=sys.modules['manual_operator'];pg=sys.modules['manual_pg_adapter'];t=sys.modules['manual_transport']
BASE_LABELS={'org.opencontainers.image.revision':'f'*40,'org.opencontainers.image.title':'Synthetic API'}
class Pipe:
    def __init__(self):self.calls=[];self.labels={};self.runs={};self.stopped_ids=set();self.bad=None;self.history=False;self.cids={}
    def exchange(self,argv,payload,secrets,**kw):
        args=shlex.split(argv[-1])[3:];self.calls.append((args,bytes(payload),list(secrets)));out=b'';err=b''
        if self.bad=='transport':raise t.TransportFailure('HANDLE_UNKNOWN',12345)
        if args[:1]==['version']:out=json.dumps('29.8.2' if self.bad=='unsupported-client' else '27.5.1').encode()
        elif args[:2]==['image','inspect']:
            isapi=args[2]=='sha256:'+'e'*64
            out=json.dumps(dict(id=args[2],os='linux',arch='arm64',**(dict(revision='f'*40,env=['PATH=/usr/bin:/bin'],labels=dict(BASE_LABELS,**({'flowzer.tt.demo.owner':'FOREIGN'} if self.bad=='image-owner' else {}))) if isapi else (dict(env=[]) if '.Config.Env' in args[-1] else {})))).encode()
        elif args[:2]==['network','inspect']:
            peers=[cid for n,cid in self.cids.items() if n in self.runs and '_api_' in n and cid not in self.stopped_ids]
            if self.bad=='peer':peers.append('f'*64)
            out=json.dumps(dict(id='a'*64,driver='bridge',internal=False,subnets=['172.30.77.0/24',None],labels={'com.docker.compose.project':'syntheticflowzer123456'},**(dict(peers=peers+[None]) if '.Containers' in args[-1] else {}))).encode()
        elif args[:2]==['ps','-a']:
            value=next((v[7:] for v in args if v.startswith('name=^/')),None)
            if value:
                value=value.removesuffix('$');out=''.join(cid+'\n' for name,cid in self.cids.items() if name in self.runs and (name==value if value.endswith('c'*32) or value.endswith('3'*32) else name.startswith(value))).encode()
        elif args[:2]==['volume','ls']:
            prefix=next(v[6:] for v in args if v.startswith('name=^'))
            out=''.join(name+'\n' for name in self.labels if name.startswith(prefix)).encode()
        elif args[:2]==['volume','create']:
            self.labels[args[-1]]=dict(v.split('=',1) for i,v in enumerate(args) if i and args[i-1]=='--label');out=args[-1].encode()+b'\n'
        elif args[:2]==['volume','inspect']:out=json.dumps(dict(name=args[2],driver='local',labels=self.labels[args[2]])).encode()
        elif args[:2]==['container','run']:
            name=args[args.index('--name')+1];self.runs[name]=args
            role='postgres' if '_postgres_' in name else 'api' if '_api_' in name else 'migrate' if '_migrate_' in name else None
            if role:
                count=sum('_'+role+'_' in k for k in self.runs)
                self.cids[name]=('b' if role=='postgres' else 'd' if role=='api' and count==1 else '8' if role=='api' else '9' if count==1 else '7')*64
            if '--attach=stdout' in args and args[-2:]==['--migrate','--secrets-stdin']:self.history=True
            if '--detach' in args or '_api_' in name:out=(self.cids[name]+'\n').encode()
        elif args[:2]==['container','inspect']:
            target=args[2];name=target if target in self.runs else next(k for k,v in self.cids.items() if v==target)
            role='postgres' if '_postgres_' in name else 'api' if '_api_' in name else 'migrate';run=self.runs[name]
            labs=dict(v.split('=',1) for i,v in enumerate(run) if i and run[i-1]=='--label')
            mounts=[]
            for i,v in enumerate(run):
                if i and run[i-1]=='--mount':
                    bits=dict(x.split('=',1) for x in v.split(',') if '=' in x)
                    mounts.append(dict(type='volume',name=bits['source'],destination=bits['target'],rw='readonly' not in v))
            running=role!='migrate' and self.cids[name] not in self.stopped_ids
            common=dict(id=self.cids[name],image=('sha256:'+'1'*64 if role=='postgres' else 'sha256:'+'e'*64),labels=labs,user='999:999' if role=='postgres' else '1654:1654',running=running,network='none' if role!='api' else 'a'*64,log='none',restart='no',mounts=mounts+[None])
            if role!='postgres':
                common['labels']=dict(BASE_LABELS,**common['labels'])
                if self.bad=='extra-label':common['labels']['unapproved.extra']='unexpected'
                env=[k+'='+v for k,v in dict([('PATH','/usr/bin:/bin')]+[tuple(v.split('=',1)) for i,v in enumerate(run) if i and run[i-1]=='--env']).items()]
                common.update(entrypoint=['dotnet','WebApiEngine.dll'],cmd=['--migrate','--secrets-stdin'] if role=='migrate' else ['--secrets-stdin'],pidmode='',ipcmode='private',name='/'+run[run.index('--name')+1],exit=0,env=env,ports={},readonly=True,privileged=False,capdrop=['ALL'],capadd=None,security=['no-new-privileges=true'],memory=(768 if role=='api' else 1024)*1024*1024,nanocpus=1000000000,pids=128,networks=[dict(name='syntheticflowzer123456' if role=='api' else 'none',id='a'*64 if role=='api' else ''),None],tmpfs={'/tmp':'rw,nosuid,nodev,noexec,size=64m,mode=1777'})
            if role=='postgres':
                common.update(privileged=self.bad=='pg-privileged',readonly=self.bad!='pg-rootfs',capdrop=['ALL'],capadd=['SYS_ADMIN'] if self.bad=='pg-cap' else None,security=[] if self.bad=='pg-security' else ['no-new-privileges=true'],ports={'5432/tcp':[{'HostPort':'5432'}]} if self.bad=='pg-ports' else {},memory=0 if self.bad=='pg-memory' else 384*1024*1024,nanocpus=750000000,pids=64,pidmode='',ipcmode='private',env=[],entrypoint=['/usr/lib/postgresql/17/bin/postgres'],cmd=['-D','/var/lib/postgresql/data','-c','listen_addresses=','-c','unix_socket_directories=/socket','-c','unix_socket_permissions=0777','-c','password_encryption=scram-sha-256','-c','log_statement=none','-c','log_min_error_statement=panic','-c','logging_collector=off'],networks=[dict(name='none',id=''),None],tmpfs={'/tmp':'rw,nosuid,nodev,noexec,size=64m,mode=1777'})
                if self.bad=='pg-cmd':common['cmd']=['UNSAFE']
                if self.bad=='pg-env':common['env']=['PGPASSWORD=UNSAFE']
            if self.bad=='mount':common['mounts'][0]['destination']='/WRONG'
            if self.bad=='env':common.setdefault('env',[]).append('Storage__PostgreSql__ConnectionString=FORBIDDEN')
            if self.bad=='labels':common['labels']={'foreign':'true'}
            if self.bad=='privileged':common['privileged']=True
            if self.bad=='api-cmd':common['cmd']=['UNSAFE']
            if self.bad=='api-pid':common['pidmode']='host'
            if self.bad=='memory':common['memory']=True
            if self.bad=='extra-network':common['networks'].insert(0,dict(name='foreign',id='f'*64))
            if role=='postgres' and '"privileged"' not in args[-1]:common={k:common[k] for k in ['id','image','labels','user','running','network','log','restart','mounts']}
            out=json.dumps(common).encode()
        elif args[:2]==['container','cp']:
            buf=io.BytesIO()
            with tarfile.open(fileobj=buf,mode='w') as z:
                info=tarfile.TarInfo('PG_VERSION');info.size=3;z.addfile(info,io.BytesIO(b'17\n'))
            out=buf.getvalue()
        elif args[:2]==['container','start']:self.stopped_ids.discard(args[2]);out=args[2].encode()+b'\n'
        elif any(v.endswith('/pg_isready') for v in args):pass
        elif any(v.endswith('/psql') for v in args):
            if b'ACL_RUNTIME_PROOF' in payload or b'HISTORY_22_PROOF' in payload:
                out=b'f\n' if self.bad=='acl' or (self.bad=='public-acl' and b'ACL_DENY_PUBLIC_AND_OTHER_ROLES' in payload) or (self.bad=='db-acl' and b'ACL_DENY_DATABASE_DDL' in payload) or (self.bad=='global-acl' and b'ACL_DENY_GLOBAL_SCOPE' in payload) or (self.bad=='extra-acl' and b'ACL_DENY_ADDITIONAL_EFFECTIVE' in payload) or (b'HISTORY_22_PROOF' in payload and (not self.history or self.bad=='head')) else b't\n';err=b'Password: '
            else:err=b'Password: \nPassword for user postgres: \n'
        elif 'wget' in args:
            status=302 if self.bad=='redirect' else 401 if args[-1].endswith('/Definition') else 200
            err=('  HTTP/1.1 '+str(status)+' OK\n').encode()
            if status==200:out=json.dumps(dict(successful=True,result=dict(status='Healthy',storage='Ready',details=dict(storageProvider='PostgreSQL (schema flowzer)',migrationState='UpToDate',pendingMigrationCount=0,expectedMigrationVersion=22,expressionEngine='Simple' if self.bad=='feel' else 'Feel')))).encode()
            return t.PipeResult(0 if status==200 else 6 if status==401 else 8,out,err)
        return t.PipeResult(0,out,err)
class RuntimeTests(unittest.TestCase):
    def make(self,mode='initial',pipe=None):
        p=pipe or Pipe();binding=pg.PgBinding('syntheticflowzer123456','12345678-1234-1234-1234-123456789abc','c'*32,'sha256:'+'1'*64,999,999,1654,1654,'2'*40,'12345')
        public=a.PublicSettings('https://synthetic.invalid/realms/TickyTask','flowzer-api-demo','flowzer-bff-demo','flowzer-directory-demo','12345678-1234-1234-1234-123456789abc','172.30.77.0/24')
        runtime=a.RuntimeBinding('sha256:'+'e'*64,'f'*40,'a'*64,1654,1654,public,(('com.docker.compose.project','syntheticflowzer123456'),),'c'*32)
        ports=a.RuntimePorts(binding,runtime,pg.SshTarget('192.0.2.8','synthetic.invalid','/synthetic/known_hosts'),lambda:None,mode=mode,pipe=p)
        return ports,p
    def bundle(self,mode='initial'):
        return op.SecretBundle('synthetic-admin-123' if mode=='initial' else None,'synthetic-migration-123','synthetic-runtime-123','synthetic-bff-123456','synthetic-directory-123')
    def initial(self):
        ports,p=self.make();result=op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()));return ports,p,result
    def test_default_isolated_cli_inert(self):
        # Testzweck: kein unbeabsichtigter Remote-/Secretzugriff durch normalen Scriptstart.
        r=subprocess.run([sys.executable,'-I','-B',str(D/'manual_runtime_adapter.py')],stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=3)
        self.assertEqual(r.returncode,0);self.assertEqual(r.stdout,b'FLOWZER_RUNTIME_ADAPTER_SOURCE_ONLY\n');self.assertFalse(r.stderr)
    def test_full_existing_operator_sequence_no_secret_env_and_exact_migration_readback(self):
        # Testzweck: nicht nur CID/CLI0, sondern Rollen, Head22 und echte Readinessports sind nötig.
        ports,p,result=self.initial();self.assertEqual(result.api_cid,'d'*64);self.assertEqual(ports.state,'COMPLETE')
        commands=[a for a,_,_ in p.calls];self.assertTrue(any('--migrate' in c for c in commands));self.assertTrue(any('wget' in c for c in commands))
        for args,_,_ in p.calls:
            self.assertFalse(any(v in ' '.join(args) for v in ['synthetic-admin-123','synthetic-runtime-123','synthetic-bff-123456']))
        self.assertIsNone(ports.expected_api);self.assertIsNone(ports.bundle)
    def test_deliver_only_after_observation_and_only_once(self):
        # Testzweck: keine Secretlieferung vor positiver Vorprüfung oder erneute Lieferung nach Teilstart.
        ports,p=self.make()
        with self.assertRaises(op.PortFailure):ports.accept_bundle(self.bundle())
        self.assertEqual(p.calls,[])
        ports,p=self.make();ports.observe();ports.accept_bundle(self.bundle())
        with self.assertRaises(op.PortFailure):ports.accept_bundle(self.bundle())
    def test_sql_packets_reject_multibyte_overflow_before_any_initial_write(self):
        # Testzweck: gültige JSON-/DSNlieferung darf SQL-16KiB erst nach Rollenwrites nicht sprengen.
        ports,p=self.make();bundle=dataclasses.replace(self.bundle(),runtime='€'*3800)
        self.assertLess(len(op._packet('api',{'runtimeConnectionString':op._connection(bundle.runtime,'flowzer_tt_demo'),'bffClientSecret':bundle.bff,'directoryClientSecret':bundle.directory})),16384)
        with self.assertRaises(op.OperatorFailure) as got:op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(bundle))
        self.assertEqual(got.exception.phase,'INPUT')
        self.assertFalse(any(args[:2] in [['volume','create'],['container','run'],['container','start'],['container','exec']] for args,_,_ in p.calls))
        self.assertIsNone(ports.bundle);self.assertEqual(ports.sql_packets,{})
        # Dieselbe frische Lieferprüfung gilt ohne Initialisierungs-Gate beim Restart.
        ports,p=self.make();ports.mode='restart';ports.state='OBSERVED'
        with self.assertRaises(op.PortFailure):ports.accept_bundle(dataclasses.replace(bundle,admin=None))
        self.assertFalse(p.calls)
        ports,p=self.make();ports.observe();ports.accept_bundle(dataclasses.replace(self.bundle(),runtime='€'*3500))
        packets=list(ports.sql_packets.values());self.assertTrue(all(len(v)<=16384 for v in packets))
        ports.close();self.assertTrue(all(not any(v) for v in packets));self.assertEqual(ports.sql_packets,{})

    def test_fixed_sql_argument_secret_collision_rejected_before_initial_write(self):
        # Testzweck: Transportcanary darf nicht erst nach Initialisierung an festem psql-argv scheitern.
        ports,p=self.make();bundle=dataclasses.replace(self.bundle(),runtime='/usr/lib/postgresql/17/bin/psql')
        with self.assertRaises(op.OperatorFailure) as got:op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(bundle))
        self.assertEqual(got.exception.phase,'INPUT')
        self.assertFalse(any(args[:2] in [['volume','create'],['container','run'],['container','start'],['container','exec']] for args,_,_ in p.calls))

    def test_bad_acl_cannot_start_migration(self):
        # Testzweck: Rechtefehler darf nicht durch spätere Migration kaschiert werden.
        ports,p=self.make();p.bad='acl'
        with self.assertRaises(op.OperatorFailure):op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()))
        self.assertFalse(any('--migrate' in args for args,_,_ in p.calls))
    def test_foreign_mount_env_or_simple_engine_never_yields_api_proof(self):
        # Testzweck: echte Inspect-/Readinessdrifts sperren auch bei plausibler CID/HTTP200.
        for bad in ['mount','env','labels','feel','privileged','memory','extra-network','redirect','peer','api-cmd','api-pid']:
            ports,p=self.make();ports.observe();bundle=ports.accept_bundle(self.bundle());ports.initialize(bundle);ports.pg_start();ports.bootstrap(bundle);ports.pg_proof();ports.migrate(ports.expected_migration)
            p.bad=bad
            with self.subTest(bad=bad),self.assertRaises(op.PortFailure):
                cid=ports.api_start(ports.expected_api);ports.api_proof(cid)
    def test_modified_packet_is_rejected_before_container_write(self):
        # Testzweck: kein freier DSN-/Secrets-/JSON-Executor statt gebundenem eigenen Paket.
        ports,p=self.make();ports.observe();b=ports.accept_bundle(self.bundle());ports.initialize(b);ports.pg_start();ports.bootstrap(b);ports.pg_proof();before=len(p.calls)
        with self.assertRaises(op.PortFailure):ports.migrate(b'{}')
        self.assertEqual(len(p.calls),before)
    def test_unknown_local_handle_not_lost_at_operator_boundary(self):
        # Testzweck: numerische eigene UNKNOWN-PID bleibt im bestehenden Operatorfehler erhalten.
        ports,p=self.make();p.bad='transport'
        with self.assertRaises(op.OperatorFailure) as got:op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()))
        self.assertEqual(got.exception.own_local_pid,12345)
    def test_restart_preserves_registered_data_and_never_initializes_or_bootstraps(self):
        # Testzweck: beaufsichtigter Wiederanlauf nur eigener CIDs/Daten, keine neue Rolle/Volume/initdb.
        first,p,result=self.initial();p.stopped_ids={'b'*64,'d'*64}
        # Vorbedingung: Root hat erfolgreiche eigene Hilfscontainer separat entfernt, nie Volumes.
        p.runs={k:v for k,v in p.runs.items() if '_postgres_' in k or '_api_' in k}
        ports=a.RuntimePorts(first.pg.b,dataclasses.replace(first.r,attempt_nonce='3'*32,prior_api_nonce='c'*32),first.pg.target,lambda:None,mode='restart',pipe=p,prior_pg_cid='b'*64,prior_api_cid='d'*64)
        before=len(p.calls);result=op.ManualOperator().run('restart',ports,lambda:ports.accept_bundle(self.bundle('restart')))
        self.assertEqual(result.api_cid,'8'*64)
        for args,_,_ in p.calls[before:]:
            self.assertNotEqual(args[:2],['volume','create']);self.assertFalse(any(v.endswith('/initdb') for v in args))
    def test_running_prior_api_blocks_before_fresh_secret_delivery(self):
        # Testzweck: laufende alte eigene API niemals automatisch stoppen oder durch eine neue umgehen.
        first,p,_=self.initial();p.runs={k:v for k,v in p.runs.items() if '_postgres_' in k or '_api_' in k};new=dataclasses.replace(first.r,attempt_nonce='3'*32,prior_api_nonce='c'*32)
        ports=a.RuntimePorts(first.pg.b,new,first.pg.target,lambda:None,mode='restart',pipe=p,prior_pg_cid='b'*64,prior_api_cid='d'*64)
        delivered=[]
        with self.assertRaises(op.OperatorFailure):op.ManualOperator().run('restart',ports,lambda:delivered.append(True))
        self.assertEqual(delivered,[])
        self.assertFalse(any(args[:2] in [['container','stop'],['container','rm']] for args,_,_ in p.calls))
    def test_restart_requires_distinct_new_attempt_and_explicit_old_nonce(self):
        # Testzweck: keine Namenskollision oder Wiederverwendung alter API-/Migratorversuche.
        first,p,_=self.initial()
        with self.assertRaises(op.PortFailure):a.RuntimePorts(first.pg.b,first.r,first.pg.target,lambda:None,mode='restart',pipe=p,prior_pg_cid='b'*64)
    def test_actual_cli_formatter_for_every_new_template_on_own_unix_fake(self):
        # Testzweck: echte Go-Templates/JSONprojektion statt sich gegenseitig bestätigender Dictfakes.
        docker=shutil.which('docker');self.assertIsNotNone(docker);backend=Pipe();native=backend.exchange
        class Server(socketserver.UnixStreamServer):pass
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self,*args):pass
            def do_HEAD(self):self.answer(True)
            def do_GET(self):self.answer(False)
            def answer(self,head):
                path=urlsplit(self.path).path.removeprefix('/v1.47')
                raw=b'OK' if path=='/_ping' else json.dumps(self.server.value).encode()
                self.send_response(200);self.send_header('API-Version','1.47');self.send_header('Content-Length',str(len(raw)));self.end_headers()
                if not head:self.wfile.write(raw)
        with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-runtime-fmt-') as temp:
            sock=str(Path(temp)/'api.sock');server=Server(sock,Handler);os.chmod(sock,0o600)
            worker=threading.Thread(target=server.serve_forever,daemon=False);worker.start();seen=set()
            class FormattingPipe:
                def exchange(inner,argv,payload,secrets,**kw):
                    args=shlex.split(argv[-1])[3:];result=native(argv,payload,secrets,**kw)
                    if '--format' not in args:return result
                    # Dieser Formattertest modelliert den Zielclient27 separat; die
                    # tatsächlich installierte29 ist kein positiver Runtimebeleg.
                    if args[:1]==['version']:return result
                    seen.add(tuple(args[:2]));v=json.loads(result.stdout) if result.stdout else None
                    if args[:2]==['image','inspect']:
                        raw=dict(Id=v['id'],Os=v['os'],Architecture=v['arch'],Config=dict(Env=v.get('env',[]),Labels=v.get('labels',{})))
                    elif args[:2]==['network','inspect']:
                        raw=dict(Id=v['id'],Driver=v['driver'],Internal=v['internal'],IPAM=dict(Config=[dict(Subnet=x) for x in v['subnets'] if x]),Labels=v['labels'],Containers={cid:dict(Name='synthetic',EndpointID='1'*64,MacAddress='',IPv4Address='172.30.77.2/24',IPv6Address='') for cid in v.get('peers',[]) if cid})
                    elif args[:2]==['volume','inspect']:raw=dict(Name=v['name'],Driver=v['driver'],Labels=v['labels'])
                    elif args[:2]==['container','inspect']:
                        raw=dict(Id=v['id'],Name=v.get('name','/unused'),Image=v['image'],Config=dict(User=v['user'],Labels=v['labels'],Env=v.get('env',[]),Entrypoint=v.get('entrypoint',[]),Cmd=v.get('cmd',[])),State=dict(Running=v['running'],ExitCode=v.get('exit',0)),
                            HostConfig=dict(NetworkMode=v['network'],LogConfig=dict(Type=v['log']),RestartPolicy=dict(Name=v['restart']),PortBindings=v.get('ports',{}),ReadonlyRootfs=v.get('readonly',True),Privileged=v.get('privileged',False),CapDrop=v.get('capdrop',['ALL']),CapAdd=v.get('capadd'),SecurityOpt=v.get('security',[]),Memory=v.get('memory',0),NanoCpus=v.get('nanocpus',0),PidsLimit=v.get('pids',0),Tmpfs=v.get('tmpfs',{}),PidMode=v.get('pidmode',''),IpcMode=v.get('ipcmode','private')),
                            NetworkSettings=dict(Networks={x['name']:dict(NetworkID=x['id']) for x in v.get('networks',[]) if x}),
                            Mounts=[dict(Type=x['type'],Name=x['name'],Destination=x['destination'],RW=x['rw']) for x in v['mounts'] if x])
                    elif args[:2]==['volume','ls']:raw=dict(Volumes=[],Warnings=[])
                    else:raw=[]
                    server.value=raw
                    r=subprocess.run([docker,'--host','unix://'+sock,*args],stdout=subprocess.PIPE,stderr=subprocess.PIPE,stdin=subprocess.DEVNULL,timeout=5,env={'PATH':'/usr/bin:/bin','HOME':'/nonexistent','DOCKER_CONFIG':'/nonexistent','DOCKER_API_VERSION':'1.47','LC_ALL':'C'})
                    self.assertEqual(r.returncode,0,msg=r.stderr.decode('utf-8','replace'));self.assertFalse(r.stderr)
                    return t.PipeResult(r.returncode,r.stdout,r.stderr)
            try:
                ports,p=self.make(pipe=FormattingPipe());result=op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()))
                self.assertEqual(result.api_cid,'d'*64)
                self.assertTrue({('image','inspect'),('network','inspect'),('container','inspect'),('volume','inspect')}<=seen)
            finally:server.shutdown();server.server_close();worker.join(2);self.assertFalse(worker.is_alive())

    def test_leftover_helper_or_wrong_volume_owner_blocks_restart_before_delivery(self):
        # Testzweck: keine automatische Adoption selbst eigener alter Hilfscontainer oder fremder Datenträger.
        for bad in ['helper','volume']:
            first,p,_=self.initial();p.stopped_ids={'b'*64,'d'*64}
            if bad=='volume':
                p.runs={k:v for k,v in p.runs.items() if '_postgres_' in k or '_api_' in k}
                p.labels[first.pg.data]={'foreign':'true'}
            ports=a.RuntimePorts(first.pg.b,dataclasses.replace(first.r,attempt_nonce='3'*32,prior_api_nonce='c'*32),first.pg.target,lambda:None,mode='restart',pipe=p,prior_pg_cid='b'*64,prior_api_cid='d'*64)
            delivered=[]
            with self.subTest(bad=bad),self.assertRaises(op.OperatorFailure):op.ManualOperator().run('restart',ports,lambda:delivered.append(True))
            self.assertEqual(delivered,[])
    def test_zero_cli_exit_without_actual_history22_cannot_start_api(self):
        # Testzweck: erfolgreicher DockerCLI-Exit ersetzt nicht die reale gelesene Migrationshistorie.
        ports,p=self.make();p.bad='head'
        with self.assertRaises(op.OperatorFailure) as got:op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()))
        self.assertEqual(got.exception.phase,'MIGRATION');self.assertFalse(any('_api_' in v for args,_,_ in p.calls if args[:2]==['container','run'] for v in args))

    def test_extra_effective_acl_is_not_least_privilege(self):
        # Testzweck: zusätzliche Default-/Tabellen-/Sequenzrechte müssen explizit negativ geprüft werden.
        ports,p=self.make();p.bad='extra-acl'
        with self.assertRaises(op.OperatorFailure) as got:op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()))
        self.assertEqual(got.exception.phase,'PG_PROOF')
    def test_existing_pg_hardening_drift_blocks_before_start(self):
        # Testzweck: gleicher CID/Owner/Mount genügt weder für Privileged/Cap/Ports/Resources noch anderes Cmd/ENV.
        for bad in ['pg-privileged','pg-rootfs','pg-cap','pg-security','pg-ports','pg-memory','pg-cmd','pg-env']:
            first,p,_=self.initial();p.stopped_ids={'b'*64,'d'*64};p.runs={k:v for k,v in p.runs.items() if '_postgres_' in k or '_api_' in k};p.bad=bad
            ports=a.RuntimePorts(first.pg.b,dataclasses.replace(first.r,attempt_nonce='3'*32,prior_api_nonce='c'*32),first.pg.target,lambda:None,mode='restart',pipe=p,prior_pg_cid='b'*64,prior_api_cid='d'*64)
            before=len(p.calls)
            with self.subTest(bad=bad):
                with self.assertRaises(op.OperatorFailure):op.ManualOperator().run('restart',ports,lambda:ports.accept_bundle(self.bundle('restart')))
                self.assertFalse(any(args[:2]==['container','start'] for args,_,_ in p.calls[before:]))
    def test_inspect_timeout_cannot_exceed_remaining_readiness_budget(self):
        # Testzweck: auch Dockerinspect gehört zur30s-Portfrist, nicht nur der HTTPaufruf.
        ports,p=self.make();ports.observe();b=ports.accept_bundle(self.bundle());ports.initialize(b);ports.pg_start();ports.bootstrap(b);ports.pg_proof();ports.migrate(ports.expected_migration);cid=ports.api_start(ports.expected_api)
        seen=[];original=ports._inspect_app
        def inspect(*args,**kwargs):seen.append(kwargs.get('timeout',60));return original(*args,**kwargs)
        ports._inspect_app=inspect;ports.api_proof(cid)
        self.assertTrue(seen);self.assertTrue(all(0<v<=30 for v in seen))

    def test_database_ddl_or_global_default_scope_drift_is_rejected(self):
        # Testzweck: DB-/Schema-DDL/Grantoptionen und globale statt schemaenge Defaults sind kein Least-Privilege-Beleg.
        for bad in ['db-acl','global-acl']:
            ports,p=self.make();p.bad=bad
            with self.subTest(bad=bad):
                with self.assertRaises(op.OperatorFailure) as got:op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()))
                self.assertEqual(got.exception.phase,'PG_PROOF')

    def test_public_grants_cannot_replace_explicit_runtime_isolation(self):
        # Testzweck: passende effektive Runtime-Rechte erlauben keine Offenlegung an PUBLIC/andere DBrollen.
        ports,p=self.make();p.bad='public-acl'
        with self.assertRaises(op.OperatorFailure) as got:op.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(self.bundle()))
        self.assertEqual(got.exception.phase,'PG_PROOF')

    def test_private_pid_uses_supported_empty_docker27_mode_not_literal_private(self):
        # Testzweck: Moby27 PidMode.Valid akzeptiert nurleer/host/container; leer ist der private Namespace.
        ports,p,_=self.initial()
        self.assertFalse(any(v=='--pid=private' for args,_,_ in p.calls for v in args))
        self.assertTrue(any('--ipc=private' in args for args,_,_ in p.calls))

    def test_image_labels_are_fully_inherited_but_foreign_extra_or_owner_collision_blocks(self):
        # Testzweck: Docker erbt vollständige Imagelabels; nur exakt attestierte Union ist zulässig.
        ports,p,_=self.initial();self.assertEqual(ports.image_labels,BASE_LABELS)
        ports,p=self.make();p.bad='image-owner';delivered=[]
        with self.assertRaises(op.OperatorFailure):op.ManualOperator().run('initial',ports,lambda:delivered.append(True))
        self.assertEqual(delivered,[]);self.assertFalse(any(args[:2]==['container','run'] for args,_,_ in p.calls))
        ports,p=self.make();ports.observe();b=ports.accept_bundle(self.bundle());ports.initialize(b);ports.pg_start();ports.bootstrap(b);ports.pg_proof();p.bad='extra-label'
        with self.assertRaises(op.PortFailure):ports.migrate(ports.expected_migration)

    def test_unsupported_cli29_stops_before_images_resources_and_secret_delivery(self):
        # Testzweck: 27er-STDIN-Protokoll nicht still auf neue Clientsemantik übertragen.
        ports,p=self.make();p.bad='unsupported-client';delivered=[]
        with self.assertRaises(op.OperatorFailure):op.ManualOperator().run('initial',ports,lambda:delivered.append(True))
        self.assertEqual(delivered,[]);self.assertEqual(len(p.calls),1)
        self.assertEqual(p.calls[0][0],['version','--format','{{json .Client.Version}}'])

    def test_actual_wget_status_exit_contract_against_only_owned_loopback(self):
        # Testzweck: echter GNU-Wget-401/6-Vertrag statt synthetisch erfundenem Exit8; kein externer Zugriff.
        executable=Path(shutil.which('wget')).resolve();digest=hashlib.sha256(executable.read_bytes()).hexdigest()
        version=t.BoundedPipe().exchange([str(executable),'--version'],b'',[],timeout=3)
        match=a.re.match(rb'GNU Wget ([0-9]+(?:\.[0-9]+){1,2}) ',version.stdout);self.assertIsNotNone(match)
        self.assertEqual(version.exit_code,0);requested=[];seen=[]
        class Handler(http.server.BaseHTTPRequestHandler):
            protocol_version='HTTP/1.1'
            def log_message(self,*args):pass
            def do_GET(self):
                requested.append(self.path);status=int(self.path[1:]) if self.path in ('/200','/401','/503','/302') else 500
                self.send_response(status);self.send_header('Content-Length','0')
                if status==401:self.send_header('WWW-Authenticate','Bearer')
                if status==302:self.send_header('Location','http://127.0.0.1:'+str(self.server.server_port)+'/unexpected')
                self.end_headers()
        server=http.server.HTTPServer(('127.0.0.1',0),Handler);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        ports,_=self.make();ports.api_cid='d'*64
        def raw(args,**kwargs):
            offset=args.index('wget');argv=[str(executable),*args[offset+1:]]
            # Nur Port des eigenen Fakes anders; alle Wget-Flags und localhost bleiben gleich.
            argv[-1]=argv[-1].replace('127.0.0.1:8080','127.0.0.1:'+str(server.server_port))
            result=t.BoundedPipe().exchange(argv,b'',[],timeout=kwargs['timeout'])
            statuses=a.re.findall(rb'(?m)^\s*HTTP/[0-9.]+ ([0-9]{3})[^\r\n]*\r?$',result.stderr)
            seen.append(dict(requestedStatus=int(argv[-1].rsplit('/',1)[1]),httpStatus=int(statuses[0]) if len(statuses)==1 else None,clientExit=result.exit_code))
            return result
        ports._raw=raw
        try:
            self.assertEqual(ports._http('/200',5),(200,b''));self.assertEqual(ports._http('/503',5),(503,b''))
            with self.assertRaises(op.PortFailure):ports._http('/302',5)
            self.assertEqual(ports._http('/401',5),(401,b''))
        finally:
            server.shutdown();server.server_close();thread.join(3);self.assertFalse(thread.is_alive())
            self.assertEqual(hashlib.sha256(executable.read_bytes()).hexdigest(),digest)
            self.assertNotIn('/unexpected',requested)
            print('WGET_LOCAL_CONTRACT_SAFE '+json.dumps(dict(clientVersion=match[1].decode(),binarySha256=digest,cases=seen,ownHandles=0),separators=(',',':')))
        self.assertEqual(seen[-1],dict(requestedStatus=401,httpStatus=401,clientExit=6))

    def test_http_status_and_exit_must_match_and_no_status_is_not_auth_proof(self):
        # Testzweck: Exit6 allein, falsche Status-/Exitpaare, Redirects und TLSfehler nie als401 auslegen.
        ports,_=self.make();ports.api_cid='d'*64
        for status,exit in [(200,6),(503,6),(401,0),(401,4),(401,8),(302,6),(302,8),(None,6),(None,5)]:
            stderr=b'' if status is None else ('  HTTP/1.1 '+str(status)+' TEST\n').encode()
            with self.subTest(status=status,exit=exit),mock.patch.object(ports,'_raw',return_value=t.PipeResult(exit,b'',stderr)),self.assertRaises(op.PortFailure):ports._http('/Definition',3)
        with mock.patch.object(ports,'_raw',return_value=t.PipeResult(4,b'',b'')):self.assertEqual(ports._http('/health/ready',3),(0,b''))

    def test_invalid_settings_block_before_remote_io(self):
        # Testzweck: keine Broadproxy-/fremdeRealm-/clearHTTP-/Rootuserbindung.
        ports,p=self.make()
        for authority,cidr in [('http://synthetic.invalid/realms/TickyTask','172.30.77.0/24'),('https://synthetic.invalid/realms/Other','172.30.77.0/24'),('https://synthetic.invalid/realms/TickyTask','0.0.0.0/0')]:
            with self.subTest(authority=authority,cidr=cidr),self.assertRaises(op.PortFailure):a.PublicSettings(authority,'aud','bff','directory','12345678-1234-1234-1234-123456789abc',cidr).environment()
if __name__=='__main__':unittest.main()
