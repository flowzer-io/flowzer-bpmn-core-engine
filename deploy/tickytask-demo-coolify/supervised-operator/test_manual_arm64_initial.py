"""Testzweck: eigener ARM64Initial-Kandidat bleibt ohne exakte Rootbindung geschlossen.

Nur reine Fakten, eigene temporäre Dateien und Fakekanäle. Kein SSH, Daemon,
Registrydownload, Vaultzugriff oder Ressourcenstart im Testsuitepfad.
"""
import importlib.util,json,sys,tempfile,unittest
from pathlib import Path
from unittest import mock
S=Path(__file__).with_name('manual_arm64_initial.py')
s=importlib.util.spec_from_file_location('arm64_initial_test_target',S)
m=importlib.util.module_from_spec(s);sys.modules[s.name]=m;s.loader.exec_module(m)
class AdmissionTests(unittest.TestCase):
 def facts(self):
  return dict(identityMatches=True,uid=0,architecture='aarch64',cpuCount=4,memAvailableBytes=6*1024**3,freeDockerBytes=50*1024**3,load1=.1,neutralPathAbsent=True,
              clientVersion='27.5.1',serverVersion='27.5.1',clientArch='arm64',serverArch='arm64',clientApi='1.47',serverApi='1.47',serverOs='linux',ownContainers=[],ownVolumes=[],foreignNetworkPeers=0)
 def test_valid_candidate_and_each_admission_drift(self):
  # Testzweck: ein echter gültiger Kandidat, jede einzelne Abweichung stoppt vor Credentials/Writes.
  self.assertIsNone(m.admit_host(self.facts()))
  for k,v in [('identityMatches',False),('uid',1),('architecture','amd64'),('cpuCount',True),('memAvailableBytes',4*1024**3-1),('freeDockerBytes',8*1024**3-1),('load1',4),('neutralPathAbsent',False),('clientVersion','29.8.2'),('serverVersion','27.5.0'),('clientArch','amd64'),('serverArch','amd64'),('serverOs','windows'),('serverApi','1.48'),('ownContainers',['a'*64]),('ownVolumes',['existing']),('foreignNetworkPeers',1)]:
   with self.subTest(k=k),self.assertRaises(m.Stop):m.admit_host(dict(self.facts(),**{k:v}))
 def image(self,role):
  return dict(id=m.API_ID if role=='api' else m.PG_ID,os='linux',arch='arm64',volumes={} if role=='api' else {'/var/lib/postgresql/data':{}},labels={'org.opencontainers.image.revision':m.API_SOURCE} if role=='api' else {},env=['PG_MAJOR=17','PG_VERSION=17.11-1.pgdg13+1'] if role=='pg' else ['APP_UID=1654'])
 def test_real_image_config_gate_and_pg_minor_boundary(self):
  # Testzweck: Root-Metadaten/307 sind kein Ersatz für tatsächliche lokale Config vor Secretlieferung.
  api=self.image('api');pg=self.image('pg');self.assertIsNone(m.admit_images(api,pg))
  for role,k,v in [('api','id','sha256:'+'0'*64),('api','arch','amd64'),('api','labels',{}),('pg','id','sha256:'+'0'*64),('pg','arch','amd64'),('api','volumes',{'/unexpected':{}}),('pg','volumes',False),('pg','volumes',{'/var/lib/postgresql/data':{},'/unexpected':{}}),('pg','env',['PG_MAJOR=16','PG_VERSION=17.11']),('pg','env',['PG_MAJOR=17','PG_VERSION=17.12']),('pg','env',['PG_MAJOR=17','PG_VERSION=17.110'])]:
   a=dict(api);p=dict(pg);(a if role=='api' else p)[k]=v
   with self.subTest(role=role,k=k,v=v),self.assertRaises(m.Stop):m.admit_images(a,p)
 def test_stage_once_is_global_for_nonce_and_never_retried(self):
  # Testzweck: gleicher Versuch oder Stage kann keine zweite Pull-/Initial-/Cleanuprunde eröffnen.
  with tempfile.TemporaryDirectory() as d:
   j=m.Once(Path(d),'a'*32);j.claim('ATTEMPT')
   with self.assertRaises(m.Stop):m.Once(Path(d),'a'*32).claim('ATTEMPT')
   j.claim('INITIAL')
   with self.assertRaises(m.Stop):j.claim('INITIAL')
 def test_default_cli_is_closed_and_never_constructs_live_channel(self):
  # Testzweck: normaler Aufruf ist inert, insbesondere kein Test-/Fake-Go als Default.
  with mock.patch.object(m.sys,'argv',['manual_arm64_initial.py']),mock.patch.object(m,'execute') as execute:
   self.assertEqual(m.main(),0);execute.assert_not_called()
