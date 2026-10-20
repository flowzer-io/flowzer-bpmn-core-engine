"""Gebundene synthetische Linuxqualifikation der vorhandenen Runtimeports.

Default inert. Kein privater Host/Keycloak, kein neuer Imagebau. Derselbe bestehende
CI-Opt-in wählt exakt alten PGmodus oder diesen neuen kombinierten Modus. Reale
Dockeraktionen benötigen darüber hinaus eine getrennte konkrete Root-Lease.
"""
import importlib.util,sys,json,os,time,hashlib,re,uuid,dataclasses
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
S=ROOT/'deploy/tickytask-demo-coolify/supervised-operator'
spec=importlib.util.spec_from_file_location('old_pg_channel',ROOT/'tests/manual-postgres-channel/run_fixture.py')
old=importlib.util.module_from_spec(spec);sys.modules[spec.name]=old;spec.loader.exec_module(old)
spec=importlib.util.spec_from_file_location('manual_runtime_adapter',S/'manual_runtime_adapter.py')
runtime=importlib.util.module_from_spec(spec);sys.modules[spec.name]=runtime;spec.loader.exec_module(runtime)
op=runtime.op;pg=runtime.pg;require=old.require
API_SOURCE='a7fcde7cba2cf526e8908037b238ee6d68bdbf85'
API_IMAGE='ghcr.io/flowzer-io/flowzer-api@sha256:0d5eb69c3592c72b7fd2fabbfa11368decadadf905610d2f07132e3fcf89a9f6'
API_ID='sha256:cd566e56e47e3b3125f8ae4734f7397c5672c804678dffd98ef8535106e1a559'
CIDR='172.31.254.0/28'
MAX_CONTAINERS=8;MAX_VOLUMES=3
FILES=tuple(dict.fromkeys((*old.FILES,'tests/manual-runtime-channel/run_fixture.py','tests/manual-runtime-channel/test_fixture.py',
    'tests/manual-runtime-channel/README.md','tests/manual-postgres-channel/test_ci_routing.py',
    *('deploy/tickytask-demo-coolify/supervised-operator/'+n+'.py' for n in ['manual_runtime_adapter','manual_operator','manual_readiness']))))


def validate_opening(env,now):
    """Genau zwei getrennte Modi; gemeinsamer alter sechs-Felder-Vertrag unverändert."""
    try:
        def unique(pairs):
            result={}
            for k,v in pairs:
                if k in result:raise ValueError()
                result[k]=v
            return result
        raw=env['PG_CHANNEL_OPENING'];require(type(raw) is str and len(raw.encode())<=4096,'OPENING')
        opening=json.loads(raw,object_pairs_hook=unique);mode=opening['mode']
        require(mode in ('pg-channel-v1','runtime-channel-v1'),'OPENING')
        # Nur den bereits separat geprüften Modus für die Wiederverwendung des
        # alten gemeinsamen Validators normalisieren; unbekannte Werte nie mappen.
        copied=dict(env,PG_CHANNEL_OPENING=json.dumps(dict(opening,mode='pg-channel-v1')))
        plan=old.validate_opening(copied,now);return dict(plan,mode=mode)
    except BaseException:raise old.FixtureFailure('OPENING') from None


def source_guard(env):
    """HEAD/Tree/Workflow und alle benutzten Sourcebytes vor jedem Dockerport binden."""
    p=validate_opening(env,time.time())
    require(old.platform.system()=='Linux' and old.platform.machine()=='x86_64','OPENING')
    require(old.git('rev-parse','HEAD').decode().strip()==p['sha'] and old.git('rev-parse','HEAD^{tree}').decode().strip()==p['tree'],'SOURCE')
    require(hashlib.sha256((ROOT/old.WF).read_bytes()).hexdigest()==p['workflow'],'SOURCE')
    for name in FILES:require(old.git('show','HEAD:'+name)==(ROOT/name).read_bytes(),'SOURCE')
    return p


