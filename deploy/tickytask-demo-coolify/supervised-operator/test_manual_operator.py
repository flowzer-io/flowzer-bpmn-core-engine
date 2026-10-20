"""Testzweck: Initial-vs-Restart und Exit-/Berechtigungsgrenzen ausschließlich mit Ports-Fakes."""
import importlib.util
from pathlib import Path
import sys
import json
from dataclasses import replace
import unittest

spec=importlib.util.spec_from_file_location('manual_operator',Path(__file__).with_name('manual_operator.py'))
m=importlib.util.module_from_spec(spec);sys.modules[spec.name]=m;spec.loader.exec_module(m)


class FakePorts:
    def __init__(self,mode):
        self.calls=[];self.bad=None
        self.observation=m.Observation(True,'ABSENT' if mode=='initial' else 'OWNED',
            'ABSENT' if mode=='initial' else 'INITIALIZED','ABSENT' if mode=='initial' else 'EXITED')
    def observe(self):self.calls.append('observe');return self.observation
    def initialize(self,bundle):self.calls.append('initialize');return 0
    def pg_start(self):self.calls.append('pg_start');return 0
    def bootstrap(self,bundle):self.calls.append('bootstrap');return 0
    def pg_proof(self):self.calls.append('pg_proof');return self.bad or m.RoleProof(True,True,True)
    def migrate(self,payload):self.calls.append('migrate');return self.bad or m.MigrationProof(0,22,0)
    def api_start(self,payload):self.calls.append('api_start');return 'a'*64
    def api_proof(self,cid):self.calls.append('api_proof');return self.bad or m.ApiProof(True,22,0,'Feel',401)


def bundle(mode):
    return m.SecretBundle('admin-public-synthetic-secret' if mode=='initial' else None,
        'migration-public-synthetic-secret','runtime-public-synthetic-secret',
        'bff-public-synthetic-secret','directory-public-synthetic-secret')