class PermitAndWireTests(unittest.TestCase):
 def fixture(self):
  manifest=dict(kind='FLOWZER_ARM64_INITIAL_MANIFEST_V1',head='1'*40,tree='2'*40,namespace='fz-arm64-source-test',owner='11111111-1111-4111-8111-111111111111',executor='22222222-2222-4222-8222-222222222222',sourcePins={'manual_arm64_initial.py':'3'*64})
  p=dict(kind='ROOT_FLOWZER_ARM64_INITIAL_RUNTIME_ONCE_V1',executionAllowed=True,syntheticOnly=True,vaultAllowed=False,realIdpAllowed=False,coolifyApiWritesAllowed=False,productionAllowed=False,
         manifestSHA256='4'*64,callerSHA256='3'*64,head=manifest['head'],tree=manifest['tree'],namespace=manifest['namespace'],owner=manifest['owner'],executor=manifest['executor'],nonce='5'*32,
         apiRef=m.API_REF,apiId=m.API_ID,apiSource=m.API_SOURCE,pgRef=m.PG_REF,pgId=m.PG_ID,maximumPulls=2,maximumContainers=6,maximumVolumes=3,maximumActiveContainers=2,hostPorts=0,
         issuedUTC='1970-01-01T00:15:00+00:00',expiresUTC='1970-01-01T00:20:00+00:00',cleanupUntilUTC='1970-01-01T00:22:00+00:00')
  return manifest,p
 def test_actual_root_lease_typed_identity_source_scope_and_cleanup_window(self):
  # Testzweck: eigener gebundener Rootversuch, niemals Source-only/AlwaysTrue oder verlängerbarer Slot.
  manifest,p=self.fixture();self.assertIsNone(m.validate_permit(p,manifest,'4'*64,1000))
  for k,v in [('executionAllowed',False),('syntheticOnly',False),('vaultAllowed',True),('realIdpAllowed',True),('productionAllowed',True),('maximumPulls',3),('maximumContainers',True),('hostPorts',1),('callerSHA256','0'*64),('nonce','bad'),('head','0'*40),('expiresUTC','1970-01-01T00:40:00+00:00')]:
   with self.subTest(k=k),self.assertRaises(m.Stop):m.validate_permit(dict(p,**{k:v}),manifest,'4'*64,1000)
  with self.assertRaises(m.Stop):m.validate_permit(p,manifest,'0'*64,1000)
  with self.assertRaises(m.Stop):m.validate_permit(p,manifest,'4'*64,1200)
  self.assertIsNone(m.validate_permit(p,manifest,'4'*64,1250,cleanup=True))
  with self.assertRaises(m.Stop):m.validate_permit(p,manifest,'4'*64,1320,cleanup=True)
 def test_host_wire_is_small_immutable_and_argument_data_not_program(self):
  # Testzweck: nur gepinnte Wirebytes, keine Dateirereads oder eingeschleuster Python-/Shellcode.
  wire=m.host_wire('synthetic','192.0.2.8');self.assertLessEqual(len(wire),16384)
  with mock.patch.object(Path,'read_bytes',side_effect=AssertionError('no reread')):self.assertEqual(m.host_wire('synthetic','192.0.2.8'),wire)
  for host,ip in [('x;touch /tmp/no','192.0.2.8'),('synthetic','127.0.0.1;other')]:
   with self.assertRaises(m.Stop):m.host_wire(host,ip)
 def test_raw_loader_pins_complete_closure_before_any_execution_and_ignores_pyc(self):
  # Testzweck: tatsächlicher retained UNCHECKED_HASH-Cache darf die Root-Sourcemenge nie ersetzen.
  import hashlib,py_compile
  with tempfile.TemporaryDirectory(prefix='fz-arm64-raw-') as td:
   d=Path(td);pins={};marker=d/'EXECUTED'
   for name in m.MODULES:
    p=d/name;p.write_bytes((S.parent/name).read_bytes());pins[name]=hashlib.sha256(p.read_bytes()).hexdigest()
    bad=d/('bad_'+name);bad.write_text('from pathlib import Path\nPath('+repr(str(marker))+').touch()\n')
    cache=Path(importlib.util.cache_from_source(str(p)));cache.parent.mkdir(exist_ok=True)
    py_compile.compile(str(bad),cfile=str(cache),doraise=True,invalidation_mode=py_compile.PycInvalidationMode.UNCHECKED_HASH)
   modules=m.load_raw_modules(d,pins);self.assertIn('manual_runtime_adapter',modules);self.assertFalse(marker.exists())
   pins[m.MODULES[-1]]='0'*64
   with mock.patch.object(m.types,'ModuleType') as module,self.assertRaises(m.Stop):m.load_raw_modules(d,pins)
   module.assert_not_called()
