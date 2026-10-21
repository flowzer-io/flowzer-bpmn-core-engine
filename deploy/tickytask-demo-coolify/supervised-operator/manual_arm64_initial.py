"""Ein eigener synthetischer ARM64-Initialversuch, standardmäßig vollständig inert.

Der öffentliche Einstieg bleibt immer SOURCE_ONLY. Nur ein separat physisch
gebundener privater Rootcaller darf später Admission, maximal zwei
Immutable-Pulls, Original-Initialsequenz und ausschließlich eigenes Cleanup
öffnen. Kein Vault, echter IdP, Coolifywrite, Installer oder allgemeiner Executor.
"""
from __future__ import annotations
import argparse,base64,datetime,hashlib,hmac,ipaddress,json,math,os,re,stat,subprocess,sys,time,types,uuid
from pathlib import Path
API_SOURCE='a7fcde7cba2cf526e8908037b238ee6d68bdbf85'
API_REF='ghcr.io/flowzer-io/flowzer-api@sha256:8528f456c890be903293fd90b931f7750247108b9b64ca56a37d44f1378222e5'
API_ID='sha256:0033d7828879afb1f8841a87eb7201583c1da865bbd13042de99e50c847718d0'
PG_REF='postgres@sha256:2d46d14f5a051d8c1e4f9d7c07dbeb877ae08150654d69ca32903ccbae4a5421'
PG_ID='sha256:a470461db3d4079fb4cb545328a6ef7c7d1ffc33e6981f5d3e2cfda1634d297e'
MODULES=('manual_transport.py','manual_postgres.py','manual_operator.py','manual_readiness.py','manual_pg_adapter.py','manual_runtime_adapter.py','manual_arm64_initial_cleanup.py')
ROLES=('ownership-pg','ownership-key','initdb','postgres','migrate','api')
STAGES=('ATTEMPT','PULL_API','PULL_PG','INITIAL','CLEANUP')
class Stop(Exception):
 """Nur endlicher Code; keine Credentials, Rohantwort oder Exceptiondetails."""
 def __init__(self,code='INPUT'):
  self.code=code if code in {'INPUT','SOURCE','LEASE','ADMISSION','IMAGE','COMMAND','TRANSPORT','ONCE','OWNERSHIP','CLEANUP','UNKNOWN'} else 'UNKNOWN'
  super().__init__('FLOWZER_ARM64_'+self.code)

def require(value,code='INPUT'):
 if not value:raise Stop(code)

def owned(path,maximum=65536):
 """Eigene reguläre begrenzte Raw-Datei; kein Symlink und keine fremde Schreibbarkeit."""
 fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
 try:
  info=os.fstat(fd);require(stat.S_ISREG(info.st_mode) and info.st_uid==os.getuid() and not info.st_mode&0o022,'SOURCE')
  raw=os.read(fd,maximum+1)
 finally:os.close(fd)
 require(len(raw)<=maximum,'SOURCE');return raw

def pinned(path,pin):
 raw=owned(path);require(hashlib.sha256(raw).hexdigest()==pin,'SOURCE');return raw

def strict_json(raw):
 def pairs(rows):
  result={}
  for key,value in rows:
   require(key not in result);result[key]=value
  return result
 return json.loads(raw,object_pairs_hook=pairs)

class Once:
 """O_EXCL pro Rootnonce und Stage; Fehler/UNKNOWN öffnet niemals einen Retry."""
 def __init__(self,directory,nonce):
  require(type(nonce) is str and re.fullmatch('[a-f0-9]{32}',nonce),'ONCE');self.directory=Path(directory);self.nonce=nonce
 def claim(self,stage):
  require(stage in STAGES,'ONCE');fd=None;directory=None
  try:
   directory=os.open(self.directory,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW);info=os.fstat(directory)
   require(info.st_uid==os.getuid() and not info.st_mode&0o022,'ONCE')
   fd=os.open('arm64-'+self.nonce+'-'+stage+'.claimed',os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600,dir_fd=directory)
   os.write(fd,b'once; unknown outcome never retry\n');os.fsync(fd);os.fsync(directory)
  except OSError:raise Stop('ONCE') from None
  finally:
   if fd is not None:os.close(fd)
   if directory is not None:os.close(directory)

