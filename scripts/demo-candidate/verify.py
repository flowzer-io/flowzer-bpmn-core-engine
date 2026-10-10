#!/usr/bin/env python3
"""Geschlossene Demo-Candidate-Bindung; weder Registry- noch Host-/IdP-Schreibzugriff."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.request

SOURCE = 'cf082ccd9a165429774de9aab907d3583832279c'
TREE = '38362a4d81f72ed1550c926883b5bb7f28ac50b5'
CHECKOUT = '399543b35af0d026c9e24fd39ddee61030d44b76'
BASE = '073e595748ba7bec56218407c5426474a4f6ba1e'
CI_RUN = 37994630569
CI_ARTIFACT = 11651284331
REPOSITORY = 'flowzer-io/flowzer-bpmn-core-engine'
BRANCH = 'refs/heads/codex/flowzer-demo-candidate'
JOBS = frozenset([
    'Build und Tests der React-Konsole', 'Restore, Build und Tests (.NET)',
    'Headless SDK und React-Bausteine', 'Abhängigkeits-/Lizenzhinweise auf Drift pruefen',
    'Installations- und Auth-Abnahme (isolierter Keycloak)',
    'Upgrade- und Restore-Rig (echte Images)', 'UI-Smoke-Tests der Konsole',
    'Sicherung und Wiederherstellung (Skripttest)',
])


def require(condition):
    """Keine Quellantwort/Identität/Exceptiondetails an die Loggrenze geben."""
    if not condition:
        raise ValueError('Demo-Candidate-Bindung nicht bestätigt.')


def context_plan(context):
    """Nur ein ausdrücklich bestätigter eigener manueller Erstlauf besitzt eigene Demo-Tags."""
    require(context['repository'] == REPOSITORY and context['ref'] == BRANCH
        and context['event'] == 'workflow_dispatch' and context['attempt'] == '1')
    require(re.fullmatch('[0-9a-f]{40}', context['workflow_sha']) is not None
        and context['confirmed_sha'] == context['workflow_sha'])
    require(re.fullmatch('[1-9][0-9]{0,19}', context['run_id']) is not None)
    return dict(sourceSha=SOURCE, sourceTree=TREE, ciCheckout=CHECKOUT, ciRun=CI_RUN,
        ciAttempt=3, ciArtifact=CI_ARTIFACT, workflowSha=context['workflow_sha'],
        buildRun=int(context['run_id']), buildAttempt=1,
        tag=f'demo-{SOURCE}-r{context["run_id"]}-a1',
        images={i: f'ghcr.io/flowzer-io/flowzer-tt-demo-{i}' for i in ['api', 'console']},
        installed=False)


def candidate_plan(context, run, jobs, commit, artifact, source_head, source_tree):
    """Verlangt die acht echten CI-Jobs, den belegten Preview-Tree und erhaltene Testartefakte."""
    plan = context_plan(context)
    require(source_head == SOURCE and source_tree == TREE)
    require(run['id'] == CI_RUN and run['head_sha'] == SOURCE and run['event'] == 'pull_request'
        and run['path'] == '.github/workflows/ci.yml' and run['run_attempt'] == 3
        and run['status'] == 'completed' and run['conclusion'] == 'success')
    entries = jobs['jobs']
    require(jobs['total_count'] == 8 and len(entries) == 8
        and {j['name'] for j in entries} == JOBS)
    require(all(j['status'] == 'completed' and j['conclusion'] == 'success'
        and j['run_id'] == CI_RUN and j['run_attempt'] == 3 for j in entries))
    require(commit['sha'] == CHECKOUT and commit['tree']['sha'] == TREE
        and len(commit['parents']) == 2 and {p['sha'] for p in commit['parents']} == {BASE, SOURCE})
    require(artifact['id'] == CI_ARTIFACT and artifact['expired'] is False
        and artifact['workflow_run']['id'] == CI_RUN and artifact['workflow_run']['head_sha'] == SOURCE)
    # Der Job-Metadatensatz der a3 enthält auch zwei bereits erfolgreiche Vorgängerjobs;
    # dieser Nachweis behauptet nicht, dass alle acht Jobs frisch ausgeführt wurden.
    return plan


def validate_plan(plan, context, source_head, source_tree):
    """Auch ein wiederholter Einzeljob muss vor Registrylogin am frischen Aufruf schließen."""
    require(source_head == SOURCE and source_tree == TREE and plan == context_plan(context))


def build_receipt(plan, context, source_head, source_tree, image, platform, digest):
    """Nicht geheimes OCI-Receipt; kein Secret/Tag ist eine Deploymentautorisierung."""
    validate_plan(plan, context, source_head, source_tree)
    require(image in ['api', 'console'] and platform in ['linux/amd64', 'linux/arm64']
        and re.fullmatch('sha256:[0-9a-f]{64}', digest) is not None)
    return dict(plan=plan, image=plan['images'][image], platform=platform, digest=digest)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """GitHub-Token darf niemals an ein Redirectziel weitergereicht werden."""
    def redirect_request(self, *args, **kwargs):
        return None


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result)
        result[key] = value
    return result


def github_metadata(path):
    """Feste HTTPS-API, kein Retry; max. ein MiB und keine Raw-Fehlerausgabe."""
    token = os.environ['GH_TOKEN']
    require(bool(token))
    request = urllib.request.Request(f'https://api.github.com/repos/{REPOSITORY}/{path}',
        headers={'Authorization': f'Bearer {token}', 'Accept': 'application/vnd.github+json',
            'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'flowzer-tt-demo-candidate'})
    with urllib.request.build_opener(NoRedirect).open(request, timeout=15) as response:
        require(response.status == 200 and response.headers.get_content_type() == 'application/json')
        data = response.read(1048577)
        require(len(data) <= 1048576)
        return json.loads(data, object_pairs_hook=unique_object)


def invocation():
    names = {'repository': 'GITHUB_REPOSITORY', 'ref': 'GITHUB_REF', 'event': 'GITHUB_EVENT_NAME',
        'attempt': 'GITHUB_RUN_ATTEMPT', 'run_id': 'GITHUB_RUN_ID', 'workflow_sha': 'GITHUB_SHA',
        'confirmed_sha': 'CONFIRMED_WORKFLOW_SHA'}
    return {key: os.environ[value] for key, value in names.items()}


def checkout(path):
    return tuple(subprocess.check_output(['git', '-C', str(path), 'rev-parse', ref],
        timeout=10, stderr=subprocess.DEVNULL, text=True).strip() for ref in ['HEAD', 'HEAD^{tree}'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['proof', 'check-plan', 'receipt'])
    parser.add_argument('--product', type=Path, required=True)
    parser.add_argument('--plan', type=Path, required=True)
    parser.add_argument('--image', choices=['api', 'console'])
    parser.add_argument('--platform', choices=['linux/amd64', 'linux/arm64'])
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    try:
        context = invocation()
        context_plan(context)  # Vor jedem externen I/O; Fail-closed bei Retry/Fremdref.
        head, tree = checkout(args.product)
        require(head == SOURCE and tree == TREE)
        if args.operation == 'proof':
            plan = candidate_plan(context, github_metadata(f'actions/runs/{CI_RUN}'),
                github_metadata(f'actions/runs/{CI_RUN}/jobs?filter=latest&per_page=100'),
                github_metadata(f'git/commits/{CHECKOUT}'),
                github_metadata(f'actions/artifacts/{CI_ARTIFACT}'), head, tree)
            args.plan.write_text(json.dumps(plan, indent=2) + '\n')
        else:
            plan = json.loads(args.plan.read_text(), object_pairs_hook=unique_object)
            validate_plan(plan, context, head, tree)
            if args.operation == 'receipt':
                require(args.output is not None)
                result = build_receipt(plan, context, head, tree, args.image, args.platform,
                    os.environ['IMAGE_DIGEST'])
                args.output.write_text(json.dumps(result, indent=2) + '\n')
        print('Demo-Candidate-Bindung bestätigt; keine Installation.')
    except Exception:
        # Auch URL-/Body-/Token-/Subprocessdetails bleiben außerhalb von Actions-Logs.
        parser.exit(1, 'Demo-Candidate-Prüfung fehlgeschlagen; kein freigegebener Quellenstand.\n')


if __name__ == '__main__':
    main()