class CleanupTests(unittest.TestCase):
 def module(self):return m.load_raw_modules(S.parent,{name:__import__('hashlib').sha256((S.parent/name).read_bytes()).hexdigest() for name in m.MODULES})['manual_arm64_initial_cleanup']
 def scope(self):
  return dict(namespace='fz-arm64-source-test',nonce='a'*32,owner='11111111-1111-4111-8111-111111111111',head='1'*40,runId='synthetic-test',apiId=m.API_ID,pgId=m.PG_ID,apiLabels={'org.opencontainers.image.revision':m.API_SOURCE})
 def rows(self):
  scope=self.scope();labels={'com.docker.compose.project':scope['namespace'],'flowzer.tt.demo.owner':scope['owner'],'flowzer.tt.demo.nonce':scope['nonce'],'flowzer.tt.demo.source':scope['head'],'flowzer.tt.demo.run':scope['runId']}
  containers=[dict(id='b'*64,name='/'+scope['namespace']+'_postgres_'+scope['nonce'],image=m.PG_ID,labels=dict(labels),running=True),dict(id='c'*64,name='/'+scope['namespace']+'_api_'+scope['nonce'],image=m.API_ID,labels=dict(labels,**scope['apiLabels']),running=True)]
  volumes=[dict(name=scope['namespace']+'_'+suffix,driver='local',labels=dict(labels)) for suffix in ['postgres-data','socket','keyring']]
  return containers,volumes
 def test_whole_exact_own_inventory_and_each_foreign_mutation(self):
  # Testzweck: alle Ressourcen vor erstem Delete prüfen, niemals nur Namenspräfix oder Labelsubset.
  cleanup=self.module();containers,volumes=self.rows();ids,names=cleanup.inventory(self.scope(),containers,volumes)
  self.assertEqual(set(ids),{'b'*64,'c'*64});self.assertEqual(len(names),3)
  import copy
  for which,key,value in [('container','image','sha256:'+'0'*64),('container','name','/foreign'),('container','labels',{}),('volume','driver','other'),('volume','labels',{}),('volume','name','foreign')]:
   cs=copy.deepcopy(containers);vs=copy.deepcopy(volumes);(cs[0] if which=='container' else vs[0])[key]=value
   with self.subTest(which=which,key=key),self.assertRaises(cleanup.CleanupStop):cleanup.inventory(self.scope(),cs,vs)
 def test_unknown_writer_or_foreign_inventory_never_deletes(self):
  # Testzweck: kein Cleanup neben unbekanntem eigenem Writer, kein Blind-Retry bei Scopefehler.
  cleanup=self.module();channel=mock.Mock();channel.writer_unknown=True;channel.own_pid=None
  self.assertEqual(cleanup.cleanup(channel,self.scope(),mock.Mock()),'UNKNOWN');channel.call.assert_not_called()
  channel.writer_unknown=False;containers,volumes=self.rows();volumes[2]['labels']={}
  with mock.patch.object(cleanup,'read_inventory',return_value=(containers,volumes)),mock.patch.object(cleanup,'check_network'):
   self.assertEqual(cleanup.cleanup(channel,self.scope(),mock.Mock()),'UNKNOWN')
  self.assertFalse(any(call.kwargs.get('write') for call in channel.call.call_args_list))