def public_settings():
    """Absichtlich nur eigener Loopback, ohne externe IdP-Anfrage oder TLSlockerung.

    Der Directoryabgleich darf hier klassifiziert fehlschlagen. Das ist niemals
    ein Keycloak-, JWT-, Login- oder Verzeichnisabgleichsnachweis.
    """
    return runtime.PublicSettings('https://127.0.0.1/realms/TickyTask','flowzer-api-demo','flowzer-bff-demo',
        'flowzer-directory-demo','12345678-1234-1234-1234-123456789abc',CIDR)


class CombinedPipe(old.LocalDockerPipe):
    """Nur synthetischer Hostedjob: feste SSHhülle auf eigenen lokalen Docker abbilden."""
    def __init__(self,guard,plan):
        super().__init__(guard,plan);self.end=time.monotonic()+420
        self.namespace='flowzer-runtime-'+plan['nonce'];self.names=[self.namespace]
        self.restart_nonce=hashlib.sha256((plan['nonce']+'restart').encode()).hexdigest()[:32]
        self.network=None;self.image_labels=None;self.cleanup_done=None
    def command_id(self,args):
        if args[:2]==['container','run']:return 'APP_START' if '--entrypoint' not in args else super().command_id(args)
        if args[0]=='network':
            require(args[1] in ('ls','inspect','create','rm'));return 'NETWORK_'+args[1].upper()
        if args[:2]==['container','cp']:return 'PG_VERSION'
        if args[:2]==['container','start']:return 'PG_RESTART'
        if args[:2]==['container','exec'] and 'wget' in args:return 'API_HTTP'
        return super().command_id(args)
    def checked(self,args,**kw):
        result=self.plain(args,**kw);require(result.exit_code==0 and not result.stderr,'DOCKER');return result.stdout
    def validate_owned(self,v):
        """Feste vollständige Labels/Image/Namen/CID vor jedem Fixturestop/-remove."""
        require(type(v) is dict and set(v)=={'id','name','image','labels','running','exit'},'CLEANUP')
        require(type(v['id']) is str and re.fullmatch('[a-f0-9]{64}',v['id']) and type(v['running']) is bool and type(v['exit']) is int,'CLEANUP')
        prefix='/'+self.namespace+'_';require(type(v['name']) is str and v['name'].startswith(prefix),'CLEANUP')
        tail=v['name'][len(prefix):];role,nonce=tail.rsplit('_',1)
        require(role in ('ownership-pg','ownership-key','initdb','postgres','migrate','api') and nonce in (self.plan['nonce'],self.restart_nonce),'CLEANUP')
        require(role in ('migrate','api') or nonce==self.plan['nonce'],'CLEANUP')
        require(v['image']==(API_ID if role in ('migrate','api') else old.IMAGE_ID),'CLEANUP')
        base={}
        if role in ('migrate','api'):
            require(type(self.image_labels) is dict,'CLEANUP');base=self.image_labels
        expected=dict(base);expected.update(self.labels(self.namespace));expected['flowzer.tt.demo.nonce']=nonce
        require(v['labels']==expected,'CLEANUP')
        return v
    def owned(self,*,cleanup=False,until=None):
        def cmd(args):return self.checked(args,cleanup=cleanup,timeout=min(5,max(.01,until-time.monotonic())) if until else 5)
        ids=cmd(['ps','-a','--no-trunc','--filter','name=^/'+self.namespace+'_','--format','{{.ID}}']).decode('ascii').splitlines()
        require(len(ids)<=MAX_CONTAINERS and len(ids)==len(set(ids)),'CLEANUP');rows=[]
        fmt='{"id":{{json .Id}},"name":{{json .Name}},"image":{{json .Image}},"labels":{{json .Config.Labels}},"running":{{json .State.Running}},"exit":{{json .State.ExitCode}}}'
        for cid in ids:
            require(re.fullmatch('[a-f0-9]{64}',cid),'CLEANUP');v=self.validate_owned(json.loads(cmd(['container','inspect',cid,'--format',fmt])))
            require(v['id']==cid,'CLEANUP');rows.append(v)
        return rows
    def remove_helpers(self):
        """Separte explizite Fixtureaktion, keine versteckte Runtimeports-Reparatur."""
        rows=self.owned();helpers=[v for v in rows if not any('_'+r+'_' in v['name'] for r in ('api','postgres'))]
        require(all(not v['running'] and v['exit']==0 for v in helpers),'CLEANUP')
        for v in helpers:self.checked(['container','rm',v['id']])
    def stop_registered(self,cids):
        """Nur erfolgreich neu eigene registrierte API/PG für geplanten Restart stoppen."""
        rows={v['id']:v for v in self.owned()};require(set(cids)<=set(rows),'CLEANUP')
        require(all(any('_'+r+'_' in rows[c]['name'] for r in ('api','postgres')) and rows[c]['running'] for c in cids),'CLEANUP')
        for cid in cids:self.checked(['container','stop','--time=3',cid],timeout=6)
        rows={v['id']:v for v in self.owned()};require(all(not rows[c]['running'] for c in cids),'CLEANUP')
    def prepare(self):
        """Nur veröffentlichte immutable Images und ein frisches eigenes nichtinternes Netz."""
        for image,pin,revision in [(old.IMAGE,old.IMAGE_ID,None),(API_IMAGE,API_ID,API_SOURCE)]:
            self.checked(['image','pull','--quiet','--platform=linux/amd64',image],timeout=90)
            fmt='{"id":{{json .ID}},"os":{{json .Os}},"arch":{{json .Architecture}},"revision":{{json (index .Config.Labels "org.opencontainers.image.revision")}},"labels":{{json .Config.Labels}}}'
            v=json.loads(self.checked(['image','inspect',image,'--format',fmt]));require(v['id']==pin and v['os']=='linux' and v['arch']=='amd64')
            if revision:
                require(v['revision']==revision and type(v['labels']) is dict and 1<=len(v['labels'])<=32)
                require(all(type(k) is str and 0<len(k)<=256 and type(value) is str and len(value)<=4096 for k,value in v['labels'].items()))
                require(v['labels'].get('org.opencontainers.image.revision')==revision
                        and not any(k in self.labels(self.namespace) or k.startswith('flowzer.tt.demo.') for k in v['labels']))
                self.image_labels=dict(v['labels'])
        require(not self.checked(['network','ls','--filter','name=^'+self.namespace+'$','--format','{{.ID}}']).strip())
        # Kein Fallback auf ein anderes Netz/Subnetz: Docker weist Überschneidungen ab.
        args=['network','create','--driver','bridge','--subnet',CIDR]
        for k,v in self.labels(self.namespace).items():args+=['--label',k+'='+v]
        raw=self.checked([*args,self.namespace]);cid=raw.decode('ascii').strip();require(re.fullmatch('[a-f0-9]{64}',cid));self.network=cid
    def cleanup(self):
        """Einmal maximal45s, nur aktuelle vollständige eigene Labels; niemals prune/retry."""
        if self.cleanup_done is not None:return self.cleanup_done
        self.cleanup_done='UNKNOWN'
        if self.local_pid:return self.cleanup_done  # unbekannter eigener Writer: keine konkurrierenden Cleanupwrites
        until=time.monotonic()+45
        def cmd(args):
            require(time.monotonic()<until,'CLEANUP')
            return self.checked(args,cleanup=True,timeout=min(6,max(.01,until-time.monotonic())))
        try:
            rows=self.owned(cleanup=True,until=until)
            for v in rows:
                if v['running']:cmd(['container','stop','--time=3',v['id']])
                cmd(['container','rm',v['id']])
            names=cmd(['volume','ls','--filter','name=^'+self.namespace+'_','--format','{{.Name}}']).decode('ascii').splitlines()
            allowed={self.namespace+s for s in ['_postgres-data','_socket','_keyring']}
            require(len(names)<=MAX_VOLUMES and len(names)==len(set(names)) and set(names)<=allowed,'CLEANUP')
            fmt='{"name":{{json .Name}},"driver":{{json .Driver}},"labels":{{json .Labels}}}'
            for name in names:
                require(json.loads(cmd(['volume','inspect',name,'--format',fmt]))==dict(name=name,driver='local',labels=self.labels(self.namespace)),'CLEANUP')
                cmd(['volume','rm',name])
            nets=cmd(['network','ls','--no-trunc','--filter','name=^'+self.namespace+'$','--format','{{.ID}}']).decode('ascii').splitlines()
            require(len(nets)<=1 and (not nets or self.network is not None and nets==[self.network]),'CLEANUP')
            if nets:
                fmt='{"id":{{json .Id}},"name":{{json .Name}},"labels":{{json .Labels}},"peers":{{len .Containers}}}'
                require(json.loads(cmd(['network','inspect',self.network,'--format',fmt]))==dict(id=self.network,name=self.namespace,labels=self.labels(self.namespace),peers=0),'CLEANUP')
                cmd(['network','rm',self.network])
            require(not self.owned(cleanup=True,until=until),'CLEANUP')
            for kind,args in [('volume',['volume','ls','--filter','name=^'+self.namespace+'_','--format','{{.Name}}']),('network',['network','ls','--filter','name=^'+self.namespace+'$','--format','{{.ID}}'])]:require(not cmd(args).strip(),'CLEANUP')
            self.cleanup_done='ABSENT'
        except BaseException:pass
        return self.cleanup_done


