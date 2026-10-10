"""Testzweck: Quellen-/Receipt-/Registrygrenzen ohne Netz, Token oder Container."""
import copy
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch,Mock
import zipfile
import proof

class ProofTests(unittest.TestCase):
    def test_receipt_requires_exact_hash_member_and_bytes(self):
        # Testzweck: Keine fremden oder abgelaufenen Publisherreceipts als Quellenfreigabe akzeptieren.
        fixture=json.loads(Path(__file__).with_name('publish-bindings.json').read_text())['receipts'][0]
        buffer=io.BytesIO()
        with zipfile.ZipFile(buffer,'w') as archive:
            archive.writestr('receipt.json',json.dumps(fixture['receipt']))
        raw=buffer.getvalue();fixture['zipSha256']=proof.digest(raw)
        self.assertEqual(fixture['receipt'],proof.receipt(raw,fixture))
        for mutation in [raw+b'changed', b'not-a-zip']:
            with self.assertRaises(ValueError):proof.receipt(mutation,fixture)

    def test_native_requires_exactly_one_linux_amd64(self):
        # Testzweck: Kein ARM-/Provenance-/mehrdeutiger Index statt nativer Runtimequelle.
        row={'digest':'sha256:'+'a'*64,'platform':{'os':'linux','architecture':'amd64'}}
        self.assertEqual(row,proof.native({'manifests':[row]}))
        for value in [{'manifests':[row,row]}, {'manifests':[]},
            {'manifests':[row|{'platform':{'os':'linux','architecture':'arm64'}}]}]:
            with self.assertRaises(ValueError):proof.native(value)

    def test_json_duplicate_keys_are_closed(self):
        # Testzweck: Widersprüchliche Sicherheitsbindung wird nicht last-wins geparst.
        with self.assertRaises(ValueError):proof.decode(b'{"id":1,"id":2}')
        self.assertEqual({'id':1},proof.decode(b'{"id":1}'))

    def test_blob_redirect_never_forwards_auth_or_accepts_foreign_scheme(self):
        # Testzweck: Nur feste GitHub-Blob-Redirectziele, keine URL-Credentials oder beliebigen Ziele.
        self.assertTrue(proof.redirect_allowed('https://pkg-containers.githubusercontent.com/a?synthetic=1','registry'))
        self.assertTrue(proof.redirect_allowed('https://productionresultssa8.blob.core.windows.net/a','artifact'))
        for value in ['http://pkg-containers.githubusercontent.com/a','https://user:pass@pkg-containers.githubusercontent.com/a',
            'https://foreign.test/a','https://pkg-containers.githubusercontent.com:444/a']:
            self.assertFalse(proof.redirect_allowed(value,'registry'))

    def test_descriptor_digest_is_verified_before_json_read(self):
        # Testzweck: Antwortbytes statt bloß Registryheader attestieren den referenzierten Inhalt.
        raw=b'{"safe":1}'
        digest=proof.digest(raw)
        self.assertEqual({'safe':1},proof.checked_json(raw,digest,digest))
        for body,expected,header in [(raw+b' ',digest,digest),(raw,digest,'sha256:'+'a'*64)]:
            with self.assertRaises(ValueError):proof.checked_json(body,expected,header)

    def test_fresh_current_run_and_all_publish_receipts_are_required(self):
        # Testzweck: Kein Spoof von Hosted-Env oder altem Publisherstatus; alle sieben Belege frisch lesen.
        binding=json.loads(Path(__file__).with_name('publish-bindings.json').read_text())
        plan=next(row['receipt'] for row in binding['receipts'] if row['name']=='tt-demo-source-proof')
        context=dict(repository='flowzer-io/flowzer-bpmn-core-engine',ref='refs/heads/codex/flowzer-runtime-calibration-pilot',
            event='workflow_dispatch',attempt='1',run_id='123456',sha='a'*40,confirmed_sha='a'*40,
            runner_environment='github-hosted',runner_os='Linux',runner_arch='X64')
        current=dict(id=123456,head_sha='a'*40,head_branch='codex/flowzer-runtime-calibration-pilot',run_attempt=1,
            event='workflow_dispatch',path='.github/workflows/ci.yml',status='in_progress')
        publish=dict(id=38009656496,head_sha=proof.PUBLISHER,run_attempt=1,status='completed',conclusion='success',
            event='workflow_dispatch',path='.github/workflows/ci.yml')
        jobs={'total_count':7,'jobs':[dict(name=name,run_id=38009656496,run_attempt=1,status='completed',conclusion='success')
            for name in binding['publisher_jobs']]}
        def metadata(path,binary=False):
            if path=='actions/runs/123456':return current
            if path=='actions/runs/38009656496':return publish
            if path.startswith('actions/runs/38009656496/jobs'):return jobs
            if path.startswith('actions/artifacts/') and path!='actions/artifacts/11651284331':
                row=next(row for row in binding['receipts'] if str(row['id'])==path.split('/')[2])
                return b'synthetic-zip' if binary else dict(id=row['id'],name=row['name'],expired=False,digest=row['zipSha256'],
                    workflow_run=dict(id=38009656496,head_sha=proof.PUBLISHER))
            return {}
        library=Mock();library.candidate_plan.return_value=plan
        with patch.object(proof,'publisher_library',return_value=library),patch.object(proof,'github',side_effect=metadata) as read,\
            patch.object(proof,'receipt',side_effect=lambda raw,row:row['receipt']) as receipts:
            self.assertEqual(binding,proof.source_proof(context,Path('/unused')))
            self.assertEqual(7,receipts.call_count)
            current['head_sha']='b'*40
            with self.assertRaises(ValueError):proof.source_proof(context,Path('/unused'))

if __name__=='__main__':unittest.main()
