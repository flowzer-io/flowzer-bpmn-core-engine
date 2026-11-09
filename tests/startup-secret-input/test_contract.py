"""Testzweck: Paketfreie echte Bootstrap-Quelle in eigenen CLI-Prozessen, ohne Dienste/Secrets."""
import base64
import json
import os
from pathlib import Path
import subprocess
import time
import unittest
import xml.etree.ElementTree as ET

DOTNET = os.environ['FLOWZER_STDIN_TEST_DOTNET']
if not Path(DOTNET).is_absolute():
    raise ValueError('Explicit absolute SDK path required')
DLL = os.environ['FLOWZER_STDIN_TEST_DLL']
MARKER = 'PUBLIC_SYNTHETIC_BOOTSTRAP_MARKER'


def child_environment():
    # Testzweck: Keine geerbten Zugänge, Konfigurationen oder Profilpfade in eigene Kinder übernehmen.
    names = ['HOME', 'DOTNET_CLI_HOME', 'TMPDIR', 'NUGET_PACKAGES']
    clean = {key: os.environ[key] for key in names}
    clean.update(PATH='/usr/bin:/bin', DOTNET_CLI_TELEMETRY_OPTOUT='1',
                 DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='true',
                 DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', DOTNET_NOLOGO='true',
                 DOTNET_ROLL_FORWARD='Disable')
    return clean


def document(role='api'):
    """Öffentlicher, niemals als Zugang verwendeter Fixturewert."""
    keys = ['migrationConnectionString'] if role == 'migrate' else [
        'runtimeConnectionString', 'bffClientSecret', 'directoryClientSecret']
    return {'version': 1, 'role': role, 'values': dict.fromkeys(keys, MARKER)}


