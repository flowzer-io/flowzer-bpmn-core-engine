"""Testzwecke: OCI-Belege und Registry-Aktionsgrenzen ohne Docker/Netzwerk prüfen."""
import copy
import importlib.util
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'scripts/demo-candidate'))
import verify as v
import manifest as m


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.context = dict(repository=v.REPOSITORY, ref=v.BRANCH, event='workflow_dispatch',
            attempt='1', run_id='123456789', workflow_sha='a' * 40, confirmed_sha='a' * 40)
        self.plan = v.context_plan(self.context)
        self.receipts = [v.build_receipt(self.plan, self.context, v.SOURCE, v.TREE, 'api',
            platform, f'sha256:{char * 64}') for platform, char in [('linux/amd64', 'a'), ('linux/arm64', 'b')]]

    # Testzweck: Nur genau zwei native Architekturbelege dürfen ein eigenes Demo-Manifest ergeben.
    def test_exact_receipts(self):
        args = m.manifest_arguments(self.receipts, self.context, 'api')
        self.assertEqual(args['target'], f'ghcr.io/flowzer-io/flowzer-tt-demo-api:{self.plan["tag"]}')
        self.assertEqual(args['sources'], [f'ghcr.io/flowzer-io/flowzer-tt-demo-api@sha256:{char * 64}' for char in 'ab'])

    # Testzweck: Manipulierte Pakete, Plattformen, Quell-/Aufrufbelege und Digests dürfen nicht publizieren.
    def test_receipts_closed(self):
        original = copy.deepcopy(self.receipts)
        variants = [original[:1], original + [original[0]], [original[0], original[0]]]
        for key, value in [('image', 'ghcr.io/flowzer-io/flowzer-api'), ('platform', 'linux/x86'),
            ('digest', 'sha256:' + 'a' * 63), ('digest', 'sha256:aaa;echo token'),
            ('plan', {**self.plan, 'sourceSha': 'b' * 40}),
            ('plan', {**self.plan, 'buildRun': 5})]:
            x = copy.deepcopy(original); x[0][key] = value; variants.append(x)
        for x in variants:
            with self.subTest(receipts=x):
                with self.assertRaises(ValueError): m.manifest_arguments(x, self.context, 'api')
        with self.assertRaises(ValueError): m.manifest_arguments(original, self.context, 'console')
        with self.assertRaises(ValueError): m.manifest_arguments(original, self.context, 'foreign')

    # Testzweck: Jeder Retry muss auch bei schon erhaltener Quellenattestation vor Publikation schließen.
    def test_repeat_closed(self):
        self.context['attempt'] = '2'
        with self.assertRaises(ValueError): m.manifest_arguments(self.receipts, self.context, 'api')




