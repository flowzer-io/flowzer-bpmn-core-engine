#!/usr/bin/env python3
"""Einmaliger, manuell SHA-bestätigter GitHub-hosted Ressourcenrunner, KEIN Deploypfad.

Subprocess-Rohlogs werden verworfen. Docker liest nur Formatprojektionen, niemals
Env/Config-Rohdumps. Jede Ressource muss vor Messung/Cleanup doppelt gebunden sein.
"""
import argparse
from contextlib import contextmanager
from decimal import Decimal
from functools import wraps
import json
import http.client
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import time
from prepare import require,project_for,BUDGETS,IMAGES,prepare,stats_row
import proof

COLUMNS={'id':'.Id','project':'index .Config.Labels "com.docker.compose.project"',
    'owner':'index .Config.Labels "io.flowzer.runtime.owner"',
    'service':'index .Config.Labels "com.docker.compose.service"',
    'oneoff':'index .Config.Labels "com.docker.compose.oneoff"',
    'image':'.Config.Image','image_id':'.Image','memory':'.HostConfig.Memory',
    'swap':'.HostConfig.MemorySwap','nano_cpus':'.HostConfig.NanoCpus',
    'oom':'.State.OOMKilled','running':'.State.Running','exit_code':'.State.ExitCode','restarts':'.RestartCount'}
INSPECT='{'+','.join('"'+key+'":{{json '+expression+'}}' for key,expression in COLUMNS.items())+'}'

# Feste Diagnosewerte, niemals Befehle, Exceptiontexte oder fremde Antworten.
PHASES=frozenset({'preflight','freshness','prepare','browser_preflight',
    'browser_calibration_process','browser_calibration_report','docker_preflight',
    'pull','start','sampling','auth','verify','stop','cleanup'})


class ProcessExitError(ValueError):
    """Nur tatsächlich beobachteter POSIX-Exitstatus; fehlender Beleg bleibt null."""
    def __init__(self,code):
        super().__init__('Eigener Unterprozess endete unerwartet.')
        self.exit_code=code if type(code) is int and -128<=code<=255 else None


class DeadlineExceededError(ValueError):
    """Unveränderte Gesamtzeitgrenze, aber als Timeout statt fachliche Validierung."""


def failure_projection(phase,error):
    """Geschlossener Fehlervertrag ohne str/repr, args, Rohdaten oder erfundene Null."""
    require(phase in PHASES)
    code=('process_exit' if isinstance(error,ProcessExitError) else
        'timeout' if isinstance(error,(subprocess.TimeoutExpired,DeadlineExceededError)) else
        'interrupted' if isinstance(error,(InterruptedError,KeyboardInterrupt)) else
        'validation' if isinstance(error,ValueError) else
        'io' if isinstance(error,OSError) else 'unknown')
    return dict(phase=phase,error=code,exit_code=error.exit_code if isinstance(error,ProcessExitError) else None)


def diagnosed(phase=None):
    """Innere Sampling-/Stopfehler behalten ihre Phase beim Weiterreichen nach außen."""
    def decorate(method):
        @wraps(method)
        def call(self,*args,**kwargs):
            with self.phase(phase or self.current_phase):
                return method(self,*args,**kwargs)
        return call
    return decorate


def environment_guard(context,env):
    """Fail-closed vor jedem I/O; keine fremde Docker-/Browser-/Proxyverbindung akzeptieren."""
    project=project_for(context)
    denied=['DOCKER_HOST','DOCKER_CONTEXT','NODE_OPTIONS','HTTP_PROXY','HTTPS_PROXY','ALL_PROXY',
        'http_proxy','https_proxy','all_proxy','PW_TEST_CONNECT_WS_ENDPOINT','PW_TEST_CONNECT_HEADERS',
        'PW_TEST_REUSE_CONTEXT','PLAYWRIGHT_DISABLE_FORCED_CHROMIUM_PROXIED_LOOPBACK']
    require(not any(env.get(key) for key in denied));return project


