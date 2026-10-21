"""Konkreter initialer PG-Adapter über begrenzte SSH-/Docker-STDIN-Kanäle.

Import/CLI inert. Der Aufrufer muss zuvor einen separaten Root-Runtimeslot mit
Hostkey/Images/Owner/Frist binden und diesen per guard vor/nach jedem Port prüfen.
Keine Secretauflösung und kein Installer-/Restart-/Adoptions-/Reparaturpfad.
"""
from dataclasses import dataclass
import ipaddress
import json
import re
import shlex
import time
import uuid
import importlib.util
import sys
from pathlib import Path
# -I entfernt das Scriptverzeichnis aus sys.path. Nur diese beiden eigenen
# Source-Dateien explizit laden, keine fremde Modul-/Providerinstallation.
for _name in ('manual_postgres','manual_transport'):
    if _name not in sys.modules:
        _spec=importlib.util.spec_from_file_location(_name,Path(__file__).with_name(_name+'.py'))
        _module=importlib.util.module_from_spec(_spec);sys.modules[_name]=_module;_spec.loader.exec_module(_module)
from manual_postgres import initial_psql_payload,psql_initial_arguments
from manual_transport import BoundedPipe,TransportFailure


class PgAdapterFailure(Exception):
    """Fester Stopp, gegebenenfalls eigener ungeklärter lokaler Transporthandle."""
    def __init__(self,own_pid=None):
        self.own_pid=own_pid if type(own_pid) is int and own_pid>0 else None
        super().__init__('FLOWZER_INITIAL_PG_ADAPTER_UNPROVEN')


# Ein fehlender Configpfad allein reicht nicht: Docker kann Credentialhelper per
# PATH automatisch finden. Alle drei Pfade sind deshalb fest abwesend. Kein
# mkdir/Fallback und kein Root-HOME-/Configread. Nur ENOENT ist echte Abwesenheit.
REMOTE_LSTAT_GUARD="""import errno,os,sys
try:os.lstat('/nonexistent')
except FileNotFoundError as error:sys.exit(0 if error.errno==errno.ENOENT else 1)
except BaseException:sys.exit(1)
else:sys.exit(1)
"""


def _remote_docker_command(command):
    """Nur feste stumme Metadatenguards und explizit neutraler absoluter Client.

    Derselbe vertrauenswürdige Root kann die Metadaten nach der Prüfung ändern;
    dies ist keine Isolation gegen beliebigen Rootcode. Der äußere Versuchsguard
    muss weiterhin Host, Source und exklusiven eigenen Operationsscope binden.
    """
    parts=[['/usr/bin/python3','-I','-S','-B','-c',REMOTE_LSTAT_GUARD],
           ['/usr/bin/test','!','-e','/nonexistent'],['/usr/bin/test','!','-L','/nonexistent'],
           ['/usr/bin/env','-i','PATH=/nonexistent','HOME=/nonexistent','DOCKER_CONFIG=/nonexistent','LC_ALL=C',
            '/usr/bin/docker','--config','/nonexistent','--host','unix:///run/docker.sock',*command]]
    return ' && '.join(shlex.join(part) for part in parts)


def remote_docker_arguments(remote):
    """Nur Fixtureübersetzung der exakten SSHhülle; kein alternativer Executor.

    Bekannte Hosted-/Formatterfakes dürfen ausschließlich diese unveränderte
    Hülle entfernen. Fehlende/zusätzliche ENV-/Config-/Socketguards sind Fehler.
    """
    try:
        words=shlex.split(remote);prefix=shlex.split(_remote_docker_command([]))
        if words[:len(prefix)]!=prefix or len(words)<=len(prefix):raise PgAdapterFailure()
        return words[len(prefix):]
    except (ValueError,TypeError):raise PgAdapterFailure() from None