class ChannelTests(unittest.TestCase):
 def modules(self):return m.load_raw_modules(S.parent,{name:__import__('hashlib').sha256((S.parent/name).read_bytes()).hexdigest() for name in m.MODULES})
 def channel(self):
  import hashlib
  modules=self.modules();manifest=dict(namespace='fz-arm64-source-test',target=dict(ipv4='192.0.2.8',alias='synthetic.invalid',hostname='synthetic',knownHosts='/synthetic/known_hosts'),wireSHA256=hashlib.sha256(m.host_wire('synthetic','192.0.2.8')).hexdigest())
  base=mock.Mock();base.exchange.return_value=modules['manual_transport'].PipeResult(0,b'',b'')
  return m.Channel(modules,manifest,{'nonce':'a'*32},mock.Mock(return_value=120),pipe=base),base,modules
 def test_exact_single_ssh_route_and_wire_no_postpin_reread(self):
  # Testzweck: keine zweite Route/fehlende Optionen/ungepinnte Hostprogrammbytes zum Originalpipe.
  channel,base,modules=self.channel();argv=channel.target.argv(['version']);argv[-1]=m.HOST_COMMAND
  channel.exchange(argv,channel.wire,[],timeout=5);self.assertEqual(base.exchange.call_count,1)
  with self.assertRaises(m.Stop):channel.exchange(argv,channel.wire+b'\n',[],timeout=5)
  argv[1]='changed'
  with self.assertRaises(m.Stop):channel.exchange(argv,channel.wire,[],timeout=5)
  self.assertEqual(base.exchange.call_count,1)
 def test_only_two_exact_immutable_pulls_never_retry_or_foreign_write(self):
  # Testzweck: auch im Pullstage kein alternatives Image/Prune/Netzwerkwrite und keine Wiederholung.
  channel,base,modules=self.channel();channel.stage='PULL_API'
  args=['image','pull','--quiet','--platform','linux/arm64',m.API_REF]
  channel.exchange(channel.target.argv(args),b'',[],timeout=5)
  with self.assertRaises(m.Stop):channel.exchange(channel.target.argv(args),b'',[],timeout=5)
  for args in [['image','pull','foreign'],['network','rm','a'*64],['container','run','--name','foreign',m.API_ID]]:
   with self.assertRaises(m.Stop):channel.exchange(channel.target.argv(args),b'',[],timeout=5)
  self.assertEqual(base.exchange.call_count,1)
 def test_unknown_write_also_blocks_a_different_later_stage(self):
  # Testzweck: kein weiterführender Pull nach unbekanntem ersten Writer, nicht nur gleicher Request verboten.
  channel,base,modules=self.channel();channel.stage='PULL_API';base.exchange.side_effect=modules['manual_transport'].TransportFailure('TIMEOUT')
  with self.assertRaises(modules['manual_transport'].TransportFailure):channel.exchange(channel.target.argv(['image','pull','--quiet','--platform','linux/arm64',m.API_REF]),b'',[],timeout=5)
  channel.stage='PULL_PG';base.exchange.side_effect=None
  with self.assertRaises(m.Stop):channel.exchange(channel.target.argv(['image','pull','--quiet','--platform','linux/arm64',m.PG_REF]),b'',[],timeout=5)
  self.assertEqual(base.exchange.call_count,1)
 def test_unknown_ssh_exit_or_invalid_status_blocks_all_later_calls_and_cleanup(self):
  # Testzweck: lokales SSHende255/Signal/ungültig bestätigt niemals das entfernte Writerende.
  for status in (255,-15,-1,True,False,None,'0',256,10**20):
   with self.subTest(status=status):
    channel,base,modules=self.channel();channel.stage='INITIAL';base.exchange.return_value=modules['manual_transport'].PipeResult(status,b'',b'')
    with self.assertRaises(m.Stop):channel.call(['container','exec','b'*64,'/usr/bin/true'],write=True)
    self.assertTrue(channel.writer_unknown);self.assertIsNone(channel.own_pid)
    self.assertEqual(modules['manual_arm64_initial_cleanup'].cleanup(channel,{},mock.Mock()),'UNKNOWN')
    base.exchange.return_value=modules['manual_transport'].PipeResult(0,b'',b'');channel.stage='PULL_API'
    for args in (['image','pull','--quiet','--platform','linux/arm64',m.API_REF],['version','--format',m.VERSION_FORMAT]):
     with self.assertRaises(m.Stop):channel.call(args)
    self.assertEqual(base.exchange.call_count,1)
 def test_confirmed_remote_docker_exit_is_not_an_unknown_writer(self):
  # Testzweck: endliche bestätigte Dockerfehler sind kein SSH255-/Signalverlust; keine stille Fehlerlockerung.
  for status in (0,1,125,126,127,254):
   with self.subTest(status=status):
    channel,base,modules=self.channel();channel.stage='INITIAL';base.exchange.return_value=modules['manual_transport'].PipeResult(status,b'',b'')
    if status==0:self.assertEqual(channel.call(['container','exec','b'*64,'/usr/bin/true']),b'')
    else:
     with self.assertRaises(m.Stop):channel.call(['container','exec','b'*64,'/usr/bin/true'])
    self.assertFalse(channel.writer_unknown);self.assertIsNone(channel.own_pid)
 def test_unknown_write_preserves_own_handle_and_blocks_cleanup(self):
  # Testzweck: unbekannter eigener SSHwriter ist kein Beleg für sicheren konkurrierenden Remove.
  channel,base,modules=self.channel();channel.stage='PULL_API';base.exchange.side_effect=modules['manual_transport'].TransportFailure('TIMEOUT',12345)
  with self.assertRaises(modules['manual_transport'].TransportFailure):channel.exchange(channel.target.argv(['image','pull','--quiet','--platform','linux/arm64',m.API_REF]),b'',[],timeout=5)
  self.assertTrue(channel.writer_unknown);self.assertEqual(channel.own_pid,12345)
  cleanup=modules['manual_arm64_initial_cleanup'];self.assertEqual(cleanup.cleanup(channel,{},mock.Mock()),'UNKNOWN');self.assertEqual(base.exchange.call_count,1)
