"""Nur-PG-Hostedqualifikation, default inert; keine privaten Ziele/Secrets/Images.

Ein späterer Root-Dispatch bindet Source/Workflow/Nonce/Frist. Drei voneinander
getrennte Wegwerfcluster prüfen den wirklichen stdin/psql/EOF-Kanal. Kein API-,
Keycloak-, Cool3-, Imagepublisher- oder allgemeiner Reparaturpfad.
"""
import hashlib,importlib.util,json,os,platform,re,selectors,shlex,signal,stat,subprocess,sys,time,uuid
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
S=ROOT/'deploy/tickytask-demo-coolify/supervised-operator'
for _name in ('manual_postgres','manual_transport','manual_pg_adapter'):
    _spec=importlib.util.spec_from_file_location(_name,S/(_name+'.py'));_m=importlib.util.module_from_spec(_spec)
    sys.modules[_name]=_m;_spec.loader.exec_module(_m)
pg=sys.modules['manual_pg_adapter'];transport=sys.modules['manual_transport']
IMAGE='docker.io/library/postgres@sha256:66aafa11cf15800a3c94763f7e11d1e7b2e37e5e84bc4ce027cb6e1e4bfaf4df'
IMAGE_ID='sha256:589f742565e4f6d22149176b032974f55f8201a8a37b71ed587ec5c6902e689f'
CASES=('positive','missingpw','noeof');MAX_CONTAINERS=9;MAX_VOLUMES=6
WF='.github/workflows/ci.yml'
FILES=(WF,'tests/manual-postgres-channel/run_fixture.py',*[str((S/(n+'.py')).relative_to(ROOT)) for n in ('manual_postgres','manual_transport','manual_pg_adapter')],
       'deploy/postgresql/01-datenbank-und-rollen.sql','deploy/postgresql/02-laufzeitrechte.sql')
CLEAN_ENV={'PATH':'/usr/bin:/bin','HOME':'/nonexistent','DOCKER_CONFIG':'/nonexistent','LC_ALL':'C'}

class FixtureFailure(Exception):
    """Nur feste Fehlercodes, kein freier Rohkanal/Secret/Exceptiontext."""
    CODES={'OPENING','SOURCE','ONCE','DEADLINE','DOCKER','PROJECTION','EXPECTED_NEGATIVE','EOF','LEAK','CLEANUP','TECHNICAL'}
    def __init__(self,code='TECHNICAL',own_pid=None):
        self.code=code if code in self.CODES else 'TECHNICAL';self.own_pid=own_pid if type(own_pid) is int and own_pid>0 else None
        self.local_cleanup='UNKNOWN' if self.own_pid else 'CLOSED'
        super().__init__('PG_CHANNEL_'+self.code)

def require(v,code='PROJECTION'):
    if not v:raise FixtureFailure(code)

