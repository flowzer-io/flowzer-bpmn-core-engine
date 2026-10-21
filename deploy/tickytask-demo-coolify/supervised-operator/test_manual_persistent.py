"""Testzweck: persistente Komposition mit Originalports und rein synthetischem Dockerfake."""
import dataclasses
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest import mock

D = Path(__file__).parent
spec = importlib.util.spec_from_file_location('persistent_fixture', D/'test_manual_runtime_adapter.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)


class PersistentTests(unittest.TestCase):
    def setUp(self):
        # Testzweck: fehlende persistente Komposition ist ein expliziter RED, kein Livefallback.
        self.assertTrue((D/'manual_persistent.py').exists(), 'persistent caller not implemented')
        spec = importlib.util.spec_from_file_location('manual_persistent', D/'manual_persistent.py')
        self.m = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = self.m
        spec.loader.exec_module(self.m)
        self.original, self.pipe = fixture.RuntimeTests().make()
        settings = dataclasses.replace(self.original.r.settings, authority='https://auth.tickytask.de/realms/TickyTask')
        self.binding = self.m.PersistentBinding(self.original.pg.b, dataclasses.replace(self.original.r, settings=settings), self.original.pg.target)
        self.claims = []
        self.deliveries = []

    def run_once(self, mode='initial', binding=None, caller=None, deliver=None, guard=None, claim=None):
        def claim_once():
            self.claims.append(mode)
            return True
        def delivery():
            self.deliveries.append(mode)
            return fixture.RuntimeTests().bundle(mode)
        return (caller or self.m.PersistentCaller()).run(mode, binding or self.binding,
            guard=guard or (lambda: None), claim_once=claim or claim_once,
            deliver=deliver or delivery, pipe=self.pipe)

    def test_initial_keeps_owned_pg_socket_keyring_and_live_api(self):
        # Testzweck: Erfolg erhält alle eigenen Ressourcen statt Pilot-finally-Cleanup.
        result = self.run_once()
        self.assertEqual(result.result, 'READY')
        self.assertEqual(result.pg_cid, 'b'*64)
        self.assertEqual(result.api_cid, 'd'*64)
        self.assertEqual(len(self.pipe.labels), 3)
        self.assertTrue(any('_postgres_' in k for k in self.pipe.runs))
        self.assertTrue(any('_api_' in k for k in self.pipe.runs))
        self.assertFalse(any(args[:2] in (['container', 'rm'], ['volume', 'rm']) for args, _, _ in self.pipe.calls))
        self.assertEqual(self.claims, ['initial'])
        self.assertEqual(self.deliveries, ['initial'])

    def test_restart_uses_exact_existing_state_without_init_or_role_overwrite(self):
        # Testzweck: neuer APIversuch benutzt ursprüngliche PG-/Keyring-/Socketidentitäten.
        self.assertEqual(self.run_once().result, 'READY')
        self.pipe.stopped_ids = {'b'*64, 'd'*64}
        self.pipe.runs = {k:v for k,v in self.pipe.runs.items() if '_postgres_' in k or '_api_' in k}
        current = dataclasses.replace(self.binding.runtime, attempt_nonce='3'*32, prior_api_nonce='c'*32)
        restart = dataclasses.replace(self.binding, runtime=current, prior_pg_cid='b'*64, prior_api_cid='d'*64)
        before = len(self.pipe.calls)
        result = self.run_once('restart', restart)
        self.assertEqual(result.result, 'READY')
        self.assertEqual(result.pg_cid, 'b'*64)
        calls = [args for args, _, _ in self.pipe.calls[before:]]
        self.assertFalse(any(args[:2] in (['volume','create'], ['volume','rm'], ['container','rm']) for args in calls))
        self.assertFalse(any('/usr/lib/postgresql/17/bin/initdb' in args or '/bin/chown' in args for args in calls))
        self.assertFalse(any(b'CREATE ROLE' in payload for _,payload,_ in self.pipe.calls[before:]))
        self.assertEqual(len(self.pipe.labels), 3)

    def test_foreign_network_fails_before_secret_delivery(self):
        # Testzweck: kein erweiterter Peer-/CIDRguard, keine Lieferung nach Netzwerkdrift.
        self.pipe.bad = 'peer'
        result = self.run_once()
        self.assertEqual((result.result, result.phase), ('STOP_UNKNOWN','PREFLIGHT'))
        self.assertEqual(self.deliveries, [])
        self.assertEqual(self.pipe.labels, {})

    def test_failure_after_api_start_preserves_candidates_and_state(self):
        # Testzweck: FEEL-/Readinessfehler ist kein Datenrollback oder Remote-Cleanupauftrag.
        self.pipe.bad = 'feel'
        result = self.run_once()
        self.assertEqual((result.result,result.phase), ('STOP_UNKNOWN','API_PROOF'))
        self.assertEqual(result.api_cid, 'd'*64)
        self.assertEqual(result.pg_cid, 'b'*64)
        self.assertEqual(len(self.pipe.labels), 3)
        self.assertFalse(any(args[:2] in (['container','rm'],['volume','rm']) for args,_,_ in self.pipe.calls))

    def test_unknown_transport_keeps_handle_without_cleanup_retry(self):
        # Testzweck: unbekannter eigener Transport wird weder versteckt noch erneut gestartet.
        self.pipe.bad = 'transport'
        caller = self.m.PersistentCaller()
        result = self.run_once(caller=caller)
        self.assertEqual(result.result, 'STOP_UNKNOWN')
        self.assertEqual(result.own_local_pid, 12345)
        before = len(self.pipe.calls)
        self.assertEqual(self.run_once(caller=caller).phase, 'ONCE')
        self.assertEqual(len(self.pipe.calls), before)
        self.assertEqual(len(self.claims), 1)

    def test_false_or_unknown_durable_claim_blocks_all_ports(self):
        # Testzweck: bloß plausibler Rückgabewert ersetzt keinen bestätigten exklusiven Versuch.
        for value in [False, None, 1, 'true']:
            with self.subTest(value=value):
                result = self.run_once(claim=lambda: value)
                self.assertEqual(result.phase, 'ONCE')
                self.assertEqual(self.pipe.calls, [])
                self.assertEqual(self.deliveries, [])

    def test_guard_rejection_never_creates_ports_or_resolves_secrets(self):
        # Testzweck: nicht ausgestellte reale Rootbindung stoppt noch vor Originalports.
        def denied():
            raise RuntimeError('secret-like raw detail must not escape')
        result = self.run_once(guard=denied)
        self.assertEqual((result.result,result.phase), ('STOP_UNKNOWN','BINDING'))
        self.assertEqual(self.pipe.calls, [])
        self.assertNotIn('secret-like', repr(result))

    def test_unissued_bindings_and_wrong_issuer_block_before_claim(self):
        # Testzweck: keine geratenen Demo-/IdPwerte oder NOT_ISSUED als ausführbare Konfiguration.
        for binding in [None, dataclasses.replace(self.binding, runtime=dataclasses.replace(self.binding.runtime,
                settings=dataclasses.replace(self.binding.runtime.settings, authority='https://other.invalid/realms/TickyTask')))]:
            result = self.run_once(binding=binding) if binding else self.m.PersistentCaller().run('initial',None,
                guard=lambda:None,claim_once=lambda:True,deliver=lambda:None,pipe=self.pipe)
            self.assertEqual(result.phase, 'BINDING')
            self.assertEqual(self.pipe.calls, [])
        self.assertEqual(self.claims, [])

    def test_initial_and_restart_coordinates_cannot_be_mixed(self):
        # Testzweck: Erststartnonce und registrierte Bestandscids dürfen nicht vertauscht werden.
        cases = [('initial',dataclasses.replace(self.binding,prior_pg_cid='b'*64)),
            ('initial',dataclasses.replace(self.binding,runtime=dataclasses.replace(self.binding.runtime,attempt_nonce='3'*32))),
            ('restart',self.binding)]
        for mode,binding in cases:
            self.assertEqual(self.run_once(mode,binding).phase,'BINDING')
        self.assertEqual(self.pipe.calls,[])
        self.assertEqual(self.claims,[])

    def test_running_prior_api_is_never_stopped_or_replaced(self):
        # Testzweck: manuell heißt nicht, eine noch laufende alte API automatisch zu beenden.
        self.run_once()
        self.pipe.runs={k:v for k,v in self.pipe.runs.items() if '_postgres_' in k or '_api_' in k}
        r=dataclasses.replace(self.binding.runtime,attempt_nonce='3'*32,prior_api_nonce='c'*32)
        b=dataclasses.replace(self.binding,runtime=r,prior_pg_cid='b'*64,prior_api_cid='d'*64)
        before=len(self.pipe.calls)
        self.assertEqual(self.run_once('restart',b).phase,'PREFLIGHT')
        self.assertFalse(any(args[:2] in (['container','stop'],['container','start'],['container','run']) for args,_,_ in self.pipe.calls[before:]))

    def test_close_failure_cannot_turn_into_ready_or_remote_cleanup(self):
        # Testzweck: auch RAMclose-Fehler bleibt STOP und löst keinerlei Reparaturlauf aus.
        with mock.patch.object(self.m.rt.RuntimePorts,'close',side_effect=RuntimeError('private detail')):
            result=self.run_once()
        self.assertEqual(result.result,'STOP_UNKNOWN')
        self.assertTrue(result.close_failed)
        self.assertNotIn('private detail',repr(result))
        self.assertEqual(len(self.pipe.labels),3)

    def test_bundle_is_zeroed_and_dropped_on_success_and_failure(self):
        # Testzweck: Caller-finally schließt nur eigene RAMpuffer, niemals Datenressourcen.
        captured=[]
        original=self.m.rt.RuntimePorts
        def construct(*args,**kwargs):
            ports=original(*args,**kwargs);captured.append(ports);return ports
        with mock.patch.object(self.m.rt,'RuntimePorts',side_effect=construct):
            self.run_once()
        self.assertIsNone(captured[0].bundle)
        self.assertIsNone(captured[0].expected_api)
        self.assertEqual(captured[0].sql_packets,{})

    def test_result_contains_no_bundle_exception_or_automatic_runtime_claim(self):
        # Testzweck: Checkpoint bleibt minimal, keine Rohfehler, Passwörter oder allgemeine Demoabnahme.
        result=self.run_once()
        text=json.dumps(dataclasses.asdict(result))
        for value in ['synthetic-admin-123','synthetic-runtime-123','synthetic-bff-123456','bundle','realIdpAccepted']:
            self.assertNotIn(value,text)
        self.assertFalse(result.retry_allowed)
        self.assertEqual(result.data_policy,'PRESERVE_NO_REMOTE_CLEANUP')

    def test_second_object_cannot_reuse_external_durable_attempt(self):
        # Testzweck: ein neues Pythonobjekt darf den dauerhaften Rootclaim nicht ersetzen.
        used=[]
        def claim():
            if used:return False
            used.append(True)
            return True
        self.assertEqual(self.run_once(claim=claim).result,'READY')
        before=len(self.pipe.calls)
        self.assertEqual(self.run_once(claim=claim).phase,'ONCE')
        self.assertEqual(len(self.pipe.calls),before)
        self.assertEqual(len(self.deliveries),1)

    def test_secret_resolver_error_is_redacted_and_does_not_initialize(self):
        # Testzweck: Lieferfehler nach positiver Vorprüfung erzeugt keine Datenressourcen.
        def broken():raise RuntimeError('synthetic-private-error-detail')
        result=self.run_once(deliver=broken)
        self.assertEqual((result.result,result.phase),('STOP_UNKNOWN','INPUT'))
        self.assertFalse(self.pipe.labels)
        self.assertNotIn('synthetic-private-error-detail',repr(result))

    def test_unknown_writer255_never_calls_following_effect_or_cleanup(self):
        # Testzweck: SSH255 nach Volume-write bleibt UNKNOWN, keine Wiederholung oder Folgeaktion.
        original=self.pipe.exchange
        def lost(argv,payload,secrets,**kw):
            args=fixture.pg.remote_docker_arguments(argv[-1])
            if args[:2]==['volume','create']:
                self.pipe.calls.append((args,bytes(payload),list(secrets)))
                return fixture.t.PipeResult(255,b'',b'')
            return original(argv,payload,secrets,**kw)
        self.pipe.exchange=lost
        result=self.run_once()
        self.assertEqual((result.result,result.phase),('STOP_UNKNOWN','INITIALIZE'))
        self.assertEqual(self.pipe.calls[-1][0][:2],['volume','create'])
        self.assertEqual(sum(args[:2]==['volume','create'] for args,_,_ in self.pipe.calls),1)

    def test_restart_does_not_adopt_old_helpers_or_foreign_pg(self):
        # Testzweck: Initial-Hilfscontainer müssen separat bestätigt aufgeräumt sein; kein Adoptionspfad.
        self.run_once()
        self.pipe.stopped_ids={'b'*64,'d'*64}
        r=dataclasses.replace(self.binding.runtime,attempt_nonce='3'*32,prior_api_nonce='c'*32)
        b=dataclasses.replace(self.binding,runtime=r,prior_pg_cid='b'*64,prior_api_cid='d'*64)
        before=len(self.pipe.calls)
        self.assertEqual(self.run_once('restart',b).phase,'PREFLIGHT')
        self.assertFalse(any(args[:2] in (['container','start'],['container','run'],['container','rm'],['volume','rm']) for args,_,_ in self.pipe.calls[before:]))

    def test_restart_bundle_with_admin_never_rotates_existing_roles(self):
        # Testzweck: beim manuellen Wiederanlauf ist auch eine neue Adminlieferung verboten.
        self.run_once()
        self.pipe.stopped_ids={'b'*64,'d'*64}
        self.pipe.runs={k:v for k,v in self.pipe.runs.items() if '_postgres_' in k or '_api_' in k}
        r=dataclasses.replace(self.binding.runtime,attempt_nonce='3'*32,prior_api_nonce='c'*32)
        b=dataclasses.replace(self.binding,runtime=r,prior_pg_cid='b'*64,prior_api_cid='d'*64)
        before=len(self.pipe.calls)
        result=self.run_once('restart',b,deliver=lambda:fixture.RuntimeTests().bundle('initial'))
        self.assertEqual(result.phase,'INPUT')
        self.assertFalse(any(args[:2] in (['container','start'],['container','run'],['container','exec']) for args,_,_ in self.pipe.calls[before:]))

    def test_original_port_first_close_error_preserves_transport_pid_and_failure(self):
        # Testzweck: Originalport-close darf UNKNOWN-PID nicht maskieren, auch wenn finally-close gelingt.
        self.pipe.bad='transport'
        close=self.m.rt.RuntimePorts.close
        attempts=[]
        def first_fails(ports):
            attempts.append(True)
            if len(attempts)==1:raise RuntimeError('synthetic-close-detail')
            return close(ports)
        with mock.patch.object(self.m.rt.RuntimePorts,'close',first_fails):
            result=self.run_once()
        self.assertEqual((result.result,result.phase),('STOP_UNKNOWN','PREFLIGHT'))
        self.assertEqual(result.own_local_pid,12345)
        self.assertTrue(result.close_failed)
        self.assertEqual(len(attempts),2)
        self.assertEqual(len(self.pipe.calls),1)
        self.assertFalse(self.deliveries)
        self.assertNotIn('synthetic-close-detail',repr(result))

    def test_only_original_transport_boundary_can_supply_strict_own_pid(self):
        # Testzweck: freie Guard-/Resolver-/Pipe-Exceptions und bool/String-PIDs sind keine Eigenhandles.
        class Foreign(Exception):own_pid=54321
        for error in [Foreign(),fixture.t.TransportFailure('HANDLE_UNKNOWN',12345)]:
            def denied():raise error
            self.assertIsNone(self.run_once(guard=denied).own_local_pid)
        original=self.pipe.exchange
        for value in [True,False,-1,0,'12345',None]:
            error=fixture.t.TransportFailure('HANDLE_UNKNOWN',12345)
            error.own_pid=value
            def failed(*args,**kwargs):raise error
            self.pipe.exchange=failed
            self.assertIsNone(self.run_once().own_local_pid)
        def foreign(*args,**kwargs):raise Foreign()
        self.pipe.exchange=foreign
        self.assertIsNone(self.run_once().own_local_pid)
        self.pipe.exchange=original

    def test_observer_preserves_exact_bound_exchange_arguments_result_and_exception(self):
        # Testzweck: reine Statusbeobachtung darf weder Kanal/Frist noch Daten/Exceptions ersetzen.
        seen=[]
        value=object()
        class Native:
            def exchange(inner,*args,**kwargs):
                seen.append((args,kwargs));return value
        observation=self.m._AttemptObservation(Native())
        argv=['bound'];payload=bytearray(b'synthetic');canaries=[b'synthetic']
        self.assertIs(observation.exchange(argv,payload,canaries,timeout=3),value)
        self.assertIs(seen[0][0][0],argv)
        self.assertIs(seen[0][0][1],payload)
        self.assertIs(seen[0][0][2],canaries)
        self.assertEqual(seen[0][1],{'timeout':3})
        error=fixture.t.TransportFailure('HANDLE_UNKNOWN',12345)
        def failed(*args,**kwargs):raise error
        observation.pipe.exchange=failed
        with self.assertRaises(fixture.t.TransportFailure) as got:observation.exchange(argv,payload,canaries,timeout=3)
        self.assertIs(got.exception,error)
        self.assertEqual(observation.own_pid,12345)

    def test_nested_pg_first_close_error_keeps_transport_pid_and_close_failure(self):
        # Testzweck: auch inneres PG-close darf Fehler/PID nicht vor Runtimeclose verbergen.
        original_exchange=self.pipe.exchange
        def lost(argv,payload,secrets,**kwargs):
            args=fixture.pg.remote_docker_arguments(argv[-1])
            if args[:2]==['volume','create']:
                self.pipe.calls.append((args,bytes(payload),list(secrets)))
                raise fixture.t.TransportFailure('HANDLE_UNKNOWN',12345)
            return original_exchange(argv,payload,secrets,**kwargs)
        self.pipe.exchange=lost
        original_close=self.m.pg.PgBootstrapAdapter.close
        attempts=[]
        def first_fails(ports):
            attempts.append(True)
            if len(attempts)==1:raise RuntimeError('synthetic-pg-close-detail')
            return original_close(ports)
        with mock.patch.object(self.m.pg.PgBootstrapAdapter,'close',first_fails):
            result=self.run_once()
        self.assertEqual((result.result,result.phase),('STOP_UNKNOWN','INITIALIZE'))
        self.assertEqual(result.own_local_pid,12345)
        self.assertTrue(result.close_failed)
        self.assertEqual(self.pipe.calls[-1][0][:2],['volume','create'])
        self.assertNotIn('synthetic-pg-close-detail',repr(result))

    def test_cli_with_execute_stays_inert_without_private_authority(self):
        # Testzweck: öffentliche CLI/Import erteilen auch mit --execute keinerlei Runtime-Go.
        run=subprocess.run([sys.executable,'-I','-B',str(D/'manual_persistent.py'),'--execute'],capture_output=True,timeout=3)
        self.assertEqual(run.returncode,0)
        self.assertEqual(run.stdout,b'FLOWZER_PERSISTENT_SOURCE_ONLY\n')
        self.assertFalse(run.stderr)


if __name__=='__main__':unittest.main()
