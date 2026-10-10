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
            sock.shutdown(socket.SHUT_WR);self.write_closed.add(sock)

    def read(self,sock,now):
        if not self.can_read(sock):return
        peer=self.peer(sock)
        try:raw=sock.recv(min(CHUNK_BYTES,BUFFER_BYTES-len(self.buffers[peer])))
        except BlockingIOError:return
        if raw:self.buffers[peer].extend(raw);self.last_activity=now
        else:self.read_closed.add(sock);self.half_close(peer)

    def write(self,sock,now):
        if not self.buffers[sock]:return
        try:count=sock.send(bytes(self.buffers[sock]))
        except BlockingIOError:return
        require(type(count) is int and 0<count<=len(self.buffers[sock]))
        del self.buffers[sock][:count];self.last_activity=now;self.half_close(sock)

    def check_deadlines(self,now):
        if self.connecting and now-self.created>=CONNECT_SECONDS:raise TimeoutError('Relay-Verbindungsgrenze.')
        if now-self.last_activity>=IDLE_SECONDS:raise TimeoutError('Relay-Inaktivitätsgrenze.')

    def close(self):
        """Beide Richtungen schließen auch wenn die erste Close-Aktion fehlschlägt."""
        try:self.client.close()
        finally:self.upstream.close()


class Engine:
    """Ein begrenzter Selectorloop, keine Thread-/Fork-/unbeschränkte Queue-Vervielfachung."""
    def __init__(self,listener,address,selector,clock=time.monotonic):
        self.listener=listener;self.address=target_address({'address':address})
        self.selector=selector;self.clock=clock;self.started=clock();self.pairs=[];self.stopped=False
        selector.register(listener,selectors.EVENT_READ,None)

    def refresh(self,pair):
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
        try:client,source=self.listener.accept()
        except BlockingIOError:return
        upstream=None
        try:
            require(type(source) is tuple and source[0]=='127.0.0.1')
            if len(self.pairs)>=MAX_PAIRS:client.close();return
            client.setblocking(False);upstream=socket.socket(socket.AF_INET,socket.SOCK_STREAM)
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
        self.pairs.remove(pair);error=None
        for sock in tuple(pair.registered):
            try:self.selector.unregister(sock)
            except BaseException as caught:error=error or caught
        pair.registered.clear()
        try:pair.close()
        except BaseException as caught:error=error or caught
        if error is not None:raise error

    def step(self):
        """Nur tatsächliche Clientverbindungen führen zum festen upstream; keine Probe."""
        now=self.clock()
        if now-self.started>=LIFETIME:raise TimeoutError('Relay-Gesamtgrenze.')
        for pair in tuple(self.pairs):
            try:pair.check_deadlines(now)
            except TimeoutError:
                if pair.connecting:raise
                # Normaler inaktiver HTTP-Keepalive bekommt keine unbegrenzte Lebenszeit.
                self.drop(pair)
        for key,mask in self.selector.select(0.1):
            pair=key.data
            if pair is None:self.accept();continue
            # select() liefert einen Snapshot. Ein früherer Key desselben Batches
            # kann dieses Paar bereits legitim beendet haben; dann niemals I/O.
            if pair not in self.pairs:continue
            sock=key.fileobj
            try:
                if sock is pair.upstream and pair.connecting:
                    require(sock.getsockopt(socket.SOL_SOCKET,socket.SO_ERROR)==0)
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
        error=None
        for pair in tuple(self.pairs):
            try:self.drop(pair)
            except BaseException as caught:error=error or caught
        try:self.listener.close()
        finally:self.selector.close()
        if error is not None:raise error


def measurements():
    """Linux-rusage: tatsächlicher Peak-RSS/CPU separat von Docker-cgroup-Werten."""
    usage=resource.getrusage(resource.RUSAGE_SELF)
    return dict(closed=True,peak_rss_bytes=int(usage.ru_maxrss)*1024,
        cpu_millis=int((usage.ru_utime+usage.ru_stime)*1000),
        address_space_limit_bytes=ADDRESS_SPACE,cpu_limit_seconds=CPU_SECONDS,
        fd_limit=FILE_DESCRIPTORS,max_pairs=MAX_PAIRS,buffer_bytes_per_direction=BUFFER_BYTES,
        lifetime_seconds=LIFETIME)


def main():
    """Nur interne eine RAM-Pipe, zwei geschlossene Ausgaben; sämtliche Rohfehler verworfen."""
    engine=None;listener=None;selector=None
    try:
        require(sys.argv==[sys.argv[0]] and sys.platform=='linux')
        raw=sys.stdin.buffer.readline(1025);require(len(raw)<=1024 and raw.endswith(b'\n'))
        address=target_address(json.loads(raw));apply_limits()
        # Kein Core-Dump der transienten TLS-Bytes; zusätzliche Schutzverengung.
        resource.setrlimit(resource.RLIMIT_CORE,(0,0))
        listener=open_listener();selector=selectors.DefaultSelector();engine=Engine(listener,address,selector)
        def stop(_signal,_frame):engine.stopped=True
        signal.signal(signal.SIGTERM,stop);signal.signal(signal.SIGINT,stop)
        print('{"ready":true}',flush=True)
        while not engine.stopped:engine.step()
        engine.close();engine=None;listener=None;selector=None
        print(json.dumps(measurements(),sort_keys=True),flush=True);return 0
    except BaseException:
        # Keine URL, Payload, Ziel-IP, Zertifikate oder Exceptionrepr/-text ausgeben.
        return 1
    finally:
        if engine is not None:
            try:engine.close()
            except BaseException:pass
        else:
            if listener is not None:listener.close()
            if selector is not None:selector.close()

if __name__=='__main__':sys.exit(main())