@dataclass(frozen=True)
class SshTarget:
    """Öffentliche Bindungsform, keine privaten Host-/Dateiwerte als Default.

    Fingerprint und unveränderte eigene known_hosts sind Pflicht des äußeren
    Rootguards; ein Hostname oder diese Dataclass ist keine Autorisierung.
    """
    ipv4: str
    alias: str
    known_hosts: str

    def validate(self):
        try:
            if str(ipaddress.IPv4Address(self.ipv4))!=self.ipv4 \
                    or not re.fullmatch('[a-z0-9.-]{1,253}',self.alias) \
                    or not re.fullmatch('/[a-zA-Z0-9_./-]+',self.known_hosts):raise ValueError()
        except Exception:raise PgAdapterFailure() from None

    def argv(self,command):
        self.validate()
        if type(command) is not list or not command or not all(type(v) is str and v and '\0' not in v for v in command):raise PgAdapterFailure()
        return ['/usr/bin/ssh','-F','/dev/null','-T','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes',
            '-o','UpdateHostKeys=no','-o','ConnectionAttempts=1','-o','ConnectTimeout=8','-o','HostKeyAlgorithms=ssh-ed25519',
            '-o','HostName='+self.ipv4,'-o','HostKeyAlias='+self.alias,'-o','UserKnownHostsFile='+self.known_hosts,
            '-o','GlobalKnownHostsFile=/dev/null','-o','ClearAllForwardings=yes','-o','PermitLocalCommand=no',
            '-o','ProxyCommand=none','-o','ProxyJump=none','-o','IdentitiesOnly=yes','-o','EscapeChar=none',
            'root@'+self.alias,_remote_docker_command(command)]


@dataclass(frozen=True)
class PgBinding:
    """Nur separat attestierte native Image-ID/UIDs, eigene Namen und neuer Versuch."""
    namespace: str
    owner: str
    nonce: str
    image: str
    pg_uid: int
    pg_gid: int
    key_uid: int
    key_gid: int
    source_sha: str
    run_id: str
    architecture: str='arm64'
    prepare_keyring: bool=True

    def validate(self):
        try:
            if str(uuid.UUID(self.owner))!=self.owner:raise ValueError()
        except Exception:raise PgAdapterFailure() from None
        if self.architecture not in ('amd64','arm64') or type(self.prepare_keyring) is not bool \
                or not re.fullmatch('[a-z0-9][a-z0-9-]{15,63}',self.namespace) or not re.fullmatch('[a-f0-9-]{36}',self.owner) \
                or not re.fullmatch('[a-f0-9]{32}',self.nonce) or not re.fullmatch('sha256:[a-f0-9]{64}',self.image) \
                or not re.fullmatch('[a-f0-9]{40}',self.source_sha) or not re.fullmatch('[a-zA-Z0-9._-]{1,80}',self.run_id) \
                or any(type(v) is not int or not 0<v<65536 for v in [self.pg_uid,self.pg_gid,self.key_uid,self.key_gid]):
            raise PgAdapterFailure()


def expected_password_prompts(value):
    """Nur genau zwei feste C-locale-Prompts, keine freien Warnungen/Fehler akzeptieren.

    Tatsächliche Docker-/psql17-Bytes müssen vor privaten Secrets synthetisch
    qualifiziert werden; diese Erkennung ist keine bereits bestandene Abnahme.
    """
    if type(value) is not bytes:return False
    v=value.replace(b'Password for user postgres: ',b'P').replace(b'Password: ',b'P').replace(b'\r\n',b'\n')
    return v in (b'PP',b'P\nP',b'PP\n',b'P\nP\n')


