"""Testzweck: Temporärer manueller Workflow darf nie Push-/PR- oder Deployrechte erhalten."""
import json
from pathlib import Path
import subprocess
import unittest
ROOT=Path(__file__).resolve().parents[2]

class WorkflowTests(unittest.TestCase):
    def config(self):
        raw=subprocess.check_output(['ruby','-ryaml','-rjson','-e','puts JSON.generate(YAML.load_file(ARGV[0]))',
            str(ROOT/'.github/workflows/ci.yml')],stderr=subprocess.DEVNULL,timeout=5)
        return json.loads(raw)

    def test_only_confirmed_manual_dispatch_exists(self):
        # Testzweck: Quellenpushs oder PR-Ereignisse dürfen keinen Ressourcenjob öffnen.
        config=self.config();self.assertEqual({'workflow_dispatch'},set(config['on']))
        inputs=config['on']['workflow_dispatch']['inputs'];self.assertEqual({'confirmed_workflow_sha'},set(inputs))
        self.assertIs(inputs['confirmed_workflow_sha']['required'],True)
        self.assertEqual('string',inputs['confirmed_workflow_sha']['type'])

    def test_every_job_is_own_sha_bound_hosted_first_attempt_readonly(self):
        # Testzweck: Auch Wiederanlauf/umgebogene Ref-/Repo-/SHA-Bindung bleibt geschlossen.
        config=self.config();self.assertEqual({},config['permissions'])
        self.assertEqual({'resources'},set(config['jobs']))
        for job in config['jobs'].values():
            self.assertEqual('ubuntu-24.04',job['runs-on'])
            for term in ["github.repository == 'flowzer-io/flowzer-bpmn-core-engine'",
                "github.ref == 'refs/heads/codex/flowzer-runtime-calibration-pilot'",
                "github.event_name == 'workflow_dispatch'",'github.run_attempt == 1','inputs.confirmed_workflow_sha == github.sha']:
                self.assertIn(term,job['if'])
            self.assertEqual({'contents':'read','packages':'read','actions':'read'},job['permissions'])
            self.assertNotIn('environment',job)

    def test_checkouts_and_uploaded_report_are_strictly_fixed(self):
        # Testzweck: Produktquelle, Publisher und Workflowhead bleiben getrennt; keine Rohlogs/Dumps uploaden.
        steps=self.config()['jobs']['resources']['steps']
        checkouts=[step for step in steps if step.get('uses','').startswith('actions/checkout@')]
        self.assertEqual(2,len(checkouts))
        self.assertEqual(['${{ github.sha }}','bcaeef6438a4befbc3832d260883f3fd55ce6d8e'],[step['with']['ref'] for step in checkouts])
        self.assertTrue(all(step['with']['persist-credentials'] is False for step in checkouts))
        self.assertEqual(0,checkouts[0]['with']['fetch-depth'])
        uploads=[step for step in steps if step.get('uses','').startswith('actions/upload-artifact@')]
        self.assertEqual(1,len(uploads));self.assertEqual('always()',uploads[0]['if'])
        self.assertEqual('${{ runner.temp }}/flowzer-runtime-${{ github.run_id }}-a1/report/resource-result.json',uploads[0]['with']['path'])
        raw=(ROOT/'.github/workflows/ci.yml').read_text()
        for value in ['secrets.','packages: write','docker/login','push:','pull_request:','environment:']:
            self.assertNotIn(value,raw)
        self.assertIn('--publisher publisher',raw)

    def test_all_other_original_workflows_are_byte_equal(self):
        # Testzweck: Die temporäre Sonder-CI darf keinen Originalrelease-/Stagingworkflow verändern.
        files=subprocess.check_output(['git','-C',str(ROOT),'ls-tree','-r','--name-only',
            'cf082ccd9a165429774de9aab907d3583832279c','--','.github/workflows'],text=True,timeout=5).splitlines()
        for name in files:
            if name=='.github/workflows/ci.yml':continue
            original=subprocess.check_output(['git','-C',str(ROOT),'show','cf082ccd9a165429774de9aab907d3583832279c:'+name],timeout=5)
            self.assertEqual(original,(ROOT/name).read_bytes())

if __name__=='__main__':unittest.main()