class CandidateTests(unittest.TestCase):
 def fixture(self):
  modules=m.load_raw_modules(S.parent,{name:__import__('hashlib').sha256((S.parent/name).read_bytes()).hexdigest() for name in m.MODULES});op=modules['manual_operator']
  manifest=dict(head='1'*40,namespace='fz-arm64-source-test',owner='11111111-1111-4111-8111-111111111111',runId='source-test',network=dict(id='a'*64,cidr='172.30.77.0/24',labels={'flowzer.tt.demo.owner':'11111111-1111-4111-8111-111111111111'}))
  channel=mock.Mock();channel.admission.side_effect=lambda:AdmissionTests().facts();channel.image.side_effect=lambda role:AdmissionTests().image(role);channel.writer_unknown=False;channel.own_pid=None;channel.last_kind='NONE';channel.last_exit=0
  class Ports:
   image_labels=AdmissionTests().image('api')['labels']
   def __init__(self):self.calls=[]
   def observe(self):self.calls.append('observe');return op.Observation(True,'ABSENT','ABSENT','ABSENT')
   def accept_bundle(self,b):self.calls.append('bundle');return b
   def initialize(self,b):self.calls.append('initialize');return 0
   def pg_start(self):self.calls.append('pg_start');return 0
   def bootstrap(self,b):self.calls.append('bootstrap');return 0
   def pg_proof(self):return op.RoleProof(True,True,True)
   def migrate(self,p):self.calls.append('migrate');return op.MigrationProof(0,22,0)
   def api_start(self,p):self.calls.append('api_start');return 'b'*64
   def api_proof(self,cid):return op.ApiProof(True,22,0,'Feel',401)
   def close(self):self.calls.append('close')
  return modules,manifest,channel,Ports()
 def test_only_original_initial_sequence_then_always_own_cleanup(self):
  # Testzweck: keine zweite Migration/Restartschleife; echte Sequenzklasse mit reinen Ports-Fakes.
  modules,manifest,channel,ports=self.fixture()
  with tempfile.TemporaryDirectory() as d,mock.patch.object(modules['manual_runtime_adapter'],'RuntimePorts',return_value=ports),mock.patch.object(modules['manual_arm64_initial_cleanup'],'cleanup',return_value='ABSENT') as cleanup:
   proof=m.run_candidate(modules,manifest,{'nonce':'c'*32},channel,m.Once(Path(d),'c'*32))
  self.assertEqual(proof['result'],'PASS');self.assertTrue(proof['initial']);self.assertEqual(proof['cleanup'],'ABSENT')
  self.assertEqual(ports.calls[:6],['observe','bundle','initialize','pg_start','bootstrap','migrate']);self.assertEqual(ports.calls.count('migrate'),1);self.assertEqual(cleanup.call_count,1)
  self.assertNotIn('password',json.dumps(proof).lower())
 def test_pg_config_drift_blocks_before_any_bundle_or_initial_resource(self):
  # Testzweck: ohne tatsächliches PG17.11-Config keine synthetischen Credentials/Initialisierung.
  modules,manifest,channel,ports=self.fixture();channel.image.side_effect=lambda role:AdmissionTests().image('api') if role=='api' else dict(AdmissionTests().image('pg'),env=['PG_MAJOR=17','PG_VERSION=17.12'])
  with tempfile.TemporaryDirectory() as d,mock.patch.object(m,'synthetic_bundle') as bundle,mock.patch.object(modules['manual_runtime_adapter'],'RuntimePorts',return_value=ports) as runtime,mock.patch.object(modules['manual_arm64_initial_cleanup'],'cleanup') as cleanup:
   proof=m.run_candidate(modules,manifest,{'nonce':'c'*32},channel,m.Once(Path(d),'c'*32))
  self.assertEqual(proof['result'],'FAIL');self.assertEqual(proof['cleanup'],'NOT_STARTED');bundle.assert_not_called();runtime.assert_not_called();cleanup.assert_not_called()
 def test_unknown_local_handle_cannot_be_pass_even_if_resource_cleanup_was_absent(self):
  # Testzweck: unbekannte eigene Prozessgruppe bleibt unabhängig vom Ressourcenzustand ein Fehler.
  modules,manifest,channel,ports=self.fixture();channel.own_pid=12345
  with tempfile.TemporaryDirectory() as d,mock.patch.object(modules['manual_runtime_adapter'],'RuntimePorts',return_value=ports),mock.patch.object(modules['manual_arm64_initial_cleanup'],'cleanup',return_value='ABSENT'):
   proof=m.run_candidate(modules,manifest,{'nonce':'c'*32},channel,m.Once(Path(d),'c'*32))
  self.assertEqual(proof['ownLocalHandles'],'UNKNOWN');self.assertEqual(proof['result'],'FAIL')
 def test_close_error_cannot_skip_bounded_cleanup_or_report_pass(self):
  # Testzweck: RAM-Close-Fehler überspringt nach Initial niemals den sicheren eigenen Cleanupversuch.
  modules,manifest,channel,ports=self.fixture();ports.close=mock.Mock(side_effect=RuntimeError('SYNTHETIC_CLOSE_FAILURE'))
  with tempfile.TemporaryDirectory() as d,mock.patch.object(modules['manual_runtime_adapter'],'RuntimePorts',return_value=ports),mock.patch.object(modules['manual_arm64_initial_cleanup'],'cleanup',return_value='ABSENT') as cleanup:
   proof=m.run_candidate(modules,manifest,{'nonce':'c'*32},channel,m.Once(Path(d),'c'*32))
  self.assertEqual(cleanup.call_count,1);self.assertEqual(proof['result'],'FAIL');self.assertEqual(proof['cleanup'],'ABSENT');self.assertEqual(proof['failure'],'CLOSE')
 def test_cleanup_unknown_cannot_be_reported_as_pass(self):
  # Testzweck: bestandene Initialsequenz ist ohne belegtes Own-Cleanup kein qualifizierter Pilot.
  modules,manifest,channel,ports=self.fixture()
  with tempfile.TemporaryDirectory() as d,mock.patch.object(modules['manual_runtime_adapter'],'RuntimePorts',return_value=ports),mock.patch.object(modules['manual_arm64_initial_cleanup'],'cleanup',return_value='UNKNOWN'):
   proof=m.run_candidate(modules,manifest,{'nonce':'c'*32},channel,m.Once(Path(d),'c'*32))
  self.assertTrue(proof['initial']);self.assertEqual(proof['result'],'FAIL');self.assertEqual(proof['cleanup'],'UNKNOWN')
