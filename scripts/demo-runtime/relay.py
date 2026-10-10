#!/usr/bin/env python3
"""Opaque Loopback-Weiterleitung ausschließlich für den eigenen isolierten Hostedrig.

Ein Prozess, ein Selector, höchstens acht Socketpaare. Keine DNS-/TLS-/HTTP-
Verarbeitung und keine Bytes/Adressen/Fehlertexte in Ausgaben. Das feste private
Ziel erhält dieser interne Kindprozess nur per Pipe vom frisch prüfenden Runner;
CLI-/Environment-Ziele, zusätzliche Probes und frei wählbare Ports existieren nicht.
"""
import errno
import ipaddress
import json
import resource
import selectors
import signal
import socket
import sys
import time

ADDRESS_SPACE=64*1024**2
CPU_SECONDS=30
FILE_DESCRIPTORS=32
MAX_PAIRS=8
BUFFER_BYTES=65536
CHUNK_BYTES=16384
LIFETIME=1500
CONNECT_SECONDS=5
IDLE_SECONDS=30
PRIVATE_NETWORKS=tuple(ipaddress.ip_network(value) for value in
    ('10.0.0.0/8','172.16.0.0/12','192.168.0.0/16'))

# Ein einziger Thread: unmittelbar VOR vorhandener Operation deren feste Nummer
# merken. Keine zusätzlichen Verbindungen, kein Exception-/Adress-/Byteexport.
# 1 Input, 2 Limits, 3 Listener, 4 Selector, 5 Engine, 6 Ready,
# 7 Deadline, 8 Select, 9 Accept, 10 Connect, 11 Registration,
# 12 Read, 13 Write, 14 Half-close, 15 Drop, 16 Close, 17 Usage, 18 Report.
CURRENT_STAGE=1
ERRNOS={code:index for index,code in enumerate((errno.EADDRINUSE,errno.ECONNREFUSED,
    errno.ECONNRESET,errno.EPIPE,errno.ENOTCONN,errno.EBADF,errno.ETIMEDOUT,
    errno.EMFILE,errno.ENOMEM,errno.ENOBUFS,errno.EACCES,errno.EPERM,
    errno.EINVAL,errno.EHOSTUNREACH,errno.ENETUNREACH,errno.EIO,errno.EINTR),1)}


def mark(stage):
    """Feste Source-Stage, ausschließlich im einen internen RAM-Loop."""
    global CURRENT_STAGE
    CURRENT_STAGE=stage


def error_numbers(error):
    """Nur feste Typ- und POSIXkategorien; niemals str/repr/args/Rohfehler."""
    category=(2 if isinstance(error,TimeoutError) else
        3 if isinstance(error,(InterruptedError,KeyboardInterrupt)) else
        5 if isinstance(error,MemoryError) else 1 if isinstance(error,ValueError) else
        4 if isinstance(error,OSError) else 6)
    code=None
    if isinstance(error,OSError):
        try:value=error.errno
        except BaseException:value=None
        if type(value) is int:code=ERRNOS.get(value)
    return category,code


def observed_usage():
    """Tatsächliche eigene Linux-rusage-Zahlen oder null, ohne Budget-/Erfolgsbehauptung."""
    try:
        usage=resource.getrusage(resource.RUSAGE_SELF)
        rss=int(usage.ru_maxrss)*1024;cpu=int((usage.ru_utime+usage.ru_stime)*1000)
        require(0<rss<=10**15 and 0<=cpu<=10**15)
        return rss,cpu
    except BaseException:return None,None


def require(value):
    """Nur geschlossene interne Vertragsverletzung, niemals Rohmaterial."""
    if not value:raise ValueError('Eigene Relay-Bindung nicht bestätigt.')


def target_address(value):
    """RAM-Ziel zusätzlich auf kanonische RFC1918-IPv4 ohne freien Port beschränken."""
    require(type(value) is dict and set(value)=={'address'} and type(value['address']) is str)
    address=ipaddress.IPv4Address(value['address'])
    require(str(address)==value['address'] and any(address in net for net in PRIVATE_NETWORKS))
    return str(address)


