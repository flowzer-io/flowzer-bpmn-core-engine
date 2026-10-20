"""Ein später explizit gebundener öffentlicher Hosted-Pilot, kein zweiter Runtime-Runner."""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('pilot_shared', ROOT/'scripts/ci/run_startup_secret_contracts.py')
shared = importlib.util.module_from_spec(spec); spec.loader.exec_module(shared)
SDK = 'mcr.microsoft.com/dotnet/sdk@sha256:b1ae36d53ae3803280431dc4b67ebdcc29ffb20c09a40748de154b584c05b259'
ASP = 'mcr.microsoft.com/dotnet/aspnet@sha256:e4b445f93a5deeb6bd7de300febceb8dd7e481ac65ccf1b474eddc3642a3010f'
PG = 'docker.io/library/postgres@sha256:66aafa11cf15800a3c94763f7e11d1e7b2e37e5e84bc4ce027cb6e1e4bfaf4df'
class PilotDiagnostics:
    """Nur feste Codes/Counts; weder Exceptions noch Kindausgaben werden in Berichte übernommen."""
    PHASES = {'OPENING','TEMP','SOURCE','RESOURCES','DAEMON','OWN_NAMES_INITIAL','ARCHIVE','EXTRACT',
              'PULL_SDK','PULL_ASP','PULL_PG','BUILD','PINS','IMAGE_INSPECT','IMAGE_BINDING',
              'PARENT_BINDING','PARENT','OWN_NAMES_FINAL','COMPLETE','WORKSPACE_REMOVE'}

    def __init__(self):
        self.phase='OPENING';self.failure_phase=None;self.tool=None
        self.runtime_state='NOT_STARTED';self.own_names='NOT_CHECKED';self.readback_tool=None
        self.artifacts=None;self.parent=None

    def enter(self, phase):
        """Phasen sind quellgebundene Konstanten, niemals aus Kind-/Benutzerausgaben abgeleitet."""
        if phase not in self.PHASES:raise ValueError('DIAGNOSTIC_PHASE')
        self.phase=phase

    def failed(self, error, *, readback=False):
        """Primärfehler behalten; ein zweiter Readbackfehler ist gesondert und niemals Cleanup-Erfolg."""
        tool={'state':'VALIDATION'}
        if isinstance(error,shared.ToolCleanupUnknown):tool={'state':'CLI_CLEANUP_UNKNOWN'}
        elif isinstance(error,subprocess.CalledProcessError):
            tool={'state':'EXIT_NONZERO','exitCode':error.returncode if type(error.returncode) is int else None}
        elif isinstance(error,subprocess.TimeoutExpired):
            tool={'state':'TIMEOUT','timeoutSeconds':error.timeout if type(error.timeout) in [int,float] and 0<error.timeout<=600 else None}
        elif isinstance(error,ValueError) and error.args==('Runtime list exceeded byte limit',):
            tool={'state':'OUTPUT_LIMIT','limitBytes':65536}
        elif isinstance(error,OSError):tool={'state':'OS_ERROR','errno':error.errno if type(error.errno) is int else None}
        if readback:self.readback_tool=tool
        if self.failure_phase is None:
            self.failure_phase=self.phase;self.tool=tool
            if self.phase=='BUILD' and isinstance(error,subprocess.CalledProcessError):
                self.tool['buildClassification']=classify_build(error.output)

    def bind_artifacts(self, value):
        """Nur vollständig bereits validierte technische Pins führen; kein Rohdaemon-/Configinhalt."""
        lengths={'sourceSha':40,'sourceTree':40,'nonce':32,'inventorySHA256':64,'apiSHA256':64,
                 'feederSHA256':64,'nativeSHA256':64,'dockerSHA256':64,'daemonIdSHA256':64}
        if set(value)!=set(lengths)|{'image','runtime','expectedMigration'} \
                or any(not isinstance(value[k],str) or not re.fullmatch('[a-f0-9]{'+str(n)+'}',value[k]) for k,n in lengths.items()) \
                or not isinstance(value['image'],str) or not re.fullmatch('sha256:[a-f0-9]{64}',value['image']) \
                or value['runtime']!='10.0.13' or type(value['expectedMigration']) is not int or value['expectedMigration']!=20:
            raise ValueError('ARTIFACT_PROVENANCE')
        self.artifacts=dict(value)

    def snapshot(self):
        """Unbekannt ist ausdrücklich kein EOF-/Leak-/Native-/Ressourcenabschlussbeleg."""
        return dict(phase=self.phase,failurePhase=self.failure_phase,tool=self.tool,
                    runtimeState=self.runtime_state,ownNames=self.own_names,readbackTool=self.readback_tool,artifacts=self.artifacts,parent=self.parent)


