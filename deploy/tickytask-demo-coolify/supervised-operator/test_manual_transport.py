"""Testzweck: Ausschließlich eigene lokale Pipe-Fixtures; niemals SSH/Docker/DB/Webhost."""
import base64
import importlib.util
from pathlib import Path
import sys
import unittest

spec=importlib.util.spec_from_file_location('manual_transport',Path(__file__).with_name('manual_transport.py'))
m=importlib.util.module_from_spec(spec);sys.modules[spec.name]=m;spec.loader.exec_module(m)
MARKER=b'PUBLIC_SYNTHETIC_SECRET_375_20261020'


class PipeTests(unittest.TestCase):
    def fixture(self, code, data=MARKER, **options):
        # Testzweck: Nur der eigene Python-Prozess ersetzt den privaten Transport vollständig.
        return m.BoundedPipe().exchange([sys.executable,'-I','-B','-c',code],data,[MARKER],**options)

    def test_input_is_complete_and_eof_before_success(self):
        # Testzweck: Der Empfänger sieht exakt alle Eingabebytes und EOF, kein Here-Doc/argvwert.
        result=self.fixture('import sys; v=sys.stdin.buffer.read(); print(len(v))')
        self.assertEqual(result.exit_code,0)
        self.assertEqual(result.stdout,str(len(MARKER)).encode()+b'\n')
        self.assertNotIn(MARKER.decode(),repr(result))

    def test_echoes_in_either_channel_or_encoding_never_escape(self):
        # Testzweck: Klartext/Base64/doppeltes Base64 auf beiden Kanälen ist kein sicherer Output.
        for fd in [1,2]:
            for level in range(3):
                code=f'import sys,os,base64; x=sys.stdin.buffer.read(); '+('x=base64.b64encode(x); '*level)+f'os.write({fd},x)'
                with self.subTest(fd=fd,level=level),self.assertRaises(m.TransportFailure) as caught:
                    self.fixture(code)
                self.assertEqual(caught.exception.code,'LEAK')
                self.assertNotIn(MARKER.decode(),str(caught.exception))

    def test_split_echo_across_channels_is_not_a_clean_transport(self):
        # Testzweck: Ein Marker darf auch durch Aufteilung in stdout-Präfix/stderr-Suffix nicht entkommen.
        with self.assertRaises(m.TransportFailure) as caught:
            self.fixture('import sys,os; x=sys.stdin.buffer.read(); n=len(x)//2; os.write(1,x[:n]); os.write(2,x[n:])')
        self.assertEqual(caught.exception.code,'LEAK')

    def test_timeout_is_not_output_eof_or_success(self):
        # Testzweck: Ein offener Ausgabekanal nach Eingabe-EOF muss begrenzt scheitern.
        with self.assertRaises(m.TransportFailure) as caught:
            self.fixture('import sys,time; sys.stdin.buffer.read(); time.sleep(9)',timeout=.15)
        self.assertEqual(caught.exception.code,'TIMEOUT')

    def test_blocked_stdin_write_has_same_total_deadline(self):
        # Testzweck: Nichtlesender Empfänger darf die Frist nicht schon beim Schreiben umgehen.
        with self.assertRaises(m.TransportFailure) as caught:
            self.fixture('import time; time.sleep(9)',data=b'x'*16384,timeout=.15)
        self.assertEqual(caught.exception.code,'TIMEOUT')

    def test_output_limit_is_enforced_before_collecting_unbounded_bytes(self):
        # Testzweck: Keine unbeschränkten communicate-Puffer für private Fehlerausgaben.
        with self.assertRaises(m.TransportFailure) as caught:
            self.fixture('import sys; sys.stdin.buffer.read(); sys.stdout.buffer.write(b"x"*65537)')
        self.assertEqual(caught.exception.code,'OUTPUT_LIMIT')

    def test_secret_in_argv_or_inherited_environment_is_not_allowed(self):
        # Testzweck: Nutzlast gehört nur auf stdin; niemals als Argument oder geerbte Konfiguration.
        with self.assertRaises(m.TransportFailure) as caught:
            m.BoundedPipe().exchange([sys.executable,'-c','pass',MARKER.decode()],b'',[MARKER])
        self.assertEqual(caught.exception.code,'ARGUMENT_SECRET')
        result=self.fixture('import os,sys; sys.stdin.buffer.read(); print(sorted(os.environ.keys()))')
        self.assertNotIn(b'TOKEN',result.stdout)
        self.assertNotIn(b'PGPASSWORD',result.stdout)

    def test_api_start_uses_stdin_only_and_no_secret_logging_or_signal_proxy(self):
        # Testzweck: Nur dieser konkrete reguläre Dockerkanal darf nach EOF ohne APIende zurückkehren.
        args=m.api_start_arguments('a'*32,'sha256:'+'b'*64)
        self.assertIn('--attach=stdin',args)
        self.assertNotIn('--attach=stdout',args)
        self.assertNotIn('--attach=stderr',args)
        for flag in ['--interactive','--sig-proxy=false','--restart=no','--log-driver=none','--pull=never']:
            self.assertIn(flag,args)
        self.assertNotIn('--detach',args)
        self.assertEqual(args[-1],'--secrets-stdin')

    def test_migrator_needs_both_output_channels_and_real_exit(self):
        # Testzweck: Anders als APIstart darf Migrationsexit nicht vom stdin-Transport abgeleitet werden.
        args=m.migration_arguments('a'*32,'sha256:'+'b'*64)
        self.assertIn('--attach=stdout',args);self.assertIn('--attach=stderr',args)
        self.assertEqual(args[-2:],['--migrate','--secrets-stdin'])
        result=self.fixture('import sys; sys.stdin.buffer.read(); sys.exit(7)')
        self.assertEqual(result.exit_code,7)

    def test_restart_never_initializes_or_recreates_persistent_data(self):
        # Testzweck: Restart ist ein eigener Pfad ohne initdb/Volume-/Rollen-/DB-Create.
        args=m.restart_pg_arguments('c'*64)
        self.assertEqual(args,['container','start','c'*64])
        for value in ['', 'c'*12, 'a; rm -rf /', 'C'*64]:
            with self.assertRaises(m.TransportFailure):m.restart_pg_arguments(value)


if __name__=='__main__':unittest.main()
