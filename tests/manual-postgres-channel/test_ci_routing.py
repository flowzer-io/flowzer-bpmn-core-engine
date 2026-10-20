"""Testzweck: enge bestehende CI-Route, unveränderte acht Normal-/Alt-Pilotjobs.

Reiner Sourcevertrag, keine GitHub-Ausführung oder allgemeiner Expressionparser.
Die exakt geprüften einfachen GitHub-Bedingungen werden als Wahrheitstabelle
nachgebildet; tatsächliche Jobs/SKIPs bleiben eine spätere Hosted-Abnahme.
"""
import hashlib,importlib.util,json,re,sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
WF=ROOT/'.github/workflows/ci.yml'
NORMAL="${{ github.event_name != 'workflow_dispatch' || inputs.pg_channel_opening == '' }}"
PG="${{ github.event_name == 'workflow_dispatch' && inputs.pg_channel_opening != '' }}"
BASE_PINS={'dotnet': 'c783940e16966582b51090a31723ab4561af7fa2024f2b1e9975ef25d2180a56', 'console': '14a0816c9d3dded8afdd07aa0025c3224516ac84a89bf714270a84295041a1f8', 'typescript_sdk': '62d323801a8360560ffcc42e96fbdb4bf457b9e799b8db373d85fcaf6200967c', 'console_smoke': 'e8ec762d3dcaeeab1368e1da39290bbe9fc20af42b9a52131ed73b8d993775b6', 'third_party_notices': '0220749362525dbf7677e5e44e1bd0bbce629dccc1e235419fbf57864680f440', 'installation_auth': '39a632a636cb3c5dcf9315c87432649e41957d8d7dd6ed5deee7873b5a2b8334', 'backup_restore_scripts': 'bb1beeb46dc34817b774a8e20315c279bf934f30993f71c7975890d741817752', 'upgrade_restore_rig': 'f570eae1ff2e2b1fe7854a764fd08fa578e16f7da70d15eb7eb0d9792772a270'}

def jobs():
    text=WF.read_text().split('jobs:\n',1)[1]
    return {m.group(1):m.group(0) for m in re.finditer(r'(?ms)^  ([a-z_]+):\n.*?(?=^  [a-z_]+:\n|\Z)',text)}

def route(event,inputs=None):
    inputs=inputs or {};selected=inputs.get('pg_channel_opening','')
    blocks=jobs();result={}
    for name,block in blocks.items():
        expression=re.search(r'^    if: (.+)$',block,re.M).group(1)
        if name=='pg_channel':
            if expression!=PG:raise AssertionError('PG route drift')
            result[name]=event=='workflow_dispatch' and selected!=''
        else:
            if expression!=NORMAL:raise AssertionError('normal route drift')
            result[name]=event!='workflow_dispatch' or selected==''
    return result

class Routes(unittest.TestCase):
    def test_all_eight_existing_jobs_keep_exact_step_bytes(self):
        # Testzweck: kein vergessener Job und keine still abgeschwächte normale Test-/Pilotsemantik.
        blocks=jobs();self.assertEqual(set(blocks),set(BASE_PINS)|{'pg_channel'})
        for name,pin in BASE_PINS.items():
            before=blocks[name].replace('    if: '+NORMAL+'\n','',1)
            # Nur die neue abschließende Jobtrennung gehört nicht zum alten letzten Job.
            if name=='upgrade_restore_rig':before=before.removesuffix('\n')
            self.assertEqual(hashlib.sha256(before.encode()).hexdigest(),pin,name)
    def test_push_pull_and_unselected_dispatch_keep_all_old_jobs(self):
        # Testzweck: auch Push/PR ohne inputs sowie alter stdinPilot behalten die bisherige Auswahl.
        for event,inputs in [('push',None),('pull_request',None),('workflow_dispatch',{}),('workflow_dispatch',{'stdin_pilot_opening':'old'})]:
            with self.subTest(event=event,inputs=inputs):
                selected=route(event,inputs);self.assertFalse(selected.pop('pg_channel'));self.assertTrue(all(selected.values()))
    def test_pg_mode_excludes_all_eight_and_mixed_inputs_cannot_hide_as_green_skip(self):
        # Testzweck: PGmodus startet ausschließlich seinen eigenen Job; Mischung muss dort rot abgewiesen werden.
        for inputs in [dict(pg_channel_opening='bound'),dict(pg_channel_opening='bound',stdin_pilot_opening='legacy')]:
            selected=route('workflow_dispatch',inputs);self.assertTrue(selected.pop('pg_channel'));self.assertFalse(any(selected.values()))
        spec=importlib.util.spec_from_file_location('pg_route_fixture',ROOT/'tests/manual-postgres-channel/run_fixture.py')
        module=importlib.util.module_from_spec(spec);sys.modules[spec.name]=module;spec.loader.exec_module(module)
        env=dict(GITHUB_EVENT_NAME='workflow_dispatch',GITHUB_RUN_ATTEMPT='1',GITHUB_REPOSITORY='flowzer-io/flowzer-bpmn-core-engine',GITHUB_REF='refs/heads/codex/test',GITHUB_SHA='a'*40,GITHUB_RUN_ID='12345',RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',RUNNER_ARCH='X64',GITHUB_WORKFLOW_REF='flowzer-io/flowzer-bpmn-core-engine/.github/workflows/ci.yml@refs/heads/codex/test',PG_CHANNEL_OPENING=json.dumps(dict(mode='pg-channel-v1',sourceSha='a'*40,sourceTree='b'*40,workflowSha256='c'*64,nonce='d'*32,expiresAt=1200)),PG_LEGACY_OPENING='legacy')
        with self.assertRaises(module.FixtureFailure):module.validate_opening(env,1000)
    def test_concurrency_and_fixed_nondeploy_artifact_contract(self):
        # Testzweck: PGjob nicht durch normalen CIjob abbrechen und nie freie Artifact-/Secret-/Deploypfade.
        text=WF.read_text();block=jobs()['pg_channel']
        self.assertIn("group: ci-${{ github.workflow }}-${{ github.ref }}${{ inputs.pg_channel_opening != '' && '-pg-channel' || '' }}",text)
        self.assertIn("cancel-in-progress: ${{ inputs.pg_channel_opening == '' }}",text)
        self.assertIn('flowzer-pg-channel-proof/proof.safe.json',block)
        self.assertIn('tests/manual-postgres-channel/test_ci_routing.py',block)
        self.assertIn('tests/manual-runtime-channel/run_fixture.py --once',block)
        self.assertIn('tests/manual-runtime-channel/run_fixture.py --preflight-only',block)
        for forbidden in ['secrets.', 'environment:', 'packages:', 'workflow_dispatch:', 'release.yml', 'staging.yml']:
            self.assertNotIn(forbidden,block)
        self.assertEqual(set(re.findall(r'uses: ([^\n]+)',block)),{'actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4','actions/upload-artifact@65c4c4a1ddee5b72f698fdd19549f0f0fb45cf08 # v4'})
        self.assertFalse((ROOT/'.github/workflows/manual-pg-channel.yml').exists())
if __name__=='__main__':unittest.main()