class EntryAndCleanupFlowTests(unittest.TestCase):
 def test_real_entry_source_only_or_missing_go_never_loads_ports(self):
  # Testzweck: tatsächlicher CLI--execute bleibt ohne Rootdateien vor jeder Projektclosure/LivePipe geschlossen.
  with mock.patch.object(m.sys,'argv',['caller','--execute']),mock.patch.object(m,'load_raw_modules') as loader,mock.patch.object(m,'Channel') as channel:
   self.assertEqual(m.main(),2);loader.assert_not_called();channel.assert_not_called()
 def test_hostkey_plain_hashed_and_wrong_alias_or_fingerprint(self):
  # Testzweck: nur genau eine bestehende ED25519-Identität, weder Scan noch blindes Trustonfirstuse.
  import hashlib,base64,hmac,struct
  key=struct.pack('>I',11)+b'ssh-ed25519'+struct.pack('>I',32)+b'x'*32
  encoded=base64.b64encode(key).decode();fp='SHA256:'+base64.b64encode(hashlib.sha256(key).digest()).decode().rstrip('=')
  plain=('synthetic.invalid ssh-ed25519 '+encoded+'\n').encode();self.assertTrue(m.hostkey_matches(plain,'synthetic.invalid',fp))
  salt=b'fixed-public-test-salt';hashed='|1|'+base64.b64encode(salt).decode()+'|'+base64.b64encode(hmac.digest(salt,b'synthetic.invalid','sha1')).decode()
  self.assertTrue(m.hostkey_matches((hashed+' ssh-ed25519 '+encoded+'\n').encode(),'synthetic.invalid',fp))
  self.assertFalse(m.hostkey_matches(plain,'other.invalid',fp));self.assertFalse(m.hostkey_matches(plain,'synthetic.invalid','SHA256:'+'0'*43))
  self.assertFalse(m.hostkey_matches(plain+plain,'synthetic.invalid',fp))
 def test_cleanup_complete_positive_and_middelete_unknown_never_continues(self):
  # Testzweck: nur nach vollständigem Inventar löschen, ABSENT erst nach echtem zweiten Readback.
  base=CleanupTests();cleanup=base.module();scope=base.scope();scope['network']={};rows=base.rows()
  channel=mock.Mock();channel.writer_unknown=False;channel.own_pid=None
  with mock.patch.object(cleanup,'read_inventory',side_effect=[rows,([],[])]),mock.patch.object(cleanup,'check_network'):
   self.assertEqual(cleanup.cleanup(channel,scope,mock.Mock()),'ABSENT')
  commands=[call.args[0] for call in channel.call.call_args_list];self.assertEqual(len(commands),5)
  self.assertTrue(all(call.kwargs.get('cleanup') is True and call.kwargs.get('write') is True for call in channel.call.call_args_list))
  channel.reset_mock();channel.call.side_effect=RuntimeError('own mock stop')
  with mock.patch.object(cleanup,'read_inventory',return_value=rows),mock.patch.object(cleanup,'check_network'):
   self.assertEqual(cleanup.cleanup(channel,scope,mock.Mock()),'UNKNOWN')
  self.assertEqual(channel.call.call_count,1)
 def test_unknown_own_reader_handle_also_prevents_delete(self):
  # Testzweck: unbekannte eigene Prozessgruppe ist nicht durch eine Ressourcenprojektion heilbar.
  cleanup=CleanupTests().module();channel=mock.Mock();channel.writer_unknown=False;channel.own_pid=12345
  self.assertEqual(cleanup.cleanup(channel,{},mock.Mock()),'UNKNOWN');channel.call.assert_not_called()
class ActualAdmissionWiringTests(unittest.TestCase):
 def test_actual_original_prefix_roundtrip_missing_gateway_and_foreign_count_only(self):
  # Testzweck: produktive Channelverdrahtung mit Fakepipe, Gateway nicht erfinden und fremde IDs nicht inspizieren.
  channel,base,modules=ChannelTests().channel();net=dict(id='a'*64,cidr='172.30.77.0/24',labels={'flowzer.tt.demo.owner':'synthetic-owner'});channel.manifest['network']=net
  peers=[None];commands=[];facts=AdmissionTests().facts()
  def reply(argv,payload,secrets,**kw):
   if argv[-1]==m.HOST_COMMAND:value={k:v for k,v in facts.items() if k in ['identityMatches','uid','architecture','cpuCount','memAvailableBytes','freeDockerBytes','load1','neutralPathAbsent']}
   else:
    args=modules['manual_pg_adapter'].remote_docker_arguments(argv[-1]);commands.append(args)
    if args[0]=='version':value={k:v for k,v in facts.items() if k.startswith('client') or k.startswith('server')}
    elif args[0]=='ps' or args[:2]==['volume','ls']:return modules['manual_transport'].PipeResult(0,b'',b'')
    elif args[:2]==['network','inspect']:value=dict(id=net['id'],driver='bridge',internal=False,subnets=[net['cidr'],None],labels=net['labels'],peers=peers)
    else:raise AssertionError('no foreign/detail/other access')
   return modules['manual_transport'].PipeResult(0,json.dumps(value).encode(),b'')
  base.exchange.side_effect=reply;m.admit_host(channel.admission())
  peers[:]=['f'*64,None]
  with self.assertRaises(m.Stop):m.admit_host(channel.admission())
  self.assertFalse(any('f'*64 in arg for args in commands for arg in args));self.assertFalse(any(args[:2]==['container','inspect'] for args in commands))
 def test_actual_wire_enoent_and_host_identity_before_metadata_reads(self):
  # Testzweck: wirklich kompilierte Wirebytes, stummer Stop bei Metadatenfehler/anderem Host vor jedem Read.
  import builtins,contextlib,io,errno,platform,socket,types,os
  wire=m.host_wire('synthetic','192.0.2.8')
  def fopen(path,*a,**kw):
   if path=='/proc/meminfo':return io.StringIO('MemAvailable: 6291456 kB\n')
   if path=='/proc/loadavg':return io.StringIO('0.1 0 0 1/1 1\n')
   raise AssertionError('unexpected file')
  with mock.patch.object(os,'getuid',return_value=0),mock.patch.object(os,'geteuid',return_value=0),mock.patch.object(os,'cpu_count',return_value=4),mock.patch.object(platform,'machine',return_value='aarch64'),mock.patch.object(socket,'gethostname',return_value='synthetic'),mock.patch.dict(os.environ,{'SSH_CONNECTION':'1 2 192.0.2.8 22'},clear=True),mock.patch.object(os,'statvfs',return_value=types.SimpleNamespace(f_bavail=50*1024**3,f_frsize=1)),mock.patch.object(builtins,'open',side_effect=fopen):
   for error,expected in [(FileNotFoundError(errno.ENOENT,'missing'),True),(PermissionError(errno.EACCES,'denied'),False)]:
    out=io.StringIO()
    with mock.patch.object(os,'lstat',side_effect=error),contextlib.redirect_stdout(out):exec(compile(wire,'own-wire','exec'),{})
    self.assertIs(json.loads(out.getvalue())['identityMatches'],expected)
   out=io.StringIO()
   with mock.patch.dict(os.environ,{'SSH_CONNECTION':'1 2 192.0.2.9 22'}),mock.patch.object(os,'lstat') as metadata,contextlib.redirect_stdout(out):exec(compile(wire,'own-wire','exec'),{})
   self.assertFalse(json.loads(out.getvalue())['identityMatches']);metadata.assert_not_called()