def bindings(port):
    """Feste native UID-/Arch-/Sourcebindungen, keine stillen Konfigurationsdefaults."""
    b=pg.PgBinding(port.namespace,str(uuid.UUID(port.plan['nonce'])),port.plan['nonce'],old.IMAGE_ID,999,999,1654,1654,
        port.plan['sha'],port.plan['run'],architecture='amd64',prepare_keyring=True)
    r=runtime.RuntimeBinding(API_ID,API_SOURCE,port.network or '0'*64,1654,1654,public_settings(),tuple(port.labels(port.namespace).items()),port.plan['nonce'])
    return b,r


def prove_active_api_block(binding,config,target,guard,port,pg_cid,api_cid):
    """Negativbeleg kausal am unveränderten Own-Inspect der laufenden alten API.

    Diese Subklasse beobachtet ausschließlich den erwarteten Inspectaufruf;
    Argumente, Rückgabe und Ausnahme des bestehenden Inspect bleiben unverändert.
    Ein beliebiger früherer Netzwerk-/Image-/Ownerfehler zählt damit niemals.
    """
    class Probe(runtime.RuntimePorts):
        rejected_old_running_api=False
        def _inspect_app(self,cid_or_name,role,running,nonce=None,*,timeout=60):
            try:return super()._inspect_app(cid_or_name,role,running,nonce,timeout=timeout)
            except op.PortFailure:
                if (cid_or_name,role,running,nonce)==(api_cid,'api',False,port.plan['nonce']):self.rejected_old_running_api=True
                raise
    probe=Probe(binding,config,target,guard,mode='restart',pipe=port,prior_pg_cid=pg_cid,prior_api_cid=api_cid)
    delivered=[]
    try:
        probe._image()  # benötigte unveränderte Image-ENVbindung vor eigenem Containerreadback
        probe._inspect_app(api_cid,'api',True,port.plan['nonce'])
        try:op.ManualOperator().run('restart',probe,lambda:delivered.append(True))
        except op.OperatorFailure as e:require(e.phase=='PREFLIGHT' and not delivered and not e.own_local_pid and probe.rejected_old_running_api,'EXPECTED_NEGATIVE')
        else:raise old.FixtureFailure('EXPECTED_NEGATIVE')
        # Vollständiger gleicher Config-/Identityreadback mit der einzigen
        # zulässigen Abweichung Running=True, auch nach dem erwarteten Abort.
        probe._inspect_app(api_cid,'api',True,port.plan['nonce'])
    finally:probe.close()