def project_parent_failure(output, nonce):
    """Kinddaten sind niemals Autorisierung: nur genaues Schema/feste Kategorien/maximal zwei eigene Kandidaten."""
    phases={'BINDING','PLAN','PREFLIGHT','VOLUME_CREATE','VOLUME_INSPECT','PG_CREATE','PG_INSPECT','API_CREATE','API_INSPECT',
            'PG_STDIN','FIXTURE','VERIFY_FIXTURE','CLEANUP_INSPECT','CLEANUP_STOP','CLEANUP_REMOVE','CLEANUP_VOLUME',
            'CLI_FINAL','WORKSPACE_REMOVE'}
    codes={'INPUT','RESOURCES','TIMEOUT','OWNERSHIP','OUTPUT','UNKNOWN','CLEANUP','BINDING','SETUP','DOCKER','ONCE'}
    violations={'ENV','IMAGE','COMMAND','TMPFS','IDENTITY','MOUNTS','ISOLATION','RESOURCES','ROOTFS','CAPABILITIES','NO_NEW_PRIVILEGES'}
    def unique(pairs):
        result={}
        for key,value in pairs:
            if key in result:raise ValueError('DUPLICATE')
            result[key]=value
        return result
    try:
        if not isinstance(output,str) or len(output.encode())>65536:raise ValueError('OUTPUT')
        value=json.loads(output,object_pairs_hook=unique)
        if set(value)!={'result','nonce','cids','home','cleanup','diagnostic'} or value['result']!='REJECTED' \
                or value['nonce']!=nonce or value['cleanup']!='CHECK_REQUIRED':raise ValueError('SCHEMA')
        cids=value['cids'];d=value['diagnostic']
        if not isinstance(cids,list) or len(cids)>2 or len(set(cids))!=len(cids) \
                or any(not isinstance(c,str) or not re.fullmatch('[a-f0-9]{64}',c) for c in cids):raise ValueError('CID')
        if set(d)!={'phase','failurePhase','failureCode','cleanup','runtimeState','violation','candidates','fixture','tool','cleanupTool'} \
                or d['phase'] not in phases or d['failurePhase'] not in phases or d['failureCode'] not in codes \
                or d['cleanup'] not in ['NOT_STARTED','COMPLETE','UNKNOWN'] \
                or d['runtimeState'] not in ['NOT_STARTED','MAY_HAVE_STARTED'] \
                or d['violation'] is not None and d['violation'] not in violations:raise ValueError('DIAGNOSTIC')
        candidates=d['candidates']
        if not isinstance(candidates,list) or len(candidates)!=len(cids):raise ValueError('CANDIDATES')
        for c in candidates:
            if set(c)!={'cid','role','ownership'} or c['cid'] not in cids or c['role'] not in ['pg','api'] \
                    or c['ownership'] not in ['VERIFIED','UNKNOWN']:raise ValueError('CANDIDATE')
        if len({c['cid'] for c in candidates})!=len(cids) or len({c['role'] for c in candidates})!=len(cids) \
                or d['runtimeState']=='NOT_STARTED' and cids:raise ValueError('OWNERSHIP')
        for key in ['tool','cleanupTool']:
            tool=d[key]
            if tool is not None and (set(tool)!={'phase','state','exitCode'} or tool['phase'] not in phases
                    or tool['state']!='EXIT_NONZERO' or type(tool['exitCode']) is not int
                    or tool['phase'].startswith('CLEANUP_')!=(key=='cleanupTool')):raise ValueError('TOOL')
        fixture=d['fixture']
        if fixture is not None:
            if fixture.get('state')=='UNKNOWN':
                if set(fixture)!={'state','exitCode'} or type(fixture['exitCode']) is not int:raise ValueError('FIXTURE')
            elif fixture.get('state')=='VALIDATED':
                if set(fixture)!={'state','exitCode','diagnostic'} or type(fixture['exitCode']) is not int:raise ValueError('FIXTURE')
                f=fixture['diagnostic'];fp={'Input','Binding','Provision','Migration','ApiStart','Readiness','Cleanup','Complete'}
                if set(f)!={'phase','failurePhase','failureCode','cleanup'} or f['phase'] not in fp or f['failurePhase'] not in fp \
                        or f['failureCode'] not in ['Input','Role','Sequence','Output','Readiness','Cleanup'] \
                        or f['cleanup'] not in ['NotStarted','Complete','Unknown']:raise ValueError('FIXTURE')
            else:raise ValueError('FIXTURE')
        return {'state':'VALIDATED','diagnostic':d}
    except Exception:return {'state':'UNKNOWN'}