class ConcreteLeaseEntryTests(unittest.TestCase):
 def test_source_only_root_data_rejected_before_any_project_exec_or_live_channel(self):
  # Testzweck: vorhandene Parent-/Source-only Dateien sind auch am echten Entry kein Root-Ausführungs-Go.
  import hashlib,types
  manifest,p=PermitAndWireTests().fixture();p['executionAllowed']=False
  with tempfile.TemporaryDirectory(prefix='fz-own-rootdata-') as td:
   d=Path(td);mp=d/'manifest';mp.write_text(json.dumps(manifest));mh=hashlib.sha256(mp.read_bytes()).hexdigest();p['manifestSHA256']=mh
   rp=d/'source-only';rp.write_text(json.dumps(p));rh=hashlib.sha256(rp.read_bytes()).hexdigest();args=types.SimpleNamespace(permit=str(rp),permit_sha256=rh,manifest=str(mp),manifest_sha256=mh)
   with mock.patch.object(m,'load_raw_modules') as loader,mock.patch.object(m,'Channel') as channel,self.assertRaises(m.Stop):m.execute(args)
   loader.assert_not_called();channel.assert_not_called()
class LocalFormatterTests(unittest.TestCase):
 def test_actual_local_cli_new_image_and_cleanup_formats_own_unix_only(self):
  # Testzweck: echte Go-Formatter/JSONfelder, ausschließlich eigener AF_UNIX-Fake; keine Docker27-/Hostattestierung.
  import hashlib,http.server,socketserver,threading,subprocess,os,urllib.parse
  cli=Path('/usr/local/bin/docker')
  if not cli.exists() or hashlib.sha256(cli.resolve().read_bytes()).hexdigest()!='31ae2d64e51d9dfc728a2934941615cdd92f31279ed6d5c1fb2bfc0a44637a8f':self.skipTest('qualified local CLI29 formatter not available; not Docker27 proof')
  cleanup=CleanupTests().module();fixture=CleanupTests();scope=fixture.scope();containers,volumes=fixture.rows();paths=[]
  class Server(socketserver.UnixStreamServer):pass
  class Handler(http.server.BaseHTTPRequestHandler):
   def log_message(self,*a):pass
   def do_HEAD(self):self.reply(True)
   def do_GET(self):self.reply(False)
   def reply(self,head):
    path=urllib.parse.unquote(urllib.parse.urlsplit(self.path).path).removeprefix('/v1.47');paths.append(path)
    if path=='/_ping':value=b'OK'
    elif path=='/version':value=dict(Version='27.5.1',ApiVersion='1.47',MinAPIVersion='1.24',Os='linux',Arch='arm64',Components=[])
    elif path.startswith('/images/'):
     role='api' if m.API_ID in path else 'pg';f=AdmissionTests().image(role);value=dict(Id=f['id'],Os='linux',Architecture='arm64',Config=dict(Labels=f['labels'],Env=f['env'],Volumes=f['volumes']))
    elif path.startswith('/containers/'):
     row=containers[0];value=dict(Id=row['id'],Name=row['name'],Image=row['image'],Config=dict(Labels=row['labels']),State=dict(Running=True))
    elif path.startswith('/volumes/'):
     row=volumes[0];value=dict(Name=row['name'],Driver=row['driver'],Labels=row['labels'])
    elif path.startswith('/networks/'):
     value=dict(Id='a'*64,Driver='bridge',Internal=False,IPAM=dict(Config=[dict(Subnet='172.30.77.0/24')]),Labels={'owner':'synthetic'},Containers={})
    else:self.send_error(404);return
    raw=value if type(value) is bytes else json.dumps(value).encode();self.send_response(200);self.send_header('API-Version','1.47');self.send_header('Content-Length',str(len(raw)));self.end_headers()
    if not head:self.wfile.write(raw)
  with tempfile.TemporaryDirectory(prefix='fz-afmt-') as td:
   socket=str(Path(td)/'s');server=Server(socket,Handler);os.chmod(socket,0o600);thread=threading.Thread(target=server.serve_forever,daemon=False);thread.start()
   try:
    def parsed(args):
     r=subprocess.run([str(cli),'--config',td+'/absent','--host','unix://'+socket,*args],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=5,env={'PATH':td+'/absent','HOME':td+'/absent','DOCKER_CONFIG':td+'/absent','LC_ALL':'C'})
     self.assertEqual(r.returncode,0);self.assertFalse(r.stderr);return json.loads(r.stdout)
    api=parsed(['image','inspect',m.API_ID,'--format',m.IMAGE_FORMAT]);pg=parsed(['image','inspect',m.PG_ID,'--format',m.IMAGE_FORMAT]);m.admit_images(api,pg)
    self.assertEqual(parsed(['container','inspect','b'*64,'--format',cleanup.CONTAINER_FORMAT]),containers[0])
    self.assertEqual(parsed(['volume','inspect',volumes[0]['name'],'--format',cleanup.VOLUME_FORMAT]),volumes[0])
    net=parsed(['network','inspect','a'*64,'--format',cleanup.NETWORK_FORMAT]);self.assertEqual(net['subnets'],['172.30.77.0/24',None]);self.assertNotIn('gateway',net)
   finally:server.shutdown();server.server_close();thread.join(timeout=2);self.assertFalse(thread.is_alive())
