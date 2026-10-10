"""Frische feste Quellen-/Publish-/Registrybelege; keine Writes, Tokens nur im Prozess.

Die bestehende Quellenattestierung wird aus dem festen Publisher-Gitblob genutzt.
Roh-OCI-Configs und signierte Download-URLs verlassen weder Speicher noch Prozess.
"""
import base64
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from prepare import SOURCE, TREE, IMAGES, require, project_for

REPO='flowzer-io/flowzer-bpmn-core-engine'
PUBLISHER='bcaeef6438a4befbc3832d260883f3fd55ce6d8e'
PUBLISH_TREE='f5739455bd65fc45d6d0a7d6bba6fecbc85e3f99'
PUBLISH_RUN=38009656496
ACCEPT='application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json'


def digest(raw):return 'sha256:'+hashlib.sha256(raw).hexdigest()


def decode(raw):
    """Eindeutige JSON-Schlüssel, keine last-wins Sicherheitskonfiguration."""
    def unique(pairs):
        result={}
        for key,value in pairs:
            require(key not in result);result[key]=value
        return result
    return json.loads(raw,object_pairs_hook=unique)


def checked_json(raw,expected,header=None):
    require(re.fullmatch('sha256:[0-9a-f]{64}',expected) is not None
        and digest(raw)==expected and (header is None or header==expected))
    return decode(raw)


def receipt(raw,expected):
    """Genau ein endliches JSON-Mitglied, unveränderlicher ZIPhash und vollständiger Quelleninhalt."""
    require(digest(raw)==expected['zipSha256'])
    member=('manifest-receipt.json' if '-manifest-' in expected['name'] else
        ('source-proof.json' if expected['name']=='tt-demo-source-proof' else 'receipt.json'))
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        require(archive.namelist()==[member] and archive.getinfo(member).file_size<=65536)
        value=decode(archive.read(member));require(value==expected['receipt']);return value


def native(index):
    rows=[row for row in index['manifests'] if row.get('platform',{}).get('os')=='linux'
        and row.get('platform',{}).get('architecture')=='amd64']
    require(len(rows)==1 and re.fullmatch('sha256:[0-9a-f]{64}',rows[0]['digest']) is not None)
    return rows[0]


def redirect_allowed(url,kind):
    """HTTPS/443 ohne Credentials; Auth wird beim zweiten expliziten Request NIE weitergereicht."""
    target=urllib.parse.urlsplit(url)
    try:port=target.port
    except ValueError:return False
    if target.scheme!='https' or port not in (None,443) or target.username or target.password:return False
    if kind=='registry':return target.hostname=='pkg-containers.githubusercontent.com'
    return kind=='artifact' and re.fullmatch(r'productionresultssa[0-9]+\.blob\.core\.windows\.net',target.hostname or '') is not None


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs):return None


def read(url,headers=None,limit=1048576,redirect=None):
    """Feste vom Aufrufer gebildete URLs, ein Request, maximal ein erlaubter Blobredirect, kein Retry."""
    opener=urllib.request.build_opener(NoRedirect)
    try:response=opener.open(urllib.request.Request(url,headers=headers or {}),timeout=30)
    except urllib.error.HTTPError as error:
        target=error.headers.get('Location','');status=error.code;error.close()
        require(status in (302,307) and redirect_allowed(target,redirect))
        response=opener.open(urllib.request.Request(target,headers={'User-Agent':'flowzer-runtime-proof'}),timeout=30)
    with response:
        require(response.status==200);raw=response.read(limit+1);require(len(raw)<=limit)
        return raw,response.headers.get('Docker-Content-Digest')


def github(path,binary=False):
    token=os.environ.get('GH_TOKEN','');require(bool(token))
    raw,_=read('https://api.github.com/repos/'+REPO+'/'+path,
        {'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json',
         'X-GitHub-Api-Version':'2022-11-28','User-Agent':'flowzer-runtime-proof'},
        1048576, 'artifact' if binary else None)
    return raw if binary else decode(raw)