def run(plan,guard):
    """Echte Initial-/Restartfolge, aber nur nach separater Hosted-Dispatchfreigabe."""
    port=CombinedPipe(guard,plan);ports=[];values={};proof=dict(kind='SYNTHETIC_COMBINED_RUNTIME_CHANNEL',sourceSha=plan['sha'],sourceTree=plan['tree'],
        nonce=plan['nonce'],apiSourceSha=API_SOURCE,apiImage=API_IMAGE,apiImageId=API_ID,pgImage=old.IMAGE,pgImageId=old.IMAGE_ID,
        architecture='linux/amd64',initial=False,activeApiBlocked=False,restart=False,expectedHead=22,actualHead=None,feel=False,anonymous401=False,result='FAIL',cleanup='UNKNOWN',ownHandles=0)
    try:
        port.prepare();b,r=bindings(port);target=pg.SshTarget('192.0.2.8','synthetic.invalid','/synthetic/known_hosts')
        values={k:'synthetic-'+k+'-'+uuid.uuid4().hex+";'\\" for k in ['admin','migration','runtime','bff','directory']}
        initial=runtime.RuntimePorts(b,r,target,guard,mode='initial',pipe=port);ports.append(initial)
        first=op.ManualOperator().run('initial',initial,lambda:initial.accept_bundle(op.SecretBundle(**values)))
        proof.update(initial=True,actualHead=22,feel=True,anonymous401=True)
        newr=dataclasses.replace(r,attempt_nonce=port.restart_nonce,prior_api_nonce=plan['nonce'])
        # Das Restartobserve muss die aktive Alt-API selbst erkennen. Davor sind
        # ausschließlich erfolgreich beendete neue eigene Helpers separat entfernt.
        port.remove_helpers()
        prove_active_api_block(b,newr,target,guard,port,initial.pg.pg_cid,first.api_cid)
        proof['activeApiBlocked']=True
        port.stop_registered([first.api_cid,initial.pg.pg_cid])
        restarted=runtime.RuntimePorts(b,newr,target,guard,mode='restart',pipe=port,prior_pg_cid=initial.pg.pg_cid,prior_api_cid=first.api_cid);ports.append(restarted)
        op.ManualOperator().run('restart',restarted,lambda:restarted.accept_bundle(op.SecretBundle(**dict(values,admin=None))))
        proof.update(restart=True,result='PASS')
    except BaseException as e:
        proof['failure']=e.code if isinstance(e,old.FixtureFailure) else e.phase if isinstance(e,op.OperatorFailure) else 'TECHNICAL'
        proof['primary']=dict(command=port.command,exit=port.command_exit,stderrEmpty=port.stderr_empty)
        pid=getattr(e,'own_pid',None) or getattr(e,'own_local_pid',None) or port.local_pid
        if type(pid) is int and pid>0:port.local_pid=pid
    finally:
        for p in ports:p.close()
        values.clear();proof['cleanup']=port.cleanup()
        if port.local_pid:proof.update(result='FAIL',ownHandles='UNKNOWN',ownLocalPid=port.local_pid)
        if proof['cleanup']!='ABSENT':proof.update(result='FAIL',ownHandles='UNKNOWN')
    return proof


