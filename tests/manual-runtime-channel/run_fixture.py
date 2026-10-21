"""Gebundene synthetische Linuxqualifikation der vorhandenen Runtimeports.

Default inert. Kein privater Host/Keycloak, kein neuer Imagebau. Derselbe bestehende
CI-Opt-in wählt exakt alten PGmodus oder diesen neuen kombinierten Modus. Reale
Dockeraktionen benötigen darüber hinaus eine getrennte konkrete Root-Lease.
"""
import importlib.util,sys,json,os,time,hashlib,re,uuid,dataclasses,tarfile,io,stat
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
    'tests/manual-runtime-channel/README.md','tests/manual-runtime-channel/docker-cli27-package.json','tests/manual-postgres-channel/test_ci_routing.py',
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


def package_pin():
    """Öffentlicher Paketpin aus unabhängig geprüftem offiziellen GPG-Metadatenpfad."""
    return json.loads((ROOT/'tests/manual-runtime-channel/docker-cli27-package.json').read_text())


def client_from_package(raw,pin):
    """Nur authentifizierte Paketbytes lesen und genau einen regulären Client liefern."""
    require(type(raw) is bytes and len(raw)==pin['size'] and len(raw)<=16*1024*1024
            and hashlib.sha256(raw).hexdigest()==pin['sha256'],'SOURCE')
    require(raw.startswith(b'!<arch>\n'),'SOURCE');offset=8;members={}
    while offset<len(raw):
        header=raw[offset:offset+60];require(len(header)==60 and header[-2:]==b'`\n','SOURCE')
        name=header[:16].decode('ascii').strip().removesuffix('/');size=int(header[48:58]);offset+=60
        require(name in ('debian-binary','control.tar.xz','control.tar.gz','data.tar.xz','_gpgbuilder') and name not in members and 0<=size<=16*1024*1024 and offset+size<=len(raw),'SOURCE')
        if name=='_gpgbuilder':
            # Offizielles gepinntes DEB enthält diesen vierten Signaturmember.
            # Nur begrenzt/zuletzt ignorieren, nie als neuen Trustanker benutzen.
            require(len(members)==3 and members.get('debian-binary')==b'2.0\n' and 'data.tar.xz' in members
                    and 0<size<=2048 and offset+size+size%2==len(raw),'SOURCE')
        members[name]=raw[offset:offset+size];offset+=size
        if size%2:require(raw[offset:offset+1]==b'\n','SOURCE');offset+=1
    require(offset==len(raw) and len(members) in (3,4) and members.get('debian-binary')==b'2.0\n' and 'data.tar.xz' in members,'SOURCE')
    found=None;expanded=0
    with tarfile.open(fileobj=io.BytesIO(members['data.tar.xz']),mode='r|xz') as archive:
        for index,item in enumerate(archive):
            expanded+=item.size;require(index<512 and 0<=expanded<=96*1024*1024,'SOURCE')
            name=item.name.removeprefix('./');require(not name.startswith('/') and '..' not in name.split('/'),'SOURCE')
            if name=='usr/bin/docker':
                require(found is None and item.isfile() and not item.issym() and not item.islnk() and 64<=item.size<=64*1024*1024,'SOURCE')
                found=archive.extractfile(item).read(64*1024*1024+1);require(len(found)==item.size,'SOURCE')
    require(found is not None and found[:7]==b'\x7fELF\x02\x01\x01' and found[18:20]==b'\x3e\0'
            and len(found)==pin['binarySize'] and hashlib.sha256(found).hexdigest()==pin['binarySha256'],'SOURCE')
    return found