class PgBootstrapAdapter:
    """Ein initialer Versuch, niemals Runtime-Claim aus bloßem CLIexit/CID.

    Die drei Ports entsprechen initialisieren/PGstart/bootstrap des bestehenden
    Sequenzkerns. Rollen-/Migrations-/APIbeweise bleiben separate Folgeports.
    Das äußere Rootguard ist ein vertrauenswürdiger lokaler Callback, kein
    vom Browser steuerbarer Bypass oder neuer allgemeiner Execute-Endpunkt.
    """
    def __init__(self,binding,target,guard,pipe=None):
        binding.validate();target.validate()
        if not callable(guard):raise PgAdapterFailure()
        self.b=binding;self.target=target;self.guard=guard;self.pipe=pipe or BoundedPipe()
        self.state='NEW';self.pg_cid=None;self._sql=None;self._admin=None;self._secrets=[]
        self.labels={'com.docker.compose.project':binding.namespace,'flowzer.tt.demo.owner':binding.owner,'flowzer.tt.demo.nonce':binding.nonce,'flowzer.tt.demo.source':binding.source_sha,'flowzer.tt.demo.run':binding.run_id}
        self.data=binding.namespace+'_postgres-data';self.socket=binding.namespace+'_socket';self.keys=binding.namespace+'_keyring'

    def _call(self,args,payload=b'',secrets=(),prompts=False):
        self.guard()
        try:
            r=self.pipe.exchange(self.target.argv(args),payload,list(secrets),timeout=60)
            self.guard()
            if type(r.exit_code) is not int or type(r.stdout) is not bytes or type(r.stderr) is not bytes or r.exit_code!=0 or (not expected_password_prompts(r.stderr) if prompts else bool(r.stderr)):raise PgAdapterFailure()
            return r.stdout
        except TransportFailure as error:raise PgAdapterFailure(error.own_pid) from None
        except Exception:raise PgAdapterFailure() from None

    def _label_args(self):
        return [x for k,v in self.labels.items() for x in ['--label',k+'='+v]]

    def _mount(self,name,target):return ['--mount','type=volume,source='+name+',target='+target]

    def _run(self,role,user,mounts,entrypoint,extra=(),detach=False,cap_chown=False):
        args=['container','run','--pull=never','--name',self.b.namespace+'_'+role+'_'+self.b.nonce,
              '--network=none','--log-driver=none','--restart=no','--read-only','--cap-drop=ALL',
              '--security-opt=no-new-privileges=true','--ipc=private','--memory=384m','--cpus=0.75','--pids-limit=64',
              '--user',user,'--workdir','/','--tmpfs','/tmp:rw,nosuid,nodev,noexec,size=64m,mode=1777',*self._label_args()]
        if cap_chown:args+=['--cap-add=CHOWN']
        # Das offizielle PGimage deklariert VOLUME data. Jeder Run bindet es
        # ausdrücklich, sonst erzeugte Docker still einen ungebundenen Datenträger.
        args+=self._mount(self.data,'/var/lib/postgresql/data')
        for name,target in mounts:args+=self._mount(name,target)
        if detach:args+=['--detach']
        else:args+=['--interactive','--attach=stdin','--attach=stdout','--attach=stderr','--sig-proxy=false']
        return [*args,'--entrypoint',entrypoint,self.b.image,*extra]

    def _new_volume(self,name):
        raw=self._call(['volume','create',*self._label_args(),name])
        if raw.decode('ascii').strip()!=name:raise PgAdapterFailure()
        fmt='{"name":{{json .Name}},"driver":{{json .Driver}},"labels":{{json .Labels}}}'
        v=json.loads(self._call(['volume','inspect',name,'--format',fmt]))
        if v!={'name':name,'driver':'local','labels':self.labels}:raise PgAdapterFailure()

    def initialize(self,*,admin,migration,runtime,sql01,sql02):
        """Alle Pakete vor erstem Write; vorhandene Präfixressourcen sperren."""
        if self.state!='NEW':raise PgAdapterFailure()
        self.state='PREPARING'
        try:
            self._sql=initial_psql_payload(admin=admin,migration=migration,runtime=runtime,sql01=sql01,sql02=sql02)
            self._admin=bytearray((admin+'\n').encode('utf-8'))
            self._secrets=[v.encode('utf-8') for v in [admin,migration,runtime]]
            self._secrets += [v.replace("'","''").encode('utf-8') for v in [migration,runtime]]
            fmt='{"id":{{json .ID}},"os":{{json .Os}},"arch":{{json .Architecture}}}'
            image=json.loads(self._call(['image','inspect',self.b.image,'--format',fmt]))
            if image!={'id':self.b.image,'os':'linux','arch':self.b.architecture}:raise PgAdapterFailure()
            for args in [['volume','ls','--filter','name=^'+self.b.namespace+'_','--format','{{.Name}}'],
                         ['ps','-a','--filter','name=^/'+self.b.namespace+'_','--format','{{.ID}}']]:
                if self._call(args).strip():raise PgAdapterFailure()
            self.state='INITIALIZING'
            for name in [self.data,self.socket]+([self.keys] if self.b.prepare_keyring else []):self._new_volume(name)
            mounts=[(self.socket,'/socket')]+([(self.keys,'/keys')] if self.b.prepare_keyring else [])
            self._call(self._run('ownership-pg','0:0',mounts,'/bin/chown',[str(self.b.pg_uid)+':'+str(self.b.pg_gid),'/var/lib/postgresql/data','/socket'],cap_chown=True))
            if self.b.prepare_keyring:
                self._call(self._run('ownership-key','0:0',mounts,'/bin/chown',[str(self.b.key_uid)+':'+str(self.b.key_gid),'/keys'],cap_chown=True))
            self._call(self._run('initdb',str(self.b.pg_uid)+':'+str(self.b.pg_gid),[(self.socket,'/socket')],
                '/usr/lib/postgresql/17/bin/initdb',['-D','/var/lib/postgresql/data','--username=postgres','--encoding=UTF8','--no-locale',
                '--auth-local=scram-sha-256','--auth-host=reject','--pwfile=/dev/stdin']),self._admin,self._secrets)
            self._admin[:]=b'\0'*len(self._admin);self._admin=None;self.state='INITIALIZED';return 0
        except BaseException as error:self.state='UNKNOWN';self.close();raise PgAdapterFailure(getattr(error,'own_pid',None)) from None

    def pg_start(self):
        """Unixsocket/SCRAM, kein TCP/Hostport; CID vor jeder SQLaktion own-inspecten."""
        if self.state!='INITIALIZED':raise PgAdapterFailure()
        self.state='STARTING'
        try:
            raw=self._call(self._run('postgres',str(self.b.pg_uid)+':'+str(self.b.pg_gid),[(self.socket,'/socket')],
                '/usr/lib/postgresql/17/bin/postgres',['-D','/var/lib/postgresql/data','-c','listen_addresses=',
                '-c','unix_socket_directories=/socket','-c','unix_socket_permissions=0777','-c','password_encryption=scram-sha-256',
                '-c','log_statement=none','-c','log_min_error_statement=panic','-c','logging_collector=off'],detach=True))
            cid=raw.decode('ascii').strip()
            if not re.fullmatch('[a-f0-9]{64}',cid):raise PgAdapterFailure()
            self.pg_cid=cid;self._verify_pg();self._wait_pg();self.state='RUNNING';return 0
        except BaseException as error:self.state='UNKNOWN';self.close();raise PgAdapterFailure(getattr(error,'own_pid',None)) from None

    def _wait_pg(self):
        """Nur eigener pg_isready-Read, max20s; unbekannter Transport stoppt sofort."""
        until=time.monotonic()+20
        while True:
            left=until-time.monotonic()
            if left<=0:raise PgAdapterFailure()
            self.guard()
            args=['container','exec','--user',str(self.b.pg_uid)+':'+str(self.b.pg_gid),self.pg_cid,
                  '/usr/lib/postgresql/17/bin/pg_isready','--host=/socket','--username=postgres','--dbname=postgres','--timeout=1']
            try:r=self.pipe.exchange(self.target.argv(args),b'',[],timeout=min(3,left))
            except TransportFailure as error:raise PgAdapterFailure(error.own_pid) from None
            self.guard()
            if type(r.exit_code) is not int or type(r.stderr) is not bytes or r.stderr or r.exit_code not in (0,1,2):raise PgAdapterFailure()
            if r.exit_code==0:return
            time.sleep(min(.25,max(0,until-time.monotonic())))

    def _verify_pg(self,running=True):
        fmt='{"id":{{json .ID}},"image":{{json .Image}},"labels":{{json .Config.Labels}},"user":{{json .Config.User}},"running":{{json .State.Running}},"network":{{json .HostConfig.NetworkMode}},"log":{{json .HostConfig.LogConfig.Type}},"restart":{{json .HostConfig.RestartPolicy.Name}},"mounts":[{{range .Mounts}}{"type":{{json .Type}},"name":{{json .Name}},"destination":{{json .Destination}},"rw":{{json .RW}}},{{end}}null]}'
        v=json.loads(self._call(['container','inspect',self.pg_cid,'--format',fmt]))
        if type(v) is not dict or type(v.get('running')) is not bool or v!={'id':self.pg_cid,'image':self.b.image,'labels':self.labels,'user':str(self.b.pg_uid)+':'+str(self.b.pg_gid),
               'running':v['running'] if running is None else running,'network':'none','log':'none','restart':'no','mounts':v.get('mounts')} \
                or type(v['mounts']) is not list or v['mounts'][-1:]!=[None]:raise PgAdapterFailure()
        # Namen allein erlaubten vertauschte/readonly Mountziele trotz passender Labels.
        expected=[dict(type='volume',name=self.data,destination='/var/lib/postgresql/data',rw=True),
                  dict(type='volume',name=self.socket,destination='/socket',rw=True)]
        mounts=v['mounts'][:-1]
        if len(mounts)!=2 or any(type(row) is not dict or type(row.get('rw')) is not bool for row in mounts) \
                or sorted(mounts,key=lambda row:row.get('name',''))!=sorted(expected,key=lambda row:row['name']):raise PgAdapterFailure()

    def verify_existing_pg(self,*,running=None):
        """Nur exakten bereits gebundenen eigenen CID/Config/Mountbestand lesen, keine Adoption.

        Der äußere Rootguard belegt dessen vorherige Registrierung. Eine beliebige
        mitgegebene CID verleiht keine Rechte. Der Rückgabewert ist kein Datenbeleg.
        """
        if not isinstance(self.pg_cid,str) or not re.fullmatch('[a-f0-9]{64}',self.pg_cid):raise PgAdapterFailure()
        self._verify_pg(running=running)
        # Docker27 PidMode.Valid: nur leer/host/container; leer bedeutet privat.
        # Zusätzliche Readbackinvarianten sind vor einem erneuten Start nötig:
        # CID/Labels/Mountnamen allein sagen nichts über Privileged/Namespaces.
        # Raw-JSON-Fallback hält API-Namen NanoCpus/PidsLimit konsistent.
        fmt='{"id":{{json .Id}},"privileged":{{json .HostConfig.Privileged}},"readonly":{{json .HostConfig.ReadonlyRootfs}},"capdrop":{{json .HostConfig.CapDrop}},"capadd":{{json .HostConfig.CapAdd}},"security":{{json .HostConfig.SecurityOpt}},"ports":{{json .HostConfig.PortBindings}},"memory":{{json .HostConfig.Memory}},"nanocpus":{{json .HostConfig.NanoCpus}},"pids":{{json .HostConfig.PidsLimit}},"pidmode":{{json .HostConfig.PidMode}},"ipcmode":{{json .HostConfig.IpcMode}},"entrypoint":{{json .Config.Entrypoint}},"cmd":{{json .Config.Cmd}},"env":{{json .Config.Env}},"networks":[{{range $name,$net := .NetworkSettings.Networks}}{"name":{{json $name}},"id":{{json $net.NetworkID}}},{{end}}null],"tmpfs":{{json .HostConfig.Tmpfs}}}'
        v=json.loads(self._call(['container','inspect',self.pg_cid,'--format',fmt]))
        expected_cmd=['-D','/var/lib/postgresql/data','-c','listen_addresses=','-c','unix_socket_directories=/socket',
                      '-c','unix_socket_permissions=0777','-c','password_encryption=scram-sha-256',
                      '-c','log_statement=none','-c','log_min_error_statement=panic','-c','logging_collector=off']
        if v['id']!=self.pg_cid or v['privileged'] is not False or v['readonly'] is not True or v['capdrop']!=['ALL'] \
                or v['capadd'] not in ([],None) or v['security']!=['no-new-privileges=true'] or v['ports'] not in ({},None) \
                or type(v['memory']) is not int or v['memory']!=384*1024*1024 or type(v['nanocpus']) is not int or v['nanocpus']!=750000000 \
                or type(v['pids']) is not int or v['pids']!=64 or v['pidmode']!='' or v['ipcmode']!='private' \
                or v['entrypoint']!=['/usr/lib/postgresql/17/bin/postgres'] or v['cmd']!=expected_cmd \
                or v['tmpfs']!={'/tmp':'rw,nosuid,nodev,noexec,size=64m,mode=1777'}:raise PgAdapterFailure()
        networks=v['networks']
        if not (networks==[None] or type(networks) is list and len(networks)==2 and networks[-1] is None
                and set(networks[0])=={'name','id'} and networks[0]['name']=='none' and type(networks[0]['id']) is str
                and (networks[0]['id']=='' or re.fullmatch('[a-f0-9]{64}',networks[0]['id']))):raise PgAdapterFailure()
        image=json.loads(self._call(['image','inspect',self.b.image,'--format','{"env":{{json .Config.Env}}}']))
        def env(rows):
            if type(rows) is not list or any(type(row) is not str or '=' not in row for row in rows):raise PgAdapterFailure()
            values=dict(row.split('=',1) for row in rows)
            if len(values)!=len(rows):raise PgAdapterFailure()
            return values
        if env(v['env'])!=env(image['env']):raise PgAdapterFailure()

    def restart_existing_pg(self):
        """Nur expliziter vorhandener CID, niemals initdb/Volumecreate/Rollenbootstrap.

        Docker start ist für einen laufenden eigenen Container idempotent. Der
        Aufrufer hat PG_VERSION/Owner schon vor Secretlieferung gebunden; nach
        dem Start werden Config und tatsächliche Bereitschaft erneut geprüft.
        """
        if self.state!='NEW':raise PgAdapterFailure()
        try:
            self.verify_existing_pg()
            raw=self._call(['container','start',self.pg_cid])
            if raw.decode('ascii').strip()!=self.pg_cid:raise PgAdapterFailure()
            self._verify_pg();self._wait_pg();self.state='RUNNING';return 0
        except BaseException as error:
            self.state='UNKNOWN';raise PgAdapterFailure(getattr(error,'own_pid',None)) from None

    def bootstrap(self):
        """Nur nach neuem Own-Inspect; keine SQL-/Passwortdatei oder Automatikwiederholung."""
        if self.state!='RUNNING':raise PgAdapterFailure()
        self.state='BOOTSTRAPPING'
        try:
            self._verify_pg()
            self._call(['container','exec','--interactive','--user',str(self.b.pg_uid)+':'+str(self.b.pg_gid),self.pg_cid,
                        '/usr/lib/postgresql/17/bin/psql',*psql_initial_arguments()],self._sql,self._secrets,prompts=True)
            self.state='BOOTSTRAPPED';return 0
        except BaseException as error:self.state='UNKNOWN';raise PgAdapterFailure(getattr(error,'own_pid',None)) from None
        finally:self.close()

    def close(self):
        """Nur eigene RAM-Puffer, kein Docker-/SSH-/Ressourcencleanup."""
        for buf in (self._sql,self._admin):
            if buf is not None:buf[:]=b'\0'*len(buf)
        self._sql=None;self._admin=None;self._secrets=[]


if __name__=='__main__':print('FLOWZER_INITIAL_PG_ADAPTER_SOURCE_ONLY')