class AuthorityEntryTests(unittest.TestCase):
 # Separater lokaler Fünf-Pfad-Runner bindet das private Testartifact explizit; kein Runtimeeingang.
 private_caller_path=None
 def test_self_issued_valid_permit_and_matching_cli_hashes_cannot_activate_public_entry(self):
  # Testzweck: fachlich gültige selbst ausgestellte Rootdaten sind keine externe Authority.
  import hashlib,datetime,types
  manifest,permit=PermitAndWireTests().fixture();now=datetime.datetime.now(datetime.timezone.utc)
  permit.update(issuedUTC=(now-datetime.timedelta(seconds=1)).isoformat(),expiresUTC=(now+datetime.timedelta(seconds=300)).isoformat(),cleanupUntilUTC=(now+datetime.timedelta(seconds=360)).isoformat())
  with tempfile.TemporaryDirectory() as td:
   root=Path(td);mp=root/'manifest.json';pp=root/'permit.json';mp.write_text(json.dumps(manifest));mh=hashlib.sha256(mp.read_bytes()).hexdigest()
   permit['manifestSHA256']=mh;pp.write_text(json.dumps(permit));ph=hashlib.sha256(pp.read_bytes()).hexdigest()
   m.validate_permit(permit,manifest,mh,now.timestamp())
   args=types.SimpleNamespace(permit=str(pp),permit_sha256=ph,manifest=str(mp),manifest_sha256=mh)
   with mock.patch.object(m,'Lease') as lease,mock.patch.object(m,'load_raw_modules') as loader,mock.patch.object(m,'Channel') as channel:
    with self.assertRaises(m.Stop):m.execute(args)
    with mock.patch.object(m.sys,'argv',['caller','--execute','--permit',str(pp),'--permit-sha256',ph,'--manifest',str(mp),'--manifest-sha256',mh]):self.assertEqual(m.main(),2)
    lease.assert_not_called();loader.assert_not_called();channel.assert_not_called()
 def test_private_not_issued_caller_has_no_cli_env_or_import_activation(self):
  # Testzweck: reale private Templatebytes scheitern vor Sourceimport/SSH auch bei gefälschtem CLI/ENV.
  import os,types
  if self.private_caller_path is None:self.skipTest('Private Rootcaller nicht Teil des öffentlichen Checkout; separater Bindingtest erforderlich')
  path=Path(self.private_caller_path);self.assertTrue(path.is_file())
  module=types.ModuleType('private_root_caller_test');module.__file__=str(path);exec(compile(path.read_bytes(),str(path),'exec'),module.__dict__)
  self.assertEqual(module.ROOT_GO_SHA256,'NOT_ISSUED')
  with mock.patch.dict(os.environ,{'ROOT_GO_SHA256':'a'*64,'ROOT_GO_PATH':'/tmp/self-issued'}),mock.patch.object(sys,'argv',['private','--execute','--permit','/tmp/self-issued']),mock.patch.object(module,'_read_pinned') as read,mock.patch.object(module,'_load_public') as load:
   self.assertEqual(module.main(),2)
   with self.assertRaises(module.AdmissionClosed):module.execute()
   read.assert_not_called();load.assert_not_called()

if __name__=='__main__':unittest.main()
