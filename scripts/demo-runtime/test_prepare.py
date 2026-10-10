"""Quellenvertrag des temporären Ressourcenrigs; kein Docker, Netz oder Test-IdP."""
import importlib.util
import json
from pathlib import Path
import tempfile
from unittest.mock import patch
import unittest

SPEC = importlib.util.spec_from_file_location('prepare', Path(__file__).with_name('prepare.py'))
rig = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(rig)
ROOT = Path(__file__).resolve().parents[2]

class PreparationTests(unittest.TestCase):
    def context(self, **changes):
        return dict(repository='flowzer-io/flowzer-bpmn-core-engine',
            ref='refs/heads/codex/flowzer-runtime-calibration-pilot', event='workflow_dispatch',
            attempt='1', run_id='123456', sha='a'*40, confirmed_sha='a'*40,
            runner_environment='github-hosted', runner_os='Linux', runner_arch='X64', **changes)

    def test_context_binds_own_unique_project(self):
        # Testzweck: Nur der bestätigte manuelle Hosted-Erstlauf erhält eine eigene Namespace-Bindung.
        self.assertEqual('flowzer-runtime-123456-a1', rig.project_for(self.context()))

    def test_stats_are_whitelisted_numeric_bytes(self):
        # Testzweck: Aus Stats gehen nur Dienst, Prozent und Bytes hervor; niemals Rohdaten oder Namen.
        self.assertEqual({'service':'api','cpu_percent':1.25,'memory_bytes_approx':12582912,
            'limit_bytes':536870912,'pids':17}, rig.stats_row('api','1.25%','12MiB / 512MiB','17'))

    def test_prepare_consumes_digests_without_product_changes(self):
        # Testzweck: Original-Harness bleibt bytegleich; Kopie pinnt alle Images und baut nichts.
        original=(ROOT/'tests/installation-auth/compose.yml').read_bytes()
        with tempfile.TemporaryDirectory() as temp:
            target=Path(temp)/'rig'
            result=rig.prepare(ROOT,target,self.context())
            self.assertEqual('flowzer-runtime-123456-a1',result['project'])
            text=(target/'tests/installation-auth/compose.yml').read_text()
            self.assertNotIn('build:',text)
            self.assertNotIn(':local',text)
            self.assertEqual(7,text.count('@sha256:'))
            self.assertIn('    internal: true',text)
            self.assertEqual(7,text.count('    mem_limit:'))
            self.assertEqual(7,text.count('    memswap_limit:'))
            self.assertEqual(12,text.count('io.flowzer.runtime.owner:'))
            self.assertEqual(7,text.count('    platform: linux/amd64'))
            self.assertIn("'flowzer-runtime-123456-a1'",(target/'tests/installation-auth/support/compose.js').read_text())
            self.assertEqual(original,(ROOT/'tests/installation-auth/compose.yml').read_bytes())

    def test_browser_contract_keeps_assertions_and_disables_diagnostics(self):
        # Testzweck: Kein neuer Testfilter/Retry/Timeout; nur geschlossene Netz- und sichere Reportgrenzen.
        with tempfile.TemporaryDirectory() as temp:
            target=Path(temp)/'rig';rig.prepare(ROOT,target,self.context())
            auth=target/'tests/installation-auth'
            self.assertTrue((auth/'playwright.config.js').is_file(), 'Vorbereitete Konfiguration fehlt')
            config=(auth/'playwright.config.js').read_text()
            for token in ['retries: 0','timeout: 180_000','workers: 1',"trace: 'off'","screenshot: 'off'","video: 'off'","serviceWorkers: 'block'"]:
                self.assertIn(token,config)
            for source in (ROOT/'tests/installation-auth/specs').glob('*.spec.js'):
                expected=source.read_text().replace("require('@playwright/test')","require('../support/restricted-test')")
                self.assertEqual(expected,(auth/'specs'/source.name).read_text())


    def test_rejects_foreign_retry_unconfirmed_and_selfhosted_contexts_before_io(self):
        # Testzweck: Falscher Ref, Retry, Plattform und ungeklärter SHA dürfen nicht einmal Fixtures lesen.
        changes = {'repository':'other/repo', 'ref':'refs/heads/main', 'event':'push',
            'attempt':'2', 'sha':'not-a-sha', 'confirmed_sha':'b'*40, 'run_id':'1;rm',
            'runner_environment':'self-hosted', 'runner_os':'Windows', 'runner_arch':'ARM64'}
        for key, value in changes.items():
            with self.subTest(key=key), patch.object(rig.subprocess,'check_output') as process:
                with self.assertRaises(ValueError):rig.prepare(ROOT,Path('/unused'),self.context() | {key:value})
                process.assert_not_called()

    def test_context_has_exact_string_fields_and_closed_errors(self):
        # Testzweck: Typfehler/fehlende Felder dürfen keine ungeprüften Key-/TypeErrors an die Grenze geben.
        for context in [self.context() | {'sha':None}, self.context() | {'run_id':True},
            self.context() | {'extra':'never-log'}, {k:v for k,v in self.context().items() if k!='sha'}, None]:
            with self.subTest(),self.assertRaises(ValueError):rig.project_for(context)

    def test_existing_or_source_nested_target_is_never_overwritten(self):
        # Testzweck: Die Vorbereitung besitzt keinen Überschreib-/Clean-Schalter für bestehende Checkouts.
        for target in [ROOT, ROOT/'scripts/new-rig']:
            with self.subTest(target=str(target)), self.assertRaises(ValueError):
                rig.prepare(ROOT,target,self.context())
        with tempfile.TemporaryDirectory() as temp:
            target=Path(temp);keep=target/'keep';keep.write_text('unverändert')
            with self.assertRaises(ValueError):rig.prepare(ROOT,target,self.context())
            self.assertEqual('unverändert',keep.read_text())

    def test_compose_drift_is_not_tolerated(self):
        # Testzweck: Fehlender/duplizierter Service oder zusätzlicher Build fällt auf statt unkontrolliert zu starten.
        text=(ROOT/'tests/installation-auth/compose.yml').read_text()
        for changed in [text.replace('    image: caddy:2','    image: other:tag'),
            text.replace('  api:\n','  api:\n  api:\n'),text.replace('    build: *api-build','    build: different')]:
            with self.subTest(),self.assertRaises(ValueError):rig.adapt_compose(changed,'flowzer-runtime-1-a1')

    def test_rejects_unknown_units_limits_non_numeric_and_secrets(self):
        # Testzweck: Unsichere Stats oder unverifizierte Limits sind Fehler, keine gültigen Null-Messwerte.
        for row in [('unknown','1%','1MiB / 512MiB','1'),('api','NaN%','1MiB / 512MiB','1'),
            ('api','1%','1MiB / 1GiB','1'),('api','1%','513MiB / 512MiB','1'),
            ('api','1%','secret / 512MiB','1'),('api','1%','1MiB / 512MiB','token'),
            ('api','1%','1MiB / 0.000001B','1')]:
            with self.subTest(row=row),self.assertRaises(ValueError):rig.stats_row(*row)
        self.assertEqual({'service':'api','cpu_percent':1.25,'memory_bytes_approx':12939428,
            'limit_bytes':536870912,'pids':17},rig.stats_row('api','1.25%','12.34MiB / 512MiB','17'))
        self.assertEqual(12000000,rig.memory_bytes('12MB'))
        self.assertEqual(12582912,rig.memory_bytes('12MiB'))

    def test_copy_uses_fixed_git_blobs_not_hidden_worktree_bytes(self):
        # Testzweck: Ein von Git-Driftprüfung übersehener Worktreewert darf die CF-Kopie nicht verändern.
        fixture=ROOT/'tests/installation-auth/keycloak/flowzer-test-realm.json'
        original=fixture.read_bytes();read=Path.read_bytes
        def bytes_for(path):
            return b'UNREVIEWED-WORKTREE' if path==fixture else read(path)
        with tempfile.TemporaryDirectory() as temp,patch.object(Path,'read_bytes',bytes_for):
            target=Path(temp)/'rig';rig.prepare(ROOT,target,self.context())
            self.assertEqual(original,(target/'tests/installation-auth/keycloak/flowzer-test-realm.json').read_bytes())

    def test_preserves_verified_executable_fixture_modes(self):
        # Testzweck: PostgreSQL muss den Init-Hook wie im CF-Tree ausführen, nicht im Entrypoint sourcen.
        with tempfile.TemporaryDirectory() as temp:
            target=Path(temp)/'rig';rig.prepare(ROOT,target,self.context())
            self.assertEqual(0o755,(target/'tests/installation-auth/postgres/10-flowzer-init.sh').stat().st_mode & 0o777)
            self.assertEqual(0o755,(target/'tests/installation-auth/certs/generate.sh').stat().st_mode & 0o777)
            self.assertEqual(0o644,(target/'tests/installation-auth/support/loopback-proxy.js').stat().st_mode & 0o777)

    def test_only_exact_owned_checkconfig_oneoff_is_kept_for_exit_audit(self):
        # Testzweck: --rm darf den echten Check-config-CID nicht vor Stats/Exit/OOM löschen; andere Befehle unverändert.
        with tempfile.TemporaryDirectory() as temp:
            target=Path(temp)/'rig';rig.prepare(ROOT,target,self.context())
            helper=target/'tests/installation-auth/support/compose.js'
            code="""const fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
              const seen=[];const sandbox={__dirname:path.dirname(process.argv[1]),module:{exports:{}},require:name=>{
                if(name==='path')return path;if(name==='child_process')return {spawnSync:(_file,args)=>{
                  seen.push(args);return {status:0,stdout:'',stderr:''};}};throw Error('closed');}};
              vm.runInNewContext(fs.readFileSync(process.argv[1],'utf8'),sandbox);
              sandbox.module.exports.compose(['run','--rm','--no-deps','-T','api','--check-config']);
              sandbox.module.exports.compose(['run','--rm','--no-deps','-T','db','--check-config']);
              process.stdout.write(JSON.stringify(seen));"""
            seen=json.loads(rig.subprocess.check_output(['node','-e',code,str(helper)],stderr=rig.subprocess.DEVNULL,timeout=10))
            self.assertNotIn('--rm',seen[0]);self.assertIn('--rm',seen[1])
            self.assertIn('flowzer-runtime-123456-a1',seen[0])

    def test_no_untracked_fixture_runtime_or_secret_material_is_copied(self):
        # Testzweck: Nur die 25 versionierten Basisdateien plus drei geprüfte Helfer gelangen in die Kopie.
        with tempfile.TemporaryDirectory() as temp:
            target=Path(temp)/'rig';rig.prepare(ROOT,target,self.context())
            files=[x.relative_to(target).as_posix() for x in target.rglob('*') if x.is_file()]
            self.assertEqual(28,len(files))
            self.assertFalse(any('node_modules/' in x or 'logs/' in x or x.endswith('.crt') for x in files))

if __name__=='__main__':unittest.main()
