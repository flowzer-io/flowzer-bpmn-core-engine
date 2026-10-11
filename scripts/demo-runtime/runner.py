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
import ipaddress
import select
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
# Go-Templatefunktionen erhalten Ausdrücke als ein geklammertes Argument:
# json (index ...) wertet das Label aus, statt index als niladisches Argument aufzurufen.
# Dieselbe geschlossene Feldliste bleibt für Sampling UND Besitzprüfung vor Cleanup maßgeblich.
INSPECT='{'+','.join('"'+key+'":{{json ('+expression+')}}' for key,expression in COLUMNS.items())+'}'

# Feste Diagnosewerte, niemals Befehle, Exceptiontexte oder fremde Antworten.
PHASES=frozenset({'preflight','freshness','prepare','browser_preflight',
    'browser_calibration_process','browser_calibration_report','docker_preflight',
    'pull','start','sampling','auth','auth_report','discovery_report','loopback_report','relay_start','relay_monitor','relay_close','relay_report','verify','stop','cleanup'} |
    {'browser_probe_'+value for value in ['modules','contract','certificate','fixture','browser','allowed',
        'redirect_ip_blocked','redirect_host_blocked','direct_ip','websocket','serviceworker',
        'context_close','browser_close','proxy_close','allowed_close','marker_close']})


class ProcessExitError(ValueError):
    """Nur tatsächlich beobachteter POSIX-Exitstatus; fehlender Beleg bleibt null."""
    def __init__(self,code):
        super().__init__('Eigener Unterprozess endete unerwartet.')
        self.exit_code=code if type(code) is int and -128<=code<=255 else None


class MeasuredProcessExitError(ProcessExitError):
    """Nur der beobachtete returncode des eigenen measured_command-Kindprozesses.

    Sampling-/Stop-Kommandos werfen weiterhin ProcessExitError ohne diese Herkunft.
    Exitprojektion und Statusprüfung sind identisch; der Typ trennt nur Diagnosebesitz.
    """


class BrowserProbeError(ValueError):
    """Nur bereits validierte eigene Stage-/Fehler-/Exitwerte, niemals fremde Roh-Ausnahmen."""
    def __init__(self,code,exit_code):
        super().__init__('Eigene Browserprobe fehlgeschlagen.');self.code=code;self.exit_code=exit_code


class DeadlineExceededError(ValueError):
    """Unveränderte Gesamtzeitgrenze, aber als Timeout statt fachliche Validierung."""


def failure_projection(phase,error):
    """Geschlossener Fehlervertrag ohne str/repr, args, Rohdaten oder erfundene Null."""
    require(phase in PHASES)
    code=(error.code if isinstance(error,BrowserProbeError) else
        'process_exit' if isinstance(error,ProcessExitError) else
        'timeout' if isinstance(error,(subprocess.TimeoutExpired,DeadlineExceededError)) else
        'interrupted' if isinstance(error,(InterruptedError,KeyboardInterrupt)) else
        'validation' if isinstance(error,ValueError) else
        'io' if isinstance(error,OSError) else 'unknown')
    return dict(phase=phase,error=code,exit_code=error.exit_code if isinstance(error,(ProcessExitError,BrowserProbeError)) else None)


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


def relay_container_inspect(project):
    """Nur eigener eng formatierter TLS-Endpoint; Adressen ausschließlich im RAM."""
    require(type(project) is str and re.fullmatch('flowzer-runtime-[1-9][0-9]{0,19}-a1',project) is not None)
    endpoint='index .NetworkSettings.Networks "'+project+'_default"'
    return INSPECT[:-1]+',"network_count":{{json (len .NetworkSettings.Networks)}}'+''.join(
        ',"'+key+'":{{with ('+endpoint+')}}{{json .'+field+'}}{{else}}null{{end}}'
        for key,field in [('network_id','NetworkID'),('network_ipv4','IPAddress'),('endpoint_id','EndpointID')])+'}'