def container_row(row,project,configs):
    """Exakte tatsächliche Image-/Limit-/Eigentümerbindung ohne Containeridentitäten im Report."""
    require(type(row) is dict and set(row)==set(COLUMNS))
    service=row['service'];require(service in BUDGETS)
    image=IMAGES['api' if service=='migrate' else service];mib,cpu=BUDGETS[service]
    require(row['project']==project and row['owner']==project and row['oneoff'] in ('False','True')
        and re.fullmatch('[0-9a-f]{64}',row['id']) is not None and row['image']==image
        and row['image_id']==configs[service])
    require(all(type(row[key]) is int for key in ['memory','swap','nano_cpus','exit_code','restarts'])
        and all(type(row[key]) is bool for key in ['oom','running'])
        and row['memory']==mib*1024**2 and row['swap']==row['memory']
        and row['nano_cpus']==int(Decimal(cpu)*10**9) and 0<=row['exit_code']<=255 and 0<=row['restarts']<=100)
    return dict(service=service,memory_limit_bytes=row['memory'],nano_cpus=row['nano_cpus'],
        oom=row['oom'],running=row['running'],exit_code=row['exit_code'],restarts=row['restarts'],oneoff=row['oneoff']=='True')


def resource_row(row,project,kind):
    """Netze/Volumes nur aus eigenem Namespace und mit eigener zusätzlicher Runlabelbindung."""
    fields={'name','project','owner'}|({'internal'} if kind=='network' else set())
    require(kind in ('network','volume') and type(row) is dict and set(row)==fields
        and row['project']==project and row['owner']==project)
    names=({'default'} if kind=='network' else {'ca-public','tls-material','db-data','keyring'})
    require(row['name'] in {project+'_'+name for name in names}
        and (kind!='network' or row['internal'] is True));return True


def auth_result(value,expected=None):
    """Nur vollständige Originaltestausführung; success ohne Zähler ist keine Abnahme."""
    keys={'total','passed','failed','skipped','interrupted','errors','success'}
    require(type(value) is dict and set(value)==keys and type(value['success']) is bool
        and all(type(value[key]) is int and 0<=value[key]<=10000 for key in keys-{'success'})
        and value['success'] and value['total']>0 and value['passed']==value['total']
        and all(value[key]==0 for key in ['failed','skipped','interrupted','errors'])
        and (expected is None or value['total']==expected));return value


def egress_result(value):
    """Nur exakt die eigene erfolgreiche numerische Netzprobe, keine Roh-/Fehlerfelder uploaden."""
    ones={'positive_control_requests','allowed_page','redirect_ip_blocked','redirect_host_blocked',
        'direct_ip_blocked','websocket_blocked','serviceworker_blocked'}
    zeros={'marker_requests','marker_assertion_red'}
    require(type(value) is dict and set(value)==ones|zeros|{'calibrated','success'}
        and value['calibrated'] is False and value['success'] is True
        and all(type(value[key]) is int and value[key]==1 for key in ones)
        and all(type(value[key]) is int and value[key]==0 for key in zeros));return value


def completed_container(row):
    """Leere/Null-Stats sind nur für tatsächlich beendeten erfolgreichen eigenen Besitz zulässig."""
    require(row['running'] is False and row['oom'] is False and row['exit_code']==0);return True


def directory_bytes(raw):
    """BusyBox-portables du -sk: allozierte gerundete KiB, keine exakte logische DBgröße."""
    require(re.fullmatch(r'[0-9]{1,11}\s+/var/lib/postgresql/data',raw) is not None)
    return int(raw.split()[0])*1024


def ram_usage(value,service):
    """Docker-Daemon/cgroup-Gesamtverbrauch inkl. Cache, nicht die bereinigte CLI-Schätzung."""
    row=value['memory_stats'];require(service in BUDGETS and type(row['usage']) is int
        and type(row['limit']) is int and row['limit']==BUDGETS[service][0]*1024**2
        and 0<=row['usage']<=row['limit']);return row['usage']


def empty_inventory(rows,count):
    require(not rows and count==0);return True


class DockerSocket(http.client.HTTPConnection):
    """Nur lokaler Standard-Hostedsocket, keine Remote-/Redirect-/Env-Transportwahl."""
    def connect(self):
        self.sock=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout);self.sock.connect('/var/run/docker.sock')


