"""Testzweck: konkrete Docker-/SSHverdrahtung nur mit Fakepipe, keine privaten Prozesse."""
import importlib.util,sys,json,shlex,unittest,subprocess,warnings,errno,tempfile,shutil,os,hashlib,re,http.server,socketserver,threading
from unittest import mock
from pathlib import Path
D=Path(__file__).parent
for name in ['manual_postgres','manual_transport','manual_pg_adapter']:
 s=importlib.util.spec_from_file_location(name,D/(name+'.py'));m=importlib.util.module_from_spec(s);sys.modules[name]=m;s.loader.exec_module(m)
a=sys.modules['manual_pg_adapter'];pg=sys.modules['manual_postgres'];transport=sys.modules['manual_transport']
class Pipe:
    def __init__(self):self.calls=[];self.labels={};self.fail=None;self.ready_codes=[0];self.mount_transform=lambda x:x
    def exchange(self,argv,payload,secrets,**options):
        args=a.remote_docker_arguments(argv[-1]);self.calls.append((args,bytes(payload),list(argv)))
        if self.fail and self.fail in args:raise transport.TransportFailure('TIMEOUT')
        out=b'';err=b''
        if args[:2]==['image','inspect']:out=json.dumps(dict(id='sha256:'+'a'*64,os='linux',arch='arm64')).encode()
        elif args[:2]==['volume','create']:
            name=args[-1];self.labels[name]=dict(v.split('=',1) for i,v in enumerate(args) if i and args[i-1]=='--label');out=name.encode()+b'\n'
        elif args[:2]==['volume','inspect']:out=json.dumps(dict(name=args[2],driver='local',labels=self.labels[args[2]])).encode()
        elif args[:2]==['container','run'] and '--detach' in args:out=b'b'*64+b'\n'
        elif args[:2]==['container','inspect']:
            run=next(v[0] for v in reversed(self.calls[:-1]) if v[0][:2]==['container','run'] and '--detach' in v[0])
            labs=dict(v.split('=',1) for i,v in enumerate(run) if i and run[i-1]=='--label')
            mounts=[dict(type='volume',name=v.split('source=',1)[1].split(',',1)[0],destination=v.split('target=',1)[1],rw=True) for i,v in enumerate(run) if i and run[i-1]=='--mount']
            mounts=self.mount_transform(mounts)
            if '.Destination' not in args[-1]:mounts=[v['name'] for v in mounts]
            out=json.dumps(dict(id='b'*64,image='sha256:'+'a'*64,labels=labs,user='999:999',running=True,network='none',log='none',restart='no',mounts=mounts+[None])).encode()
        elif any(v.endswith('/pg_isready') for v in args):
            return transport.PipeResult(self.ready_codes.pop(0) if len(self.ready_codes)>1 else self.ready_codes[0],b'',b'')
        elif args[:2]==['container','exec']:err=b'Password: \nPassword for user postgres: \n'
        return transport.PipeResult(0,out,err)