def relay_network_inspect(cid):
    """Nur eigenes Netz und exakt dessen zuvor gebundener TLS-CID, kein Containerdump."""
    require(type(cid) is str and re.fullmatch('[0-9a-f]{64}',cid) is not None)
    columns={'name':'.Name','project':'index .Labels "com.docker.compose.project"',
        'owner':'index .Labels "io.flowzer.runtime.owner"','internal':'.Internal',
        'id':'.Id','driver':'.Driver'}
    return '{'+','.join('"'+k+'":{{json ('+v+')}}' for k,v in columns.items())         + ',"subnet":{{if eq (len .IPAM.Config) 1}}{{json (index .IPAM.Config 0).Subnet}}{{else}}null{{end}}'         + ''.join(',"'+key+'":{{with (index .Containers "'+cid+'")}}{{json .'+field+'}}{{else}}null{{end}}'
            for key,field in [('container_ipv4','IPv4Address'),('endpoint_id','EndpointID')])+'}'


def relay_result(value):
    """Eigene echte Relay-Ressourcen separat ausweisen; kein impliziter Null-/Containerbeleg."""
    limits=dict(address_space_limit_bytes=64*1024**2,cpu_limit_seconds=30,fd_limit=32,
        max_pairs=8,buffer_bytes_per_direction=65536,lifetime_seconds=1500)
    require(type(value) is dict and set(value)==set(limits)|{'closed','peak_rss_bytes','cpu_millis'}
        and value['closed'] is True and all(type(value[k]) is int and value[k]==v for k,v in limits.items())
        and type(value['peak_rss_bytes']) is int and 0<value['peak_rss_bytes']<=limits['address_space_limit_bytes']
        and type(value['cpu_millis']) is int and 0<=value['cpu_millis']<=30000)
    return value


def relay_failure_result(value):
    """Nur eigene tatsächliche Fehlerzahlen; niemals erfolgreicher Relay-/Budgetvertrag.

    POSIXwerte sind feste Sourcekategorien 1–17, nicht variable Plattform-Errnos.
    Fehlende rusage/Close-/Half-closebelege bleiben null. Echte Werte oberhalb der Limits
    bleiben als Fehlerbeobachtung erhalten, niemals als akzeptierte Grenze.
    """
    keys={'failed','stage','error','errno_category','closed','close_stage','close_error',
        'close_errno_category','peak_rss_bytes','cpu_millis'}
    number=lambda value,maximum:type(value) is int and 1<=value<=maximum
    require(type(value) is dict and set(value) in (keys,keys|{'half_close_state'}) and value['failed'] is True
        and number(value['stage'],18) and number(value['error'],6)
        and (value['errno_category'] is None or number(value['errno_category'],17))
        and (value['closed'] is None or type(value['closed']) is bool))
    close=(value['close_stage'],value['close_error'],value['close_errno_category'])
    require((close==(None,None,None) and value['closed'] is not False) or
        (value['closed'] is False and type(close[0]) is int and close[0] in (15,16) and number(close[1],6)
         and (close[2] is None or number(close[2],17))))
    for key,minimum in [('peak_rss_bytes',1),('cpu_millis',0)]:
        require(value[key] is None or type(value[key]) is int and minimum<=value[key]<=10**15)
    if 'half_close_state' in value:
        require(value['stage']==14)
        state=value['half_close_state']
        if state is not None:
            booleans={'connecting','client_eof','upstream_eof','client_write_closed','upstream_write_closed'}
            lengths={'client_buffer_bytes','upstream_buffer_bytes'}
            require(type(state) is dict and set(state)==booleans|lengths|{'direction'}
                and type(state['direction']) is int and state['direction'] in (1,2)
                and all(type(state[key]) is bool for key in booleans)
                and all(type(state[key]) is int and 0<=state[key]<=65536 for key in lengths))
            # Der Bericht darf nur einen tatsächlich erreichbaren bestehenden
            # Half-close-Aufruf bezeichnen, nicht FIN vor Drain/Pending Connect
            # oder einen ohnehin terminalen Pairzustand erfinden.
            target='client' if state['direction']==1 else 'upstream'
            peer='upstream' if target=='client' else 'client'
            require(state[peer+'_eof'] and state[target+'_buffer_bytes']==0
                and not state[target+'_write_closed'] and not (target=='upstream' and state['connecting'])
                and not (not state['connecting'] and state['client_eof'] and state['upstream_eof']
                    and state['client_buffer_bytes']==state['upstream_buffer_bytes']==0))
    return value