def publisher_library(root):
    """Nur exakt das schon geprüfte unveränderliche Publisherblob aus dem bestätigten Checkout laden."""
    def git(*args):return subprocess.check_output(['git','-C',str(root),*args],stderr=subprocess.DEVNULL,timeout=10)
    require(git('rev-parse','HEAD').decode().strip()==PUBLISHER
        and git('rev-parse','HEAD^{tree}').decode().strip()==PUBLISH_TREE)
    path=Path(root)/'scripts/demo-candidate/verify.py'
    require(path.read_bytes()==git('show',PUBLISHER+':scripts/demo-candidate/verify.py'))
    spec=importlib.util.spec_from_file_location('fixed_candidate_verify',path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module


def source_proof(context,publisher):
    """Original a3, unveränderlicher CI-Preview und sieben erhaltene a1-Publishreceipts frisch prüfen."""
    project_for(context)
    current=github('actions/runs/'+context['run_id'])
    require(current['id']==int(context['run_id']) and current['head_sha']==context['sha']
        and current['head_branch']=='codex/flowzer-runtime-calibration-pilot' and current['run_attempt']==1
        and current['event']=='workflow_dispatch' and current['path']=='.github/workflows/ci.yml'
        and current['status']=='in_progress')
    library=publisher_library(publisher)
    fixed={'repository':REPO,'ref':'refs/heads/codex/flowzer-demo-candidate',
        'event':'workflow_dispatch','attempt':'1','run_id':str(PUBLISH_RUN),
        'workflow_sha':PUBLISHER,'confirmed_sha':PUBLISHER}
    plan=library.candidate_plan(fixed,github('actions/runs/37994630569'),
        github('actions/runs/37994630569/jobs?filter=latest&per_page=100'),
        github('git/commits/399543b35af0d026c9e24fd39ddee61030d44b76'),
        github('actions/artifacts/11651284331'),SOURCE,TREE)
    binding=decode(Path(__file__).with_name('publish-bindings.json').read_bytes())
    require(binding['publisher_sha']==PUBLISHER and binding['publisher_run']==PUBLISH_RUN
        and binding['publisher_attempt']==1 and len(binding['receipts'])==7)
    run=github(f'actions/runs/{PUBLISH_RUN}');jobs=github(f'actions/runs/{PUBLISH_RUN}/jobs?filter=latest&per_page=100')
    require(run['id']==PUBLISH_RUN and run['head_sha']==PUBLISHER and run['run_attempt']==1
        and run['status']=='completed' and run['conclusion']=='success'
        and run['event']=='workflow_dispatch' and run['path']=='.github/workflows/ci.yml')
    require(jobs['total_count']==7 and len(jobs['jobs'])==7
        and sorted(j['name'] for j in jobs['jobs'])==binding['publisher_jobs']
        and all(j['run_id']==PUBLISH_RUN and j['run_attempt']==1 and j['status']=='completed'
            and j['conclusion']=='success' for j in jobs['jobs']))
    for expected in binding['receipts']:
        metadata=github('actions/artifacts/'+str(expected['id']))
        require(metadata['id']==expected['id'] and metadata['name']==expected['name']
            and metadata['expired'] is False and metadata['digest']==expected['zipSha256']
            and metadata['workflow_run']['id']==PUBLISH_RUN and metadata['workflow_run']['head_sha']==PUBLISHER)
        value=receipt(github('actions/artifacts/'+str(expected['id'])+'/zip',binary=True),expected)
        receipt_plan=value.get('plan',value.get('arguments',{}).get('plan',value))
        require(receipt_plan==plan)
    return binding


def registry_proof(binding):
    """Volle Publishdescriptorunion und native Configrevision; Baseimages mit festen nativen Configdigests."""
    headers={'Accept':ACCEPT,'User-Agent':'flowzer-runtime-proof'};configs={}
    rows=binding['receipts']
    for image in ['api','console']:
        repo='flowzer-io/flowzer-tt-demo-'+image
        token=os.environ.get('GH_TOKEN','');require(bool(token))
        auth=base64.b64encode(('x-access-token:'+token).encode()).decode()
        raw,_=read('https://ghcr.io/token?'+urllib.parse.urlencode({'service':'ghcr.io','scope':'repository:'+repo+':pull'}),
            {'Authorization':'Basic '+auth},65536);del auth
        bearer=decode(raw)['token'];require(type(bearer) is str and bool(bearer))
        def get(kind,dg):
            raw,header=read('https://ghcr.io/v2/'+repo+'/'+kind+'/'+dg,
                headers|{'Authorization':'Bearer '+bearer},1048576,'registry' if kind=='blobs' else None)
            return checked_json(raw,dg,header)
        manifest=next(row['receipt'] for row in rows if row['name']=='tt-demo-manifest-'+image)
        require(manifest['image']==IMAGES[image])
        index=get('manifests',manifest['digest']);descriptors=[]
        for source in manifest['arguments']['sources']:descriptors+=get('manifests',source.rsplit('@',1)[1])['manifests']
        canonical=lambda value:sorted(json.dumps(row,sort_keys=True) for row in value)
        require(canonical(descriptors)==canonical(index['manifests']))
        runtimes=[row for row in index['manifests'] if row.get('platform',{}).get('os')=='linux']
        require(sorted(row['platform']['architecture'] for row in runtimes)==['amd64','arm64'])
        for row in runtimes:
            runtime=get('manifests',row['digest']);config=get('blobs',runtime['config']['digest'])
            require(config['os']=='linux' and config['architecture']==row['platform']['architecture']
                and config['config']['Labels']['org.opencontainers.image.revision']==SOURCE)
            if config['architecture']=='amd64':configs[image]=runtime['config']['digest']
        del bearer
    base=decode(Path(__file__).with_name('base-images.json').read_bytes())['images']
    for name,service,host,repo in [('postgres','db','registry-1.docker.io','library/postgres'),
        ('keycloak','keycloak','quay.io','keycloak/keycloak'),('caddy','tls','registry-1.docker.io','library/caddy'),
        ('openssl','certs','registry-1.docker.io','alpine/openssl')]:
        expected=next(row for row in base if row['name']==name)
        require(IMAGES[service].rsplit('@',1)[1]==expected['index_digest'])
        auth_headers=headers.copy()
        if host=='registry-1.docker.io':
            raw,_=read('https://auth.docker.io/token?'+urllib.parse.urlencode({'service':'registry.docker.io','scope':'repository:'+repo+':pull'}),limit=65536)
            auth_headers['Authorization']='Bearer '+decode(raw)['token']
        def get_base(dg):
            raw,header=read('https://'+host+'/v2/'+repo+'/manifests/'+dg,auth_headers)
            return checked_json(raw,dg,header)
        index=get_base(expected['index_digest']);row=native(index)
        require(row['digest']==expected['linux_amd64_manifest'])
        runtime=get_base(row['digest']);dg=runtime['config']['digest']
        require(re.fullmatch('sha256:[0-9a-f]{64}',dg) is not None);configs[service]=dg
    configs['migrate']=configs['api'];return configs


def attest(context,publisher):
    """Vor Docker: nur bestätigte fixe Quellen, Runtimeconfigs und eigene Workflowidentität projizieren."""
    binding=source_proof(context,publisher);configs=registry_proof(binding)
    return dict(source_sha=SOURCE,source_tree=TREE,publisher_sha=PUBLISHER,publish_run=PUBLISH_RUN,
        publish_attempt=1,ci_run=37994630569,ci_attempt=3,receipt_count=7,
        workflow_sha=context['sha'],project=project_for(context),image_configs=configs)
