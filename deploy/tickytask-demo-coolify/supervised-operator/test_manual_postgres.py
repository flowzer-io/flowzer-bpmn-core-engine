"""Testzweck: nur synthetische Secrets/öffentliche SQLquellen, keine Datenbank oder Prozesse."""
import importlib.util,unittest
from pathlib import Path
P=Path(__file__);s=importlib.util.spec_from_file_location('pg_subject',P.with_name('manual_postgres.py'));m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
class BootstrapTests(unittest.TestCase):
    def scripts(self):
        d=P.resolve().parents[2]/'postgresql';return (d/'01-datenbank-und-rollen.sql').read_bytes(),(d/'02-laufzeitrechte.sql').read_bytes()
    def packet(self,**changes):
        return m.initial_psql_payload(**dict(dict(admin='synthetic-admin-123',migration='synthetic-migration-123',runtime='synthetic-runtime-123',sql01=self.scripts()[0],sql02=self.scripts()[1]),**changes))
    def test_canonical_scripts_inlined_and_admin_password_is_first_line(self):
        # Testzweck: keine Remote-SQL-/Passwortdatei oder freier Include nötig.
        p=self.packet();self.assertIs(type(p),bytearray);self.assertTrue(p.startswith(b'synthetic-admin-123\n'))
        self.assertNotIn(b'\\ir 02-laufzeitrechte.sql',p);self.assertIn(b'REVOKE ALL ON TABLE',p)
        self.assertIn(b'flowzer_tt_demo_migration',p);self.assertLessEqual(len(p),16384)
    def test_only_reviewed_script_bytes_can_be_sent(self):
        # Testzweck: kein freier SQL-Executor über den Paketbauer.
        for changes in [dict(sql01=b'SELECT unsafe'),dict(sql02=self.scripts()[1]+b'\n')]:
            with self.assertRaises(m.BootstrapFailure):self.packet(**changes)
    def test_sql_metacharacters_are_quoted_not_executed(self):
        # Testzweck: Passwortapostroph/Backslash bleiben Daten bei standard_conforming_strings=on.
        p=self.packet(runtime="synthetic'\\;--value123")
        self.assertIn(b"PASSWORD 'synthetic''\\;--value123'",p)
        self.assertIn(b'SET standard_conforming_strings = on;',p)
    def test_bad_password_or_full_rendered_batch_is_rejected_before_effect(self):
        # Testzweck: C0/C1/Surrogate und zu großes Gesamtpaket nicht erst nach initdb entdecken.
        for changes in [dict(admin='synthetic\npassword'),dict(runtime='synthetic\x85password'),dict(runtime='\ud800'*16),dict(admin='x'*4096,migration='y'*4096,runtime='z'*4096)]:
            with self.assertRaises(m.BootstrapFailure) as e:self.packet(**changes)
            self.assertEqual(str(e.exception),'FLOWZER_INITIAL_BOOTSTRAP_INPUT')
    def test_psql_arguments_have_no_secret_env_shell_or_external_file(self):
        # Testzweck: erste stdin-Zeile für Passwort, restlicher Stream SQL; kein TTY/ENV/.pgpass.
        a=m.psql_initial_arguments();self.assertIn('--password',a);self.assertEqual(a[-2:],['--file','-'])
        self.assertNotIn('PGPASSWORD',' '.join(a));self.assertNotIn('synthetic',' '.join(a))
    def test_packet_can_be_wiped_without_immutable_raw_copy_contract(self):
        # Testzweck: Aufrufer kann eigenen versendeten Bytepuffer explizit leeren; Strings nicht zeroisierbar.
        p=self.packet();p[:]=b'\0'*len(p);self.assertEqual(set(p),{0})
if __name__=='__main__':unittest.main()