def classify_build(output):
    """Begrenzte Buildausgabe nur auf finite Fehlerkategorien zählen, keine Freitext-Fallbacks."""
    if not isinstance(output,str) or len(output.encode())>65536:return {'state':'UNKNOWN','counts':{}}
    patterns={'NUGET_SOURCE':r'\berror NU1301\b','NUGET_AUDIT':r'\berror NU190[34]\b',
              'COMPILER':r'\berror CS[0-9]{4}\b','SDK':r'\berror NETSDK[0-9]{4}\b',
              'FILE_COLLISION':r'\bdiffer: byte [0-9]+, line [0-9]+(?:\n|$)'}
    counts={key:len(re.findall(pattern,output)) for key,pattern in patterns.items()}
    counts={key:value for key,value in counts.items() if value}
    return {'state':'MATCHED' if len(counts)==1 else 'AMBIGUOUS' if counts else 'UNKNOWN','counts':counts}


def validate_lease(raw, now, environment):
    """Nur Root-Dispatch/Attempt1 auf dem Topic; diese Identitätsbindung ersetzt keine Root-Freigabe."""
    if len(raw.encode()) > 16384: raise ValueError('OPENING')
    def unique(pairs):
        result={}
        for key,value in pairs:
            if key in result: raise ValueError('OPENING')
            result[key]=value
        return result
    value=json.loads(raw,object_pairs_hook=unique)
    keys={'version','owner','nonce','sourceSha','sourceTree','workflowSha256','expiresUnix','p3Accepted'}
    if not isinstance(value,dict) or set(value)!=keys or type(value['version']) is not int or value['version']!=1 \
            or value['owner']!='01a11f64-c814-7f51-abdb-1b7462e0491e' or value['p3Accepted'] is not True \
            or type(value['expiresUnix']) is not int or not now < value['expiresUnix'] <= now+1800:
        raise ValueError('OPENING')
    for key,length in [('nonce',32),('sourceSha',40),('sourceTree',40),('workflowSha256',64)]:
        if not isinstance(value[key],str) or not re.fullmatch('[a-f0-9]{'+str(length)+'}',value[key]):
            raise ValueError('OPENING')
    expected=dict(GITHUB_REPOSITORY='flowzer-io/flowzer-bpmn-core-engine',GITHUB_EVENT_NAME='workflow_dispatch',
        GITHUB_RUN_ATTEMPT='1',GITHUB_SHA=value['sourceSha'],
        GITHUB_REF='refs/heads/codex/flowzer-stdin-bootstrap-prototype-20261020',
        RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',RUNNER_ARCH='X64')
    if any(environment.get(key)!=wanted for key,wanted in expected.items()):raise ValueError('HOSTED')
    return value


def parse_pins(output):
    """Ausschließlich einmalige tatsächliche Build-Ausgaben, keine aus Mac-/Riglogs übernommenen DLLpins."""
    fields={}
    for line in output.splitlines():
        match=re.fullmatch(r'#\d+ [0-9.]+ FLOWZER_PILOT_([A-Z_]+)=([^\s]+)',line)
        if match:
            key,value=match.groups()
            if key in fields: raise ValueError('DUPLICATE_PIN')
            fields[key]=value
    if set(fields)!={'API','FEEDER','NATIVE','INVENTORY','API_CONFIG','FEEDER_CONFIG'}:
        raise ValueError('MISSING_PIN')
    for key in ['API','FEEDER','NATIVE','INVENTORY']:
        if not re.fullmatch('[a-f0-9]{64}',fields[key]):raise ValueError('PIN')
    for key,names in [('API_CONFIG',{'Microsoft.NETCore.App','Microsoft.AspNetCore.App'}),
                      ('FEEDER_CONFIG',{'Microsoft.NETCore.App'})]:
        raw=base64.b64decode(fields[key],validate=True)
        if len(raw)>16384:raise ValueError('CONFIG')
        options=json.loads(raw)['runtimeOptions'];frameworks=options.get('frameworks',[options.get('framework')])
        if len(frameworks)!=len(names) or {f['name'] for f in frameworks}!=names \
                or options.get('rollForward','Minor') not in ['Minor','LatestPatch','Disable'] \
                or 'applyPatches' in options:raise ValueError('FRAMEWORK')
        for item in frameworks:
            if not re.fullmatch(r'10\.0\.\d+',item['version']):raise ValueError('FRAMEWORK')
            if int(item['version'].split('.')[2])>13 or options.get('rollForward')=='Disable' and item['version']!='10.0.13':
                raise ValueError('FRAMEWORK')
    return fields


def preflight_resources(work):
    """Vor Pull/Build Reserve prüfen; der vorhandene Parent prüft erneut unmittelbar vor Runtimewrites."""
    memory=int(next(s.split()[1] for s in Path('/proc/meminfo').read_text().splitlines()
                    if s.startswith('MemAvailable:')))*1024
    if memory<2*1024**3 or shutil.disk_usage(work).free<4*1024**3:raise ValueError('RESOURCES')


def file_digest(path):
    """Nur vorhandenes reguläres Operatorbinary hashen, keine Zugangsdaten/CLI-Defaults auflösen."""
    if not path.is_absolute() or not path.is_file() or path.stat().st_size>64*1024**2:raise ValueError('BINARY')
    with path.open('rb') as source:return hashlib.file_digest(source,'sha256').hexdigest()


def _execute(lease, work, docker, now, report):
    """Kleine feste Assemblierung; Runtime ausschließlich durch den bestehenden --bound-run-Entrypoint."""
    environment=shared.isolated_environment(work)
    environment['DOCKER_CONFIG']=str(work/'docker')
    base=[str(docker),'--host','unix:///var/run/docker.sock']
    def command(args,limit=90,phase=None):
        if phase:report.enter(phase)
        if int(now())>=lease['expiresUnix']:raise ValueError('LEASE_EXPIRED')
        return shared.run_tool(args,environment,capture=True,merge_stderr=True,timeout=limit)
    report.enter('SOURCE')
    if command(['git','rev-parse','HEAD']).strip()!=lease['sourceSha'] \
            or command(['git','rev-parse','HEAD^{tree}']).strip()!=lease['sourceTree'] \
            or hashlib.sha256((ROOT/'.github/workflows/ci.yml').read_bytes()).hexdigest()!=lease['workflowSha256']:
        raise ValueError('SOURCE')
    report.enter('RESOURCES');preflight_resources(work)
    info=json.loads(command(base+['info','--format','{{json .}}'],phase='DAEMON'))
    if info['OSType']!='linux' or info['Architecture'] not in ['x86_64','amd64'] or info['NCPU']<2 \
            or info['MemTotal']<2*1024**3 or not info['ID']:raise ValueError('DAEMON')
    prefix='flowzer-smoke-'+lease['nonce']
    def absent(phase):
        report.enter(phase);report.own_names='UNKNOWN'
        # Nur die drei exakten eigenen Namen lesen, auch nach Leaseablauf; niemals neue Writes freigeben.
        try:
            for name in [prefix+'-pg',prefix+'-api']:
                if shared.run_tool(base+['ps','--all','--filter','name=^/'+name+'$','--format','{{.ID}}'],
                                   environment,capture=True,merge_stderr=True,timeout=10).strip():
                    raise ValueError('CONTAINER_REMAINS')
            if shared.run_tool(base+['volume','ls','--filter','name=^'+prefix+'$','--format','{{.Name}}'],
                               environment,capture=True,merge_stderr=True,timeout=10).strip():
                raise ValueError('VOLUME_REMAINS')
            report.own_names='ABSENT'
        except Exception as error:
            report.failed(error,readback=phase=='OWN_NAMES_FINAL');raise
    absent('OWN_NAMES_INITIAL')
    context=work/'source';context.mkdir()
    archive=work/'source.tar'
    command(['git','archive','--format=tar','--output',str(archive),lease['sourceSha']],phase='ARCHIVE')
    command(['tar','--no-same-owner','--no-same-permissions','-xf',str(archive),'-C',str(context)],phase='EXTRACT')
    # Keine impliziten Registryziele/Tags: ausschließlich drei öffentliche unveränderliche Basisbilder.
    for image,phase in [(SDK,'PULL_SDK'),(ASP,'PULL_ASP'),(PG,'PULL_PG')]:
        command(base+['pull','--platform','linux/amd64',image],120,phase=phase)
    imagefile=work/'image-id'
    build=command(base+['build','--platform','linux/amd64','--pull=false','--progress','plain',
        '--build-arg','PILOT_NONCE='+lease['nonce'],'--label','org.opencontainers.image.revision='+lease['sourceSha'],
        '--label','flowzer-smoke.nonce='+lease['nonce'],'--iidfile',str(imagefile),
        '--file',str(context/'tests/runtime-bootstrap-smoke/Dockerfile.pilot'),str(context)],300,phase='BUILD')
    report.enter('PINS');pins=parse_pins(build);image=imagefile.read_text().strip()
    if not re.fullmatch('sha256:[a-f0-9]{64}',image):raise ValueError('IMAGE')
    items=json.loads(command(base+['image','inspect',image],phase='IMAGE_INSPECT'));item=items[0]
    report.enter('IMAGE_BINDING')
    if len(items)!=1 or item['Id']!=image or item['Os']!='linux' or item['Architecture']!='amd64' \
            or item['Config']['Labels'].get('org.opencontainers.image.revision')!=lease['sourceSha'] \
            or item['Config']['Labels'].get('flowzer-smoke.nonce')!=lease['nonce']:raise ValueError('IMAGE')
    report.enter('PARENT_BINDING')
    if int(now())+300>lease['expiresUnix']:raise ValueError('LEASE_TOO_SHORT')
    opening=dict(version=1,owner=lease['owner'],nonce=lease['nonce'],expiresUnix=int(now())+300,
        dockerPath=str(docker),dockerSha256=file_digest(docker),dockerHost='unix:///var/run/docker.sock',daemonId=info['ID'],
        pgImage=PG,apiImage=image,apiSha256=pins['API'],feederSha256=pins['FEEDER'],
        nativePath='/app/runtimes/linux-x64/native/ClearScriptV8.linux-x64.so',nativeSha256=pins['NATIVE'],
        runtime='10.0.13',pgUid=999,apiUid=1654,gid=22000,expectedMigration=20)
    path=work/'opening.json';raw=(json.dumps(opening,sort_keys=True)+'\n').encode();path.write_bytes(raw);path.chmod(0o600)
    result=dict(sourceSha=lease['sourceSha'],sourceTree=lease['sourceTree'],nonce=lease['nonce'],
                image=image,inventorySHA256=pins['INVENTORY'],runtime='10.0.13',expectedMigration=20,p3Accepted=True)
    (work/'artifact-pins.json').write_text(json.dumps(dict(result,apiSHA256=pins['API'],feederSHA256=pins['FEEDER'],
                                                       nativeSHA256=pins['NATIVE']),indent=2)+'\n')
    report.bind_artifacts(dict({k:v for k,v in result.items() if k!='p3Accepted'},apiSHA256=pins['API'],feederSHA256=pins['FEEDER'],nativeSHA256=pins['NATIVE'],
        dockerSHA256=opening['dockerSha256'],daemonIdSHA256=hashlib.sha256(info['ID'].encode()).hexdigest()))
    try:
        # Schon ein fehlgeschlagener CLIstart kann Dockerwrites angestoßen haben: konservativ unbekannt.
        report.enter('PARENT');report.runtime_state='MAY_HAVE_STARTED'
        output=command([sys.executable,'-I','-B',str(ROOT/'tests/runtime-bootstrap-smoke/run_smoke.py'),
                        '--bound-run',str(path),hashlib.sha256(raw).hexdigest(),lease['nonce']],420)
        if output!='FLOWZER_SMOKE_PARENT_OK\n':raise ValueError('PARENT_RESULT')
        report.runtime_state='PARENT_SUCCEEDED'
    except Exception as error:
        if isinstance(error,subprocess.CalledProcessError):report.parent=project_parent_failure(error.output,lease['nonce'])
        report.failed(error);raise
    finally:
        absent('OWN_NAMES_FINAL')  # Auch bei Exit!=0 nur lesen; niemals fremde/unklare CIDs stoppen oder force-räumen.
    return result


def execute(lease, work, docker, now, report=None):
    """Diagnose ohne neue Prozesssteuerung; fehlgeschlagene Ausführung bleibt unverändert fehlgeschlagen."""
    report=report if report is not None else PilotDiagnostics()
    try:return _execute(lease,work,docker,now,report)
    except Exception as error:
        report.failed(error);raise


def main():
    """Geschlossener Normalaufruf; Fehler behalten den eigenen Arbeitsbereich und erlauben keinen Retry."""
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--opening',required=True)
    parser.add_argument('--docker',type=Path,required=True)
    parser.add_argument('--temp-root',type=Path,required=True)
    args=parser.parse_args()
    work=None;lease=None;report=PilotDiagnostics()
    try:
        lease=validate_lease(args.opening,int(time.time()),os.environ)
        report.enter('TEMP')
        if not args.temp_root.is_absolute() or not args.temp_root.is_dir():raise ValueError('TEMP')
        file_digest(args.docker)
        work=Path(tempfile.mkdtemp(prefix='flowzer-pilot-',dir=args.temp_root))
        result=execute(lease,work,args.docker,lambda:int(time.time()),report)
        report.enter('WORKSPACE_REMOVE')
        shutil.rmtree(work)  # Ausschließlich nach verifiziert terminalen Tools/Parent und Namensabwesenheit.
        report.enter('COMPLETE')
        print(json.dumps(dict(result,result='SYNTHETIC_PILOT_OK',ownNamesAbsent=True,diagnostic=report.snapshot())))
        return 0
    except Exception as error:
        report.failed(error)
        print(json.dumps(dict(result='PILOT_REJECTED',nonce=lease['nonce'] if lease else None,
                              work=str(work) if work else None,cleanup='CHECK_REQUIRED',diagnostic=report.snapshot())))
        return 2


if __name__=='__main__':raise SystemExit(main())
