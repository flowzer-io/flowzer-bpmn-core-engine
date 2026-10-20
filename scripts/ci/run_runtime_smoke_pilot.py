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
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('pilot_shared', ROOT/'scripts/ci/run_startup_secret_contracts.py')
shared = importlib.util.module_from_spec(spec); spec.loader.exec_module(shared)
SDK = 'mcr.microsoft.com/dotnet/sdk@sha256:b1ae36d53ae3803280431dc4b67ebdcc29ffb20c09a40748de154b584c05b259'
ASP = 'mcr.microsoft.com/dotnet/aspnet@sha256:e4b445f93a5deeb6bd7de300febceb8dd7e481ac65ccf1b474eddc3642a3010f'
PG = 'docker.io/library/postgres@sha256:66aafa11cf15800a3c94763f7e11d1e7b2e37e5e84bc4ce027cb6e1e4bfaf4df'
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


def execute(lease, work, docker, now):
    """Kleine feste Assemblierung; Runtime ausschließlich durch den bestehenden --bound-run-Entrypoint."""
    environment=shared.isolated_environment(work)
    environment['DOCKER_CONFIG']=str(work/'docker')
    base=[str(docker),'--host','unix:///var/run/docker.sock']
    def command(args,limit=90):
        if int(now())>=lease['expiresUnix']:raise ValueError('LEASE_EXPIRED')
        return shared.run_tool(args,environment,capture=True,merge_stderr=True,timeout=limit)
    if command(['git','rev-parse','HEAD']).strip()!=lease['sourceSha'] \
            or command(['git','rev-parse','HEAD^{tree}']).strip()!=lease['sourceTree'] \
            or hashlib.sha256((ROOT/'.github/workflows/ci.yml').read_bytes()).hexdigest()!=lease['workflowSha256']:
        raise ValueError('SOURCE')
    preflight_resources(work)
    info=json.loads(command(base+['info','--format','{{json .}}']))
    if info['OSType']!='linux' or info['Architecture'] not in ['x86_64','amd64'] or info['NCPU']<2 \
            or info['MemTotal']<2*1024**3 or not info['ID']:raise ValueError('DAEMON')
    prefix='flowzer-smoke-'+lease['nonce']
    def absent():
        # Nur die drei exakten eigenen Namen lesen, auch nach Leaseablauf; niemals neue Writes freigeben.
        for name in [prefix+'-pg',prefix+'-api']:
            if shared.run_tool(base+['ps','--all','--filter','name=^/'+name+'$','--format','{{.ID}}'],
                               environment,capture=True,merge_stderr=True,timeout=10).strip():
                raise ValueError('CONTAINER_REMAINS')
        if shared.run_tool(base+['volume','ls','--filter','name=^'+prefix+'$','--format','{{.Name}}'],
                           environment,capture=True,merge_stderr=True,timeout=10).strip():
            raise ValueError('VOLUME_REMAINS')
    absent()
    context=work/'source';context.mkdir()
    archive=work/'source.tar'
    command(['git','archive','--format=tar','--output',str(archive),lease['sourceSha']])
    command(['tar','--no-same-owner','--no-same-permissions','-xf',str(archive),'-C',str(context)])
    # Keine impliziten Registryziele/Tags: ausschließlich drei öffentliche unveränderliche Basisbilder.
    for image in [SDK,ASP,PG]:command(base+['pull','--platform','linux/amd64',image],120)
    imagefile=work/'image-id'
    build=command(base+['build','--platform','linux/amd64','--pull=false','--progress','plain',
        '--build-arg','PILOT_NONCE='+lease['nonce'],'--label','org.opencontainers.image.revision='+lease['sourceSha'],
        '--label','flowzer-smoke.nonce='+lease['nonce'],'--iidfile',str(imagefile),
        '--file',str(context/'tests/runtime-bootstrap-smoke/Dockerfile.pilot'),str(context)],300)
    pins=parse_pins(build);image=imagefile.read_text().strip()
    if not re.fullmatch('sha256:[a-f0-9]{64}',image):raise ValueError('IMAGE')
    items=json.loads(command(base+['image','inspect',image]));item=items[0]
    if len(items)!=1 or item['Id']!=image or item['Os']!='linux' or item['Architecture']!='amd64' \
            or item['Config']['Labels'].get('org.opencontainers.image.revision')!=lease['sourceSha'] \
            or item['Config']['Labels'].get('flowzer-smoke.nonce')!=lease['nonce']:raise ValueError('IMAGE')
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
    try:
        output=command([sys.executable,'-I','-B',str(ROOT/'tests/runtime-bootstrap-smoke/run_smoke.py'),
                        '--bound-run',str(path),hashlib.sha256(raw).hexdigest(),lease['nonce']],420)
        if output!='FLOWZER_SMOKE_PARENT_OK\n':raise ValueError('PARENT_RESULT')
    finally:
        absent()  # Auch bei Exit!=0 nur lesen; niemals fremde/unklare CIDs stoppen oder force-räumen.
    print(json.dumps(dict(result,result='SYNTHETIC_PILOT_OK',ownNamesAbsent=True)))
    return 0


def main():
    """Geschlossener Normalaufruf; Fehler behalten den eigenen Arbeitsbereich und erlauben keinen Retry."""
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--opening',required=True)
    parser.add_argument('--docker',type=Path,required=True)
    parser.add_argument('--temp-root',type=Path,required=True)
    args=parser.parse_args()
    work=None;lease=None
    try:
        lease=validate_lease(args.opening,int(time.time()),os.environ)
        if not args.temp_root.is_absolute() or not args.temp_root.is_dir():raise ValueError('TEMP')
        file_digest(args.docker)
        work=Path(tempfile.mkdtemp(prefix='flowzer-pilot-',dir=args.temp_root))
        code=execute(lease,work,args.docker,lambda:int(time.time()))
        shutil.rmtree(work)  # Ausschließlich nach verifiziert terminalen Tools/Parent und Namensabwesenheit.
        return code
    except Exception:
        print(json.dumps(dict(result='PILOT_REJECTED',nonce=lease['nonce'] if lease else None,
                              work=str(work) if work else None,cleanup='CHECK_REQUIRED')))
        return 2


if __name__=='__main__':raise SystemExit(main())