class Rig:
    """Eigener begrenzter Prozess-/Ressourcenbesitz für exakt einen Hosted-Erstlauf."""
    def __init__(self,source,publisher,context):
        self.context=context;self.project=environment_guard(context,os.environ)
        self.source=source.resolve();self.publisher=publisher.resolve();self.configs={}
        self.root=Path(os.environ['RUNNER_TEMP'])/self.project
        self.report={'success':False,'runtime_started':False,'cleanup_complete':False,
            'sample_count':0,'samples_are_discrete':True,'container_reads_are_sequential':True,
            'oom_observed':False,'peak_memory_bytes_approx':0,'per_service':{},'disk':{}}
        self.owned=False
        self.current_phase='preflight';self.failure_ids=set();self.report['failures']=[]
        # Childumgebung ist eine Whitelist: kein GH-/Cloud-/IdP-/Vaulttoken in Browser oder Containerstarts.
        self.env={key:os.environ[key] for key in ['PATH','LANG','LC_ALL'] if key in os.environ}
        self.env.update(HOME=str(self.root/'home'),DOCKER_CONFIG=str(self.root/'docker-config'),
            PLAYWRIGHT_BROWSERS_PATH=str(self.root/'browsers'),CI='true')
        self.compose=['docker','compose','-p',self.project,'-f',str(self.root/'rig/tests/installation-auth/compose.yml')]

    def note_failure(self,phase,error):
        """Erste Ursache plus höchstens Stop/Cleanup erhalten; nur Identitätsnummern im Speicher."""
        self.report['success']=False
        if id(error) not in self.failure_ids and len(self.report['failures'])<3:
            self.failure_ids.add(id(error));self.report['failures'].append(failure_projection(phase,error))

    @contextmanager
    def phase(self,phase):
        """Diagnose annotiert Fehler, verändert aber weder Reihenfolge noch Exception-/Cleanupvertrag."""
        require(phase in PHASES);previous=self.current_phase;self.current_phase=phase
        try:yield
        except BaseException as error:
            self.note_failure(phase,error);raise
        finally:self.current_phase=previous

    @diagnosed('stop')
    def stop_process(self,process):
        """Unverändert ausschließlich eigene Prozessgruppe: TERM, fünf Sekunden, dann KILL."""
        os.killpg(process.pid,signal.SIGTERM)
        try:process.wait(timeout=5)
        except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL);process.wait(timeout=5)

    @diagnosed()
    def command(self,args,timeout=30,capture=True,env=None,cwd=None,accepted=0):
        """Nur eigener POSIX-Prozessbaum; Timeout beendet ausschließlich diesen eigenen Baum."""
        process=subprocess.Popen(args,env=env or self.env,cwd=cwd,stdout=subprocess.PIPE if capture else subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,start_new_session=True)
        try:stdout,_=process.communicate(timeout=timeout)
        except BaseException as error:
            self.note_failure(self.current_phase,error)
            self.stop_process(process)
            raise
        if process.returncode!=accepted:raise ProcessExitError(process.returncode)
        require(not capture or len(stdout)<=1048576)
        return stdout.decode() if capture else ''

    def audited_container(self,row):
        """Eigenen OOM monoton merken, bevor spätere Prüfungen abbrechen; Fremd-/Rohdaten niemals übernehmen."""
        state=container_row(row,self.project,self.configs)
        self.report['oom_observed']=self.report['oom_observed'] or state['oom']
        return state

    def inventory(self):
        """Nur projektgefilterte IDs/Formate, kein allgemeines inspect mit Secret-Env."""
        ids=self.command(['docker','ps','-aq','--no-trunc','--filter','label=com.docker.compose.project='+self.project]).split()
        require(len(ids)<=16 and len(set(ids))==len(ids) and all(re.fullmatch('[0-9a-f]{64}',cid) for cid in ids))
        rows=[]
        for cid in ids:
            row=proof.decode(self.command(['docker','inspect','--format',INSPECT,cid]).encode())
            self.audited_container(row);rows.append(row)
        self.resource_count=0
        for kind in ['network','volume']:
            fmt='{{.ID}}' if kind=='network' else '{{.Name}}'
            values=self.command(['docker',kind,'ls','--filter','label=com.docker.compose.project='+self.project,'--format',fmt]).split()
            require(len(values)<=4);self.resource_count+=len(values)
            for value in values:
                format_json='{"name":{{json .Name}},"project":{{json (index .Labels "com.docker.compose.project")}},"owner":{{json (index .Labels "io.flowzer.runtime.owner")}}'
                if kind=='network':format_json+=',"internal":{{json .Internal}}'
                format_json+='}'
                row=proof.decode(self.command(['docker',kind,'inspect','--format',format_json,value]).encode())
                resource_row(row,self.project,kind)
        return rows

    @diagnosed('sampling')
    def sample(self):
        rows=self.inventory();running={row['id']:row['service'] for row in rows if row['running']}
        # OOM ist ein echter Befund; nicht als geringer RAM-Bedarf oder erfolgreiche Nullprobe werten.
        for row in rows:self.audited_container(row)
        require(not self.report['oom_observed'])
        if not running:return
        raw=self.command(['docker','stats','--no-stream','--no-trunc','--format',
            '{{.ID}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.PIDs}}',*running],timeout=15)
        measured={};total=0;seen=set();finished=set()
        for line in raw.splitlines():
            cid,cpu,memory,pids=line.split('\t');require(cid in running and cid not in seen);seen.add(cid)
            if memory=='0B / 0B':
                state=proof.decode(self.command(['docker','inspect','--format',INSPECT,cid]).encode())
                self.audited_container(state);completed_container(state);finished.add(cid);continue
            row=stats_row(running[cid],cpu,memory,pids);total+=row['memory_bytes_approx']
            values=measured.setdefault(row['service'],{'memory_bytes_approx':0,'cpu_percent':0.0,'pids':0})
            for key in values:values[key]+=row[key]
        for cid in set(running)-seen:
            # Eine Oneoff kann zwischen den Momentaufnahmen enden; kein erfundener Nullverbrauch.
            state=proof.decode(self.command(['docker','inspect','--format',INSPECT,cid]).encode())
            self.audited_container(state)
            completed_container(state)
        total_ram=0
        for cid in seen-finished:
            connection=DockerSocket('localhost',timeout=5)
            try:
                connection.request('GET','/containers/'+cid+'/stats?stream=false')
                response=connection.getresponse();require(response.status==200)
                body=response.read(1048577);require(len(body)<=1048576)
                value=proof.decode(body)
            finally:connection.close()
            if not value.get('memory_stats') or (value['memory_stats'].get('usage')==0 and value['memory_stats'].get('limit')==0):
                state=proof.decode(self.command(['docker','inspect','--format',INSPECT,cid]).encode())
                self.audited_container(state)
                completed_container(state);continue
            usage=ram_usage(value,running[cid]);total_ram+=usage
            values=measured[running[cid]];values['memory_bytes']=values.get('memory_bytes',0)+usage
        self.report['peak_memory_bytes']=max(self.report.get('peak_memory_bytes',0),total_ram)
        self.report['sample_count']+=1
        self.report['peak_memory_bytes_approx']=max(self.report['peak_memory_bytes_approx'],total)
        for service,values in measured.items():
            peak=self.report['per_service'].setdefault(service,{})
            for key in values:peak[key]=max(peak.get(key,0),values[key])

    @diagnosed()
    def measured_command(self,args,timeout,cwd=None):
        """Startup und echte Originalspecs gleichzeitig diskret messen; keine synthetischen RAMwerte."""
        process=subprocess.Popen(args,env=self.env,cwd=cwd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
        try:
            deadline=time.monotonic()+timeout
            while process.poll() is None:
                if not time.monotonic()<deadline:
                    raise DeadlineExceededError('Eigene Gesamtzeitgrenze überschritten.')
                with self.phase('sampling'):self.sample()
                time.sleep(1)
            if process.returncode!=0:raise ProcessExitError(process.returncode)
            with self.phase('sampling'):self.sample()
        except BaseException as error:
            self.note_failure(self.current_phase,error);raise
        finally:
            if process.poll() is None:
                self.stop_process(process)

    def disk(self,key):
        usage=shutil.disk_usage('/var/lib/docker')
        self.report['disk'][key]={'host_used_bytes':usage.used,'host_free_bytes':usage.free}

    def pull(self):
        """GHCR-Token ausschließlich über Credentialhelper-Pipe; niemals Dockerlogin/Authsdatei."""
        config=self.root/'docker-config';config.mkdir()
        helper=self.root/'bin';helper.mkdir()
        path=helper/'docker-credential-flowzer-runtime'
        path.write_text('#!/usr/bin/env python3\nimport json,os,sys\n'
            'if sys.argv[1:]!=["get"] or sys.stdin.read().strip()!="ghcr.io":sys.exit(1)\n'
            'token=os.environ.get("GH_TOKEN","")\nif not token:sys.exit(1)\n'
            'sys.stdout.write(json.dumps({"Username":"x-access-token","Secret":token}))\n')
        path.chmod(0o700);(config/'config.json').write_text('{"credHelpers":{"ghcr.io":"flowzer-runtime"}}')
        try:
            env=self.env|{'PATH':str(helper)+os.pathsep+self.env['PATH'],'GH_TOKEN':os.environ['GH_TOKEN']}
            for image in dict.fromkeys(IMAGES.values()):
                self.command(['docker','pull','--platform','linux/amd64',image],timeout=300,capture=False,env=env)
            for service,image in IMAGES.items():
                value=self.command(['docker','image','inspect','--format','{{.Id}}\t{{.Os}}\t{{.Architecture}}',image]).strip().split('\t')
                require(value==[self.configs[service],'linux','amd64'])
        finally:
            # Nur dieser nichtgeheime helper/config ist unser Besitz. Kein gespeicherter Login existiert.
            (config/'config.json').write_text('{}');path.unlink();helper.rmdir()

    @diagnosed('browser_preflight')
    def browser_preflight(self,auth):
        """Echte Gegenkalibrierung + Netzgrenzen + tatsächliche Fixture vor jedem Containerstart."""
        cli=auth/'node_modules/playwright/cli.js';self.env['NODE_PATH']=str(auth/'node_modules')
        self.command(['npm','ci','--ignore-scripts','--no-audit','--no-fund'],timeout=300,capture=False,cwd=auth)
        self.command(['node',str(cli),'install','--with-deps','chromium'],timeout=600,capture=False,cwd=auth)
        for name in ['calibration','egress','fixture']:(self.root/name).mkdir()
        # Kalibrierung muss am echten Markerassert scheitern, nie als Browser-/Setupfehler akzeptieren.
        calibration=self.root/'calibration/egress-result.json'
        # Exit1 allein belegt keinen Marker-RED: Prozess-/CWD-I/O und fehlender Bericht bleiben unterscheidbar.
        # Die Unterphasen ändern nur die geschlossene Diagnose, nie Statusannahme, Schutz oder Folgeaktionen.
        with self.phase('browser_calibration_process'):
            self.command(['node',str(self.source/'scripts/demo-runtime/egress-probe.js'),str(calibration),'--calibrate-local-only'],
                timeout=60,capture=False,accepted=1)
        with self.phase('browser_calibration_report'):
            value=proof.decode(calibration.read_bytes())
            require(value['marker_assertion_red']==1 and value['marker_requests']==1
                and value['positive_control_requests']==1 and value['allowed_page']==1 and not value['success'])
        self.command(['node',str(self.source/'scripts/demo-runtime/egress-probe.js'),str(self.root/'egress/egress-result.json')],timeout=60,capture=False)
        value=proof.decode((self.root/'egress/egress-result.json').read_bytes())
        egress_result(value)
        self.env['FLOWZER_RUNTIME_REPORT']=str(self.root/'fixture/auth-result.json')
        self.command(['node',str(cli),'test','--config',str(self.source/'scripts/demo-runtime/fixture-probe.config.js')],timeout=90,capture=False)
        self.report['fixture']=auth_result(proof.decode((self.root/'fixture/auth-result.json').read_bytes()),2)
        self.report['egress']=value;self.report['calibration_marker_assertion_red']=1

    def run(self):
        """Ein beschränkter Authpilot; weder Backup/Restore noch Demo-/45min-Abnahme werden behauptet."""
        require(os.name=='posix' and os.uname().sysname=='Linux' and os.uname().machine=='x86_64')
        require(self.source.is_dir() and not self.root.exists() and self.root.parent.is_absolute())
        require(self.command(['git','-C',str(self.source),'rev-parse','HEAD']).strip()==self.context['sha'])
        tree=self.command(['git','-C',str(self.source),'rev-parse','HEAD^{tree}']).strip()
        require(re.fullmatch('[0-9a-f]{40}',tree) is not None)
        # Frische Belege zuerst, noch vor Package-/Browser-/Dockerzugriffen.
        with self.phase('freshness'):
            attestation=proof.attest(self.context,self.publisher);self.configs=attestation['image_configs']
        self.root.mkdir(mode=0o700);(self.root/'home').mkdir();(self.root/'report').mkdir()
        self.report.update(source_proof=attestation,workflow_tree=tree)
        try:
            with self.phase('prepare'):prepare(self.source,self.root/'rig',self.context)
            auth=self.root/'rig/tests/installation-auth'
            self.browser_preflight(auth)
            with self.phase('docker_preflight'):
                require(self.command(['docker','context','inspect','--format','{{.Endpoints.docker.Host}}']).strip()=='unix:///var/run/docker.sock')
                require(self.command(['docker','info','--format','{{.OSType}}\t{{.Architecture}}\t{{.DockerRootDir}}']).strip()
                    in ['linux\tx86_64\t/var/lib/docker','linux\tamd64\t/var/lib/docker'])
                empty_inventory(self.inventory(),self.resource_count);self.owned=True
            with self.phase('pull'):
                self.disk('before_pull');self.pull();self.disk('after_pull')
            self.report['runtime_started']=True
            with self.phase('start'):
                self.measured_command(self.compose+['up','-d','--no-build','--pull','never','--wait','--wait-timeout','600'],timeout=660)
                rows=self.inventory();require(len(rows)==7 and {row['service'] for row in rows}==set(BUDGETS))
                self.disk('after_start');self.env['FLOWZER_RUNTIME_REPORT']=str(self.root/'auth-result.json')
            with self.phase('auth'):
                self.measured_command(['node',str(auth/'node_modules/playwright/cli.js'),'test','--config',str(auth/'playwright.config.js')],timeout=1500,cwd=auth)
                self.report['auth']=auth_result(proof.decode((self.root/'auth-result.json').read_bytes()),16)
            with self.phase('verify'):
                rows=self.inventory();db=next(row for row in rows if row['service']=='db' and row['oneoff']=='False')
                raw=self.command(['docker','exec',db['id'],'du','-sk','/var/lib/postgresql/data']).strip()
                self.report['database_directory_allocated_bytes_approx']=directory_bytes(raw);self.disk('after_tests')
                states=[self.audited_container(row) for row in rows]
                require(not any(row['oom'] for row in states) and all(row['exit_code']==0 for row in states))
                self.report['final_states']=states;require(self.report['sample_count']>0);self.report['success']=True
        finally:
            try:
                with self.phase('cleanup'):
                    if self.owned:
                        # Prüfen, bevor down neu entstandene fremde Ressourcen gleichen Namens entfernen könnte.
                        rows=self.inventory()
                        # Nur zuvor vollständig gebundene eigene Check-config-Oneoffs, keine Namen-/Fremdsuche.
                        oneoffs=[row['id'] for row in rows if row['oneoff']=='True']
                        if oneoffs:self.command(['docker','rm','--force',*oneoffs],timeout=30,capture=False)
                        self.command(self.compose+['down','--volumes','--remove-orphans'],timeout=120,capture=False)
                        empty_inventory(self.inventory(),self.resource_count)
                        self.report['cleanup_complete']=True;self.disk('after_cleanup')
            finally:
                # Auch Cleanupfehler müssen als solche sichtbar bleiben, niemals still „success“ melden.
                with self.phase('cleanup'):
                    self.report['success']=self.report['success'] and self.report['cleanup_complete'] \
                        and not self.report['oom_observed'] and not self.report['failures']
                    output=self.root/'report/resource-result.json'
                    raw=json.dumps(self.report,sort_keys=True).encode();require(len(raw)<=65536)
                    with output.open('xb') as stream:
                        # Kein Erfolgsartefakt, bevor der eigene Bericht sicher berechtigt ist.
                        output.chmod(0o600);stream.write(raw+b'\n')



def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',required=True,type=Path)
    parser.add_argument('--publisher',required=True,type=Path);args=parser.parse_args()
    names={'repository':'GITHUB_REPOSITORY','ref':'GITHUB_REF','event':'GITHUB_EVENT_NAME','attempt':'GITHUB_RUN_ATTEMPT',
        'run_id':'GITHUB_RUN_ID','sha':'GITHUB_SHA','confirmed_sha':'CONFIRMED_WORKFLOW_SHA',
        'runner_environment':'RUNNER_ENVIRONMENT','runner_os':'RUNNER_OS','runner_arch':'RUNNER_ARCH'}
    rig=None
    try:
        context={key:os.environ[name] for key,name in names.items()}
        # Actions schickt SIGINT/SIGTERM beim Abbruch: finally muss noch ausschließlich eigenen Besitz schließen.
        def interrupted(_signal,_frame):raise InterruptedError('Eigener Lauf abgebrochen.')
        signal.signal(signal.SIGTERM,interrupted);signal.signal(signal.SIGINT,interrupted)
        rig=Rig(args.source,args.publisher,context);rig.run();require(rig.report['success'])
        print('Isolierter Ressourcenpilot erfolgreich; keine Installation.')
    except BaseException as error:
        failures=rig.report['failures'] if rig is not None else []
        if not failures:failures=[failure_projection('preflight',error)]
        parser.exit(1,'Isolierter Ressourcenpilot fehlgeschlagen; kein Installations-Go.\n'
            +json.dumps({'failures':failures},sort_keys=True)+'\n')

if __name__=='__main__':main()
