"""Testzweck: Ausschließlich eigene stdlib-Fakes, kein Docker/Prozess/Netz/echte Uhr."""
import ast
import base64
import copy
import hashlib
import json
from types import SimpleNamespace
from unittest import mock
import contextlib
import io
import importlib.util
from pathlib import Path
import sys
import unittest

SPEC = importlib.util.spec_from_file_location('source_smoke', Path(__file__).with_name('run_smoke.py'))
m = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = m
SPEC.loader.exec_module(m)
SECRET = b'PUBLIC_SYNTHETIC_SENTINEL_00000001'
PLAN = m.Plan('a' * 32, 'docker.io/library/postgres@sha256:' + '1' * 64,
              'flowzer-smoke/api@sha256:' + '2' * 64)


class Clock:
    now = 0


class Ports:
    def __init__(self):
        self.calls, self.owned, self.volume = [], {}, None
        self.corrupt = None
        self.unknown = False
        self.clean = True
        self.stop_result = dict(code=0, stdout=b'', stderr=b'', eof=True, terminal=True, timeout=False)
        self.short = False
        self.result = dict(code=0, stdout=b'FLOWZER_FIXTURE_OK\n', stderr=b'', eof=True, terminal=True, timeout=False)

    def preflight(self):
        self.calls.append('preflight')
        return dict(architecture='linux/amd64', cpus=2, memory=2 * 1024**3, disk=4 * 1024**3,
                    images=(PLAN.pg_image, PLAN.api_image))

    def create_volume(self, name, nonce):
        self.calls.append('volume-create'); self.volume = dict(name=name, nonce=nonce); return name

    def inspect_volume(self, name):
        return copy.deepcopy(self.volume)

    def create(self, spec):
        self.calls.append('create-' + spec['role'])
        if self.unknown: raise OSError('PUBLIC_NO_PAYLOAD')
        cid = ('3' if spec['role'] == 'pg' else '4') * 64
        self.owned[cid] = copy.deepcopy(spec)
        return cid

    def inspect(self, cid):
        result = copy.deepcopy(self.owned.get(cid))
        if result is not None and self.corrupt: result.update(self.corrupt)
        return result

    def start_pg(self, cid, payload, remaining_ms):
        self.calls.append('start-pg')
        return dict(written=len(payload) - int(self.short), eof=True, timeout=False,
                    stdout=b'', stderr=b'')

    def run_fixture(self, cid, payload, remaining_ms):
        self.calls.append('fixture'); return copy.deepcopy(self.result)

    def stop(self, cid, remaining_ms):
        self.calls.append('stop-' + cid[0]); return dict(self.stop_result, terminal=self.clean)

    def remove(self, cid):
        self.calls.append('remove-' + cid[0])
        if self.clean: self.owned.pop(cid, None)

    def remove_volume(self, name):
        self.calls.append('volume-remove'); self.volume = None