class ContractTests(unittest.TestCase):
    def run_case(self, payload, args=('--secrets-stdin',), fixture='normal', accepted=False):
        # Testzweck: Rückgabecode UND beide Ausgabekanäle prüfen, auch kodierte Fehlerfallen.
        raw = payload if isinstance(payload, bytes) else json.dumps(payload).encode()
        child = subprocess.run([DOTNET, DLL, fixture, *args], input=raw,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=9, env=child_environment())
        output = child.stdout + child.stderr
        for marker in (MARKER.encode(), base64.b64encode(MARKER.encode()),
                       base64.b64encode(base64.b64encode(MARKER.encode()))):
            self.assertNotIn(marker, output)
        self.assertEqual(child.returncode, 0 if accepted else 2)
        self.assertEqual(child.stderr, b'')
        result = json.loads(child.stdout)
        self.assertTrue(result['SafeDescription'])
        self.assertTrue(result['RepeatRejected'])
        return result

    def test_api_pkcs_security_minimum(self):
        # Testzweck: Den echten Audit-RED transitive PKCS10.0.0 nicht erneut ungepinnt zulassen.
        project = Path(__file__).resolve().parents[2] / 'src/WebApiEngine/WebApiEngine.csproj'
        entries = [x for x in ET.parse(project).iter('PackageReference')
                   if x.attrib.get('Include') == 'System.Security.Cryptography.Pkcs']
        self.assertEqual(len(entries), 1)
        version = entries[0].attrib['Version']
        self.assertTrue(version.startswith('10.') and all(part.isdigit() for part in version.split('.')))
        self.assertGreaterEqual(tuple(int(part) for part in version.split('.')), (10, 0, 13))

    def test_api_exact_projection(self):
        # Testzweck: Nur drei feste API-Schlüssel und unveränderte Werte in den Callback reichen.
        r = self.run_case(document(), accepted=True)
        self.assertTrue(r['Selected'] and r['Applied'] and r['KeysCorrect'] and r['ValuesCorrect'])
        self.assertEqual(r['ApplyCalls'], 1)

    def test_migration_separate_identity(self):
        # Testzweck: Die eine Migrationsidentität bindet beide DB-Optionen, keine API-Secrets.
        r = self.run_case(document('migrate'), args=('--MIGRATE', '--secrets-stdin'), accepted=True)
        self.assertTrue(r['KeysCorrect'] and r['ValuesCorrect'] and r['ArgumentsPreserved'])

    def test_normal_mode_never_reads(self):
        # Testzweck: Ohne Opt-in unveränderte Argumente, keine Quelle lesen/Callbacks ausführen.
        r = self.run_case(b'', args=('--check-config',), fixture='read-throws', accepted=True)
        self.assertFalse(r['Selected'])
        self.assertEqual(r['LookupCalls'] + r['ApplyCalls'], 0)
        self.assertTrue(r['ArgumentsPreserved'])

    def test_cli_ambiguity(self):
        # Testzweck: Keine freien CLI-Overrides, falsche Schreibweise oder Mehrfachmodus.
        for args in [('--secrets-stdin', '--urls=http://localhost'), ('--secrets-stdin',)*2,
                     ('--secrets-stdin=anything',), ('--Secrets-Stdin',),
                     ('--secrets-stdin', '--migrate', '--migrate'), ('--secrets-stdin', '--check-config')]:
            with self.subTest(args=args):
                self.run_case(document(), args=args)

    def test_invalid_envelopes(self):
        # Testzweck: EOF, Syntax, UTF-8, Mehrfachwerte und übergroße Eingabe fail-closed.
        for raw in [b'', b'{', b'null', b'[]', b'{}', b'\xff', b'{}{}', b' '*16385,
                    b'{"a":{"b":{"c":{"d":{"e":1}}}}}', b'{"version":1,"version":1}',
                    json.dumps(document()).encode()+b'\x00']:
            with self.subTest(size=len(raw)):
                self.run_case(raw)

    def test_exact_byte_limit(self):
        # Testzweck: Exakte Byteobergrenze akzeptieren, ein zusätzliches Byte ablehnen.
        raw = json.dumps(document()).encode()
        padded = raw+b' '*(16384-len(raw))
        self.run_case(padded, accepted=True)
        self.run_case(padded+b' ')

    def test_default_input_and_escaped_duplicates(self):
        # Testzweck: Tatsächlicher Standardstream sowie gültige Hülle mit semantisch doppeltem Namen.
        self.run_case(document(), fixture='default-input', accepted=True)
        raw = json.dumps(document()).replace('"version": 1', '"version": 1,"version": 1')
        self.run_case(raw.encode())
        raw = json.dumps(document()).replace('"role": "api"', '"role": "api","r\\u006fle":"api"')
        self.run_case(raw.encode())

    def test_invalid_fields(self):
        # Testzweck: Rollen-/Typ-/Allowlist-/Längenfehler nicht auf andere Quellen zurückfallen.
        variants = []
        for key, value in [('version', 2), ('version', '1'), ('role', 'migrate'),
                           ('role', 'API'), ('values', []), ('unknown', MARKER)]:
            d = document(); d[key] = value; variants.append(d)
        for key in ('version', 'role', 'values'):
            d = document(); del d[key]; variants.append(d)
        for value in ['', ' ', None, 4, MARKER+'\n', 'x'*4097]:
            d = document(); d['values']['bffClientSecret'] = value; variants.append(d)
        for key in ['runtimeConnectionString', 'bffClientSecret', 'directoryClientSecret']:
            d = document(); del d['values'][key]; variants.append(d)
        d = document(); d['values']['Storage:Provider'] = MARKER; variants.append(d)
        for d in variants:
            with self.subTest(keys=list(d)):
                self.run_case(d)

    def test_duplicate_nested_keys(self):
        # Testzweck: Parser darf doppelte oder normalisierte Namen nicht zusammenklappen.
        raw = json.dumps(document()).replace('"bffClientSecret":', '"bffClientSecret":"x","bffClientSecret":')
        self.run_case(raw.encode())
        self.run_case(raw.replace('"bffClientSecret":"x"', '"bffClientSecret\\u0000":"x"').encode())

    def test_collisions_and_callbacks(self):
        # Testzweck: Vorhandene auch rollenfremde Quellen und fehlerhafte Callback-Sinks blockieren.
        for fixture in ['collision', 'migration-collision', 'lookup-throws', 'apply-throws', 'read-throws', 'read-partial-disconnect']:
            with self.subTest(fixture=fixture):
                r = self.run_case(document(), fixture=fixture)
                if fixture in ('collision', 'migration-collision', 'lookup-throws', 'read-throws', 'read-partial-disconnect'):
                    self.assertEqual(r['ApplyCalls'], 0)

    def test_actual_configuration_providers(self):
        # Testzweck: Reale reine In-Memory-Provider erkennen auch unten versteckte Fremdwerte.
        for fixture in ['provider-empty', 'provider-placeholder']:
            r = self.run_case(document(), fixture=fixture, accepted=True)
            self.assertTrue(r['Applied'] and r['KeysCorrect'] and r['ValuesCorrect'])
        for fixture in ['provider-collision', 'provider-hidden', 'provider-case', 'provider-whitespace']:
            self.run_case(document(), fixture=fixture)
        self.run_case(document('migrate'), args=('--secrets-stdin', '--migrate'), fixture='provider-hidden')

    def test_nested_configuration_providers(self):
        # Testzweck: Auch mehrfach verschachtelte Ketten prüfen; nicht prüfbare Sections ablehnen.
        self.run_case(document(), fixture='provider-chain-empty', accepted=True)
        for fixture in ['provider-chain-collision', 'provider-chain-hidden', 'provider-section']:
            with self.subTest(fixture=fixture):
                self.run_case(document(), fixture=fixture)

    def test_configuration_graph_limits(self):
        # Testzweck: Endliche Providerzahl/Tiefe sowie Zyklusbehandlung, ohne Secrets zu traversieren.
        self.run_case(document(), fixture='provider-count-limit', accepted=True)
        self.run_case(document(), fixture='provider-depth-limit', accepted=True)
        for fixture in ['provider-count-exceeded', 'provider-cycle', 'provider-depth-exceeded']:
            with self.subTest(fixture=fixture):
                self.run_case(document(), fixture=fixture)

    def test_uncancellable_stream_deadline(self):
        # Testzweck: Eigener Prozess endet trotz einer Cancellation-ignorierenden Quelle.
        start = time.monotonic()
        self.run_case(b'', fixture='read-ignores-cancellation')
        self.assertLess(time.monotonic()-start, 8)

    def test_synchronously_blocking_stream_deadline(self):
        # Testzweck: Selbst ein blockierender ReadAsync-Aufruf darf keinen unendlichen Start bewirken.
        self.run_case(b'', fixture='read-blocks-synchronously')

    def test_actual_open_stdin_deadline(self):
        # Testzweck: Echte offene Pipe ohne EOF darf weder Hoststart noch endloses Warten bewirken.
        child = subprocess.Popen([DOTNET, DLL, 'normal', '--secrets-stdin'], stdin=subprocess.PIPE,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=child_environment())
        try:
            child.wait(timeout=8)
            self.assertEqual(child.returncode, 2)
            self.assertEqual(child.stderr.read(), b'')
            self.assertFalse(json.loads(child.stdout.read())['Accepted'])
        finally:
            if child.poll() is None:
                child.kill(); child.wait(timeout=3)  # Nur eigener synthetischer Testprozess.
            child.stdin.close(); child.stdout.close(); child.stderr.close()


if __name__ == '__main__':
    unittest.main()