class OperatorTests(unittest.TestCase):
    def test_initial_sequence_migrates_before_api(self):
        # Testzweck: Eigene neue PG wird einmal initialisiert, danach Rollen, Migration22, erst dann API.
        p=FakePorts('initial');result=m.ManualOperator().run('initial',p,lambda:bundle('initial'))
        self.assertEqual(p.calls,['observe','initialize','pg_start','bootstrap','pg_proof','migrate','api_start','api_proof'])
        self.assertEqual(result.api_cid,'a'*64)

    def test_restart_has_no_init_volume_or_role_bootstrap(self):
        # Testzweck: Vorhandene eigene Daten werden weder neu initialisiert noch mit Rollen-SQL überarbeitet.
        p=FakePorts('restart');m.ManualOperator().run('restart',p,lambda:bundle('restart'))
        self.assertEqual(p.calls,['observe','pg_start','pg_proof','migrate','api_start','api_proof'])

    def test_existing_or_unknown_blocks_initial_before_secrets_or_effects(self):
        # Testzweck: Ein passender Altbestand ist kein leerer neuer Datenträger, UNKNOWN keine Adoption.
        for state in ['OWNED','UNKNOWN']:
            p=FakePorts('initial');p.observation=m.Observation(True,state,'INITIALIZED','ABSENT')
            with self.assertRaises(m.OperatorFailure):
                m.ManualOperator().run('initial',p,lambda:self.fail('secret read before safe preflight'))
            self.assertEqual(p.calls,['observe'])

    def test_foreign_target_unknown_pg_or_running_api_blocks_restart(self):
        # Testzweck: Keine fremde/unklare Ressource oder laufende alte API wird automatisch gestoppt.
        for o in [m.Observation(False,'OWNED','INITIALIZED','EXITED'),
                  m.Observation(True,'UNKNOWN','INITIALIZED','EXITED'),
                  m.Observation(True,'OWNED','UNKNOWN','EXITED'),
                  m.Observation(True,'OWNED','INITIALIZED','RUNNING')]:
            p=FakePorts('restart');p.observation=o
            with self.assertRaises(m.OperatorFailure):m.ManualOperator().run('restart',p,lambda:self.fail('secret read'))
            self.assertEqual(p.calls,['observe'])

    def test_nonzero_migration_or_old_schema_never_starts_api(self):
        # Testzweck: CLIexit0 und Head22/Pending0 müssen gemeinsam vorliegen, kein bool/Head20/Unknown.
        for proof in [m.MigrationProof(1,22,0),m.MigrationProof(0,20,0),m.MigrationProof(True,22,0),
                      m.MigrationProof(0,22,1),None]:
            p=FakePorts('restart')
            def migrate(payload):p.calls.append('migrate');return proof
            p.migrate=migrate
            with self.assertRaises(m.OperatorFailure):m.ManualOperator().run('restart',p,lambda:bundle('restart'))
            self.assertNotIn('api_start',p.calls)

    def test_runtime_rights_missing_blocks_migration(self):
        # Testzweck: Runtime darf kein DDL, Migration muss getrennte DDL-Identität haben; Portproof ist verpflichtend.
        p=FakePorts('restart');p.pg_proof=lambda:m.RoleProof(True,False,True)
        with self.assertRaises(m.OperatorFailure):m.ManualOperator().run('restart',p,lambda:bundle('restart'))
        self.assertNotIn('migrate',p.calls)

    def test_transport_unknown_no_retry_no_cleanup_no_raw_exception(self):
        # Testzweck: Unbekannter Start darf nicht doppelt ausgeführt oder mit Löschung repariert werden.
        p=FakePorts('restart')
        def fail(payload):p.calls.append('api_start');raise RuntimeError('PRIVATE_SYNTHETIC_RAW_PASSWORD')
        p.api_start=fail;op=m.ManualOperator()
        with self.assertRaises(m.OperatorFailure) as caught:op.run('restart',p,lambda:bundle('restart'))
        self.assertEqual(caught.exception.phase,'API_START');self.assertNotIn('PASSWORD',str(caught.exception))
        with self.assertRaises(m.OperatorFailure):op.run('restart',p,lambda:bundle('restart'))
        self.assertEqual(p.calls.count('api_start'),1)

    def test_readiness_requires_owned_schema_feel_and_anonymous401(self):
        # Testzweck: CID/Start-CLIexit reicht nicht für Freigabe, insbesondere kein anonymer200.
        for proof in [m.ApiProof(False,22,0,'Feel',401),m.ApiProof(True,20,0,'Feel',401),
                      m.ApiProof(True,22,0,'Simple',401),m.ApiProof(True,22,0,'Feel',200)]:
            p=FakePorts('restart');p.api_proof=lambda cid:proof
            with self.assertRaises(m.OperatorFailure):m.ManualOperator().run('restart',p,lambda:bundle('restart'))

    def test_packets_use_only_fixed_runtime_and_migration_identity_and_are_scrubbed(self):
        # Testzweck: Keine DDL-Identität im APIpaket, keine BFFsecrets im Migrator; eigene Bytebuffer danach nullen.
        p=FakePorts('restart');buffers=[]
        def migrate(payload):
            buffers.append(payload);v=json.loads(payload)
            self.assertEqual(v['role'],'migrate');self.assertEqual(set(v['values']),{'migrationConnectionString'})
            self.assertIn('Username=flowzer_tt_demo_migration;',v['values']['migrationConnectionString'])
            return m.MigrationProof(0,22,0)
        def api(payload):
            buffers.append(payload);v=json.loads(payload)
            self.assertEqual(v['role'],'api');self.assertNotIn('migrationConnectionString',v['values'])
            self.assertIn('Username=flowzer_tt_demo;',v['values']['runtimeConnectionString'])
            return 'a'*64
        p.migrate=migrate;p.api_start=api
        m.ManualOperator().run('restart',p,lambda:bundle('restart'))
        self.assertTrue(all(not any(b) for b in buffers))
        self.assertNotIn('synthetic',repr(bundle('restart')))

    def test_nonterminal_or_missing_output_eof_is_not_a_migration_proof(self):
        # Testzweck: Transportexit ist kein Containerabschluss, fehlendes EOF ist kein erfolgreiches SQLkommando.
        for proof in [m.MigrationProof(0,22,0,False,True),m.MigrationProof(0,22,0,True,False)]:
            p=FakePorts('restart');p.migrate=lambda payload:proof
            with self.assertRaises(m.OperatorFailure):m.ManualOperator().run('restart',p,lambda:bundle('restart'))
            self.assertNotIn('api_start',p.calls)

    def test_init_failure_never_starts_pg_or_retries_init(self):
        # Testzweck: Ein teilweise initialisiertes eigenes Volume wird nicht gelöscht oder erneut initialisiert.
        p=FakePorts('initial')
        def initialize(value):p.calls.append('initialize');return 1
        p.initialize=initialize;op=m.ManualOperator()
        with self.assertRaises(m.OperatorFailure):op.run('initial',p,lambda:bundle('initial'))
        with self.assertRaises(m.OperatorFailure):op.run('initial',p,lambda:bundle('initial'))
        self.assertEqual(p.calls,['observe','initialize'])

    def test_password_quoting_keeps_semicolons_and_quotes_inside_one_fixed_value(self):
        # Testzweck: Keine Stringkonkatenation mit ungequotetem Passwortfragment wie bei #375.
        self.assertEqual(m._connection('x;"y=z','flowzer_tt_demo'),
            'Host=/socket;Database=flowzer_tt_demo;Username=flowzer_tt_demo;Password="x;""y=z"')

    def test_unknown_local_handle_is_preserved_without_signal_or_raw_diagnostics(self):
        # Testzweck: Ein ungeschlossener eigener Transporthandle darf im Sequenzfehler nicht verschwinden.
        p=FakePorts('restart')
        def failed(payload):raise m.PortFailure(12345)
        p.api_start=failed
        with self.assertRaises(m.OperatorFailure) as caught:m.ManualOperator().run('restart',p,lambda:bundle('restart'))
        self.assertEqual(caught.exception.own_local_pid,12345)
        self.assertEqual(caught.exception.phase,'API_START')
        self.assertNotIn('stop',p.calls)

    def test_rendered_dsn_utf16_or_control_violation_blocks_before_any_effect(self):
        # Testzweck: Derselbe finale 4096-UTF16-/Controlvertrag wie RuntimeSecretInput,
        # nicht bloß die Länge des ungequoteten Passworts, muss vor init/PG/SQL gelten.
        for mode in ['initial','restart']:
            for changes in [dict(migration='x'*4096),dict(runtime='"'*2050),
                            dict(bff='😀'*2050),dict(directory='synthetic-secret-\u0085-control')]:
                with self.subTest(mode=mode,field=next(iter(changes))):
                    p=FakePorts(mode)
                    with self.assertRaises(m.OperatorFailure) as caught:
                        m.ManualOperator().run(mode,p,lambda:replace(bundle(mode),**changes))
                    self.assertEqual(caught.exception.phase,'INPUT')
                    self.assertEqual(p.calls,['observe'])

    def test_exact_utf16_limit_without_control_is_preserved(self):
        # Testzweck: Ein APIsecret mit exakt4096 UTF16-Einheiten bleibt erlaubt;
        # Supplementary-Codepoints zählen zweifach wie bei .NETstring.Length.
        p=FakePorts('restart')
        m.ManualOperator().run('restart',p,lambda:replace(bundle('restart'),bff='😀'*2048))
        self.assertIn('api_proof',p.calls)


if __name__=='__main__':unittest.main()