class ContractTests(unittest.TestCase):
    def invoke(self, ports=None, clock=None, plan=PLAN):
        ports = ports or Ports()
        m.SmokeRun(ports, clock or Clock()).run(plan, SECRET, b'PUBLIC_FIXTURE_INPUT', [SECRET])
        return ports

    def test_owned_sequence_and_complete_cleanup(self):
        # Testzweck: Genau zwei eigene CIDs, keine Hostports, Ergebnis plus echte Abwesenheit der Fakehandles.
        p = self.invoke()
        self.assertEqual([x for x in p.calls if x.startswith('create-')], ['create-pg', 'create-api'])
        self.assertFalse(p.owned); self.assertIsNone(p.volume)
        self.assertLess(p.calls.index('start-pg'), p.calls.index('fixture'))

    def test_nonce_images_and_resources_fail_before_create(self):
        # Testzweck: Keine unbekannten Images/Architektur/Reserve oder ungebundene Nonce zulassen.
        for plan in [m.Plan('bad', PLAN.pg_image, PLAN.api_image), m.Plan(PLAN.nonce, 'postgres:17', PLAN.api_image)]:
            p = Ports()
            with self.assertRaises(m.SmokeFailure): self.invoke(p, plan=plan)
            self.assertNotIn('volume-create', p.calls)
        for change in [dict(memory=1), dict(disk=1), dict(architecture='darwin/arm64'), dict(images=())]:
            p = Ports(); original = p.preflight
            p.preflight = lambda: dict(original(), **change)
            with self.assertRaises(m.SmokeFailure): self.invoke(p)
            self.assertNotIn('volume-create', p.calls)

    def test_inspect_mismatch_never_signals_foreign_resource(self):
        # Testzweck: Auch ein zurückgegebener CID beweist keine eigene Nonce/Netz-/Rechtekonfiguration.
        for change in [dict(nonce='b' * 32), dict(network='host'), dict(privileged=True), dict(portBindings={'80': '80'})]:
            p = Ports(); p.corrupt = change
            with self.assertRaises(m.SmokeFailure): self.invoke(p)
            self.assertNotIn('start-pg', p.calls)
            self.assertFalse(any(x.startswith('stop-') for x in p.calls))
            self.assertNotIn('volume-remove', p.calls)

    def test_unknown_create_has_no_retry_and_retains_volume(self):
        # Testzweck: Unbekannter Writeausgang wird nur zurückgelesen, nie mit zweitem Create überdeckt.
        p = Ports(); p.unknown = True
        with self.assertRaises(m.SmokeFailure): self.invoke(p)
        self.assertEqual(p.calls.count('create-pg'), 1)
        self.assertNotIn('create-api', p.calls); self.assertNotIn('volume-remove', p.calls)
        # Testzweck: Unklarer Createausgang bleibt auch ohne CID konservativ möglicherweise gestartet.
        d=m.RunDiagnostics();p=Ports();p.unknown=True
        with self.assertRaises(m.SmokeFailure):m.SmokeRun(p,Clock(),d).run(PLAN,SECRET,b'PUBLIC_INPUT',[SECRET])
        proof=d.snapshot();self.assertEqual(proof['runtimeState'],'MAY_HAVE_STARTED')
        self.assertEqual(proof['failurePhase'],'PG_CREATE');self.assertEqual(proof['failureCode'],'OWNERSHIP')
        self.assertEqual(proof['cleanup'],'UNKNOWN');self.assertEqual(proof['candidates'],[])
        # Testzweck: Auch partiell angelegte Volumes ohne CID/Ownershipbeleg sind niemals COMPLETE.
        p=Ports();d=m.RunDiagnostics()
        def uncertain_volume(name,nonce):
            p.volume=dict(name=name,nonce=nonce)
            raise OSError('PUBLIC_UNKNOWN_VOLUME_OUTCOME')
        p.create_volume=uncertain_volume
        with self.assertRaises(m.SmokeFailure):m.SmokeRun(p,Clock(),d).run(PLAN,SECRET,b'PUBLIC_INPUT',[SECRET])
        self.assertIsNotNone(p.volume);self.assertEqual(d.snapshot()['failurePhase'],'VOLUME_CREATE')
        self.assertEqual(d.snapshot()['runtimeState'],'MAY_HAVE_STARTED');self.assertEqual(d.snapshot()['cleanup'],'UNKNOWN')


    def test_short_write_never_starts_fixture(self):
        # Testzweck: Vorzeitiges EOF/Shortwrite beendet die PG-Initialisierung, bevor irgendein APIkind startet.
        p = Ports(); p.short = True
        with self.assertRaises(m.SmokeFailure): self.invoke(p)
        self.assertNotIn('fixture', p.calls)

    def test_no_second_run_after_success_or_failure(self):
        # Testzweck: Ein Runner ist absichtlich ein Versuch, kein Wiederanlauf-/Generalscheduler.
        p = Ports(); runner = m.SmokeRun(p, Clock())
        runner.run(PLAN, SECRET, b'PUBLIC_INPUT', [SECRET]); count = len(p.calls)
        with self.assertRaises(m.SmokeFailure): runner.run(PLAN, SECRET, b'PUBLIC_INPUT', [SECRET])
        self.assertEqual(len(p.calls), count)

    def test_parent_output_all_encodings_both_channels(self):
        # Testzweck: Auch PG-/Feederkanäle, nicht nur C#-APIausgabe, vollständig gegen Sentinels prüfen.
        for value in [SECRET, base64.b64encode(SECRET), base64.b64encode(base64.b64encode(SECRET))]:
            for channel in ['stdout', 'stderr']:
                r = copy.deepcopy(Ports().result); r[channel] = value
                with self.assertRaises(m.SmokeFailure): m.verify_output(r, [SECRET])

    def test_output_limit_and_channel_deadline_terminal(self):
        # Testzweck: Weder Trunkierung noch Exit0 bei unvollständigen/überfälligen Readern grün werten.
        for changed in [dict(stdout=b'x' * 65537), dict(eof=False), dict(terminal=False), dict(timeout=True), dict(code=1)]:
            with self.assertRaises(m.SmokeFailure): m.verify_output(dict(Ports().result, **changed), [SECRET])

    def test_unclassified_success_payload_is_not_acceptance(self):
        # Testzweck: Bloß Exit0 oder ein erfundenes "Healthy" ersetzt nicht den festen geprüften Kindvertrag.
        p = Ports(); p.result['stdout'] = b'Healthy'
        with self.assertRaises(m.SmokeFailure): self.invoke(p)

    def test_cleanup_unknown_is_not_success(self):
        # Testzweck: Kind-/Containerabschluss und tatsächliches Verschwinden sind separate Gates.
        p = Ports(); p.clean = False
        with self.assertRaises(m.SmokeFailure): self.invoke(p)
        self.assertNotIn('volume-remove', p.calls)
        # Testzweck: Primärer Fixturefehler und misslungener Cleanup bleiben zwei unabhängige Fakten.
        p=Ports();p.clean=False;p.result['code']=2;d=m.RunDiagnostics()
        with self.assertRaises(m.SmokeFailure):m.SmokeRun(p,Clock(),d).run(PLAN,SECRET,b'PUBLIC_INPUT',[SECRET])
        proof=d.snapshot();self.assertEqual(proof['failurePhase'],'VERIFY_FIXTURE')
        self.assertEqual(proof['failureCode'],'OUTPUT');self.assertEqual(proof['cleanup'],'UNKNOWN')
        self.assertTrue(all(c['ownership']=='VERIFIED' for c in proof['candidates']))
        self.assertNotIn(SECRET.decode(),json.dumps(proof))
        # Testzweck: Erste API-Cleanupstörung/-Outputquelle nicht durch spätere PG-Removephase ersetzen.
        for output_fault in [False,True]:
            p=Ports();d=m.RunDiagnostics();original=p.stop
            def stop(cid,remaining):
                if cid=='4'*64:
                    if output_fault:return dict(original(cid,remaining),code=1)
                    raise OSError('PUBLIC_API_STOP_FAULT')
                return original(cid,remaining)
            p.stop=stop
            with self.assertRaises(m.SmokeFailure):m.SmokeRun(p,Clock(),d).run(PLAN,SECRET,b'PUBLIC_INPUT',[SECRET])
            self.assertEqual(d.snapshot()['failurePhase'],'CLEANUP_STOP')
            self.assertEqual(d.snapshot()['failureCode'],'OUTPUT' if output_fault else 'CLEANUP')

        # Testzweck: C#-Diagnose nur nach Canaryprüfung, unbekannte/duplizierte/abgeschnittene Daten nie ausgeben.
        f=dict(result='FLOWZER_SMOKE_REJECTED',diagnostic=dict(phase='Cleanup',failurePhase='Migration',failureCode='Output',cleanup='Complete'))
        result=dict(code=2,stdout=b'',stderr=json.dumps(f).encode(),eof=True,terminal=True,timeout=False)
        self.assertEqual(m.project_fixture_failure(result,[SECRET])['state'],'VALIDATED')
        for change in [dict(eof=False),dict(terminal=False),dict(timeout=True)]:
            self.assertEqual(m.project_fixture_failure(dict(result,**change),[SECRET])['state'],'UNKNOWN')
        for output in [SECRET,b'Bearer_CANARY',json.dumps(f).encode()[:-2],json.dumps(f).encode()+b'EXTRA',
                       json.dumps(f).replace('"result":','"result":"FLOWZER_SMOKE_REJECTED","result":').encode()]:
            if output==SECRET:
                with self.assertRaises(m.SmokeFailure):m.project_fixture_failure(dict(result,stderr=output),[SECRET])
            else:self.assertEqual(m.project_fixture_failure(dict(result,stderr=output),[SECRET])['state'],'UNKNOWN')


    def test_clock_advance_after_start_stops_before_fixture(self):
        # Testzweck: Gesamte Frist statt nur scheinbar erfolgreicher einzelner Adapterrückgaben.
        p = Ports(); clock = Clock(); original = p.start_pg
        def slow(*args):
            clock.now += 300001
            return original(*args)
        p.start_pg = slow
        with self.assertRaises(m.SmokeFailure): self.invoke(p, clock)
        self.assertNotIn('fixture', p.calls)

    def test_late_pg_output_is_checked_during_cleanup(self):
        # Testzweck: Die PG-Pipe ist beim frühen stdin-EOF noch live; erst Ende beweist komplette Ausgabe.
        p = Ports(); p.stop_result['stderr'] = SECRET
        with self.assertRaises(m.SmokeFailure): self.invoke(p)
        self.assertFalse(p.owned)

    def test_module_initializes_no_live_adapter_at_import(self):
        # Testzweck: Reale Adapterquelle ist erlaubt, deren Konstruktion/Transport beim Import dagegen nie.
        source = Path(__file__).with_name('run_smoke.py').read_text(); tree = ast.parse(source)
        top_calls = [n for n in tree.body if isinstance(n, ast.Expr) and isinstance(n.value, ast.Call)]
        self.assertEqual(top_calls, [])
        self.assertIn('FLOWZER_SMOKE_SOURCE_ONLY', source)