def admit_host(facts):
 """Nur frischer eigener Host, konservative Reserve und vollständig leerer Scope."""
 require(type(facts) is dict,'ADMISSION')
 require(facts.get('identityMatches') is True and type(facts.get('uid')) is int and facts['uid']==0
         and facts.get('architecture')=='aarch64' and type(facts.get('cpuCount')) is int and facts['cpuCount']==4,'ADMISSION')
 for key,minimum in [('memAvailableBytes',4*1024**3),('freeDockerBytes',8*1024**3)]:
  require(type(facts.get(key)) is int and facts[key]>=minimum,'ADMISSION')
 load=facts.get('load1');require(type(load) in (int,float) and math.isfinite(load) and 0<=load<4,'ADMISSION')
 require(facts.get('neutralPathAbsent') is True and facts.get('clientVersion')==facts.get('serverVersion')=='27.5.1'
         and facts.get('clientArch')==facts.get('serverArch')=='arm64' and facts.get('clientApi')==facts.get('serverApi')=='1.47'
         and facts.get('serverOs')=='linux' and facts.get('ownContainers')==[]
         and facts.get('ownVolumes')==[] and type(facts.get('foreignNetworkPeers')) is int and facts['foreignNetworkPeers']==0,'ADMISSION')

def admit_images(api,pg):
 """Lokale tatsächliche Imageconfig VOR Credentiallieferung, niemals nur Registry-307/Versionstag."""
 for image,expected in [(api,API_ID),(pg,PG_ID)]:
  require(type(image) is dict and image.get('id')==expected and image.get('os')=='linux' and image.get('arch')=='arm64','IMAGE')
  require('volumes' in image,'IMAGE');volumes=image['volumes']
  if volumes is None:volumes={}
  require(type(volumes) is dict and set(volumes)<=(set() if expected==API_ID else {'/var/lib/postgresql/data'}),'IMAGE')
  # Das offizielle PGimage deklariert data; Originalports binden genau dies explizit.
  # Jede zusätzliche Image-VOLUME-Deklaration erzeugte sonst ungebundene anonyme Volumes.
  rows=image.get('env');require(type(rows) is list and all(type(row) is str and '=' in row for row in rows),'IMAGE')
  env=dict(row.split('=',1) for row in rows);require(len(env)==len(rows),'IMAGE')
  if expected==API_ID:
   labels=image.get('labels');require(type(labels) is dict and labels.get('org.opencontainers.image.revision')==API_SOURCE and env.get('APP_UID')=='1654','IMAGE')
  else:
   require(image.get('labels') in ({},None) and env.get('PG_MAJOR')=='17' and type(env.get('PG_VERSION')) is str
           and len(env['PG_VERSION'])<=128 and re.fullmatch(r'17\.11(?:-[A-Za-z0-9.+~]+)?',env['PG_VERSION']),'IMAGE')


