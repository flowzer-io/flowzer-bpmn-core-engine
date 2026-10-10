"""Testzweck: Transparenter eigener Relay ausschließlich mit Socket-/Prozessmocks.

Keine reale lokale Runtime, TCP-Verbindung, TLS- oder Docker-Anfrage. Die Tests
beweisen Byteweitergabe und Fail-closed-Verträge, nicht den späteren Hosted-Erfolg.
"""
import contextlib
import copy
import errno
import importlib
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import tempfile
import unittest
from unittest.mock import Mock,patch
import runner
from test_runner import CONTEXT,CONFIGS,PROJECT,container,EXPECTED_INSPECT
import test_diagnostics

ADDRESS='172.28.0.2'
NETWORK='d'*64
ENDPOINT='e'*64

def binding_rows():
    tls=container('tls')|dict(network_count=1,network_id=NETWORK,
        network_ipv4=ADDRESS,endpoint_id=ENDPOINT)
    net=dict(name=PROJECT+'_default',project=PROJECT,owner=PROJECT,internal=True,
        id=NETWORK,driver='bridge',subnet='172.28.0.0/16',
        container_ipv4=ADDRESS+'/16',endpoint_id=ENDPOINT)
    return tls,net

class RelayTests(unittest.TestCase):
    def module(self):
        self.assertIsNotNone(importlib.util.find_spec('relay'),'Begrenzter Relay fehlt')
        return importlib.import_module('relay')

    @contextlib.contextmanager
    def rig(self):
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{'RUNNER_TEMP':temp,'PATH':'/synthetic'},clear=True):
            rig=runner.Rig(Path('/unused'),Path('/unused'),CONTEXT)
            rig.configs=CONFIGS;rig.startup_tls_id='b'*64
            yield rig

    def test_actual_target_requires_fresh_double_owned_running_tls_and_private_endpoint(self):
        # Testzweck: Jede Zieladresse stammt ausschließlich aus zwei frischen
        # engen Inspects desselben CID/Netzes; Fremdeigentum und Drift vor I/O stoppen.
        target=getattr(runner.Rig,'relay_target',None)
        self.assertTrue(callable(target),'Frisch gebundener Relay-Zielvertrag fehlt')
        tls,net=binding_rows()
        with self.rig() as rig:
            rig.command=Mock(side_effect=[json.dumps(tls),json.dumps(net)])
            self.assertEqual(ADDRESS,target(rig))
            self.assertEqual(2,rig.command.call_count)
            for row in [tls|{'id':'a'*64},tls|{'owner':'foreign'},tls|{'image_id':'sha256:'+'f'*64},
                tls|{'running':False},tls|{'restarts':1},tls|{'oneoff':'True'},tls|{'network_count':2},
                tls|{'network_ipv4':'8.8.8.8'},tls|{'raw':'never-export'}]:
                rig.command=Mock(side_effect=[json.dumps(row),json.dumps(net)])
                with self.subTest(),self.assertRaises(ValueError):target(rig)
            for row in [net|{'owner':'foreign'},net|{'internal':False},net|{'id':'f'*64},
                net|{'driver':'overlay'},net|{'subnet':'0.0.0.0/0'},net|{'container_ipv4':'172.28.0.3/16'},
                net|{'endpoint_id':'f'*64},net|{'name':'foreign'},net|{'raw':'never-export'}]:
                rig.command=Mock(side_effect=[json.dumps(tls),json.dumps(row)])
                with self.subTest(),self.assertRaises(ValueError):target(rig)
            rig.startup_tls_id=None;rig.command.reset_mock()
            with self.assertRaises(ValueError):target(rig)
            rig.command.assert_not_called()

    def test_transport_formats_select_only_fixed_own_endpoint_not_raw_configs(self):
        # Testzweck: Vollständige Formatliste begrenzt den RAM-Read auf eigene
        # Identität/Netzadresse; keine Env-/Config-/Logs-/fremden Containerdumps.
        fmt=getattr(runner,'relay_container_inspect',None)
        self.assertTrue(callable(fmt),'Enger Relay-Inspect fehlt')
        actual=fmt(PROJECT)
        endpoint='index .NetworkSettings.Networks "'+PROJECT+'_default"'
        expected=EXPECTED_INSPECT[:-1]+',"network_count":{{json (len .NetworkSettings.Networks)}}'+''.join(
            ',"'+key+'":{{with ('+endpoint+')}}{{json .'+field+'}}{{else}}null{{end}}'
            for key,field in [('network_id','NetworkID'),('network_ipv4','IPAddress'),('endpoint_id','EndpointID')])+'}'
        self.assertEqual(expected,actual)
        for forbidden in ['.Config.Env','json .HostConfig','json .NetworkSettings','LogPath']:
            self.assertNotIn(forbidden,actual)
        net=getattr(runner,'relay_network_inspect',None)
        self.assertTrue(callable(net));actual=net('b'*64)
        expected='{"name":{{json (.Name)}},"project":{{json (index .Labels "com.docker.compose.project")}},' \
            + '"owner":{{json (index .Labels "io.flowzer.runtime.owner")}},"internal":{{json (.Internal)}},' \
            + '"id":{{json (.Id)}},"driver":{{json (.Driver)}},' \
            + '"subnet":{{if eq (len .IPAM.Config) 1}}{{json (index .IPAM.Config 0).Subnet}}{{else}}null{{end}},' \
            + '"container_ipv4":{{with (index .Containers "'+ 'b'*64 + '")}}{{json .IPv4Address}}{{else}}null{{end}},' \
            + '"endpoint_id":{{with (index .Containers "'+ 'b'*64 + '")}}{{json .EndpointID}}{{else}}null{{end}}}'
        self.assertEqual(expected,actual)
        self.assertNotIn('json .Containers',actual);self.assertNotIn('json .IPAM',actual)
        for foreign in ['bad','flowzer-runtime-1-a2','flowzer-runtime-1-a1"}}']:
            with self.assertRaises(ValueError):fmt(foreign)
        with self.assertRaises(ValueError):net('foreign')

    def test_child_has_fixed_small_limits_and_no_env_cli_target(self):
        # Testzweck: Genau ein Kindprozess, feste geringe AS/CPU/FD-/Socket-/Buffer-
        # Limits, kein frei wählbarer Port/Host und kein TLS-/HTTP-Verarbeiten.
        m=self.module()
        self.assertEqual((64*1024**2,30,32,8,65536,16384,1500,5,30),
            (m.ADDRESS_SPACE,m.CPU_SECONDS,m.FILE_DESCRIPTORS,m.MAX_PAIRS,m.BUFFER_BYTES,
             m.CHUNK_BYTES,m.LIFETIME,m.CONNECT_SECONDS,m.IDLE_SECONDS))
        with patch.object(m.resource,'setrlimit') as limits:m.apply_limits()
        self.assertEqual([((m.resource.RLIMIT_AS,(m.ADDRESS_SPACE,m.ADDRESS_SPACE)),),
            ((m.resource.RLIMIT_CPU,(30,30)),),((m.resource.RLIMIT_NOFILE,(32,32)),)],
            [(call.args,) for call in limits.call_args_list])
        self.assertEqual(ADDRESS,m.target_address({'address':ADDRESS}))
        for value in [dict(address='127.0.0.1'),dict(address='8.8.8.8'),dict(address='::1'),
            dict(address='example.invalid'),dict(address=ADDRESS,port=443),dict(address=True)]:
            with self.subTest(),self.assertRaises(ValueError):m.target_address(value)

    def test_listener_is_only_loopback_and_occupied_port_never_kills_foreign_process(self):
        # Testzweck: Belegter fester Port ist STOP, kein fremdes Bind-Reuse/-Kill.
        m=self.module();sock=Mock()
        with patch.object(m.socket,'socket',return_value=sock):
            self.assertIs(sock,m.open_listener())
        sock.bind.assert_called_once_with(('127.0.0.1',8443));sock.listen.assert_called_once_with(8)
        sock.setsockopt.assert_not_called()
        sock=Mock();sock.bind.side_effect=OSError(errno.EADDRINUSE,'never-export')
        with patch.object(m.socket,'socket',return_value=sock),self.assertRaises(OSError):m.open_listener()
        sock.close.assert_called_once();sock.listen.assert_not_called()

    def test_positive_opaque_bytes_partial_writes_and_backpressure(self):
        # Testzweck: Synthetische TLS-/Nullbytes werden ohne Parsing exakt in beide
        # Richtungen weitergegeben; Partialwrites und Buffergrenze sind echt geübt.
        m=self.module();client=Mock();upstream=Mock();pair=m.Pair(client,upstream,0)
        wire=b'\x16\x03\x03\x00\x04\x00\xffA\x00';client.recv.return_value=wire
        pair.read(client,1);self.assertEqual(wire,bytes(pair.buffers[upstream]))
        upstream.send.side_effect=[3,len(wire)-3]
        pair.write(upstream,2);pair.write(upstream,3)
        self.assertEqual([wire,wire[3:]],[call.args[0] for call in upstream.send.call_args_list])
        self.assertEqual(b'',bytes(pair.buffers[upstream]))
        upstream.recv.return_value=wire[::-1];pair.read(upstream,4)
        self.assertEqual(wire[::-1],bytes(pair.buffers[client]))
        pair.buffers[upstream]=bytearray(b'x'*65536);client.recv.reset_mock()
        self.assertFalse(pair.can_read(client));pair.read(client,5);client.recv.assert_not_called()

    def test_half_close_drains_pending_bytes_and_connect_timeout_fails_closed(self):
        # Testzweck: EOF zerstört noch nicht gesendete Gegenrichtung nicht; Connect-
        # und Idle-/Gesamtgrenzen sind echte geschlossene Fehler, keine Fake-Erfolge.
        m=self.module();client=Mock();upstream=Mock();pair=m.Pair(client,upstream,0)
        client.recv.side_effect=[b'opaque',b''];pair.read(client,1);pair.read(client,2)
        upstream.shutdown.assert_not_called();upstream.send.return_value=6;pair.write(upstream,3)
        upstream.shutdown.assert_called_once_with(m.socket.SHUT_WR)
        pair.connecting=True
        with self.assertRaises(TimeoutError):pair.check_deadlines(5)
        pair.connecting=False;pair.last_activity=0
        with self.assertRaises(TimeoutError):pair.check_deadlines(30)
        pair.close();client.close.assert_called_once();upstream.close.assert_called_once()

    def test_single_target_max_connections_and_no_dns(self):
        # Testzweck: Maximal acht Verbindungspaare und feste eigene IP/8443;
        # kein Nutzerziel, Hostname, DNS, Queue oder zusätzlicher Probe-Request.
        m=self.module();listener=Mock();client=Mock();listener.accept.return_value=(client,('127.0.0.1',1))
        selector=Mock();upstream=Mock();engine=m.Engine(listener,ADDRESS,selector,lambda:0)
        with patch.object(m.socket,'socket',return_value=upstream) as create:
            upstream.connect_ex.return_value=errno.EINPROGRESS;engine.accept()
        create.assert_called_once_with(m.socket.AF_INET,m.socket.SOCK_STREAM)
        upstream.connect_ex.assert_called_once_with((ADDRESS,8443));self.assertEqual(1,len(engine.pairs))
        engine.pairs.extend([Mock()]*7);listener.accept.reset_mock();client=Mock()
        listener.accept.return_value=(client,('127.0.0.1',2));engine.accept();client.close.assert_called_once()
        with patch.object(m.socket,'socket') as create:
            engine.accept();create.assert_not_called()
        self.assertEqual(8,len(engine.pairs))

    def test_closed_resource_report_accounts_relay_separately(self):
        # Testzweck: Relay-RSS/CPU und harte Obergrenzen sind separat und echt,
        # nie aus Containerpeak verschwunden oder als unbekannte Null erfunden.
        project=getattr(runner,'relay_result',None)
        self.assertTrue(callable(project),'Relay-Ressourcenvertrag fehlt')
        value=dict(closed=True,peak_rss_bytes=20000000,cpu_millis=12,
            address_space_limit_bytes=64*1024**2,cpu_limit_seconds=30,
            fd_limit=32,max_pairs=8,buffer_bytes_per_direction=65536,lifetime_seconds=1500)
        self.assertEqual(value,project(value))
        for bad in [value|{'closed':False},value|{'peak_rss_bytes':True},value|{'cpu_millis':30001},
            value|{'max_pairs':9},value|{'peak_rss_bytes':None},value|{'raw':'never-export'}]:
            with self.subTest(),self.assertRaises(ValueError):project(bad)

    def test_actual_session_closes_own_relay_before_docker_cleanup_even_on_auth_failure(self):
        # Testzweck: Echte Run-Orchestrierung, gemockte neue Transportabhängigkeit;
        # weder Authfehler noch Relay-Closefehler überspringt Docker-Cleanup.
        session=getattr(runner.Rig,'transport_session',None)
        self.assertTrue(callable(session),'Relay-Lebenszyklus fehlt')
        with test_diagnostics.DiagnosticsTests().synthetic_runtime() as rig:
            events=[];primary=runner.MeasuredProcessExitError(1)
            @contextlib.contextmanager
            def transport():
                events.append('relay-start')
                try:yield
                finally:events.append('relay-close')
            rig.transport_session=transport
            def measured(args,**kw):
                rig.report['sample_count']=1
                if args[0]=='node':events.append('auth');raise primary
            rig.measured_command=Mock(side_effect=measured)
            rows=[container(service) for service in runner.BUDGETS]
            rig.inventory=Mock(side_effect=[[],rows,rows,[]])
            original=rig.command
            def command(args,**kw):
                if 'down' in args:events.append('docker-cleanup')
                return original(args,**kw)
            rig.command=Mock(side_effect=command)
            with self.assertRaises(runner.MeasuredProcessExitError):rig.run()
            self.assertEqual(['relay-start','auth','relay-close','docker-cleanup'],events)
            self.assertTrue(rig.report['cleanup_complete']);self.assertFalse(rig.report['success'])

    def test_real_transport_session_pipe_only_ready_then_own_term_and_bound_readback(self):
        # Testzweck: Tatsächlicher Sessioncode bekommt nur RAM-Ziel per Pipe;
        # bei Exit/Timeout endet nur eigener Prozessbaum und Report bleibt geschlossen.
        session=getattr(runner.Rig,'transport_session',None)
        self.assertTrue(callable(session),'Eigene Relay-Session fehlt')
        with self.rig() as rig:
            rig.relay_target=Mock(return_value=ADDRESS)
            child=Mock(pid=2468,returncode=0);child.stdin=Mock();input_pipe=child.stdin;child.stdout=Mock()
            child.stdout.readline.return_value=b'{"ready":true}\n';child.poll.side_effect=[None,None,0]
            final=dict(closed=True,peak_rss_bytes=20000000,cpu_millis=12,
                address_space_limit_bytes=64*1024**2,cpu_limit_seconds=30,fd_limit=32,
                max_pairs=8,buffer_bytes_per_direction=65536,lifetime_seconds=1500)
            child.communicate.return_value=(json.dumps(final).encode(),None)
            with patch.object(runner.subprocess,'Popen',return_value=child) as spawn,\
                    patch.object(runner.Rig,'relay_ready') as ready,\
                    patch.object(runner.os,'killpg') as kill:
                with session(rig):pass
            input_pipe.write.assert_called_once_with(json.dumps({'address':ADDRESS}).encode()+b'\n')
            self.assertEqual(['python3','-I','-B',str(rig.source/'scripts/demo-runtime/relay.py')],spawn.call_args.args[0])
            self.assertIs(spawn.call_args.kwargs['start_new_session'],True)
            self.assertNotIn(ADDRESS,json.dumps(spawn.call_args.kwargs,default=str))
            ready.assert_called_once_with(child)
            kill.assert_called_once_with(2468,signal.SIGTERM)
            self.assertEqual(final,rig.report['relay']);self.assertIsNone(rig.relay_process)

    def test_actual_ready_reader_is_time_bounded_on_partial_or_missing_line(self):
        # Testzweck: Auch partiell geschriebene eigene Pipebytes besitzen die
        # echte fünfsekündige Grenze; niemals blockierendes readline hinter select.
        read=getattr(runner.Rig,'relay_ready',None)
        self.assertTrue(callable(read),'Zeitgebundener Readiness-Reader fehlt')
        child=Mock();child.stdout.fileno.return_value=7
        with self.rig() as rig,patch.object(runner.select,'select',return_value=([child.stdout],[],[])),\
                patch.object(runner.os,'set_blocking'),patch.object(runner.os,'read',side_effect=[b'{"ready":',b'true}\n']),\
                patch.object(runner.time,'monotonic',side_effect=[0,0,1]):
            read(rig,child)
        with self.rig() as rig,patch.object(runner.select,'select',return_value=([],[],[])),\
                patch.object(runner.os,'set_blocking'),patch.object(runner.time,'monotonic',side_effect=[0,0,5]):
            with self.assertRaises(TimeoutError):read(rig,child)

    def test_engine_deadline_and_connect_failure_close_all_owned_fds(self):
        # Testzweck: Timeout ist kein Erfolg und sämtliche bereits eigenen
        # Socketpaare/Listener/Selector bekommen ihren Close auch bei erster Ausnahme.
        m=self.module();clock=Mock(side_effect=[0,1500]);listener=Mock();selector=Mock()
        engine=m.Engine(listener,ADDRESS,selector,clock)
        with self.assertRaises(TimeoutError):engine.step()
        pair=m.Pair(Mock(),Mock(),0);pair.client.close.side_effect=OSError('never-export')
        engine.pairs=[pair]
        with self.assertRaises(OSError):engine.close()
        pair.upstream.close.assert_called_once();listener.close.assert_called_once();selector.close.assert_called_once()

    def test_actual_close_failure_cannot_skip_docker_cleanup_or_hide_first_auth_exit(self):
        # Testzweck: Ein real gemockter neuer Session-Closefehler bleibt rot,
        # erste Authursache erhalten und Docker-Cleanup wird dennoch erreicht.
        with test_diagnostics.DiagnosticsTests().synthetic_runtime() as rig:
            events=[];primary=runner.MeasuredProcessExitError(1);close=OSError('never-export')
            @contextlib.contextmanager
            def transport():
                try:yield
                finally:
                    with rig.phase('relay_close'):events.append('relay-close');raise close
            rig.transport_session=transport
            def measured(args,**kw):
                rig.report['sample_count']=1
                if args[0]=='node':raise primary
            rig.measured_command=Mock(side_effect=measured)
            rows=[container(s) for s in runner.BUDGETS];rig.inventory=Mock(side_effect=[[],rows,rows,[]])
            original=rig.command
            def command(args,**kw):
                if 'down' in args:events.append('docker-cleanup')
                return original(args,**kw)
            rig.command=Mock(side_effect=command)
            with self.assertRaises(OSError):rig.run()
            self.assertEqual(['relay-close','docker-cleanup'],events)
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                dict(phase='auth_report',error='validation',exit_code=None),
                dict(phase='relay_close',error='io',exit_code=None)],rig.report['failures'])
            self.assertTrue(rig.report['cleanup_complete']);self.assertFalse(rig.report['success'])

    def test_engine_completed_connect_preserves_actual_opaque_bytes_end_to_end(self):
        # Testzweck: Echter gemockter Selectorpfad akzeptiert eine lokale Verbindung,
        # verbindet ausschließlich festes Ziel und schreibt genau dieselben TLSbytes.
        m=self.module();listener=Mock();client=Mock();upstream=Mock();selector=Mock()
        engine=m.Engine(listener,ADDRESS,selector,lambda:0)
        listener.accept.return_value=(client,('127.0.0.1',1));upstream.connect_ex.return_value=errno.EINPROGRESS
        with patch.object(m.socket,'socket',return_value=upstream):engine.accept()
        pair=engine.pairs[0];upstream.getsockopt.return_value=0
        wire=b'\x16\x03\x03opaque\x00'
        client.recv.return_value=wire
        selector.select.return_value=[(type('Key',(),dict(data=pair,fileobj=client))(),m.selectors.EVENT_READ)]
        engine.step();self.assertEqual(wire,bytes(pair.buffers[upstream]))
        upstream.send.return_value=len(wire)
        selector.select.return_value=[(type('Key',(),dict(data=pair,fileobj=upstream))(),m.selectors.EVENT_WRITE)]
        engine.step();upstream.send.assert_called_once_with(wire)
        self.assertFalse(pair.connecting);self.assertEqual(b'',bytes(pair.buffers[upstream]))
        engine.close();client.close.assert_called_once();upstream.close.assert_called_once()

    def test_actual_full_run_prioritizes_real_relay_close_over_only_secondary_reports(self):
        # Testzweck (RELAY-P2-01): Echter Rig.run→transport_session-finally-Pfad
        # hat bereits auth/auth_report/loopback_report; echte Closeursache muss
        # innerhalb Max3 den optionalen Platz erhalten, Index0 bleibt unverändert.
        with test_diagnostics.DiagnosticsTests().synthetic_runtime() as rig:
            primary=runner.MeasuredProcessExitError(1);loopback=runner.ProcessExitError(29)
            close=OSError('synthetic-private-must-not-escape')
            rig.relay_target=Mock(return_value=ADDRESS);rig.relay_ready=Mock();rig.stop_process=Mock()
            child=Mock(pid=2468);child.stdin=Mock();child.stdout=Mock();child.poll.return_value=None
            child.communicate.side_effect=close
            def measured(args,**kwargs):
                rig.report['sample_count']=1
                if args[0]!='node':return
                (rig.root/'auth-result.json').write_text(json.dumps({'raw':'private'}))
                (rig.root/'discovery-result.json').write_text(json.dumps(dict(status=None,
                    transport_code=3,issuer_matches=None,pkce_s256=None)))
                raise primary
            rig.measured_command=Mock(side_effect=measured);rig.tls_loopback_snapshot=Mock(side_effect=loopback)
            rows=[container(service) for service in runner.BUDGETS];rig.inventory=Mock(side_effect=[[],rows,rows,[]])
            with patch.object(runner.subprocess,'Popen',return_value=child),patch.object(runner.os,'killpg'):
                with self.assertRaises(OSError) as raised:rig.run()
            self.assertIs(close,raised.exception)
            actual=json.loads((rig.root/'report/resource-result.json').read_text())
            self.assertEqual([dict(phase='auth',error='process_exit',exit_code=1),
                dict(phase='auth_report',error='validation',exit_code=None),
                dict(phase='relay_close',error='io',exit_code=None)],actual['failures'])
            self.assertEqual(3,len(actual['failures']));self.assertTrue(actual['cleanup_complete'])
            self.assertFalse(actual['success']);self.assertNotIn('relay',actual)
            self.assertNotIn('synthetic-private',json.dumps(actual));self.assertIsNone(rig.relay_process)
            rig.stop_process.assert_called_once_with(child)
            # Drei echte Ursachen werden niemals zugunsten einer vierten verdrängt.
            with self.rig() as other:
                errors=(runner.MeasuredProcessExitError(1),ValueError('private'),OSError('private'),close)
                for phase,error in zip(('auth','sampling','verify','relay_close'),errors):other.note_failure(phase,error)
                self.assertEqual(['auth','sampling','verify'],[row['phase'] for row in other.report['failures']])

    def test_actual_selector_batch_skips_dropped_pair_keys_and_preserves_other_stream(self):
        # Testzweck (RELAY-P2-02): Zwei bereits gelieferte Keys desselben Paares
        # und ein weiteres Paar. Reset/BrokenPipe schließt nur das erste; stale
        # Key bekommt nie I/O, Drop bleibt idempotent und anderes Paar fließt weiter.
        m=self.module()
        for error in (ConnectionResetError('private'),BrokenPipeError('private')):
            with self.subTest(error=type(error).__name__):
                listener=Mock();selector=Mock();engine=m.Engine(listener,ADDRESS,selector,lambda:0)
                a=m.Pair(Mock(),Mock(),0);b=m.Pair(Mock(),Mock(),0)
                engine.pairs=[a,b];a.registered=set(a.sockets);b.registered=set(b.sockets)
                a.client.recv.side_effect=error;a.upstream.recv.side_effect=OSError(errno.EBADF,'private')
                wire=b'\x16\x03opaque\x00';b.client.recv.return_value=wire
                key=lambda pair,sock:type('Key',(),dict(data=pair,fileobj=sock))()
                selector.select.return_value=[(key(a,a.client),m.selectors.EVENT_READ),
                    (key(a,a.upstream),m.selectors.EVENT_READ),(key(b,b.client),m.selectors.EVENT_READ)]
                try:engine.step()
                except (OSError,ValueError):self.fail('Bereits gedropptes Paar bekam erneut I/O/Drop')
                self.assertEqual([b],engine.pairs);a.upstream.recv.assert_not_called()
                self.assertEqual(wire,bytes(b.buffers[b.upstream]))
                engine.drop(a);a.client.close.assert_called_once();a.upstream.close.assert_called_once()
                b.client.close.assert_not_called();b.upstream.close.assert_not_called()
                engine.close();b.client.close.assert_called_once();b.upstream.close.assert_called_once()

if __name__=='__main__':unittest.main()