class PgAdapterTests(unittest.TestCase):
    def binding(self,**changes):
        return a.PgBinding(**dict(dict(namespace='syntheticflowzer123456',owner='12345678-1234-1234-1234-123456789abc',nonce='c'*32,image='sha256:'+'a'*64,pg_uid=999,pg_gid=999,key_uid=1654,key_gid=1654,source_sha='d'*40,run_id='12345'),**changes))
    def adapter(self,pipe=None,**changes):
        return a.PgBootstrapAdapter(self.binding(**changes),a.SshTarget('192.0.2.8','synthetic.invalid','/synthetic/known_hosts'),lambda:None,pipe or Pipe())
    def scripts(self):
        d=D.resolve().parents[1]/'postgresql';return (d/'01-datenbank-und-rollen.sql').read_bytes(),(d/'02-laufzeitrechte.sql').read_bytes()
    def initialize(self,p=None):
        p=p or Pipe();x=self.adapter(p);x.initialize(admin='synthetic-admin-123',migration='synthetic-migration-123',runtime='synthetic-runtime-123',sql01=self.scripts()[0],sql02=self.scripts()[1]);return x,p
    def test_sources_compile_without_escape_warnings_on_fresh_runner(self):
        # Testzweck: Python3.12 meldet ungültige Docstringescapes auf frischen Hostedrunnern sichtbar.
        for name in ['manual_postgres.py','manual_transport.py','manual_pg_adapter.py']:
            with self.subTest(name=name),warnings.catch_warnings():
                warnings.simplefilter('error')
                compile((D/name).read_text(),name,'exec')
    def test_default_isolated_cli_is_source_only(self):
        # Testzweck: auch -I lädt nur eigene Quellmodule, startet niemals SSH/Docker.
        r=subprocess.run([sys.executable,'-I','-B',str(D/'manual_pg_adapter.py')],stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=3)
        self.assertEqual(r.returncode,0);self.assertEqual(r.stdout,b'FLOWZER_INITIAL_PG_ADAPTER_SOURCE_ONLY\n');self.assertFalse(r.stderr)
    def test_actual_phase_order_no_secret_env_argv_and_no_anonymous_image_volumes(self):
        # Testzweck: kein Env-/argv-Passwort, PGimage-VOLUME immer explizit auf eigenen Datenträger gebunden.
        x,p=self.initialize();x.pg_start();x.bootstrap()
        runs=[v for v in p.calls if v[0][:2]==['container','run']]
        self.assertEqual(len(runs),4)
        for args,payload,argv in runs:
            self.assertIn('--pull=never',args);self.assertIn('--log-driver=none',args);self.assertIn('--restart=no',args)
            self.assertTrue(any('target=/var/lib/postgresql/data' in q for q in args));self.assertNotIn('synthetic-admin-123',' '.join(argv))
            self.assertNotIn('--privileged',args);self.assertNotIn('--env',args)
        self.assertEqual(x.state,'BOOTSTRAPPED');self.assertEqual(x.pg_cid,'b'*64)
    def test_waits_for_own_pg_before_password_delivery(self):
        # Testzweck: Running-CID ist keine PGbereitschaft, nur begrenztes read-only Polling.
        x,p=self.initialize();p.ready_codes=[2,1,0];x.pg_start();x.bootstrap()
        probes=[i for i,(args,_,_) in enumerate(p.calls) if any(v.endswith('/pg_isready') for v in args)]
        sql=[i for i,(args,_,_) in enumerate(p.calls) if any(v.endswith('/psql') for v in args)]
        self.assertEqual(len(probes),3);self.assertLess(probes[-1],sql[0])
    def test_reuse_or_restart_never_reinitializes_or_repairs(self):
        # Testzweck: ein Adapterversuch ist einmalig, keine automatische Adoption oder Reparatur.
        x,p=self.initialize();before=len(p.calls)
        with self.assertRaises(a.PgAdapterFailure):x.initialize(admin='synthetic-admin-123',migration='synthetic-migration-123',runtime='synthetic-runtime-123',sql01=self.scripts()[0],sql02=self.scripts()[1])
        self.assertEqual(len(p.calls),before)
    def test_full_payload_prepared_before_any_docker_action(self):
        # Testzweck: große Paketwerte vor Volume-/Containeranlage zurückweisen.
        p=Pipe();x=self.adapter(p)
        with self.assertRaises(a.PgAdapterFailure):x.initialize(admin='x'*4096,migration='y'*4096,runtime='z'*4096,sql01=self.scripts()[0],sql02=self.scripts()[1])
        self.assertEqual(p.calls,[])
    def test_unknown_docker_exit_blocks_followup_and_does_not_remove_resources(self):
        # Testzweck: ein unklarer Create-/Startausgang darf keinen Retry oder cleanupWrite auslösen.
        p=Pipe();p.fail='create';x=self.adapter(p)
        with self.assertRaises(a.PgAdapterFailure):x.initialize(admin='synthetic-admin-123',migration='synthetic-migration-123',runtime='synthetic-runtime-123',sql01=self.scripts()[0],sql02=self.scripts()[1])
        self.assertEqual(x.state,'UNKNOWN');self.assertFalse(any('rm' in args or 'stop' in args for args,_,_ in p.calls))
    def test_typed_bindings_and_strict_ssh_without_private_defaults(self):
        # Testzweck: keine Tags/Root-PGuser/Shellsonderzeichen oder geerbten Proxy-/Configpfade.
        for changes in [dict(namespace='x;unsafe'),dict(image='postgres:17'),dict(pg_uid=0),dict(pg_gid=True),dict(nonce='bad'),dict(owner='-'*36)]:
            with self.subTest(changes=changes),self.assertRaises(a.PgAdapterFailure):self.adapter(**changes)
        x,p=self.initialize();argv=p.calls[0][2]
        for fixed in ['StrictHostKeyChecking=yes','ConnectionAttempts=1','ClearAllForwardings=yes','ProxyCommand=none','IdentitiesOnly=yes']:self.assertIn(fixed,argv)
        self.assertEqual(argv[:3],['/usr/bin/ssh','-F','/dev/null'])
    def test_wrong_mount_destination_or_readonly_does_not_allow_sql(self):
        # Testzweck: gleiche Volumenamen sind kein Beleg für richtige Daten-/Socketziele und Schreibbarkeit.
        for mode in ['swapped','readonly','bind']:
            p=Pipe();x,p=self.initialize(p)
            def mutate(rows):
                rows=[dict(v) if isinstance(v,dict) else dict(type='volume',name=v,destination='/WRONG',rw=True) for v in rows]
                if mode=='swapped':
                    rows[0]['destination'],rows[1]['destination']=rows[1]['destination'],rows[0]['destination']
                elif mode=='readonly':rows[0]['rw']=False
                else:rows[0]['type']='bind'
                return rows
            p.mount_transform=mutate
            with self.subTest(mode=mode),self.assertRaises(a.PgAdapterFailure):x.pg_start()
            self.assertFalse(any(any(v.endswith('/psql') for v in args) for args,_,_ in p.calls))

    def test_only_exact_two_password_prompts_are_accepted(self):
        # Testzweck: freier stderr/Error/zusätzlicher Prompt ist kein erwarteter psqlDialog.
        self.assertTrue(a.expected_password_prompts(b'Password: \nPassword for user postgres: \n'))
        for data in [b'',b'Password: ',b'Password: PRIVATE_ERROR',b'Password: Password: Password: ']:self.assertFalse(a.expected_password_prompts(data))
    def test_remote_envelope_has_fixed_absence_env_config_and_socket_not_root_defaults(self):
        # Testzweck: SSHchild erbt weder Root-HOME/config noch Proxy/Context/Credentialhelper-PATH.
        words=shlex.split(self.adapter().target.argv(['version'])[-1])
        for value in ['/usr/bin/python3','-I','-S','-B','-c','/usr/bin/test','/usr/bin/env','-i','PATH=/nonexistent','HOME=/nonexistent','DOCKER_CONFIG=/nonexistent','LC_ALL=C','--config','/nonexistent','--host','unix:///run/docker.sock']:self.assertIn(value,words)
        self.assertEqual(words.count('&&'),3);self.assertEqual(a.remote_docker_arguments(self.adapter().target.argv(['version'])[-1]),['version'])

    def test_fixed_lstat_guard_only_accepts_actual_enoent_and_never_metadata_errors(self):
        # Testzweck: !-e allein würde Metadatenfehler mit Abwesenheit verwechseln; exakt ein lstat ohne Rohtext.
        for error,exit in [(FileNotFoundError(errno.ENOENT,'synthetic'),0),(FileNotFoundError(errno.EACCES,'synthetic'),1),(PermissionError(errno.EACCES,'synthetic'),1),(OSError(errno.EIO,'synthetic'),1),(None,1)]:
            with self.subTest(error=type(error).__name__),mock.patch('os.lstat',side_effect=error,return_value=object()) as check:
                with self.assertRaises(SystemExit) as end:exec(a.REMOTE_LSTAT_GUARD,{})
                self.assertEqual(end.exception.code,exit);check.assert_called_once_with('/nonexistent')

    def neutral_test_command(self,command,path,executable,socket=None):
        # Testzweck: nur eigene Testpfade/Binary/Socket ersetzen, keine SSH-/Daemonaktion.
        remote=self.adapter().target.argv(command)[-1].replace('/usr/bin/docker',str(executable)).replace('/usr/bin/python3',sys.executable)
        # macOS besitzt /bin/test statt /usr/bin/test; Linux-Sourceprefix bleibt exakt.
        if not Path('/usr/bin/test').exists():remote=remote.replace('/usr/bin/test','/bin/test')
        remote=remote.replace('/nonexistent',str(path))
        if socket is not None:remote=remote.replace('unix:///run/docker.sock','unix://'+str(socket))
        return remote

    def test_actual_env_prefix_suppresses_poisoned_home_context_proxy_and_helper(self):
        # Testzweck: Original-BoundedPipe führt nur eigenen Stub aus; geerbter Helfer darf nicht aufgerufen werden.
        with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-neutral-env-') as temp:
            root=Path(temp);absent=root/'absent';poison=root/'poison';poison.mkdir();marker=root/'helper-ran';stub=root/'own-client'
            helper=poison/'docker-credential-canary';helper.write_text('#!'+sys.executable+'\nfrom pathlib import Path\nPath('+repr(str(marker))+').touch()\n');helper.chmod(0o700)
            stub.write_text('#!'+sys.executable+'\nimport os,json,shutil,subprocess\np=shutil.which("docker-credential-canary")\nif p:subprocess.run([p],check=True)\nprint(json.dumps(dict(home=os.getenv("HOME"),config=os.getenv("DOCKER_CONFIG"),path=os.getenv("PATH"),forbidden=[k for k in ["DOCKER_CONTEXT","DOCKER_HOST","DOCKER_TLS_VERIFY","HTTPS_PROXY","LD_LIBRARY_PATH"] if k in os.environ],helper=p)))\n');stub.chmod(0o700)
            argv=['/usr/bin/env','HOME='+str(poison),'DOCKER_CONFIG='+str(poison),'PATH='+str(poison),'DOCKER_CONTEXT=synthetic-poison','DOCKER_HOST=unix://'+str(root/'never.sock'),'DOCKER_TLS_VERIFY=1','HTTPS_PROXY=http://synthetic.invalid:9','LD_LIBRARY_PATH='+str(poison),'/bin/sh','-c',self.neutral_test_command(['version'],absent,stub)]
            result=transport.BoundedPipe().exchange(argv,b'',[],timeout=5);self.assertEqual(result.exit_code,0);self.assertFalse(result.stderr)
            value=json.loads(result.stdout);self.assertEqual(value,dict(home=str(absent),config=str(absent),path=str(absent),forbidden=[],helper=None));self.assertFalse(marker.exists())

    def test_actual_cli_own_unix_socket_ignores_poison_and_blocks_present_or_symlink_scope(self):
        # Testzweck: echter versions-/digestgebundener Client erreicht nur eigenen AF_UNIX-Fake ohne fremde Config.
        docker=Path(shutil.which('docker')).resolve();digest=hashlib.sha256(docker.read_bytes()).hexdigest()
        version=transport.BoundedPipe().exchange([str(docker),'--version'],b'',[],timeout=3)
        match=re.fullmatch(rb'Docker version ([0-9]+\.[0-9]+\.[0-9]+)[^\n]*\n?',version.stdout);self.assertIsNotNone(match)
        requests=[]
        class Server(socketserver.UnixStreamServer):pass
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self,*args):pass
            def do_HEAD(self):self.reply(True)
            def do_GET(self):self.reply(False)
            def reply(self,head):
                path=re.sub(r'^/v[0-9.]+','',self.path);requests.append(path)
                value=b'OK' if path=='/_ping' else json.dumps(dict(Version='27.5.1',ApiVersion='1.47',MinAPIVersion='1.24',Os='linux',Arch='amd64',Components=[])).encode()
                self.send_response(200);self.send_header('API-Version','1.47');self.send_header('Content-Length',str(len(value)));self.end_headers()
                if not head:self.wfile.write(value)
        with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-neutral-cli-') as temp:
            root=Path(temp);sock=root/'own.sock';absent=root/'absent';poison=root/'poison';poison.mkdir()
            (poison/'config.json').write_text(json.dumps(dict(currentContext='synthetic-poison',proxies={'default':{'httpProxy':'http://synthetic.invalid:9'}},credsStore='canary')))
            server=Server(str(sock),Handler);thread=threading.Thread(target=server.serve_forever,daemon=False);thread.start()
            remote=self.neutral_test_command(['version','--format','{{json .Client.Version}}'],absent,docker,sock)
            argv=['/usr/bin/env','HOME='+str(poison),'DOCKER_CONFIG='+str(poison),'DOCKER_CONTEXT=synthetic-poison','DOCKER_HOST=unix://'+str(root/'never.sock'),'HTTPS_PROXY=http://synthetic.invalid:9','/bin/sh','-c',remote]
            try:
                result=transport.BoundedPipe().exchange(argv,b'',[],timeout=5);self.assertEqual(result.exit_code,0);self.assertFalse(result.stderr);self.assertEqual(json.loads(result.stdout),match[1].decode());self.assertTrue(requests)
                count=len(requests)
                for symlink in [False,True]:
                    if symlink:absent.symlink_to(poison,target_is_directory=True)
                    else:absent.mkdir()
                    blocked=transport.BoundedPipe().exchange(argv,b'',[],timeout=5);self.assertNotEqual(blocked.exit_code,0);self.assertFalse(blocked.stdout);self.assertFalse(blocked.stderr);self.assertEqual(len(requests),count)
                    if symlink:absent.unlink()
                    else:absent.rmdir()
            finally:
                server.shutdown();server.server_close();thread.join(3);self.assertFalse(thread.is_alive());self.assertEqual(hashlib.sha256(docker.read_bytes()).hexdigest(),digest)
            print('REMOTE_NEUTRAL_LOCAL_CLI_SAFE '+json.dumps(dict(clientVersion=match[1].decode(),binarySha256=digest,ownUnixOnly=True,rootConfigRead=False,ownHandles=0),separators=(',',':')))

if __name__=='__main__':unittest.main()