def auth_result(value,expected=None):
    """Nur vollständige Originaltestausführung; success ohne Zähler ist keine Abnahme."""
    keys={'total','passed','failed','skipped','interrupted','errors','success'}
    require(type(value) is dict and set(value)==keys and type(value['success']) is bool
        and all(type(value[key]) is int and 0<=value[key]<=10000 for key in keys-{'success'})
        and value['success'] and value['total']>0 and value['passed']==value['total']
        and all(value[key]==0 for key in ['failed','skipped','interrupted','errors'])
        and (expected is None or value['total']==expected));return value


def auth_attempt_result(value,expected):
    """Geschlossene Zahlen eines gescheiterten Authversuchs, ausdrücklich KEINE Abnahme.

    Bei globalem Setupfehler können noch nicht alle Tests ein Endereignis haben.
    Solche fehlenden Ergebnisse werden nicht als bestanden, übersprungen oder null erfunden.
    """
    keys={'total','passed','failed','skipped','interrupted','errors','success'}
    counts=keys-{'success','errors'}
    require(type(expected) is int and 1<=expected<=10000
        and type(value) is dict and set(value) in (keys,keys|{'failed_test_indexes'}) and value['success'] is False
        and all(type(value[key]) is int and 0<=value[key]<=expected for key in counts)
        and type(value['errors']) is int and 0<=value['errors']<=10000
        and sum(value[key] for key in counts-{'total'})<=value['total'])
    if 'failed_test_indexes' in value:
        indexes=value['failed_test_indexes']
        require(type(indexes) is list and 1<=len(indexes)<=value['failed']
            and all(type(index) is int and 1<=index<=value['total'] for index in indexes)
            and indexes==sorted(set(indexes)))
    return value


def discovery_result(value):
    """Vorhandene Discovery-Beobachtung: nur Status/Code und nullable Feldvergleiche.

    Kein Auth-Gate: null ist unbekannt, false bleibt false, Status wird nicht als
    Erfolg interpretiert. Transportcode 1..13 ist eine feste dokumentierte Node-Liste.
    """
    keys={'status','transport_code','issuer_matches','pkce_s256'}
    require(type(value) is dict and set(value)==keys)
    status,code=value['status'],value['transport_code']
    require((status is None or type(status) is int and 100<=status<=599)
        and (code is None or type(code) is int and 1<=code<=13)
        and (status is None or code is None)
        and all(value[key] is None or type(value[key]) is bool for key in ['issuer_matches','pkce_s256'])
        and (status is not None or value['issuer_matches'] is None and value['pkce_s256'] is None))
    return value


def tls_loopback_inspect(project):
    """Fester eigener TLS-Inspect: nur Portanzahl/-vergleich und Netzanzahl/-besitz.

    Die unveränderten 14 Containerfelder binden CID, doppelte Labels, Image und
    Ressourcen erneut. Zusätzliche Felder exportieren niemals IPs/Ports als Text
    oder Konfigurationsobjekte; die Go-Blöcke behandeln fehlende Bindungen explizit.
    """
    require(type(project) is str and re.fullmatch(r'flowzer-runtime-[1-9][0-9]{0,19}-a1',project) is not None)
    def count(expression):return '{{with '+expression+'}}{{json (len .)}}{{else}}0{{end}}'
    def match(expression):return '{{with '+expression+'}}{{if eq (len .) 1}}' \
        + '{{json (and (eq (index . 0).HostIp "127.0.0.1") (eq (index . 0).HostPort "8443"))}}' \
        + '{{else}}null{{end}}{{else}}null{{end}}'
    configured='index .HostConfig.PortBindings "8443/tcp"'
    published='index .NetworkSettings.Ports "8443/tcp"'
    return INSPECT[:-1]+','+','.join([
        '"configured_binding_count":'+count(configured),'"configured_loopback_match":'+match(configured),
        '"published_binding_count":'+count(published),'"published_loopback_match":'+match(published),
        '"network_count":'+count('.NetworkSettings.Networks'),
        '"only_owned_network":{{with .NetworkSettings.Networks}}{{json (and (eq (len .) 1) '
        + '(ne (index . "'+project+'_default") nil))}}{{else}}false{{end}}'])+'}'