def validate_permit(permit,manifest,manifest_pin,wall,*,cleanup=False):
 """Exakter kurzer synthetischer Root-Slot; keine Sourceöffnung oder frei erneuerbare Nonce."""
 require(type(permit) is dict and type(manifest) is dict and manifest.get('kind')=='FLOWZER_ARM64_INITIAL_MANIFEST_V1','LEASE')
 for key,length in [('head',40),('tree',40)]:require(type(manifest.get(key)) is str and re.fullmatch('[a-f0-9]{'+str(length)+'}',manifest[key]),'LEASE')
 for key in ('owner','executor'):
  try:require(str(uuid.UUID(manifest[key]))==manifest[key],'LEASE')
  except Exception:raise Stop('LEASE') from None
 require(type(manifest.get('namespace')) is str and re.fullmatch('[a-z0-9][a-z0-9-]{15,63}',manifest['namespace']),'LEASE')
 fixed=dict(kind='ROOT_FLOWZER_ARM64_INITIAL_RUNTIME_ONCE_V1',executionAllowed=True,syntheticOnly=True,vaultAllowed=False,realIdpAllowed=False,
            coolifyApiWritesAllowed=False,productionAllowed=False,manifestSHA256=manifest_pin,callerSHA256=manifest['sourcePins']['manual_arm64_initial.py'],
            apiRef=API_REF,apiId=API_ID,apiSource=API_SOURCE,pgRef=PG_REF,pgId=PG_ID,maximumPulls=2,maximumContainers=6,maximumVolumes=3,maximumActiveContainers=2,hostPorts=0)
 fixed.update({k:manifest[k] for k in ('head','tree','namespace','owner','executor')})
 require(all(type(permit.get(k)) is type(v) and permit[k]==v for k,v in fixed.items()),'LEASE')
 require(type(permit.get('nonce')) is str and re.fullmatch('[a-f0-9]{32}',permit['nonce']),'LEASE')
 try:
  issued=datetime.datetime.fromisoformat(permit['issuedUTC']);end=datetime.datetime.fromisoformat(permit['expiresUTC']);last=datetime.datetime.fromisoformat(permit['cleanupUntilUTC'])
  require(all(x.tzinfo is not None for x in (issued,end,last)) and 0<(end-issued).total_seconds()<=600 and 0<=(last-end).total_seconds()<=120,'LEASE')
  require(issued.timestamp()<=wall<(last if cleanup else end).timestamp(),'LEASE')
 except Stop:raise
 except Exception:raise Stop('LEASE') from None


def load_raw_modules(directory,pins):
 """Alle sieben Raw-Snapshots prüfen, dann dieselben RAMbytes ausführen; kein .pyc.

 Die sechs vorhandenen Module bleiben unverändert. Canonical preload verhindert
 auch deren historische importlib-Fallbacks. Ein fremdes sys.modules ist kein Pin.
 """
 require(all(name in pins for name in MODULES),'SOURCE');directory=Path(directory)
 snapshots={name:pinned(directory/name,pins[name]) for name in MODULES}
 result={}
 for filename in MODULES:
  name=filename.removesuffix('.py');module=types.ModuleType(name);module.__file__=str(directory/filename);module.__package__=''
  sys.modules[name]=module;exec(compile(snapshots[filename],module.__file__,'exec',dont_inherit=True),module.__dict__);result[name]=module
 return result


# Keine Kindprozesse/Netzwerkports im Hostprobe. Nur nach gebundener SSHidentität
# eigene Kapazitätsmetadaten lesen. Dockerfakten kommen separat über SshTarget.
HOST_PROBE="""import errno,json,os,platform,socket
try:
 c=os.environ.get('SSH_CONNECTION','').split()
 assert os.getuid()==os.geteuid()==0 and platform.machine()=='aarch64' and socket.gethostname()==expected['hostname'] and len(c)==4 and c[2]==expected['ipv4']
 try:os.lstat('/nonexistent')
 except FileNotFoundError as error:assert error.errno==errno.ENOENT
 else:raise ValueError()
 memory={}
 for line in open('/proc/meminfo'):
  k,v=line.split(':',1)
  if k=='MemAvailable':memory[k]=int(v.split()[0])*1024
 disk=os.statvfs('/var/lib/docker')
 value=dict(identityMatches=True,uid=0,architecture='aarch64',cpuCount=os.cpu_count(),memAvailableBytes=memory['MemAvailable'],freeDockerBytes=disk.f_bavail*disk.f_frsize,load1=float(open('/proc/loadavg').read().split()[0]),neutralPathAbsent=True)
except BaseException:value=dict(identityMatches=False)
print(json.dumps(value))
"""