class ClientBinding:
    """Geprüfte Pfadidentität und Source-Digest, kein globaler Dockerfallback.

    Der unabhängig aus dem authentifizierten Paket abgeleitete Binärdigest steht
    fest im Sourcepin; die mutable Bindingdatei ist dafür kein Vertrauensanker.
    Der offene FD verhindert Path-Swap, nicht nachträgliche In-place-Schreibrechte
    eines beliebigen gleich-UID-Prozesses. Der eigene Hostedrunner samt Source
    bleibt vertrauenswürdig; dies ist keine Sandbox gegen dessen Benutzer.
    """
    def __init__(self,directory,facts,expected_sha=None):
        self.directory=directory;self.facts=facts;self.expected_sha=expected_sha
    def open_verified(self):
        try:return self._open_verified()
        except OSError:raise old.FixtureFailure('SOURCE') from None
    def _open_verified(self):
        parent=os.open(self.directory,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            st=os.fstat(parent);require(st.st_uid==os.getuid() and stat.S_IMODE(st.st_mode)==0o700,'SOURCE')
            fd=os.open('docker',os.O_RDONLY|os.O_NOFOLLOW,dir_fd=parent)
        finally:os.close(parent)
        try:
            before=os.fstat(fd);f=self.facts
            require(stat.S_ISREG(before.st_mode) and before.st_nlink==1 and before.st_uid==os.getuid()
                    and stat.S_IMODE(before.st_mode)==0o500 and (before.st_dev,before.st_ino,before.st_size)==(f['dev'],f['ino'],f['size']) and 64<=before.st_size<=64*1024*1024,'SOURCE')
            digest=hashlib.sha256();header=os.read(fd,64);digest.update(header)
            while True:
                part=os.read(fd,1024*1024)
                if not part:break
                digest.update(part)
            after=os.fstat(fd)
            stable=lambda st:(st.st_dev,st.st_ino,st.st_nlink,st.st_uid,st.st_mode,st.st_size,st.st_mtime_ns,st.st_ctime_ns)
            require(stable(before)==stable(after) and digest.hexdigest()==f['binarySha256']==self.expected_sha and header[:7]==b'\x7fELF\x02\x01\x01' and header[18:20]==b'\x3e\0','SOURCE')
            os.lseek(fd,0,os.SEEK_SET);return fd
        except BaseException:os.close(fd);raise


def store_client(parent,raw,pin,on_store=None):
    """Exklusiv nur Client und öffentliche Bindung speichern, keine Installation."""
    binary=client_from_package(raw,pin)
    if on_store is not None:on_store()
    root=os.open(parent,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
    try:
        st=os.fstat(root);require(st.st_uid==os.getuid() and not stat.S_IMODE(st.st_mode)&0o022,'SOURCE')
        os.mkdir('flowzer-cli27-verified',0o700,dir_fd=root);directory=os.open('flowzer-cli27-verified',os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW,dir_fd=root)
    finally:os.close(root)
    try:
        fd=os.open('docker',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o500,dir_fd=directory)
        try:
            with os.fdopen(fd,'wb',closefd=False) as stream:stream.write(binary);stream.flush();os.fsync(fd)
            st=os.fstat(fd);facts=dict(version='27.5.1',packageSha256=pin['sha256'],binarySha256=hashlib.sha256(binary).hexdigest(),size=st.st_size,dev=st.st_dev,ino=st.st_ino)
        finally:os.close(fd)
        fd=os.open('binding.safe.json',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o400,dir_fd=directory)
        with os.fdopen(fd,'w') as stream:json.dump(facts,stream);stream.flush();os.fsync(stream.fileno())
    finally:os.close(directory)
    return ClientBinding(parent/'flowzer-cli27-verified',facts,pin['binarySha256'])


def load_client(parent):
    """Keine Suche über PATH/Systemconfig; nur eigene attestierte Runnerdateien."""
    directory=parent/'flowzer-cli27-verified';d=os.open(directory,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
    try:
        st=os.fstat(d);require(st.st_uid==os.getuid() and stat.S_IMODE(st.st_mode)==0o700,'SOURCE')
        fd=os.open('binding.safe.json',os.O_RDONLY|os.O_NOFOLLOW,dir_fd=d)
        try:
            st=os.fstat(fd);require(st.st_uid==os.getuid() and st.st_nlink==1 and stat.S_ISREG(st.st_mode) and stat.S_IMODE(st.st_mode)==0o400 and st.st_size<=2048,'SOURCE')
            raw=os.read(fd,2049)
        finally:os.close(fd)
    finally:os.close(d)
    pin=package_pin();f=json.loads(raw);require(type(f) is dict and set(f)=={'version','packageSha256','binarySha256','size','dev','ino'} and f['version']=='27.5.1' and f['packageSha256']==pin['sha256']
        and type(f['binarySha256']) is str and re.fullmatch('[a-f0-9]{64}',f['binarySha256']) and f['binarySha256']==pin['binarySha256'] and f['size']==pin['binarySize'] and all(type(f[k]) is int and f[k]>=0 for k in ['size','dev','ino']),'SOURCE')
    binding=ClientBinding(directory,f,pin['binarySha256']);fd=binding.open_verified();os.close(fd);return binding


def prepare_diagnostic():
    """Feste sichere Projektion, auch vor Runtime-/Artifact-Verzeichnis vorhanden."""
    return dict(stage='SOURCE',exit=None,httpStatus=None,transportCode=None,ownPid=None)


def prepare_client(parent,guard,diagnostic=None):
    """Gebundener Download mit festen Teilmeilensteinen, nie Rohfehlerdiagnose.

    Die Diagnose ändert weder Transportgrenzen noch Authentifizierung. Ein
    unbekannter eigener Writer wird gemeldet, nicht mit Dateicleanup übergangen.
    """
    d=diagnostic if diagnostic is not None else prepare_diagnostic()
    guard();pin=package_pin();root=os.open(parent,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
    try:
        st=os.fstat(root);require(st.st_uid==os.getuid() and not stat.S_IMODE(st.st_mode)&0o022,'SOURCE')
        fd=os.open('flowzer-cli27-package.download',os.O_RDWR|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600,dir_fd=root)
        try:
            target='/proc/'+str(os.getpid())+'/fd/'+str(fd);d['stage']='DOWNLOAD'
            try:r=old.transport.BoundedPipe().exchange(['/usr/bin/curl','--disable','--proto','=https','--max-time','60','--max-filesize',str(16*1024*1024),'--fail','--silent','--show-error','--write-out','%{http_code}','--output',target,pin['url']],b'',[],timeout=65)
            except old.transport.TransportFailure as error:
                d['transportCode']=error.code
                if type(error.own_pid) is int and 0<error.own_pid<2**31:d['ownPid']=error.own_pid
                raise
            d['stage']='HTTP'
            if type(r.exit_code) is int and -255<=r.exit_code<=255:d['exit']=r.exit_code
            if type(r.stdout) is bytes and re.fullmatch(rb'[1-5][0-9]{2}',r.stdout):d['httpStatus']=int(r.stdout)
            require(d['exit']==0 and r.stdout==b'200' and not r.stderr,'SOURCE');guard()
            d['stage']='SIZE';require(os.fstat(fd).st_size==pin['size'],'SOURCE');os.lseek(fd,0,os.SEEK_SET)
            with os.fdopen(os.dup(fd),'rb') as stream:raw=stream.read(16*1024*1024+1)
            require(len(raw)==pin['size'] and len(raw)<=16*1024*1024,'SOURCE')
            d['stage']='HASH';require(hashlib.sha256(raw).hexdigest()==pin['sha256'],'SOURCE')
            d['stage']='ARCHIVE'
            client=store_client(parent,raw,pin,on_store=lambda:d.update(stage='STORE'))
            d['stage']='COMPLETE';return client
        finally:
            os.close(fd)
            if d['transportCode']!='HANDLE_UNKNOWN' and d['ownPid'] is None:os.unlink('flowzer-cli27-package.download',dir_fd=root)
    finally:os.close(root)


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
        self.network=None;self.image_labels=None;self.cleanup_done=None;self.cli_version=None;self.client=None;self.transport_code=None;self.transport_command=None
        owner=self
        class ObservedPipe(old.transport.BoundedPipe):
            # Nur feste Diagnose beobachten; Originalargumente/Return/Exception
            # sowie gesamter Transport-/Cleanupalgorithmus bleiben unverändert.
            def exchange(self,argv,payload,secrets,**kwargs):
                fd=None
                try:
                    require(argv[:3]==['/usr/bin/docker','--host','unix:///var/run/docker.sock'] and isinstance(owner.client,ClientBinding),'SOURCE')
                    fd=owner.client.open_verified()
                    # Der geprüfte FD bleibt bis zum terminalen Pipeabschluss offen.
                    # close_fds im unveränderten Transport braucht kein pass_fds:
                    # /proc/des lebenden eigenen Parents bindet denselben Inode.
                    args=list(argv);args[0]='/proc/'+str(os.getpid())+'/fd/'+str(fd)
                    return super().exchange(args,payload,secrets,**kwargs)
                except old.transport.TransportFailure as error:
                    if owner.transport_code is None:owner.transport_code=error.code;owner.transport_command=owner.command
                    raise
                finally:
                    if fd is not None:os.close(fd)
        self.pipe=ObservedPipe()
    def command_id(self,args):
        if args==['--version'] or args[:1]==['version']:return 'CLI_VERSION'
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
        self.client=load_client(Path(os.environ['RUNNER_TEMP']))
        version=self.checked(['--version'])
        match=re.fullmatch(rb'Docker version ([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})(?:[-+][A-Za-z0-9.-]{1,40})?, build [A-Za-z0-9.-]{1,80}\r?\n?',version)
        require(match is not None and len(version)<=256 and match[1]==b'27.5.1');self.cli_version=match[1].decode('ascii')
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
        architecture='linux/amd64',dockerCliVersion=None,dockerCliPackageSha256=None,dockerCliBinarySha256=None,migrationVerified=False,transportFailure=None,initial=False,activeApiBlocked=False,restart=False,expectedHead=22,actualHead=None,feel=False,anonymous401=False,result='FAIL',cleanup='UNKNOWN',ownHandles=0)
    try:
        port.prepare();proof['dockerCliVersion']=port.cli_version
        if isinstance(port.client,ClientBinding):proof.update(dockerCliPackageSha256=port.client.facts['packageSha256'],dockerCliBinarySha256=port.client.facts['binarySha256'])
        b,r=bindings(port);target=pg.SshTarget('192.0.2.8','synthetic.invalid','/synthetic/known_hosts')
        values={k:'synthetic-'+k+'-'+uuid.uuid4().hex+";'\\" for k in ['admin','migration','runtime','bff','directory']}
        class Milestones(runtime.RuntimePorts):
            # Kein eigener Migrationspfad: Originalbeleg nur beobachten und
            # erst nach vollständiger typ-/wertsicherer Validierung projizieren.
            def migrate(self,packet):
                value=super().migrate(packet)
                if isinstance(value,op.MigrationProof) and all(type(v) is int for v in (value.exit_code,value.head,value.pending)) and (value.exit_code,value.head,value.pending)==(0,22,0) and value.terminal is True and value.output_eof is True:
                    proof.update(migrationVerified=True,actualHead=22)
                return value
        initial=Milestones(b,r,target,guard,mode='initial',pipe=port);ports.append(initial)
        first=op.ManualOperator().run('initial',initial,lambda:initial.accept_bundle(op.SecretBundle(**values)))
        proof.update(initial=True,actualHead=22,feel=True,anonymous401=True)
        newr=dataclasses.replace(r,attempt_nonce=port.restart_nonce,prior_api_nonce=plan['nonce'])
        # Das Restartobserve muss die aktive Alt-API selbst erkennen. Davor sind
        # ausschließlich erfolgreich beendete neue eigene Helpers separat entfernt.
        port.remove_helpers()
        prove_active_api_block(b,newr,target,guard,port,initial.pg.pg_cid,first.api_cid)
        proof['activeApiBlocked']=True
        port.stop_registered([first.api_cid,initial.pg.pg_cid])
        restarted=Milestones(b,newr,target,guard,mode='restart',pipe=port,prior_pg_cid=initial.pg.pg_cid,prior_api_cid=first.api_cid);ports.append(restarted)
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
        if port.transport_code:proof['transportFailure']=dict(code=port.transport_code,command=port.transport_command)
        if port.local_pid:proof.update(result='FAIL',ownHandles='UNKNOWN',ownLocalPid=port.local_pid)
        if proof['cleanup']!='ABSENT':proof.update(result='FAIL',ownHandles='UNKNOWN')
    return proof


def main():
    if sys.argv[1:] not in (['--preflight-only'],['--once'],['--prepare-client-only']):print('RUNTIME_CHANNEL_SOURCE_ONLY');return 0
    diagnostic=prepare_diagnostic() if sys.argv[1:]==['--prepare-client-only'] else None
    try:
        plan=source_guard(os.environ)
        if sys.argv[1:]==['--prepare-client-only']:
            if plan['mode']=='pg-channel-v1':print('PG_MODE_NO_CLIENT_DOWNLOAD');return 0
            prepare_client(Path(os.environ['RUNNER_TEMP']),lambda:source_guard(os.environ),diagnostic)
            print('VERIFIED_CLI27_CLIENT_PREPARED');return 0
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
    except BaseException:
        if diagnostic is not None:print('CLI27_PREPARE_SAFE '+json.dumps(diagnostic,separators=(',',':')))
        print('RUNTIME_CHANNEL_STOP_NO_RAW_OUTPUT');return 2

if __name__=='__main__':raise SystemExit(main())