def tls_loopback_result(value):
    """Nur bereits beobachtete feste Zahlen/Booleans, kein Verbindungs- oder Authnachweis."""
    counts={'configured_binding_count','published_binding_count','network_count'}
    flags={'only_owned_network','tls_running','tls_oom'}
    matches={'configured_loopback_match','published_loopback_match'}
    require(type(value) is dict and set(value)==counts|flags|matches|{'tls_exit_code','tls_restarts'}
        and all(type(value[key]) is int and 0<=value[key]<=16 for key in counts)
        and all(type(value[key]) is bool for key in flags)
        and type(value['tls_exit_code']) is int and 0<=value['tls_exit_code']<=255
        and type(value['tls_restarts']) is int and 0<=value['tls_restarts']<=100)
    for prefix in ['configured','published']:
        count=value[prefix+'_binding_count'];match=value[prefix+'_loopback_match']
        require(type(match) is bool if count==1 else match is None)
    return value


def egress_result(value):
    """Nur exakt die eigene erfolgreiche numerische Netzprobe, keine Roh-/Fehlerfelder uploaden."""
    ones={'positive_control_requests','allowed_page','redirect_ip_blocked','redirect_host_blocked',
        'direct_ip_blocked','websocket_blocked','serviceworker_blocked'}
    zeros={'marker_requests','marker_assertion_red'}
    require(type(value) is dict and set(value)==ones|zeros|{'calibrated','success'}
        and value['calibrated'] is False and value['success'] is True
        and all(type(value[key]) is int and value[key]==1 for key in ones)
        and all(type(value[key]) is int and value[key]==0 for key in zeros));return value