def apply_limits():
    """Harte zusätzliche Relaygrenzen; kein Containerbudget wird dadurch kleingerechnet."""
    for kind,limit in ((resource.RLIMIT_AS,ADDRESS_SPACE),(resource.RLIMIT_CPU,CPU_SECONDS),
            (resource.RLIMIT_NOFILE,FILE_DESCRIPTORS)):
        resource.setrlimit(kind,(limit,limit))


def open_listener():
    """Belegter fester Loopbackport ist STOP: keine Reuse-/fremde Prozessaktion."""
    listener=socket.socket(socket.AF_INET,socket.SOCK_STREAM)
    try:
        listener.setblocking(False);listener.bind(('127.0.0.1',8443));listener.listen(MAX_PAIRS)
        return listener
    except BaseException:
        listener.close();raise


class Pair:
    """Zwei opaque Richtungsbuffer mit Backpressure und korrektem TCP-Half-close."""
    def __init__(self,client,upstream,now):
        self.client=client;self.upstream=upstream;self.sockets=(client,upstream)
        self.buffers={client:bytearray(),upstream:bytearray()}
        self.read_closed=set();self.write_closed=set();self.registered=set()
        self.connecting=False;self.created=now;self.last_activity=now

    def peer(self,sock):return self.upstream if sock is self.client else self.client

    def can_read(self,sock):
        return sock not in self.read_closed and len(self.buffers[self.peer(sock)])<BUFFER_BYTES

    def half_close(self,sock):
        """Erst nach Drain der zugehörigen Richtung FIN weiterreichen, nie Bytes verwerfen."""
        if (self.peer(sock) in self.read_closed and not self.buffers[sock]
                and sock not in self.write_closed and not (sock is self.upstream and self.connecting)):
            mark(14);sock.shutdown(socket.SHUT_WR);self.write_closed.add(sock)

    def read(self,sock,now):
        if not self.can_read(sock):return
        peer=self.peer(sock)
        mark(12)
        try:raw=sock.recv(min(CHUNK_BYTES,BUFFER_BYTES-len(self.buffers[peer])))
        except BlockingIOError:return
        if raw:self.buffers[peer].extend(raw);self.last_activity=now
        else:self.read_closed.add(sock);self.half_close(peer)

    def write(self,sock,now):
        if not self.buffers[sock]:return
        mark(13)
        try:count=sock.send(bytes(self.buffers[sock]))
        except BlockingIOError:return
        require(type(count) is int and 0<count<=len(self.buffers[sock]))
        del self.buffers[sock][:count];self.last_activity=now;self.half_close(sock)

    def check_deadlines(self,now):
        if self.connecting and now-self.created>=CONNECT_SECONDS:raise TimeoutError('Relay-Verbindungsgrenze.')
        if now-self.last_activity>=IDLE_SECONDS:raise TimeoutError('Relay-Inaktivitätsgrenze.')

    def close(self):
        """Beide Richtungen schließen auch wenn die erste Close-Aktion fehlschlägt."""
        mark(16)
        try:self.client.close()
        finally:mark(16);self.upstream.close()


