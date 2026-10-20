"""Testzweck: echter vorhandener Docker-Formatter, ausschließlich eigener AF_UNIX-Fake.

Kein realer Daemon/Container/Imagepull. Der lokale CLIstand ist kein Docker27-
Hostbinarybeleg; alle neuen ausgewählten Go-Felder werden tatsächlich gerendert.
"""
import http.server,socketserver,threading,tempfile,os,shutil,subprocess,json,unittest,importlib.util,sys
from pathlib import Path
from urllib.parse import urlsplit
ROOT=Path(__file__).resolve().parents[2];D=ROOT/'deploy/tickytask-demo-coolify/supervised-operator'
for name in ['manual_postgres','manual_transport','manual_pg_adapter','test_manual_pg_adapter']:
 s=importlib.util.spec_from_file_location(name,D/(name+'.py'));m=importlib.util.module_from_spec(s);sys.modules[name]=m;s.loader.exec_module(m)
a=sys.modules['manual_pg_adapter'];subjects=sys.modules['test_manual_pg_adapter']
class Fake(socketserver.UnixStreamServer):pass
class Handler(http.server.BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def do_HEAD(self):self.respond(True)
 def do_GET(self):self.respond(False)
 def respond(self,head):
  path=urlsplit(self.path).path.removeprefix('/v1.47');s=self.server;value=None
  if path=='/_ping':value=b'OK'
  elif path.startswith('/images/'):
   value=dict(Id='sha256:'+'a'*64,Os='linux',Architecture='arm64')
  elif path=='/volumes':value=dict(Volumes=[],Warnings=[])
  elif path.startswith('/volumes/'):
   name=path.rsplit('/',1)[-1];value=dict(Name=name,Driver='local',Labels=s.backend.labels[name])
  elif path=='/containers/json':value=[]
  elif path.startswith('/containers/'):
   run=next(v[0] for v in reversed(s.backend.calls) if v[0][:2]==['container','run'] and '--detach' in v[0])
   labs=dict(v.split('=',1) for i,v in enumerate(run) if i and run[i-1]=='--label')
   mounts=[dict(Name=v.split('source=',1)[1].split(',',1)[0],Type='volume',Destination=v.split('target=',1)[1],RW=True) for i,v in enumerate(run) if i and run[i-1]=='--mount']
   value=dict(Id='b'*64,Image='sha256:'+'a'*64,Config=dict(Labels=labs,User='999:999'),State=dict(Running=True),
     HostConfig=dict(NetworkMode='none',LogConfig=dict(Type='none'),RestartPolicy=dict(Name='no')),Mounts=mounts)
  else:self.send_error(404);return
  raw=value if type(value) is bytes else json.dumps(value).encode()
  self.send_response(200);self.send_header('API-Version','1.47');self.send_header('Content-Length',str(len(raw)));self.end_headers()
  if not head:self.wfile.write(raw)
class Formats(unittest.TestCase):
 def test_all_adapter_read_templates_real_render_and_parse(self):
  # Testzweck: keine nur gegenseitig bestätigenden Python-Dictfakes für neue Templates.
  docker=shutil.which('docker');self.assertIsNotNone(docker)
  backend=subjects.Pipe();native=backend.exchange
  with tempfile.TemporaryDirectory(dir='/tmp',prefix='fz-pg-fmt-') as temp:
   sock=str(Path(temp)/'api.sock');server=Fake(sock,Handler);server.backend=backend;os.chmod(sock,0o600)
   worker=threading.Thread(target=server.serve_forever,daemon=False);worker.start();seen=[]
   class Pipe:
    def exchange(_,argv,payload,secrets,**kw):
     import shlex
     args=shlex.split(argv[-1])[3:]
     if '--format' not in args:return native(argv,payload,secrets,**kw)
     seen.append(args[:2]);r=subprocess.run([docker,'--host','unix://'+sock,*args],stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=5,
       env={'PATH':'/usr/bin:/bin','HOME':'/nonexistent','DOCKER_CONFIG':'/nonexistent','DOCKER_API_VERSION':'1.47','LC_ALL':'C'})
     self.assertEqual(r.returncode,0);self.assertFalse(r.stderr)
     return sys.modules['manual_transport'].PipeResult(r.returncode,r.stdout,r.stderr)
   try:
    b=a.PgBinding('syntheticflowzer123456','12345678-1234-1234-1234-123456789abc','c'*32,'sha256:'+'a'*64,999,999,1654,1654,'d'*40,'12345')
    port=a.PgBootstrapAdapter(b,a.SshTarget('192.0.2.8','synthetic.invalid','/synthetic/known_hosts'),lambda:None,Pipe())
    scripts=ROOT/'deploy/postgresql'
    port.initialize(admin='synthetic-admin-123',migration='synthetic-migration-123',runtime='synthetic-runtime-123',
       sql01=(scripts/'01-datenbank-und-rollen.sql').read_bytes(),sql02=(scripts/'02-laufzeitrechte.sql').read_bytes())
    port.pg_start();port.bootstrap();self.assertEqual(port.state,'BOOTSTRAPPED')
    for pair in [['image','inspect'],['volume','inspect'],['container','inspect'],['volume','ls'],['ps','-a']]:self.assertIn(pair,seen)
   finally:server.shutdown();server.server_close();worker.join(2);self.assertFalse(worker.is_alive())
if __name__=='__main__':unittest.main()