def host_wire(hostname,ipv4):
 """Feste Source plus streng typisierte Identitätsdaten, kein Reread nach Root-Pin."""
 require(type(hostname) is str and re.fullmatch('[a-z0-9.-]{1,253}',hostname),'SOURCE')
 try:require(str(ipaddress.IPv4Address(ipv4))==ipv4,'SOURCE')
 except Exception:raise Stop('SOURCE') from None
 data=json.dumps(dict(hostname=hostname,ipv4=ipv4),sort_keys=True)
 wire=('import json\nexpected=json.loads('+repr(data)+')\n'+HOST_PROBE).encode()
 require(len(wire)<=16384,'SOURCE');return wire




def source_paths(directory):
 """Feste zehn Inputdateien, keine frei verkleinerbare/erweiterbare Root-Sourcemenge."""
 directory=Path(directory)
 result={name:directory/name for name in (*MODULES,'manual_arm64_initial.py')}
 result.update(sql01=directory.parents[1]/'postgresql/01-datenbank-und-rollen.sql',sql02=directory.parents[1]/'postgresql/02-laufzeitrechte.sql')
 return result


def hostkey_matches(raw,alias,fingerprint):
 """Nur bestehende eigene known_hosts, auch Hashform; kein Scan/Update/Fallback."""
 matches=[]
 for line in raw.decode('ascii').splitlines():
  if not line or line.startswith('#'):continue
  row=line.split()
  if len(row)<3:continue
  host,algorithm,key=row[:3];match=alias in host.split(',')
  if host.startswith('|1|'):
   _,_,salt,digest=host.split('|');match=hmac.compare_digest(hmac.digest(base64.b64decode(salt),alias.encode(),'sha1'),base64.b64decode(digest))
  if match and algorithm=='ssh-ed25519':matches.append('SHA256:'+base64.b64encode(hashlib.sha256(base64.b64decode(key,validate=True)).digest()).decode().rstrip('='))
 return matches==[fingerprint]


