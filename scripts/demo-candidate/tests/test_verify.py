"""Testzwecke: echte geschlossene Quellenbindung, keine HTTP-/Registry-/Hostaktionen."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('candidate_verify', ROOT / 'scripts/demo-candidate/verify.py')
v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)


class CandidateTests(unittest.TestCase):
    def setUp(self):
        self.context = dict(repository='flowzer-io/flowzer-bpmn-core-engine',
            ref='refs/heads/codex/flowzer-demo-candidate', event='workflow_dispatch',
            attempt='1', run_id='123456789', workflow_sha='a' * 40, confirmed_sha='a' * 40)
        self.run = dict(id=v.CI_RUN, head_sha=v.SOURCE, event='pull_request',
            path='.github/workflows/ci.yml', run_attempt=3, status='completed', conclusion='success')
        self.jobs = dict(total_count=8, jobs=[dict(name=n, status='completed', conclusion='success',
            run_id=v.CI_RUN, run_attempt=3) for n in v.JOBS])
        self.commit = dict(sha=v.CHECKOUT, tree=dict(sha=v.TREE),
            parents=[dict(sha=v.BASE), dict(sha=v.SOURCE)])
        self.artifact = dict(id=v.CI_ARTIFACT, expired=False,
            workflow_run=dict(id=v.CI_RUN, head_sha=v.SOURCE))

    def plan(self):
        return v.candidate_plan(self.context, self.run, self.jobs, self.commit,
            self.artifact, v.SOURCE, v.TREE)

    # Testzweck: Nur der feste grüne Originalquelltree ergibt eigene unbenutzte Demo-Paketnamen.
    def test_valid_source_emits_closed_plan(self):
        plan = self.plan()
        self.assertEqual(plan['sourceSha'], v.SOURCE)
        self.assertEqual(plan['sourceTree'], v.TREE)
        self.assertEqual(plan['ciCheckout'], v.CHECKOUT)
        self.assertEqual(plan['ciRun'], v.CI_RUN)
        self.assertEqual(plan['tag'], f'demo-{v.SOURCE}-r123456789-a1')
        self.assertEqual(plan['images'], {image: f'ghcr.io/flowzer-io/flowzer-tt-demo-{image}'
            for image in ['api', 'console']})
        self.assertFalse(plan['installed'])

    # Testzweck: Push/PR/Main/Tag/Fork/Retry und unbestätigter Workflow-SHA dürfen nicht publizieren.
    def test_context_closed(self):
        variants = [('repository', 'foreign/repo'), ('ref', 'refs/heads/main'),
            ('ref', 'refs/tags/v1'), ('event', 'push'), ('event', 'pull_request'),
            ('attempt', '2'), ('run_id', '0'), ('run_id', '1;echo x'),
            ('workflow_sha', 'a' * 39), ('confirmed_sha', 'b' * 40)]
        for key, value in variants:
            with self.subTest(key=key, value=value):
                old = self.context[key]; self.context[key] = value
                with self.assertRaises(ValueError): self.plan()
                self.context[key] = old

    # Testzweck: Ein anderer/staler/roter Lauf ersetzt nicht die konkret gelesene Pflicht-CI.
    def test_ci_closed(self):
        for key, value in [('id', 1), ('head_sha', 'b' * 40), ('event', 'push'),
                ('path', '.github/workflows/diagnostic.yml'), ('run_attempt', 4),
                ('status', 'in_progress'), ('conclusion', 'failure')]:
            with self.subTest(key=key):
                old = self.run[key]; self.run[key] = value
                with self.assertRaises(ValueError): self.plan()
                self.run[key] = old

    # Testzweck: Fehlende/doppelte/zusätzliche/übersprungene Jobs und fremde Quellen bleiben geschlossen.
    def test_all_eight_original_jobs_required(self):
        original = copy.deepcopy(self.jobs)
        variants = [dict(total_count=7, jobs=original['jobs'][:-1]),
            dict(total_count=9, jobs=original['jobs'] + [original['jobs'][0]]),
            dict(total_count=8, jobs=[original['jobs'][0]] * 8)]
        for key, value in [('name', 'Other'), ('status', 'queued'), ('conclusion', 'skipped'),
                ('run_id', 1), ('run_attempt', 2)]:
            item = copy.deepcopy(original); item['jobs'][0][key] = value; variants.append(item)
        for item in variants:
            with self.subTest(item=item):
                self.jobs = item
                with self.assertRaises(ValueError): self.plan()
        self.jobs = original

    # Testzweck: PR-Head allein ist kein Tree-Beweis; genau der belegte Mergepreview muss passen.
    def test_source_tree_and_merge_parents_required(self):
        original = copy.deepcopy(self.commit)
        for path, value in [('sha', 'b' * 40), ('tree', dict(sha='b' * 40)),
                ('parents', [dict(sha=v.SOURCE)]),
                ('parents', [dict(sha=v.SOURCE), dict(sha='b' * 40)])]:
            with self.subTest(path=path):
                self.commit[path] = value
                with self.assertRaises(ValueError): self.plan()
                self.commit = copy.deepcopy(original)
        for head, tree in [('b' * 40, v.TREE), (v.SOURCE, 'b' * 40)]:
            with self.assertRaises(ValueError):
                v.candidate_plan(self.context, self.run, self.jobs, self.commit, self.artifact, head, tree)

    # Testzweck: Verfallene oder fremde Testartefakte erlauben keinen Quellen-Candidate.
    def test_real_ci_artifact_required(self):
        original = copy.deepcopy(self.artifact)
        for key, value in [('id', 1), ('expired', True),
                ('workflow_run', dict(id=1, head_sha=v.SOURCE)),
                ('workflow_run', dict(id=v.CI_RUN, head_sha='b' * 40))]:
            self.artifact[key] = value
            with self.assertRaises(ValueError): self.plan()
            self.artifact = copy.deepcopy(original)

    # Testzweck: Dieser Weg darf nie die shared Images-/Deploy-Workflows aufrufen oder normal auslösen.
    def test_workflow_is_manual_only_separate_source_proposal(self):
        s = (ROOT / '.github/workflows/ci.yml').read_text()
        self.assertTrue(s.startswith('name: TT-Demo Candidate (source proposal only)'))
        self.assertNotIn('workflow_call:', s)
        self.assertNotIn('push:', s)
        self.assertNotIn('pull_request:', s)
        self.assertNotIn('environment:', s)
        self.assertNotIn('secrets: inherit', s)
        self.assertNotIn('uses: ./.github/workflows/', s)
        self.assertNotIn('prod-next', s)
        self.assertNotIn('publish_latest', s)


if __name__ == '__main__':
    unittest.main()