class PublicationBoundaryTests(unittest.TestCase):
    def setUp(self):
        ManifestTests.setUp(self)
        import hashlib
        import json
        self.contents = []
        self.descriptors = []
        for architecture, char in [('amd64', 'c'), ('arm64', 'd')]:
            descriptor = dict(mediaType='application/vnd.oci.image.manifest.v1+json', size=100,
                digest=f'sha256:{char * 64}', platform=dict(os='linux', architecture=architecture))
            raw = json.dumps(dict(schemaVersion=2, manifests=[descriptor])).encode()
            self.contents.append((raw, 'sha256:' + hashlib.sha256(raw).hexdigest()))
            self.descriptors.append(descriptor)
        self.receipts = [v.build_receipt(self.plan, self.context, v.SOURCE, v.TREE, 'api',
            f'linux/{arch}', body[1]) for arch, body in zip(['amd64', 'arm64'], self.contents)]
        self.arguments = m.manifest_arguments(self.receipts, self.context, 'api')
        self.raw = json.dumps(dict(schemaVersion=2, manifests=self.descriptors)).encode()
        self.final = self.raw, 'sha256:' + hashlib.sha256(self.raw).hexdigest()

    # Testzweck: Reale Publikationsfunktion ruft genau einen geschlossenen Dockerargv ohne Shell auf.
    def test_one_effect_bound_to_readback(self):
        from unittest.mock import Mock, patch
        effect = Mock()
        with patch.object(m, 'registry_token', return_value='synthetic'), patch.object(m, 'registry_manifest',
                side_effect=[None, *self.contents, self.final]) as reads:
            result = m.publish(self.arguments, self.context, effect)
        effect.assert_called_once_with(['docker', 'buildx', 'imagetools', 'create', '--tag',
            self.arguments['target'], *self.arguments['sources']], check=True, timeout=300,
            stdout=m.subprocess.DEVNULL, stderr=m.subprocess.DEVNULL)
        self.assertEqual(result['image'], self.plan['images']['api'] + '@' + self.final[1])
        self.assertFalse(result['installed'])
        self.assertEqual(reads.call_count, 4)

    # Testzweck: Vorhandener Tag oder unklarer Registryzustand darf vor Docker niemals schreiben.
    def test_tag_present_or_uncertain_has_no_effect(self):
        from unittest.mock import Mock, patch
        for response in [(b'', 'synthetic'), ValueError('synthetic')]:
            effect = Mock()
            with patch.object(m, 'registry_token', return_value='synthetic'), patch.object(m, 'registry_manifest',
                    side_effect=[response]):
                with self.assertRaises(ValueError): m.publish(self.arguments, self.context, effect)
            effect.assert_not_called()

    # Testzweck: Falscher Quellmanifest-Digest/Plattform muss vor Registry-Schreibwirkung schließen.
    def test_source_metadata_tamper_before_effect(self):
        from unittest.mock import Mock, patch
        for bad in [(self.contents[0][0], 'sha256:' + 'e' * 64), self.contents[1]]:
            effect = Mock()
            with patch.object(m, 'registry_token', return_value='synthetic'), patch.object(m, 'registry_manifest',
                    side_effect=[None, bad]):
                with self.assertRaises(ValueError): m.publish(self.arguments, self.context, effect)
            effect.assert_not_called()

    # Testzweck: Nach einem Effekt bestätigt falscher finaler Digest/Inhalt keinen installierbaren Candidate.
    def test_final_digest_or_descriptor_tamper_fails_without_second_effect(self):
        from unittest.mock import Mock, patch
        import hashlib
        import json
        wrong_raw = json.dumps(dict(manifests=[self.descriptors[0]] * 2)).encode()
        for final in [(self.raw, 'sha256:' + 'e' * 64),
                (wrong_raw, 'sha256:' + hashlib.sha256(wrong_raw).hexdigest())]:
            effect = Mock()
            with patch.object(m, 'registry_token', return_value='synthetic'), patch.object(m, 'registry_manifest',
                    side_effect=[None, *self.contents, final]):
                with self.assertRaises(ValueError): m.publish(self.arguments, self.context, effect)
            self.assertEqual(effect.call_count, 1)

    # Testzweck: Allein ein echtes HEAD-404 am festen GHCR-Ziel heißt frei, nie andere HTTP-/Netzfehler.
    def test_registry_absence_classification_and_no_redirect(self):
        from unittest.mock import Mock, patch
        import urllib.error
        for code in [301, 401, 403, 404, 429, 500]:
            error = urllib.error.HTTPError('https://ghcr.io/synthetic', code, '', {}, None)
            opener = Mock(); opener.open.side_effect = error
            with patch.object(m.urllib.request, 'build_opener', return_value=opener) as factory:
                if code == 404:
                    self.assertIsNone(m.registry_manifest('api', self.plan['tag'], 'synthetic', head=True))
                else:
                    with self.assertRaises(ValueError): m.registry_manifest('api', self.plan['tag'], 'synthetic', head=True)
            self.assertIs(factory.call_args.args[0], v.NoRedirect)
            self.assertEqual(opener.open.call_args.kwargs['timeout'], 15)


if __name__ == '__main__': unittest.main()