def main():
    if sys.argv[1:] not in (['--preflight-only'],['--once']):print('RUNTIME_CHANNEL_SOURCE_ONLY');return 0
    try:
        plan=source_guard(os.environ)
        if plan['mode']=='pg-channel-v1':return old.main()
        if sys.argv[1:]==['--preflight-only']:print('RUNTIME_CHANNEL_PREFLIGHT_OK');return 0
        parent=Path(os.environ['RUNNER_TEMP']);fd=os.open(parent,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            st=os.fstat(fd);require(st.st_uid==os.getuid() and not st.st_mode&0o022,'ONCE');os.mkdir('flowzer-pg-channel-proof',0o700,dir_fd=fd)
        finally:os.close(fd)
        directory=parent/'flowzer-pg-channel-proof';claim=os.open(directory/'once.consumed',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(claim,'w') as stream:stream.write(plan['nonce']);stream.flush();os.fsync(stream.fileno())
        proof=run(plan,lambda:source_guard(os.environ));raw=(json.dumps(proof,indent=2)+'\n').encode()
        fd=os.open(directory/'proof.safe.json',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
        with os.fdopen(fd,'wb') as stream:stream.write(raw);stream.flush();os.fsync(stream.fileno())
        print(json.dumps(dict(result=proof['result'],cleanup=proof['cleanup'],ownHandles=proof['ownHandles'])))
        return 0 if proof['result']=='PASS' else 2
    except BaseException:print('RUNTIME_CHANNEL_STOP_NO_RAW_OUTPUT');return 2

if __name__=='__main__':raise SystemExit(main())