def calibration_probe_failures(value):
    """Eigene optionale JS-Diagnosen strikt schließen; erwarteter Marker-Assert ist allein kein Fehler.

    Ältere synthetische Quellenfixtures ohne die neue optionale Diagnose bleiben
    kompatibel. Alle bisher verlangten Markerzahlen werden anschließend unverändert geprüft.
    """
    if 'failures' not in value:return []
    rows=value['failures'];require(type(rows) is list and 1<=len(rows)<=3)
    projected=[]
    for row in rows:
        require(type(row) is dict and set(row)=={'phase','error','exit_code'})
        phase='browser_probe_'+row['phase'] if type(row['phase']) is str else ''
        code=row['error'];status=row['exit_code']
        require(phase in PHASES and phase.startswith('browser_probe_')
            and code in ('process_exit','timeout','validation','io','unknown'))
        require((code=='process_exit' and type(status) is int and -128<=status<=255)
            or (code!='process_exit' and status is None))
        projected.append((phase,BrowserProbeError(code,status)))
    expected=dict(phase='redirect_ip_blocked',error='validation',exit_code=None)
    # Absichtlicher echter Marker-RED ist genau der erste bekannte Assert; zusätzliche Closes bleiben Fehler.
    if (rows[0]==expected and value['marker_assertion_red']==1 and value['marker_requests']==1
            and value['positive_control_requests']==1 and value['allowed_page']==1 and not value['success']):
        projected=projected[1:]
    return projected


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
        self.owned=False;self.startup_tls_id=None;self.relay_process=None;self.relay_start_failure=None
        self.current_phase='preflight';self.failure_ids=set();self.report['failures']=[]
        # Childumgebung ist eine Whitelist: kein GH-/Cloud-/IdP-/Vaulttoken in Browser oder Containerstarts.
        self.env={key:os.environ[key] for key in ['PATH','LANG','LC_ALL'] if key in os.environ}
        self.env.update(HOME=str(self.root/'home'),DOCKER_CONFIG=str(self.root/'docker-config'),
            PLAYWRIGHT_BROWSERS_PATH=str(self.root/'browsers'),CI='true')
        self.compose=['docker','compose','-p',self.project,'-f',str(self.root/'rig/tests/installation-auth/compose.yml')]

    def note_failure(self,phase,error):
        """Erste Ursache plus Stop/Cleanup innerhalb Max3; nur Identitätsnummern im Speicher.

        Optionale Zahlenberichtdiagnosen dürfen die spätere echte Stop-/Cleanup-
        oder eigene Relay-Closeursache nicht verdrängen. Nur ein solcher Zusatzplatz (nie Index0 oder
        eine echte Runtimeursache) wird bei Bedarf freigemacht; Aktionen, Exit-
        entscheidung und Reihenfolge der verbliebenen Ursachen bleiben gleich.
        """
        self.report['success']=False
        if id(error) in self.failure_ids:return
        rows=self.report['failures'];replace=None
        if len(rows)>=3:
            if phase not in ('stop','cleanup','relay_close'):return
            replace=next((index for index in range(len(rows)-1,0,-1)
                if rows[index]['phase'] in ('auth_report','discovery_report','loopback_report','relay_report')),None)
            if replace is None:return
        value=failure_projection(phase,error)
        if replace is not None:del rows[replace]
        # Bereits gesehene Identitäten bleiben auch nach Verdrängung bekannt,
        # damit äußere Rahmen denselben optionalen Fehler nicht wieder hinzufügen.
        self.failure_ids.add(id(error));rows.append(value)

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
                if self.relay_process is not None:
                    with self.phase('relay_monitor'):
                        code=self.relay_process.poll()
                        if code is not None:raise ProcessExitError(code)
                if not time.monotonic()<deadline:
                    raise DeadlineExceededError('Eigene Gesamtzeitgrenze überschritten.')
                with self.phase('sampling'):self.sample()
                time.sleep(1)
            if process.returncode!=0:raise MeasuredProcessExitError(process.returncode)
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
            failures=calibration_probe_failures(value)
            if failures:
                for phase,error in failures:self.note_failure(phase,error)
                raise failures[0][1]
            require(value['marker_assertion_red']==1 and value['marker_requests']==1
                and value['positive_control_requests']==1 and value['allowed_page']==1 and not value['success'])
        self.command(['node',str(self.source/'scripts/demo-runtime/egress-probe.js'),str(self.root/'egress/egress-result.json')],timeout=60,capture=False)
        value=proof.decode((self.root/'egress/egress-result.json').read_bytes())
        egress_result(value)
        self.env['FLOWZER_RUNTIME_REPORT']=str(self.root/'fixture/auth-result.json')
        self.command(['node',str(cli),'test','--config',str(self.source/'scripts/demo-runtime/fixture-probe.config.js')],timeout=90,capture=False)
        self.report['fixture']=auth_result(proof.decode((self.root/'fixture/auth-result.json').read_bytes()),2)
        self.report['egress']=value;self.report['calibration_marker_assertion_red']=1

    def relay_target(self):
        """Frisch doppelt gebundene eigene private IP, kein freier Zielparameter oder DNS.

        Container und Netz werden erneut vollständig an Labels/Digests/Limits,
        laufenden unrestarteten TLS-CID und denselben einzelnen Endpoint gebunden.
        Nur diese eigene IP verbleibt im RAM; kein Netz-/Konfigurationsdump im Report.
        """
        cid=self.startup_tls_id
        require(type(cid) is str and re.fullmatch('[0-9a-f]{64}',cid) is not None)
        raw=self.command(['docker','inspect','--type','container','--format',relay_container_inspect(self.project),cid])
        require(len(raw.encode())<=4096);row=proof.decode(raw.encode())
        require(type(row) is dict and set(row)==set(COLUMNS)|{'network_count','network_id','network_ipv4','endpoint_id'})
        state=self.audited_container({key:row[key] for key in COLUMNS})
        require(row['id']==cid and state['service']=='tls' and not state['oneoff']
            and state['running'] and not state['oom'] and state['exit_code']==state['restarts']==0
            and type(row['network_count']) is int and row['network_count']==1
            and type(row['network_id']) is str and re.fullmatch('[0-9a-f]{64}',row['network_id']) is not None
            and type(row['endpoint_id']) is str and re.fullmatch('[0-9a-f]{64}',row['endpoint_id']) is not None
            and type(row['network_ipv4']) is str)
        address=ipaddress.IPv4Address(row['network_ipv4'])
        private=tuple(ipaddress.ip_network(x) for x in ('10.0.0.0/8','172.16.0.0/12','192.168.0.0/16'))
        require(str(address)==row['network_ipv4'] and any(address in network for network in private))
        raw=self.command(['docker','network','inspect','--format',relay_network_inspect(cid),row['network_id']])
        require(len(raw.encode())<=2048);net=proof.decode(raw.encode())
        require(type(net) is dict and set(net)=={'name','project','owner','internal','id','driver','subnet','container_ipv4','endpoint_id'})
        resource_row({k:net[k] for k in ('name','project','owner','internal')},self.project,'network')
        require(net['id']==row['network_id'] and net['driver']=='bridge' and net['endpoint_id']==row['endpoint_id']
            and type(net['subnet']) is str and type(net['container_ipv4']) is str)
        network=ipaddress.IPv4Network(net['subnet']);endpoint=ipaddress.IPv4Interface(net['container_ipv4'])
        require(any(network.subnet_of(allowed) for allowed in private) and address in network
            and endpoint.ip==address and endpoint.network==network
            and address not in (network.network_address,network.broadcast_address))
        return str(address)

    def relay_ready(self,process):
        """Harte fünfsekündige eigene Pipegrenze auch bei partieller Readinesszeile."""
        fd=process.stdout.fileno();data=bytearray();deadline=time.monotonic()+5
        os.set_blocking(fd,False)
        try:
            while not data.endswith(b'\n'):
                remaining=deadline-time.monotonic()
                if remaining<=0:raise TimeoutError('Eigene Relay-Startgrenze.')
                ready,_,_=select.select([process.stdout],[],[],remaining)
                if not ready:raise TimeoutError('Eigene Relay-Startgrenze.')
                try:part=os.read(fd,1025-len(data))
                except BlockingIOError:continue
                require(bool(part));data.extend(part);require(len(data)<=1024)
            value=proof.decode(bytes(data))
            if type(value) is dict and value.get('failed') is True:
                # Noch kein Exitbeleg: nur RAM merken, Start bleibt strikt rot.
                self.relay_start_failure=relay_failure_result(value)
            require(type(value) is dict and set(value)=={'ready'} and value['ready'] is True)
        finally:os.set_blocking(fd,True)

    @contextmanager
    def transport_session(self):
        """Eigener Relay lebt nur um Auth, wird garantiert VOR Docker-Cleanup geschlossen.

        Readiness ist allein der tatsächliche eigene bind(), keine zusätzliche
        TCP-/HTTP-Probe. Ein belegter Port wird weder übernommen noch freigeschossen.
        Relay-Ausgaben bleiben zwei streng geschlossene Zahlen-/Boolean-Pipezeilen.
        """
        process=None
        try:
            with self.phase('relay_start'):
                address=self.relay_target()
                process=subprocess.Popen(['python3','-I','-B',str(self.source/'scripts/demo-runtime/relay.py')],
                    env=self.env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,start_new_session=True)
                self.relay_process=process
                process.stdin.write(json.dumps({'address':address}).encode()+b'\n');process.stdin.close();process.stdin=None
                self.relay_ready(process)
                require(process.poll() is None)
            yield
        finally:
            if process is not None:
                # Einmalige eigene TERM/5s/KILL-Beendigung; nicht den fremden Listenport anfassen.
                with self.phase('relay_close'):
                    try:
                        if process.poll() is None:
                            os.killpg(process.pid,signal.SIGTERM)
                        try:raw,_=process.communicate(timeout=5)
                        except subprocess.TimeoutExpired:
                            self.stop_process(process);raise
                        if process.returncode!=0:
                            error=ProcessExitError(process.returncode)
                            # Unknown behält exakt den bisherigen strengen Größenpfad.
                            # Nur ein wirklich bekannter Fehlstatus hat Exitvorrang.
                            if error.exit_code is None:require(len(raw)<=1024)
                            # Tatsächlicher Childexit zuerst; nur dessen eigene Pipe
                            # darf eine optionale geschlossene Diagnose ergänzen.
                            self.note_failure('relay_close',error)
                            try:
                                if error.exit_code is not None:
                                    require(len(raw)<=1024)
                                    if raw:self.report['relay_failure']=relay_failure_result(proof.decode(raw))
                                    elif self.relay_start_failure is not None:
                                        self.report['relay_failure']=relay_failure_result(self.relay_start_failure)
                            except BaseException as report_error:self.note_failure('relay_report',report_error)
                            raise error
                        require(len(raw)<=1024)
                        self.report['relay']=relay_result(proof.decode(raw))
                        if 'peak_memory_bytes' in self.report:
                            # Summe zweier verschiedenzeitiger echter Peaks: konservative
                            # Ergänzung, ausdrücklich kein synchron beobachteter Gesamtpeak.
                            self.report['peak_memory_with_relay_upper_bound_bytes']=self.report['peak_memory_bytes'] \
                                + self.report['relay']['peak_rss_bytes']
                    finally:
                        # Auch Pipe-/Readinessfehler dürfen keinen eigenen Kindprozess hinterlassen.
                        try:
                            if process.poll() is None:self.stop_process(process)
                        finally:
                            for stream in (process.stdin,process.stdout):
                                if stream is not None:stream.close()
                            self.relay_process=None;self.relay_start_failure=None

    def tls_loopback_snapshot(self):
        """Genau ein Format-Inspect des beim Start gebundenen eigenen TLS-CIDs.

        Ausschließlich Diagnose nach bereits vorhandenem Discovery-Code3; kein
        weiterer Netzrequest, keine Aufweichung von TLS/Netz/Auth oder Cleanup.
        Vor Übernahme werden dieselben Eigentums-/Image-/Ressourcenfelder neu geprüft.
        """
        cid=self.startup_tls_id
        require(type(cid) is str and re.fullmatch('[0-9a-f]{64}',cid) is not None)
        raw=self.command(['docker','inspect','--type','container','--format',tls_loopback_inspect(self.project),cid])
        require(len(raw.encode())<=4096)
        row=proof.decode(raw.encode())
        fields={'configured_binding_count','configured_loopback_match','published_binding_count',
            'published_loopback_match','network_count','only_owned_network'}
        require(type(row) is dict and set(row)==set(COLUMNS)|fields)
        state=self.audited_container({key:row[key] for key in COLUMNS})
        require(row['id']==cid and state['service']=='tls' and not state['oneoff'])
        return tls_loopback_result({key:row[key] for key in fields}|dict(tls_running=state['running'],
            tls_oom=state['oom'],tls_exit_code=state['exit_code'],tls_restarts=state['restarts']))

    @diagnosed('auth')
    def run_auth(self,auth):
        """Originaltests unverändert; Exitfehler bleiben rot, sichere vorhandene Zähler erhalten.

        Der optionale Fehlversuchsbericht darf weder den primären Exit ersetzen
        noch Auth akzeptieren. Fehlender/manipulierter Report bleibt eine eigene
        geschlossene Diagnose, Cleanup läuft im unveränderten äußeren finally.
        """
        try:
            self.measured_command(['node',str(auth/'node_modules/playwright/cli.js'),'test',
                '--config',str(auth/'playwright.config.js')],timeout=1500,cwd=auth)
        except MeasuredProcessExitError as primary:
            self.note_failure('auth',primary)
            try:
                output=self.root/'auth-result.json'
                require(not output.is_symlink())
                with output.open('rb') as stream:raw=stream.read(65537)
                require(len(raw)<=65536)
                self.report['auth_attempt']=auth_attempt_result(proof.decode(raw),16)
            except Exception as diagnostic:
                self.note_failure('auth_report',diagnostic)
            # Nur nach dem wirklichen Auth-Kindprozess-Exit und nur wenn dessen
            # vorhandene Anfrage eine Datei erzeugt hat. Fehlend heißt unbekannt,
            # Sampling-/Stopfehler erreichen diesen Pfad ausdrücklich nicht.
            output=self.root/'discovery-result.json'
            try:
                if output.exists() or output.is_symlink():
                    require(not output.is_symlink())
                    with output.open('rb') as stream:raw=stream.read(1025)
                    require(len(raw)<=1024)
                    self.report['discovery']=discovery_result(proof.decode(raw))
            except Exception as diagnostic:
                self.note_failure('discovery_report',diagnostic)
            if self.report.get('discovery',{}).get('transport_code')==3:
                try:
                    with self.phase('loopback_report'):
                        self.report['tls_loopback']=self.tls_loopback_snapshot()
                except Exception as diagnostic:
                    self.note_failure('loopback_report',diagnostic)
            raise
        self.report['auth']=auth_result(proof.decode((self.root/'auth-result.json').read_bytes()),16)

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
                # Nur RAM-CID aus der bereits vollständig geprüften eigenen Startinventur.
                self.startup_tls_id=next(row['id'] for row in rows if row['service']=='tls')
                self.disk('after_start');self.env['FLOWZER_RUNTIME_REPORT']=str(self.root/'auth-result.json')
            with self.transport_session():
                with self.phase('auth'):
                    self.run_auth(auth)
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