class Engine:
    """Ein begrenzter Selectorloop, keine Thread-/Fork-/unbeschränkte Queue-Vervielfachung."""
    def __init__(self,listener,address,selector,clock=time.monotonic):
        self.listener=listener;self.address=target_address({'address':address})
        self.selector=selector;self.clock=clock;self.started=clock();self.pairs=[];self.stopped=False
        selector.register(listener,selectors.EVENT_READ,None)

    def refresh(self,pair):
        mark(11)
        for sock in pair.sockets:
            mask=0
            if sock is pair.upstream and pair.connecting:mask=selectors.EVENT_WRITE
            else:
                if pair.can_read(sock):mask|=selectors.EVENT_READ
                if pair.buffers[sock]:mask|=selectors.EVENT_WRITE
            if sock in pair.registered:
                if mask:self.selector.modify(sock,mask,pair)
                else:self.selector.unregister(sock);pair.registered.remove(sock)
            elif mask:self.selector.register(sock,mask,pair);pair.registered.add(sock)

    def accept(self):
        mark(9)
        try:client,source=self.listener.accept()
        except BlockingIOError:return
        upstream=None
        try:
            require(type(source) is tuple and source[0]=='127.0.0.1')
            if len(self.pairs)>=MAX_PAIRS:client.close();return
            mark(10);client.setblocking(False);upstream=socket.socket(socket.AF_INET,socket.SOCK_STREAM)
            upstream.setblocking(False);code=upstream.connect_ex((self.address,8443))
            require(code in (0,errno.EINPROGRESS,errno.EWOULDBLOCK,errno.EALREADY))
            pair=Pair(client,upstream,self.clock());pair.connecting=code!=0
            self.pairs.append(pair);self.refresh(pair)
        except BaseException:
            try:client.close()
            finally:
                if upstream is not None:upstream.close()
            raise

    def drop(self,pair):
        """Einmaliger eigener Besitzabschluss; stale Batch-Keys besitzen danach kein I/O-Recht."""
        if pair not in self.pairs:return
        # Vor Close inaktiv machen: auch ein noch gelieferter Key oder ein zweiter
        # Drop darf keine fremde/erneut geschlossene Socketaktion auslösen.
        self.pairs.remove(pair);error=None;error_stage=None
        for sock in tuple(pair.registered):
            try:mark(15);self.selector.unregister(sock)
            except BaseException as caught:
                if error is None:error=caught;error_stage=CURRENT_STAGE
        pair.registered.clear()
        try:pair.close()
        except BaseException as caught:
            if error is None:error=caught;error_stage=CURRENT_STAGE
        if error is not None:mark(error_stage);raise error

    def step(self):
        """Nur tatsächliche Clientverbindungen führen zum festen upstream; keine Probe."""
        mark(7);now=self.clock()
        if now-self.started>=LIFETIME:raise TimeoutError('Relay-Gesamtgrenze.')
        for pair in tuple(self.pairs):
            try:mark(7);pair.check_deadlines(now)
            except TimeoutError:
                if pair.connecting:raise
                # Normaler inaktiver HTTP-Keepalive bekommt keine unbegrenzte Lebenszeit.
                self.drop(pair)
        mark(8)
        for key,mask in self.selector.select(0.1):
            pair=key.data
            if pair is None:self.accept();continue
            # select() liefert einen Snapshot. Ein früherer Key desselben Batches
            # kann dieses Paar bereits legitim beendet haben; dann niemals I/O.
            if pair not in self.pairs:continue
            sock=key.fileobj
            try:
                if sock is pair.upstream and pair.connecting:
                    mark(10);require(sock.getsockopt(socket.SOL_SOCKET,socket.SO_ERROR)==0)
                    pair.connecting=False;pair.half_close(sock)
                if mask&selectors.EVENT_READ:pair.read(sock,self.clock())
                if mask&selectors.EVENT_WRITE:pair.write(sock,self.clock())
                if len(pair.read_closed)==2 and not any(pair.buffers.values()):self.drop(pair)
                else:self.refresh(pair)
            except (ConnectionResetError,BrokenPipeError):
                # Browser darf einen bereits legitimen HTTP/TLS-Stream abbrechen.
                # Keine Weiterleitung zu anderem Ziel, kein geänderter Authstatus.
                self.drop(pair)

    def close(self):
        """Alle eigenen FDs selbst bei einer Close-Ausnahme weiter schließen."""
        error=None;error_stage=None
        for pair in tuple(self.pairs):
            try:self.drop(pair)
            except BaseException as caught:
                if error is None:error=caught;error_stage=CURRENT_STAGE
        mark(16)
        try:self.listener.close()
        finally:mark(16);self.selector.close()
        if error is not None:mark(error_stage);raise error


