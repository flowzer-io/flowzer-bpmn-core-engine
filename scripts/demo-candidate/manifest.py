#!/usr/bin/env python3
"""Publiziert nur separat freigegebene Demo-Pakete; kein Host-/IdP-/Deploy-Zugriff."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import urllib.error
import urllib.parse
import urllib.request

from verify import NoRedirect, context_plan, invocation, require, unique_object


def manifest_arguments(receipts, context, image):
    """Schließt fremde/fehlende/duplizierte Quellen; keine frei gewählte Registry oder Tags."""
    plan = context_plan(context)
    require(image in plan['images'] and len(receipts) == 2)
    platforms = {r['platform'] for r in receipts}
    require(platforms == {'linux/amd64', 'linux/arm64'})
    by_platform = {}
    for r in receipts:
        require(set(r) == {'plan', 'image', 'platform', 'digest'} and r['plan'] == plan
            and r['image'] == plan['images'][image]
            and re.fullmatch('sha256:[0-9a-f]{64}', r['digest']) is not None)
        by_platform[r['platform']] = r['digest']
    return dict(plan=plan, image=image, target=f'{plan["images"][image]}:{plan["tag"]}',
        sources=[f'{plan["images"][image]}@{by_platform[p]}' for p in sorted(platforms)])


def registry_token(image):
    """Kurzlebiges reines Pulltoken nur am festen GHCR-Endpoint; keine Secretdatei/Logs."""
    scope = urllib.parse.urlencode({'service': 'ghcr.io', 'scope': f'repository:flowzer-io/flowzer-tt-demo-{image}:pull'})
    credentials = base64.b64encode(f'{os.environ["GITHUB_ACTOR"]}:{os.environ["GH_TOKEN"]}'.encode()).decode()
    request = urllib.request.Request(f'https://ghcr.io/token?{scope}',
        headers={'Authorization': f'Basic {credentials}', 'User-Agent': 'flowzer-tt-demo-candidate'})
    with urllib.request.build_opener(NoRedirect).open(request, timeout=15) as response:
        require(response.status == 200)
        data = response.read(65537)
        require(len(data) <= 65536)
        token = json.loads(data, object_pairs_hook=unique_object)['token']
        require(isinstance(token, str) and 0 < len(token) < 65536 and '\r' not in token and '\n' not in token)
        return token


def registry_manifest(image, tag, token, head=False):
    """Nur ein bestätigtes 404 bedeutet Tag fehlt; 401/403/Timeouts bleiben geschlossen."""
    request = urllib.request.Request(f'https://ghcr.io/v2/flowzer-io/flowzer-tt-demo-{image}/manifests/{tag}',
        method='HEAD' if head else 'GET', headers={'Authorization': f'Bearer {token}',
            'Accept': 'application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json',
            'User-Agent': 'flowzer-tt-demo-candidate'})
    try:
        response = urllib.request.build_opener(NoRedirect).open(request, timeout=15)
    except urllib.error.HTTPError as error:
        if error.code == 404 and head:
            error.close()
            return None
        error.close()
        raise ValueError('Registryzustand nicht bestätigt.') from None
    with response:
        require(response.status == 200)
        data = response.read(1048577)
        require(len(data) <= 1048576)
        return data, response.headers.get('Docker-Content-Digest')


def publish(arguments, context, execute=subprocess.run):
    """Ein neuer eindeutiger Run-Tag; kein Retry/Overwrite/Deploy und kein shell=True."""
    plan = context_plan(context)
    image = arguments['image']
    # Argumentdatei ist kein freier Effektcallback: vollständig nochmals rekonstruieren.
    require(image in plan['images'])
    receipts = [dict(plan=plan, image=plan['images'][image], platform=p,
        digest=source.rsplit('@', 1)[1]) for p, source in zip(['linux/amd64', 'linux/arm64'], arguments['sources'])]
    require(arguments == manifest_arguments(receipts, context, image))
    token = registry_token(image)
    require(registry_manifest(image, plan['tag'], token, head=True) is None)
    descriptors = []
    for platform, source in zip(['amd64', 'arm64'], arguments['sources']):
        expected = source.rsplit('@', 1)[1]
        source_raw, source_digest = registry_manifest(image, expected, token)
        require(source_digest == expected == 'sha256:' + hashlib.sha256(source_raw).hexdigest())
        children = json.loads(source_raw, object_pairs_hook=unique_object)['manifests']
        require(len(children) in [1, 2])
        runtime = [d for d in children if d['platform']['os'] != 'unknown']
        require(len(runtime) == 1 and runtime[0]['platform']['os'] == 'linux'
            and runtime[0]['platform']['architecture'] == platform)
        require(all((d['platform']['os'], d['platform']['architecture']) in
            {('linux', platform), ('unknown', 'unknown')} for d in children))
        descriptors.extend(children)
    execute(['docker', 'buildx', 'imagetools', 'create', '--tag', arguments['target'], *arguments['sources']],
        check=True, timeout=300, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    raw, digest = registry_manifest(image, plan['tag'], token)
    require(digest == 'sha256:' + hashlib.sha256(raw).hexdigest())
    index = json.loads(raw, object_pairs_hook=unique_object)
    # Imagetools flacht die zwei Quellindizes ab. Nicht bloß Plattformnamen zählen:
    # alle runtime-/provenance-Deskriptoren müssen exakt aus den belegten Digests stammen.
    canonical = lambda ds: sorted(json.dumps(d, sort_keys=True, separators=(',', ':')) for d in ds)
    require(canonical(index['manifests']) == canonical(descriptors))
    return dict(arguments=arguments, digest=digest, image=f'{plan["images"][image]}@{digest}', installed=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['arguments', 'publish'])
    parser.add_argument('--receipts', type=Path)
    parser.add_argument('--arguments', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        context = invocation()
        context_plan(context)  # Vor jedem Registry-I/O.
        if args.operation == 'arguments':
            require(args.receipts is not None)
            files = list(args.receipts.glob('*/receipt.json'))
            require(len(files) == 2)
            receipts = [json.loads(p.read_text(), object_pairs_hook=unique_object) for p in files]
            result = manifest_arguments(receipts, context, os.environ['CANDIDATE_IMAGE'])
        else:
            require(args.arguments is not None)
            result = publish(json.loads(args.arguments.read_text(), object_pairs_hook=unique_object), context)
        args.output.write_text(json.dumps(result, indent=2) + '\n')
        print('Demo-Manifestbeleg gesichert; keine Installation.')
    except Exception:
        parser.exit(1, 'Demo-Manifest nicht bestätigt; kein Installationsbeleg.\n')


if __name__ == '__main__':
    main()