class AdapterSourceContractTests(unittest.TestCase):
    def test_existing_volume_never_becomes_owned(self):
        # Testzweck: Docker-create ist idempotent; selbst passende Altlabels erlauben keine Übernahme/Löschung.
        port = object.__new__(m.DockerPorts); port.b = self.binding()
        port._fresh = lambda: None
        name = 'flowzer-smoke-' + PLAN.nonce
        for existing in [dict(name=name, nonce=PLAN.nonce), dict(name=name, nonce='b'*32)]:
            port.inspect_volume = mock.Mock(return_value=existing); port._run = mock.Mock()
            with self.assertRaises(m.SmokeFailure): port.create_volume(name, PLAN.nonce)
            port._run.assert_not_called()
        calls = []
        port.inspect_volume = lambda name: calls.append('inspect')
        port._run = lambda args: calls.append('create')
        self.assertEqual(port.create_volume(name, PLAN.nonce), name)
        self.assertEqual(calls, ['inspect', 'create'])

    def test_constructor_faults_close_and_retain_own_handle(self):
        # Testzweck: Jeder Setupfehler nach Start behält den eigenen Handle und beendet nur den Fakeprozess.
        for stage in ['selector', 'spawn', 'blocking1', 'blocking2', 'blocking3',
                      'register1', 'register2', 'register3', 'cleanup']:
            with self.subTest(stage=stage):
                registry = []; signals = []
                streams = [SimpleNamespace(closed=False, fileno=lambda i=i: 100+i) for i in range(3)]
                for stream in streams:
                    stream.close = lambda stream=stream: setattr(stream, 'closed', True)
                process = SimpleNamespace(stdin=streams[0], stdout=streams[1], stderr=streams[2],
                    pid=777, returncode=None)
                process.poll = lambda: process.returncode
                process.wait = lambda timeout: setattr(process, 'returncode', -15)
                selector = mock.Mock()
                def fault_at(prefix):
                    calls = 0
                    def operation(*args):
                        nonlocal calls
                        calls += 1
                        if stage == prefix+str(calls) or stage == 'cleanup' and prefix == 'blocking':
                            raise OSError('PUBLIC_SETUP_FAULT')
                    return operation
                def signal(pid, value):
                    signals.append((pid, value))
                    if stage == 'cleanup': raise PermissionError('PUBLIC_UNKNOWN')
                    if value == 0: raise ProcessLookupError()
                port = object.__new__(m.DockerPorts)
                port.base, port.env, port.secrets, port.commands = [], {}, [SECRET], registry
                port._fresh = lambda: None
                with mock.patch.object(m, 'LiveClock', return_value=Clock()), \
                        mock.patch('subprocess.Popen', side_effect=OSError('PUBLIC_SPAWN') if stage == 'spawn' else None,
                                   return_value=process) as spawn, \
                        mock.patch('selectors.DefaultSelector', side_effect=OSError('PUBLIC_SELECTOR') if stage == 'selector' else None,
                                   return_value=selector), \
                        mock.patch('os.set_blocking', side_effect=fault_at('blocking')), \
                        mock.patch('os.killpg', side_effect=signal):
                    selector.register.side_effect = fault_at('register')
                    with self.assertRaises(m.SmokeFailure):
                        port.start_pg('3'*64, b'PUBLIC_INPUT', 1000)
                self.assertEqual(len(registry), 1)
                self.assertEqual(registry[0].data, bytearray(len(b'PUBLIC_INPUT')))
                self.assertEqual(registry[0].verified_closed, stage != 'cleanup')
                if stage == 'selector': spawn.assert_not_called()
                elif stage != 'spawn':
                    self.assertTrue(all(s.closed for s in streams)); self.assertTrue(signals)
                    self.assertTrue(all(pid == 777 for pid, _ in signals))

    @staticmethod
    def binding():
        return dict(version=1, owner='01a11f64-c814-7f51-abdb-1b7462e0491e', nonce=PLAN.nonce,
            expiresUnix=200, dockerPath='/usr/bin/docker', dockerSha256='a'*64,
            dockerHost='unix:///var/run/docker.sock', daemonId='PUBLIC_OWN_DAEMON',
            pgImage=PLAN.pg_image, apiImage=PLAN.api_image, apiSha256='b'*64, feederSha256='c'*64,
            nativePath='/app/ClearScriptV8.linux-x64.so', nativeSha256='d'*64, runtime='10.0.13',
            pgUid=999, apiUid=1654, gid=22000, expectedMigration=21)

    def test_binding_hash_nonce_version_and_expiry_are_pure_gates(self):
        # Testzweck: Keine Dateizugriffe/Clockreads: gleiche reine Validierung vor irgendeinem Adapter.
        value = self.binding(); raw = json.dumps(value).encode()
        self.assertEqual(m.validate_binding(raw, hashlib.sha256(raw).hexdigest(), PLAN.nonce, 100), value)
        for changed in [dict(version=True), dict(expiresUnix=100), dict(expiresUnix=401),
                        dict(nonce='b'*32), dict(nativePath='/app/../outside.so'),
                        dict(dockerHost='tcp://foreign:2375'), dict(pgUid=0), dict(extra='UNKNOWN')]:
            payload = json.dumps(dict(value, **changed)).encode()
            with self.assertRaises(m.SmokeFailure):
                m.validate_binding(payload, hashlib.sha256(payload).hexdigest(), PLAN.nonce, 100)
        duplicate = raw.replace(b'"version": 1', b'"version": 1, "version": 1')
        with self.assertRaises(m.SmokeFailure):
            m.validate_binding(duplicate, hashlib.sha256(duplicate).hexdigest(), PLAN.nonce, 100)
        with self.assertRaises(m.SmokeFailure): m.validate_binding(raw, '0'*64, PLAN.nonce, 100)

    def test_real_inspection_projection_uses_only_fake_command_json(self):
        # Testzweck: Nicht nur der Kernfake: der echte Docker-JSONprüfer darf fremde Flags/ENV/Cmd nicht übernehmen.
        port = object.__new__(m.DockerPorts); port.b = self.binding(); port.binding_sha = 'e'*64
        cid = '4'*64
        expected = dict(nonce=PLAN.nonce, role='api', image=PLAN.api_image, network='none', logDriver='none',
            privileged=False, portBindings={}, hostMounts=[], socketVolume='flowzer-smoke-'+PLAN.nonce,
            memory=768*1024**2, pids=128, cpus=1)
        port.diagnostic=m.RunDiagnostics()
        port.expected = {cid: expected}; port.image_ids = {PLAN.api_image: 'sha256:'+'f'*64}
        port.image_env = {PLAN.api_image: {'DOTNET_VERSION':'10.0.13'}}
        config = dict(User='1654:22000', Env=[k+'='+v for k,v in dict(port.image_env[PLAN.api_image], **port._api_environment()).items()],
            Entrypoint=['/usr/share/dotnet/dotnet'], Cmd=['/app/SmokeFeeder.dll','--bound-fixture',PLAN.nonce,'b'*64,'c'*64],
            Labels={'flowzer-smoke.nonce':PLAN.nonce,'flowzer-smoke.role':'api'}, Image=PLAN.api_image)
        host = dict(Binds=None, PidMode='', IpcMode='private', PublishAllPorts=False, Memory=768*1024**2,
            PidsLimit=128, NanoCpus=1000000000, ReadonlyRootfs=True, CapAdd=None, CapDrop=['ALL'],
            SecurityOpt=['no-new-privileges:true'], Tmpfs={'/tmp':'rw,noexec,nosuid,size=67108864,uid=1654,gid=22000'},
            NetworkMode='none', LogConfig={'Type':'none'}, Privileged=False, PortBindings={})
        value = dict(Id=cid, Image=port.image_ids[PLAN.api_image], Config=config, HostConfig=host,
            Mounts=[dict(Type='volume',Name=expected['socketVolume'],Destination='/flowzer-socket',RW=True)])
        port._run = lambda *args,**kw: json.dumps([value]).encode()
        self.assertEqual(port.inspect(cid),expected)
        for key,new in [('Env',config['Env']+['LD_PRELOAD=/PUBLIC_NOT_A_LOADER']),('Cmd',['UNKNOWN']),('Entrypoint',['/bin/sh'])]:
            original=value['Config'][key];value['Config'][key]=new
            with self.assertRaises(m.SmokeFailure):port.inspect(cid)
            value['Config'][key]=original
        port.diagnostic=m.RunDiagnostics()
        value['HostConfig']['Tmpfs']={}
        with self.assertRaises(m.SmokeFailure):port.inspect(cid)
        self.assertEqual(port.diagnostic.violation,'TMPFS')
        # Testzweck: Echter _run-Adapter mit zwei Fake-CLIfehlern: primärer Exit1 bleibt neben CleanupExit2 erhalten.
        port=object.__new__(m.DockerPorts);d=m.RunDiagnostics();port.diagnostic=d
        port.base=['PUBLIC'];port.env={};port.secrets=[SECRET];port.commands=[]
        port.clock=Clock();port.cleanup_until=None;port.pg=None
        child=mock.Mock();child.tick.return_value=True
        child.result.side_effect=[dict(code=n,stdout=b'',stderr=b'PUBLIC') for n in [1,2]]
        with mock.patch.object(m,'AttachedCommand',return_value=child):
            d.enter('PG_CREATE')
            with self.assertRaises(m.SmokeFailure) as first:port._run(['PUBLIC'])
            d.failed(first.exception);d.enter('CLEANUP_INSPECT')
            with self.assertRaises(m.SmokeFailure):port._run(['PUBLIC'])
        self.assertEqual(d.snapshot()['tool'],dict(phase='PG_CREATE',state='EXIT_NONZERO',exitCode=1))
        self.assertEqual(d.snapshot()['cleanupTool'],dict(phase='CLEANUP_INSPECT',state='EXIT_NONZERO',exitCode=2))


    def test_real_channel_pump_uses_fake_fds_clock_and_selector(self):
        # Testzweck: Der wirkliche Reader erkennt einen über zwei Lesezyklen geteilten Sentinel, ohne Prozess/FD.
        command = object.__new__(m.AttachedCommand)
        chunks=iter([SECRET[:9],SECRET[9:]])
        command.os=SimpleNamespace(read=lambda *args:next(chunks))
        command.clock=SimpleNamespace(now=0);command.deadline=1000;command.secrets=[SECRET]
        command.out=bytearray();command.err=bytearray()
        key=SimpleNamespace(data='out',fd=123,fileobj=None)
        command.selector=SimpleNamespace(select=lambda timeout:[(key,None)],get_map=lambda:{123:key})
        command.process=SimpleNamespace(poll=lambda:None)
        self.assertFalse(command.tick(0))
        with self.assertRaises(m.SmokeFailure):command.tick(0)

    def test_unbound_entrypoint_never_constructs_transport(self):
        # Testzweck: Der unveränderte normale Aufruf bleibt vor Clock-/Datei-/Dockerinitialisierung geschlossen.
        with mock.patch.object(m,'DockerPorts') as ctor,contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(m.main([]),78)
            ctor.assert_not_called()


if __name__ == '__main__':
    unittest.main(verbosity=2)