class Lease:
 """Tatsächliche Rootbyte-/Source-/Zeit-/Hostkeybindung vor jedem Originalkanalcall.

 Lokale Rootdateien sind Bindungen, keine kryptografischen Signaturen. Der Mensch/
 Rootkoordinator muss deren exakten Hash extern liefern und die tatsächlichen
 Caller-Rawbytes vor Entry binden. Kein Callback-Default autorisiert I/O.
 """
 def __init__(self,permit_path,permit_pin,manifest_path,manifest_pin):
  self.directory=Path(__file__).resolve().parent;self.permit_path=Path(permit_path);self.manifest_path=Path(manifest_path)
  self.permit_raw=pinned(self.permit_path,permit_pin);self.manifest_raw=pinned(self.manifest_path,manifest_pin)
  self.permit=strict_json(self.permit_raw);self.manifest=strict_json(self.manifest_raw);self.manifest_pin=manifest_pin
  validate_permit(self.permit,self.manifest,manifest_pin,time.time())
  self.start=time.monotonic();self.wall=time.time();self.paths=source_paths(self.directory)
  require(type(self.manifest['sourcePins']) is dict and set(self.manifest['sourcePins'])==set(self.paths),'SOURCE')
  target=self.manifest['target'];net=self.manifest['network']
  require(type(target['alias']) is str and re.fullmatch('[a-z0-9.-]{1,253}',target['alias']) and re.fullmatch('SHA256:[A-Za-z0-9+/]{43}',target['fingerprint']),'SOURCE')
  require(re.fullmatch('[a-f0-9]{64}',net['id']) and type(net['labels']) is dict and 1<=len(net['labels'])<=16
          and net['labels'].get('flowzer.tt.demo.owner')==self.manifest['owner'],'SOURCE')
  require(all(type(k) is str and type(v) is str and 0<len(k)<=256 and 0<len(v)<=256 for k,v in net['labels'].items()),'SOURCE')
  try:network=ipaddress.IPv4Network(net['cidr'],strict=True);require(str(network)==net['cidr'] and 24<=network.prefixlen<=30,'SOURCE')
  except Exception:raise Stop('SOURCE') from None
  require(type(self.manifest['runId']) is str and re.fullmatch('[A-Za-z0-9._-]{1,80}',self.manifest['runId']),'SOURCE')
  wire=host_wire(target['hostname'],target['ipv4']);require(hashlib.sha256(wire).hexdigest()==self.manifest['wireSHA256'],'SOURCE')
  self.guard()
 def guard(self,*,cleanup=False):
  """Frische Pins/Head/Tree/Key sowie absolute UND monotone Frist, keine Rechteerneuerung."""
  now=time.time();validate_permit(self.permit,self.manifest,self.manifest_pin,now,cleanup=cleanup)
  require(owned(self.permit_path)==self.permit_raw and owned(self.manifest_path)==self.manifest_raw,'SOURCE')
  for name,path in self.paths.items():pinned(path,self.manifest['sourcePins'][name])
  target=self.manifest['target'];require(hostkey_matches(owned(target['knownHosts'],131072),target['alias'],target['fingerprint']),'SOURCE')
  result=subprocess.run(['/usr/bin/git','-C',str(self.directory.parents[2]),'show','-s','--format=%H %T','HEAD'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=5,
                        env={'PATH':'/usr/bin:/bin','HOME':'/nonexistent','LC_ALL':'C'})
  require(result.returncode==0 and not result.stderr and result.stdout==(self.manifest['head']+' '+self.manifest['tree']+'\n').encode(),'SOURCE')
  end=datetime.datetime.fromisoformat(self.permit['cleanupUntilUTC' if cleanup else 'expiresUTC']).timestamp()
  left=min(end-now,end-self.wall-(time.monotonic()-self.start));require(left>0,'LEASE');return left


HOST_COMMAND='/usr/bin/python3 -I -S -B -'
IMAGE_FORMAT='{"id":{{json .ID}},"os":{{json .Os}},"arch":{{json .Architecture}},"labels":{{json .Config.Labels}},"env":{{json .Config.Env}},"volumes":{{json .Config.Volumes}}}'
VERSION_FORMAT='{"clientVersion":{{json .Client.Version}},"serverVersion":{{json .Server.Version}},"clientArch":{{json .Client.Arch}},"serverArch":{{json .Server.Arch}},"clientApi":{{json .Client.APIVersion}},"serverApi":{{json .Server.APIVersion}},"serverOs":{{json .Server.Os}}}'

class Channel:
 """Genau ein vorhandener SshTarget/OriginalBoundedPipe, kein alternativer Executor.

 Führt selbst keine Secretauflösung aus. Ein unbestätigter schreibender SSHcall
 setzt writer_unknown, selbst wenn nur der lokale Prozess schon geschlossen ist.
 Dann darf kein Cleanup gegen einen möglicherweise noch laufenden Writer starten.
 """
 def __init__(self,modules,manifest,permit,guard,*,pipe=None):
  self.modules=modules;self.manifest=manifest;self.permit=permit;self.guard=guard
  target=manifest['target'];self.target=modules['manual_pg_adapter'].SshTarget(target['ipv4'],target['alias'],target['knownHosts']);self.target.validate()
  self.ssh_prefix=tuple(self.target.argv(['version'])[:-1]);self.wire=host_wire(target['hostname'],target['ipv4'])
  require(hashlib.sha256(self.wire).hexdigest()==manifest['wireSHA256'],'SOURCE')
  self.base=pipe or modules['manual_transport'].BoundedPipe();self.stage='ADMISSION';self.cleanup_mode=False;self.cleanup_deadline=None
  self.writer_unknown=False;self.own_pid=None;self.pulls=set();self.containers=set();self.volumes=set();self.last_kind='NONE';self.last_exit=None
 def _remaining(self):
  left=self.guard(cleanup=self.cleanup_mode)
  require(type(left) in (int,float) and math.isfinite(left) and left>0,'LEASE')
  if self.cleanup_mode:
   if self.cleanup_deadline is None:self.cleanup_deadline=time.monotonic()+min(120,left)
   left=min(left,self.cleanup_deadline-time.monotonic());require(left>0,'LEASE')
  return left
 def _policy(self,args):
  """Nur Original-Initialports, zwei feste Pulls und eigenes explizites Cleanup."""
  require(type(args) is list and args,'COMMAND');pair=tuple(args[:2]);write=False
  if pair==('image','pull'):
   role='API' if self.stage=='PULL_API' else 'PG' if self.stage=='PULL_PG' else None
   ref=API_REF if role=='API' else PG_REF
   require(role is not None and args==['image','pull','--quiet','--platform','linux/arm64',ref] and ref not in self.pulls,'COMMAND')
   self.pulls.add(ref);write=True
  elif pair==('container','run'):
   require(self.stage=='INITIAL' and not self.cleanup_mode and args.count('--name')==1,'COMMAND')
   name=args[args.index('--name')+1];role=next((r for r in ROLES if name==self.manifest['namespace']+'_'+r+'_'+self.permit['nonce']),None)
   require(role is not None and name not in self.containers and (API_ID if role in ('api','migrate') else PG_ID) in args
           and not any(x in args for x in ['--privileged','--publish','-p','--network=host']),'COMMAND')
   self.containers.add(name);write=True
  elif pair==('volume','create'):
   name=args[-1];allowed={self.manifest['namespace']+'_'+suffix for suffix in ('postgres-data','socket','keyring')}
   require(self.stage=='INITIAL' and not self.cleanup_mode and name in allowed and name not in self.volumes,'COMMAND');self.volumes.add(name);write=True
  elif pair==('container','exec'):
   require(self.stage=='INITIAL' and not self.cleanup_mode,'COMMAND');write=True
  elif pair in (('container','rm'),('volume','rm')):
   require(self.cleanup_mode,'COMMAND');write=True
  else:
   require(args[0] in ('version','ps') or pair in [('image','inspect'),('network','inspect'),('container','inspect'),('volume','inspect'),('volume','ls')],'COMMAND')
  return write
 def exchange(self,argv,payload,secrets,*,timeout=15):
  """Originale RAMkanäle, keine Rohoutputs im Status; Optionen und Wire exakt binden."""
  require(not self.writer_unknown and self.own_pid is None,'UNKNOWN')
  left=self._remaining();require(type(argv) is list and tuple(argv[:-1])==self.ssh_prefix,'COMMAND')
  if argv[-1]==HOST_COMMAND:
   require(type(payload) is bytes and payload==self.wire and not secrets and self.stage=='ADMISSION','SOURCE');write=False;self.last_kind='HOST'
  else:
   args=self.modules['manual_pg_adapter'].remote_docker_arguments(argv[-1]);write=self._policy(args)
   self.last_kind=args[0]+('_'+args[1] if len(args)>1 and args[1] in ('pull','run','exec','inspect','create','rm','ls') else '')
  require(type(timeout) in (int,float) and 0<timeout<=120,'COMMAND');self.last_exit=None
  try:result=self.base.exchange(argv,payload,secrets,timeout=min(timeout,left))
  except BaseException as error:
   if write:self.writer_unknown=True
   pid=getattr(error,'own_pid',None)
   if type(pid) is int and pid>0:self.own_pid=pid
   raise
  self.last_exit=result.exit_code if type(result.exit_code) is int else None
  self._remaining();return result
 def call(self,args,*,write=False,cleanup=False,timeout=15):
  """Kleine gebundene Metadaten-/Pull-/Cleanupcalls, keine zusätzliche Pipe."""
  old=self.cleanup_mode;self.cleanup_mode=cleanup
  try:
   result=self.exchange(self.target.argv(args),b'',[],timeout=timeout)
   require(result.exit_code==0 and not result.stderr,'COMMAND');return result.stdout
  finally:self.cleanup_mode=old
 def image(self,role):
  require(role in ('api','pg'),'IMAGE');return strict_json(self.call(['image','inspect',API_ID if role=='api' else PG_ID,'--format',IMAGE_FORMAT]))
 def admission(self):
  """Eigene frische Kapazität und vollständig leerer Prefix vor jedem Initialstage."""
  self.stage='ADMISSION';argv=[*self.ssh_prefix,HOST_COMMAND];result=self.exchange(argv,self.wire,[],timeout=10)
  require(result.exit_code==0 and not result.stderr,'ADMISSION');facts=strict_json(result.stdout)
  require(type(facts) is dict and facts.get('identityMatches') is True,'ADMISSION')
  facts.update(strict_json(self.call(['version','--format',VERSION_FORMAT])))
  ns=self.manifest['namespace']
  facts['ownContainers']=self.call(['ps','-a','--no-trunc','--filter','name=^/'+ns+'_','--format','{{.ID}}']).decode('ascii').splitlines()
  facts['ownVolumes']=self.call(['volume','ls','--filter','name=^'+ns+'_','--format','{{.Name}}']).decode('ascii').splitlines()
  cleanup=self.modules['manual_arm64_initial_cleanup'];net=self.manifest['network']
  value=strict_json(self.call(['network','inspect',net['id'],'--format',cleanup.NETWORK_FORMAT]))
  require(value['id']==net['id'] and value['driver']=='bridge' and value['internal'] is False
          and value['subnets']==[net['cidr'],None] and value['labels']==net['labels'],'ADMISSION')
  peers=value['peers'];require(type(peers) is list and peers[-1:]==[None],'ADMISSION')
  # Beim Initialversuch ist jedes vorhandene Netzmitglied fremd/unerwartet. Keine Detailreads.
  facts['foreignNetworkPeers']=len(peers)-1;return facts



def bindings(modules,manifest,permit):
 """Nur Original-Initialbindung und synthetischer Loopback-IdP, unveränderte Limits."""
 pg=modules['manual_pg_adapter'];runtime=modules['manual_runtime_adapter'];net=manifest['network']
 binding=pg.PgBinding(manifest['namespace'],manifest['owner'],permit['nonce'],PG_ID,999,999,1654,1654,manifest['head'],manifest['runId'],architecture='arm64')
 settings=runtime.PublicSettings('https://127.0.0.1/realms/TickyTask','flowzer-api-demo','flowzer-bff-demo','flowzer-directory-demo',
                                 '11111111-1111-4111-8111-111111111111',net['cidr'])
 bound=runtime.RuntimeBinding(API_ID,API_SOURCE,net['id'],1654,1654,settings,tuple(sorted(net['labels'].items())),permit['nonce'])
 binding.validate();bound.validate();return binding,bound


def synthetic_bundle(operator):
 """Fünf frische reine RAMwerte, erst NACH tatsächlicher positiver Original-Observe.

 Kein Vault, ENV, Datei oder IdP. Pythonstrings sind nicht beweisbar zeroisierbar;
 die Originalports leeren ihre veränderlichen Paketpuffer beim Schließen.
 """
 import secrets
 return operator.SecretBundle(**{key:secrets.token_urlsafe(32) for key in ('admin','migration','runtime','bff','directory')})


def run_candidate(modules,manifest,permit,channel,once):
 """Ein begrenzter synthetischer Initialpfad und immer eigenes belegtes Cleanup.

 Vor INITIAL nur Admission und zwei exakte Immutable-Pulls. Schlägt etwa PGconfig
 fehl, wird weder ein Bundle erzeugt noch ein bestehender Prefix bereinigt.
 """
 proof=dict(kind='SYNTHETIC_OWN_ARM64_INITIAL_NOT_DEMO',result='FAIL',initial=False,cleanup='NOT_STARTED',
            architecture='linux/arm64',source=manifest['head'],apiSource=API_SOURCE,nonce=permit['nonce'],ownLocalHandles=0)
 ports=None;started=False;scope=None;phase='ADMISSION'
 try:
  require(not channel.writer_unknown and channel.own_pid is None,'UNKNOWN')
  for role,ref in [('API',API_REF),('PG',PG_REF)]:
   admit_host(channel.admission());phase='PULL_'+role;once.claim(phase);channel.stage=phase
   channel.call(['image','pull','--quiet','--platform','linux/arm64',ref],write=True,timeout=120)
  phase='IMAGE';api=channel.image('api');pg=channel.image('pg');admit_images(api,pg)
  admit_host(channel.admission());binding,bound=bindings(modules,manifest,permit)
  scope=dict(namespace=manifest['namespace'],owner=manifest['owner'],nonce=permit['nonce'],head=manifest['head'],runId=manifest['runId'],
             apiId=API_ID,pgId=PG_ID,apiLabels=api['labels'],network=manifest['network'])
  phase='INITIAL';once.claim('INITIAL');channel.stage='INITIAL';started=True
  runtime=modules['manual_runtime_adapter'];operator=modules['manual_operator']
  ports=runtime.RuntimePorts(binding,bound,channel.target,channel.guard,mode='initial',pipe=channel)
  operator.ManualOperator().run('initial',ports,lambda:ports.accept_bundle(synthetic_bundle(operator)))
  # Nur nach vollständigen echten Originalproofs: Migration22, ACL, FEEL und401.
  proof.update(initial=True,actualHead=22,pending=0,feel=True,anonymous401=True,result='PASS')
 except BaseException as error:
  proof['failure']=error.code if isinstance(error,Stop) else phase
 finally:
  # RAM-Close darf das begrenzte Ressourcen-Cleanup niemals überspringen.
  try:
   if ports is not None:ports.close()
  except BaseException:
   proof.update(result='FAIL',failure='CLOSE')
  if started:
   try:proof['cleanup']=modules['manual_arm64_initial_cleanup'].cleanup(channel,scope,once)
   except BaseException:proof['cleanup']='UNKNOWN'
  if channel.own_pid:proof.update(ownLocalHandles='UNKNOWN',ownLocalPid=channel.own_pid)
  if proof['cleanup']!='ABSENT' or channel.writer_unknown or channel.own_pid is not None:proof['result']='FAIL'
  proof.update(writerUnknown=channel.writer_unknown,lastCommand=channel.last_kind,lastExit=channel.last_exit)
 return proof


def execute(args=None):
 """Öffentlicher API-/CLIentry ist immer geschlossen, auch mit selbst ausgestellten Go-Dateien.

 Lease/Channel sind prüfbare Bibliotheksbausteine, keine Authority. Nur ein später
 separat von Root gebundener privater Caller darf sie zusammensetzen. Beliebiger
 bösartiger Same-UID-Pythoncode wird dadurch nicht zu einer Sandbox eingeschränkt.
 """
 raise Stop('LEASE')


ACTIVE={'channel':None,'proof':None}
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--execute',action='store_true')
 for name in ('permit','permit-sha256','manifest','manifest-sha256'):parser.add_argument('--'+name)
 args=parser.parse_args()
 if not args.execute:print('FLOWZER_ARM64_INITIAL_SOURCE_ONLY');return 0
 try:return execute(args)
 except BaseException:
  channel=ACTIVE['channel'];pid=channel.own_pid if channel is not None else None
  print(json.dumps(dict(result='STOP_NO_RAW',retryAllowed=False,ownLocalHandles='UNKNOWN' if pid else 0,ownLocalPid=pid,
                        resourceCleanup=ACTIVE['proof']['cleanup'] if ACTIVE['proof'] else 'NOT_CONFIRMED')))
  return 2
if __name__=='__main__':raise SystemExit(main())