def validate_opening(env,now):
    """Öffentliche Dispatchbindung; ersetzt nicht Roots vorherige Freigabe."""
    try:
        fixed=dict(GITHUB_EVENT_NAME='workflow_dispatch',GITHUB_RUN_ATTEMPT='1',GITHUB_REPOSITORY='flowzer-io/flowzer-bpmn-core-engine',
                   RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',RUNNER_ARCH='X64')
        require(all(env.get(k)==v for k,v in fixed.items()),'OPENING')
        require(env['GITHUB_REF'].startswith('refs/heads/codex/'),'OPENING')
        raw=env['PG_CHANNEL_OPENING']
        require(isinstance(raw,str) and len(raw.encode('utf-8'))<=4096 and env.get('PG_LEGACY_OPENING','')=='','OPENING')
        def unique(pairs):
            result={}
            for key,value in pairs:
                if key in result:raise ValueError()
                result[key]=value
            return result
        opening=json.loads(raw,object_pairs_hook=unique)
        require(type(opening) is dict and set(opening)=={'mode','sourceSha','sourceTree','workflowSha256','nonce','expiresAt'}
                and opening['mode']=='pg-channel-v1','OPENING')
        sha=opening['sourceSha'];tree=opening['sourceTree'];nonce=opening['nonce'];wf=opening['workflowSha256'];end=opening['expiresAt']
        require(all(type(v) is str for v in [sha,tree,nonce,wf]) and re.fullmatch('[a-f0-9]{40}',sha)
                and re.fullmatch('[a-f0-9]{40}',tree) and re.fullmatch('[a-f0-9]{32}',nonce)
                and re.fullmatch('[a-f0-9]{64}',wf) and sha==env['GITHUB_SHA']
                and re.fullmatch('[0-9]{1,20}',env['GITHUB_RUN_ID']),'OPENING')
        require(env.get('GITHUB_WORKFLOW_REF')=='flowzer-io/flowzer-bpmn-core-engine/'+WF+'@'+env['GITHUB_REF'],'OPENING')
        require(type(end) is int and now<end<=now+900,'OPENING')
        return dict(sha=sha,tree=tree,nonce=nonce,workflow=wf,end=end,run=env['GITHUB_RUN_ID'])
    except Exception:raise FixtureFailure('OPENING') from None

def namespace(nonce,case):
    require(case in CASES and re.fullmatch('[a-f0-9]{32}',nonce));return 'flowzer-pg-channel-'+nonce+'-'+case

def omit_reconnect_password(payload,password):
    marker=b'\\connect :datenbank\n'+password+b'\n';require(payload.count(marker)==1)
    return payload.replace(marker,b'\\connect :datenbank\n')

def case_proof(v):
    require(type(v) is dict and set(v)=={'case','expected','bootstrap_exit','prompts','eof_wait_observed','role_acl_verified','cleanup'})
    require(v['case'] in CASES and type(v['expected']) is bool and type(v['prompts']) is int and 0<=v['prompts']<=2
            and type(v['eof_wait_observed']) is bool and type(v['role_acl_verified']) is bool and v['cleanup'] in ('ABSENT','UNKNOWN')
            and (v['bootstrap_exit'] is None or type(v['bootstrap_exit']) is int and -255<=v['bootstrap_exit']<=255));return v

def git(*args):
    r=subprocess.run(['/usr/bin/git','-C',str(ROOT),*args],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=3,env=CLEAN_ENV)
    require(r.returncode==0 and not r.stderr,'SOURCE');return r.stdout

def source_guard(env):
    p=validate_opening(env,time.time())
    require(platform.system()=='Linux' and platform.machine()=='x86_64','OPENING')
    require(git('rev-parse','HEAD').decode().strip()==p['sha'] and git('rev-parse','HEAD^{tree}').decode().strip()==p['tree'],'SOURCE')
    require(hashlib.sha256((ROOT/WF).read_bytes()).hexdigest()==p['workflow'],'SOURCE')
    # Auch uncommittete/geänderte Helfer dürfen nicht unter demselben Source-SHA laufen.
    for name in FILES:require(git('show','HEAD:'+name)==(ROOT/name).read_bytes(),'SOURCE')
    return p

class LocalDockerPipe:
    """Nur der Hostedtest übersetzt das feste synthetische SSHziel auf lokalen Docker.

    Dies ist kein Produktionsfallback: niemals SSH starten, keine freien Ziele,
    keine private Konfiguration. Alle echten Prozesse laufen im frischen Hostedjob.
    """
    def __init__(self,guard,plan):
        self.guard=guard;self.plan=plan;self.pipe=transport.BoundedPipe();self.mode='positive';self.last=None;self.eof=False;self.names=[];self.failure=None;self.local_pid=None;self._cleaned_count=-1;self._cleanup_state=None;self.command='NONE';self.command_exit=None;self.stderr_empty=None;self.end=time.monotonic()+180
    def budget(self):require(time.monotonic()<self.end,'DEADLINE')
    def command_id(self,args):
        if args[:2]==['image','pull']:return 'PULL'
        if args[:2]==['image','inspect']:return 'IMAGE'
        if args[0]=='ps':return 'OWN_CONTAINERS'
        if args[0]=='volume':
            require(args[1] in ('create','inspect','ls','rm'));return 'VOLUME_'+args[1].upper()
        if args[:2]==['container','run']:
            entry=args[args.index('--entrypoint')+1]
            return 'INITDB' if entry.endswith('/initdb') else 'PG_START' if entry.endswith('/postgres') else 'OWNERSHIP'
        if args[:2]==['container','exec']:
            return 'PG_READY' if any(v.endswith('/pg_isready') for v in args) else 'PSQL_VERSION' if '--version' in args else 'PSQL_SQL'
        require(args[:2] in (['container','inspect'],['container','stop'],['container','rm']))
        return 'CONTAINER_'+args[1].upper()
    def plain(self,args,payload=b'',secrets=(),timeout=20,cleanup=False):
        if not cleanup:
            self.guard();self.budget();timeout=min(timeout,max(.01,self.end-time.monotonic()))
        self.command=self.command_id(args);self.command_exit=None;self.stderr_empty=None
        try:
            r=self.pipe.exchange(['/usr/bin/docker','--host','unix:///var/run/docker.sock',*args],payload,list(secrets),timeout=timeout)
            self.command_exit=r.exit_code;self.stderr_empty=not r.stderr;return r
        except transport.TransportFailure as e:
            self.failure='LEAK' if e.code=='LEAK' else 'DOCKER';self.local_pid=e.own_pid or self.local_pid
            raise FixtureFailure(self.failure,e.own_pid) from None
    def checked(self,args,**kw):
        r=self.plain(args,**kw);require(type(r.exit_code) is int and r.exit_code==0,'DOCKER');return r.stdout
    def exchange(self,argv,payload,secrets,**options):
        require(argv[-2]=='root@synthetic.invalid' and argv[:3]==['/usr/bin/ssh','-F','/dev/null'])
        try:args=pg.remote_docker_arguments(argv[-1])
        except pg.PgAdapterFailure:raise FixtureFailure('SOURCE') from None
        is_psql=any(v.endswith('/psql') for v in args)
        if is_psql and self.mode=='missingpw':payload=omit_reconnect_password(payload,secrets[0])
        r=self.hold_eof(args,payload,secrets) if is_psql and self.mode=='noeof' else self.plain(args,payload,secrets,timeout=options.get('timeout',20))
        if is_psql:
            # Kein stderrtext speichern, nur die zwei bekannten Promptmarker zählen.
            count=r.stderr.count(b'Password: ')+r.stderr.count(b'Password for user postgres: ')
            self.last=dict(exit=r.exit_code,prompts=count)
        return r
    def hold_eof(self,args,payload,secrets):
        """Echte eigene Dockerexec-Pipes: zwei Sekunden kein EOF/kein vorzeitiger Exit.

        Anschließend explizites EOF und terminaler Exit/OutputEOF. Kein Testmodus
        oder EOF-Ausweg im produktiven BoundedPipe; diese Manipulation lebt nur hier.
        """
        self.guard();self.budget();self.command='PSQL_EOF';self.command_exit=None;self.stderr_empty=None;p=None;out=bytearray();err=bytearray();offset=0;held=None;until=min(self.end,time.monotonic()+15);failure=None;result=None
        try:
            p=subprocess.Popen(['/usr/bin/docker','--host','unix:///var/run/docker.sock',*args],stdin=subprocess.PIPE,stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,env=CLEAN_ENV,close_fds=True,start_new_session=True)
            with selectors.DefaultSelector() as sel:
                for stream,event in [(p.stdin,selectors.EVENT_WRITE),(p.stdout,selectors.EVENT_READ),(p.stderr,selectors.EVENT_READ)]:
                    os.set_blocking(stream.fileno(),False);sel.register(stream,event)
                while sel.get_map():
                    self.guard();self.budget();require(time.monotonic()<until,'DEADLINE')
                    if held is not None and not p.stdin.closed:
                        require(p.poll() is None,'EOF')
                        if time.monotonic()-held>=2:p.stdin.close();self.eof=True
                    for key,_ in sel.select(.05):
                        stream=key.fileobj
                        if stream is p.stdin:
                            try:offset+=os.write(stream.fileno(),payload[offset:offset+4096])
                            except BlockingIOError:continue
                            if offset==len(payload):sel.unregister(stream);held=time.monotonic()
                        else:
                            buf=out if stream is p.stdout else err;chunk=os.read(stream.fileno(),4096)
                            if not chunk:sel.unregister(stream);continue
                            require(len(buf)+len(chunk)<=65536,'DOCKER');buf.extend(chunk)
            code=p.wait(timeout=max(.01,until-time.monotonic()));self.pipe._require_empty_group(p.pid)
            markers=transport._sentinels(secrets)
            require(not any(x in channel for channel in (out,err,out+err,err+out) for x in markers),'LEAK')
            require(self.eof,'EOF');self.command_exit=code;self.stderr_empty=not err;result=transport.PipeResult(code,bytes(out),bytes(err))
        except BaseException as error:
            failure=error if isinstance(error,FixtureFailure) else FixtureFailure('DOCKER',getattr(error,'own_pid',None))
        finally:
            out.clear();err.clear()
            if p is not None:
                for stream in (p.stdin,p.stdout,p.stderr):stream.close()
                try:self.pipe._finish_own(p)
                except BaseException as error:
                    if failure is None:failure=FixtureFailure('CLEANUP')
                    failure.own_pid=getattr(error,'own_pid',None) or p.pid;failure.local_cleanup='UNKNOWN'
        if failure is not None:
            self.failure=failure.code;self.local_pid=failure.own_pid;raise failure
        return result

    def cleanup(self):
        """Nur exakt neu eigene Nonce-/Source-/Run-CIDs/Volumes, max30s, kein Prune."""
        if self._cleaned_count==len(self.names):return self._cleanup_state
        self._cleaned_count=len(self.names);self._cleanup_state='UNKNOWN'
        end=time.monotonic()+30
        def cmd(args):
            require(time.monotonic()<end,'CLEANUP')
            r=self.plain(args,timeout=min(5,max(.1,end-time.monotonic())),cleanup=True)
            require(r.exit_code==0 and not r.stderr,'CLEANUP');return r.stdout
        try:
            raw=cmd(['ps','-a','--no-trunc','--filter','label=flowzer.tt.demo.nonce='+self.plan['nonce'],'--format','{{.ID}}'])
            ids=raw.decode('ascii').splitlines();require(len(ids)<=MAX_CONTAINERS and len(set(ids))==len(ids),'CLEANUP')
            for cid in ids:
                require(re.fullmatch('[a-f0-9]{64}',cid),'CLEANUP')
                fmt='{"id":{{json .ID}},"name":{{json .Name}},"image":{{json .Image}},"labels":{{json .Config.Labels}},"running":{{json .State.Running}}}'
                v=json.loads(cmd(['container','inspect',cid,'--format',fmt]));labs=v.get('labels');require(type(labs) is dict,'CLEANUP')
                ns=labs.get('com.docker.compose.project');require(ns in self.names,'CLEANUP')
                expected=self.labels(ns);allowed=[ns+'_'+r+'_'+self.plan['nonce'] for r in ['ownership-pg','initdb','postgres']]
                require(v['id']==cid and v['image']==IMAGE_ID and labs==expected and v['name'].removeprefix('/') in allowed and type(v['running']) is bool,'CLEANUP')
                if v['running']:cmd(['container','stop','--time=3',cid])
                cmd(['container','rm',cid])
            for ns in self.names:
                rows=cmd(['volume','ls','--filter','name=^'+ns+'_','--format','{{.Name}}']).decode('ascii').splitlines()
                require(set(rows)<=set([ns+'_postgres-data',ns+'_socket']),'CLEANUP')
                for name in rows:
                    fmt='{"name":{{json .Name}},"driver":{{json .Driver}},"labels":{{json .Labels}}}'
                    v=json.loads(cmd(['volume','inspect',name,'--format',fmt]))
                    require(v==dict(name=name,driver='local',labels=self.labels(ns)),'CLEANUP');cmd(['volume','rm',name])
                require(not cmd(['volume','ls','--filter','name=^'+ns+'_','--format','{{.Name}}']).strip(),'CLEANUP')
            require(not cmd(['ps','-a','--filter','label=flowzer.tt.demo.nonce='+self.plan['nonce'],'--format','{{.ID}}']).strip(),'CLEANUP')
            self._cleanup_state='ABSENT';return 'ABSENT'
        except BaseException:return 'UNKNOWN'
    def labels(self,ns):
        return {'com.docker.compose.project':ns,'flowzer.tt.demo.owner':str(uuid.UUID(self.plan['nonce'])),'flowzer.tt.demo.nonce':self.plan['nonce'],
                'flowzer.tt.demo.source':self.plan['sha'],'flowzer.tt.demo.run':self.plan['run']}

ACL_SQL=b"""SELECT
 (SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='flowzer_tt_demo_migration')
 AND (SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='flowzer_tt_demo')
 AND (SELECT pg_get_userbyid(datdba)='flowzer_tt_demo_migration' FROM pg_database WHERE datname='flowzer_tt_demo')
 AND has_schema_privilege('flowzer_tt_demo','flowzer','USAGE')
 AND NOT has_schema_privilege('flowzer_tt_demo','flowzer','CREATE')
 AND has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','SELECT')
 AND NOT has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','INSERT')
 AND NOT has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','UPDATE')
 AND NOT has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','DELETE')
 AND (SELECT count(*)=0 FROM flowzer.schema_migrations);
"""

def role_acl_proof(port,cid,values):
    """Echte synthetische Rollenlogins/DML und DDL-/Historienverweigerung, nie privat."""
    def sql(user,password,body,expected_zero=True):
        args=['container','exec','--interactive','--user','999:999',cid,'/usr/lib/postgresql/17/bin/psql',
              '--no-psqlrc','--no-readline','--quiet','--tuples-only','--no-align','--set','ON_ERROR_STOP=1',
              '--set','VERBOSITY=terse','--set','SHOW_CONTEXT=never','--host','/socket','--username',user,
              '--dbname','flowzer_tt_demo','--password','--file','-']
        payload=bytearray(password.encode()+b'\n'+body)
        try:
            r=port.plain(args,payload,[v.encode() for v in values.values()])
            require(type(r.exit_code) is int and (r.exit_code==0 if expected_zero else r.exit_code!=0),'EXPECTED_NEGATIVE')
            if expected_zero:require(r.stdout.strip()==b't' and r.stderr in (b'Password: ',b'Password: \n'))
        finally:payload[:]=b'\0'*len(payload)
    sql('postgres',values['admin'],ACL_SQL)
    sql('flowzer_tt_demo_migration',values['migration'],b'CREATE TABLE flowzer.channel_acl_probe (id integer PRIMARY KEY); SELECT true;\n')
    sql('flowzer_tt_demo',values['runtime'],b"INSERT INTO flowzer.channel_acl_probe VALUES (1); UPDATE flowzer.channel_acl_probe SET id=2; DELETE FROM flowzer.channel_acl_probe; SELECT current_user='flowzer_tt_demo';\n")
    sql('flowzer_tt_demo',values['runtime'],b'CREATE TABLE flowzer.must_not_create (id integer);\n',False)
    sql('flowzer_tt_demo',values['runtime'],b"INSERT INTO flowzer.schema_migrations VALUES (999,'must-not-write',now());\n",False)
    return True

def run(plan,guard):
    """Genau drei neue Cluster, nur synthetische Passwörter, kein Migrate/APIstart."""
    port=LocalDockerPipe(guard,plan);proof=dict(kind='SYNTHETIC_PG_CHANNEL_ONLY',sourceSha=plan['sha'],sourceTree=plan['tree'],nonce=plan['nonce'],
        image=IMAGE,imageId=IMAGE_ID,architecture='linux/amd64',cases=[],result='FAIL',cleanup='UNKNOWN',ownHandles=0)
    try:
        port.checked(['image','pull','--quiet','--platform=linux/amd64',IMAGE],timeout=60)
        image=json.loads(port.checked(['image','inspect',IMAGE,'--format','{"id":{{json .ID}},"os":{{json .Os}},"arch":{{json .Architecture}}}']))
        require(image==dict(id=IMAGE_ID,os='linux',arch='amd64'))
        for case in CASES:
            guard();port.mode=case;port.last=None;port.eof=False;port.failure=None;ns=namespace(plan['nonce'],case);port.names.append(ns)
            binding=pg.PgBinding(ns,str(uuid.UUID(plan['nonce'])),plan['nonce'],IMAGE_ID,999,999,1654,1654,plan['sha'],plan['run'],architecture='amd64',prepare_keyring=False)
            adapter=pg.PgBootstrapAdapter(binding,pg.SshTarget('192.0.2.8','synthetic.invalid','/synthetic/known_hosts'),guard,port)
            # Nur RAM. Metazeichen erzwingen den tatsächlichen PW-/SQLquotingkanal.
            values={k:'synthetic-'+k+'-'+uuid.uuid4().hex+";'\\" for k in ['admin','migration','runtime']}
            try:
                adapter.initialize(**values,sql01=(ROOT/'deploy/postgresql/01-datenbank-und-rollen.sql').read_bytes(),sql02=(ROOT/'deploy/postgresql/02-laufzeitrechte.sql').read_bytes())
                adapter.pg_start()
                version=port.checked(['container','exec','--user','999:999',adapter.pg_cid,'/usr/lib/postgresql/17/bin/psql','--version'],secrets=[v.encode() for v in values.values()])
                require(re.match(rb'^psql \(PostgreSQL\) 17\.[0-9]+',version));proof['psqlMajor']=17
                failed=False
                try:adapter.bootstrap()
                except pg.PgAdapterFailure:failed=True
                if port.failure:raise FixtureFailure(port.failure,port.local_pid)
                require(port.last is not None,'EXPECTED_NEGATIVE')
                expected=failed and port.last['exit']!=0 and port.last['prompts']==2 if case=='missingpw' else not failed and port.last['exit']==0 and port.last['prompts']==2 and (case!='noeof' or port.eof)
                require(expected,'EXPECTED_NEGATIVE')
                acl=False if case=='missingpw' else role_acl_proof(port,adapter.pg_cid,values)
            finally:adapter.close();values.clear()
            cleanup=port.cleanup()
            proof['cases'].append(case_proof(dict(case=case,expected=True,bootstrap_exit=port.last['exit'],prompts=port.last['prompts'],eof_wait_observed=port.eof,role_acl_verified=acl,cleanup=cleanup)))
            require(cleanup=='ABSENT','CLEANUP')
        proof['result']='PASS'
    except BaseException as e:
        proof['failure']=e.code if isinstance(e,FixtureFailure) else 'TECHNICAL'
        proof['primary']=dict(command=port.command,exit=port.command_exit,stderrEmpty=port.stderr_empty)
        pid=getattr(e,'own_pid',None) or port.local_pid
        if type(pid) is int and pid>0:proof.update(ownHandles='UNKNOWN',ownLocalPid=pid,localCleanup='UNKNOWN')
    finally:
        proof['cleanup']=port.cleanup()
        # Dockerressourcen-ABSENT ist kein Beleg für einen unbekannten lokalen Prozess.
        if port.local_pid:
            proof.update(result='FAIL',ownHandles='UNKNOWN',ownLocalPid=port.local_pid,localCleanup='UNKNOWN')
        if proof['cleanup']!='ABSENT':proof['result']='FAIL';proof['ownHandles']='UNKNOWN'
    return proof

def main():
    if sys.argv[1:] not in (['--preflight-only'],['--once']):print('PG_CHANNEL_SOURCE_ONLY');return 0
    try:
        plan=source_guard(os.environ)
        if sys.argv[1:] == ['--preflight-only']:print('PG_CHANNEL_PREFLIGHT_OK');return 0
        parent=Path(os.environ['RUNNER_TEMP']);fd=os.open(parent,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            st=os.fstat(fd);require(st.st_uid==os.getuid() and not st.st_mode&0o022,'ONCE')
            os.mkdir('flowzer-pg-channel-proof',0o700,dir_fd=fd)
        finally:os.close(fd)
        directory=parent/'flowzer-pg-channel-proof';claim=os.open(directory/'once.consumed',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(claim,'w') as stream:stream.write(plan['nonce']);stream.flush();os.fsync(stream.fileno())
        proof=run(plan,lambda:source_guard(os.environ));raw=(json.dumps(proof,indent=2)+'\n').encode()
        fd=os.open(directory/'proof.safe.json',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(fd,'wb') as stream:stream.write(raw);stream.flush();os.fsync(stream.fileno())
        print(json.dumps(dict(result=proof['result'],cleanup=proof['cleanup'],ownHandles=proof['ownHandles'])))
        return 0 if proof['result']=='PASS' else 2
    except BaseException:print('PG_CHANNEL_STOP_NO_RAW_OUTPUT');return 2

if __name__=='__main__':raise SystemExit(main())