def measurements():
    """Linux-rusage: tatsächlicher Peak-RSS/CPU separat von Docker-cgroup-Werten."""
    usage=resource.getrusage(resource.RUSAGE_SELF)
    return dict(closed=True,peak_rss_bytes=int(usage.ru_maxrss)*1024,
        cpu_millis=int((usage.ru_utime+usage.ru_stime)*1000),
        address_space_limit_bytes=ADDRESS_SPACE,cpu_limit_seconds=CPU_SECONDS,
        fd_limit=FILE_DESCRIPTORS,max_pairs=MAX_PAIRS,buffer_bytes_per_direction=BUFFER_BYTES,
        lifetime_seconds=LIFETIME)


def main():
    """Dieselben Aktionen/Exitentscheidungen, Fehler nur als gebundene Zahlenzeile.

    Die erste wirklich entwichene Ursache wird VOR den bisherigen Closeversuchen
    festgehalten. Spätere Closefehler bleiben separat. Keine Fehlertoleranz oder
    zusätzliche Probe; Ressourcen im Fehlerbericht sind kein erfolgreicher Budgetbeleg.
    """
    engine=None;listener=None;selector=None;failure=None;close_failure=None;closed=None
    try:
        mark(1);require(sys.argv==[sys.argv[0]] and sys.platform=='linux')
        raw=sys.stdin.buffer.readline(1025);require(len(raw)<=1024 and raw.endswith(b'\n'))
        address=target_address(json.loads(raw));mark(2);apply_limits()
        # Kein Core-Dump der transienten TLS-Bytes; zusätzliche Schutzverengung.
        resource.setrlimit(resource.RLIMIT_CORE,(0,0))
        mark(3);listener=open_listener();mark(4);selector=selectors.DefaultSelector()
        mark(5);engine=Engine(listener,address,selector)
        def stop(_signal,_frame):engine.stopped=True
        mark(6);signal.signal(signal.SIGTERM,stop);signal.signal(signal.SIGINT,stop)
        print('{"ready":true}',flush=True)
        while not engine.stopped:engine.step()
        engine.close();closed=True;engine=None;listener=None;selector=None
        mark(17);value=measurements();mark(18)
        print(json.dumps(value,sort_keys=True),flush=True);return 0
    except BaseException as error:
        failure=(CURRENT_STAGE,*error_numbers(error))
        # Ein erster tatsächlicher Drop-/Closefehler wird nicht durch einen
        # späteren erneut erfolgreichen Versuch zur geschlossenen FD-Abnahme.
        if CURRENT_STAGE in (15,16):close_failure=failure;closed=False
        return 1
    finally:
        had_handles=engine is not None or listener is not None or selector is not None
        try:
            # Exakt dieselben bisherigen eigenen Closeversuche, vor jeder Diagnose.
            if engine is not None:engine.close()
            else:
                mark(16)
                if listener is not None:listener.close()
                if selector is not None:selector.close()
            if had_handles and close_failure is None:closed=True
        except BaseException as error:
            closed=False
            if close_failure is None:close_failure=(CURRENT_STAGE,*error_numbers(error))
        if failure is not None:
            rss,cpu=observed_usage()
            value=dict(failed=True,stage=failure[0],error=failure[1],errno_category=failure[2],
                closed=closed,close_stage=None if close_failure is None else close_failure[0],
                close_error=None if close_failure is None else close_failure[1],
                close_errno_category=None if close_failure is None else close_failure[2],
                peak_rss_bytes=rss,cpu_millis=cpu)
            try:print(json.dumps(value,sort_keys=True),flush=True)
            except BaseException:pass  # Nicht beschreibbare eigene Pipe bleibt unbekannt/Exit1.

if __name__=='__main__':sys.exit(main())
