"""Testzweck: Nur exakt der neue eigene Kandidat, ohne Workflow-/Provider-/Runtimeausführung."""
from pathlib import Path
import unittest
from unittest.mock import patch
import prepare
import proof
import runner
import test_workflow

OWN_BRANCH = 'codex/flowzer-runtime-pilot-diagnostics'
OWN_REF = 'refs/heads/' + OWN_BRANCH
DENIED_REFS = ('refs/heads/main', 'refs/heads/release', 'refs/heads/codex/foreign',
    'refs/heads/codex/flowzer-demo-runtime-budget', 'refs/heads/codex/flowzer-runtime-diagnostics',
    OWN_REF + '-other', OWN_REF + '/child', 'refs/tags/' + OWN_BRANCH)
CONTEXT = dict(repository='flowzer-io/flowzer-bpmn-core-engine', ref=OWN_REF,
    event='workflow_dispatch', attempt='1', run_id='123456', sha='a' * 40, confirmed_sha='a' * 40,
    runner_environment='github-hosted', runner_os='Linux', runner_arch='X64')


class ReachedPublisher(Exception):
    """Synthetischer Stopp direkt hinter der eigenen API-Ref-Prüfung, noch vor Publisher-I/O."""


class CandidateRefTests(unittest.TestCase):
    def test_only_exact_new_ref_passes_context_and_runner_without_io(self):
        # Testzweck: Auch alter Ressourcen-/Archivbranch bleiben ausgeschlossen; keine Topic-Wildcard.
        for ref in (OWN_REF,) + DENIED_REFS:
            for guard in (prepare.project_for, lambda value: runner.environment_guard(value, {})):
                with self.subTest(ref=ref, guard=guard.__name__), \
                        patch.object(prepare.subprocess, 'check_output') as process:
                    try:
                        guard(CONTEXT | {'ref': ref})
                        accepted = True
                    except ValueError:
                        accepted = False
                    self.assertEqual(ref == OWN_REF, accepted)
                    process.assert_not_called()

    def reaches_publisher(self, ref, head_branch):
        """Nur synthetisches aktuelles Run-Metadatum; feste Publisher-/Receiptprüfung bleibt separat."""
        current = dict(id=123456, head_sha='a' * 40, head_branch=head_branch, run_attempt=1,
            event='workflow_dispatch', path='.github/workflows/ci.yml', status='in_progress')
        with patch.object(proof, 'github', return_value=current), \
                patch.object(proof, 'publisher_library', side_effect=ReachedPublisher):
            try:
                proof.source_proof(CONTEXT | {'ref': ref}, Path('/unused'))
            except ReachedPublisher:
                return True
            except ValueError:
                return False
        self.fail('Der synthetische Stopp vor Publisher-I/O muss erreicht oder geschlossen abgelehnt werden.')

    def test_current_api_branch_must_match_the_same_exact_candidate(self):
        # Testzweck: Env-Ref allein reicht nicht; aktuelle GitHub-Run-Identität wird unabhängig gebunden.
        for ref in (OWN_REF,) + DENIED_REFS:
            head_branch = ref.removeprefix('refs/heads/')
            with self.subTest(ref=ref):
                self.assertEqual(ref == OWN_REF, self.reaches_publisher(ref, head_branch))
        for head_branch in ('main', 'release', 'codex/foreign', 'codex/flowzer-demo-runtime-budget',
                'codex/flowzer-runtime-diagnostics', OWN_BRANCH + '-other'):
            with self.subTest(current_api_branch=head_branch):
                self.assertFalse(self.reaches_publisher(OWN_REF, head_branch))

    def test_workflow_has_only_the_exact_candidate_conjunction(self):
        # Testzweck: Nicht nur passende Teilstrings; keine Alternative oder weggefallene Schutzbedingung.
        expression = test_workflow.WorkflowTests().config()['jobs']['resources']['if']
        expected = ("github.repository == 'flowzer-io/flowzer-bpmn-core-engine' && "
            "github.ref == 'refs/heads/codex/flowzer-runtime-pilot-diagnostics' && "
            "github.event_name == 'workflow_dispatch' && github.run_attempt == 1 && "
            'inputs.confirmed_workflow_sha == github.sha')
        self.assertEqual(expected, ' '.join(expression.split()))


if __name__ == '__main__':
    unittest.main()
