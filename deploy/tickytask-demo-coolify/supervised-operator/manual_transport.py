"""Begrenzter manueller STDIN-Transportkern; standardmäßig ausschließlich Source-only.

Keine Secretauflösung, kein Docker-/SSH-/DBstart beim Import oder CLIaufruf.
Die Commandbuilder bilden nur den Kanalvertrag ab, KEINE komplette Betriebs-
konfiguration. Ziel-/Image-/Ressourcen- und Root-Autorisierung bleiben getrennt.
"""
import base64
from dataclasses import dataclass, field
import os
import re
import selectors
import signal
import subprocess
import time


class TransportFailure(Exception):
    """Nur feste Codes und gegebenenfalls der eigene unbekannte lokale Gruppenhandle."""
    CODES={'INPUT','ARGUMENT_SECRET','TIMEOUT','OUTPUT_LIMIT','LEAK','OS_ERROR','HANDLE_UNKNOWN'}

    def __init__(self, code, own_pid=None):
        self.code=code if code in self.CODES else 'OS_ERROR'
        self.own_pid=own_pid if type(own_pid) is int and own_pid>0 else None
        super().__init__('FLOWZER_TRANSPORT_'+self.code)


@dataclass(frozen=True)
class PipeResult:
    """Rohkanäle nur für interne RAM-Prüfungen; niemals implizit in Diagnose repr/str."""
    exit_code: int
    stdout: bytes=field(repr=False)
    stderr: bytes=field(repr=False)


def _sentinels(secrets):
    values=[]
    for secret in secrets:
        if not isinstance(secret,bytes) or not secret:raise TransportFailure('INPUT')
        values.extend((secret,base64.b64encode(secret),base64.b64encode(base64.b64encode(secret))))
    return values


class BoundedPipe:
    """Ein eigener nichtinteraktiver Prozess, gemeinsame Deadline für Write/EOF/Read/Exit.

    Defaultumgebung enthält keine geerbten Credentials oder Loader-/CLIhooks.
    Kein shell=True, kein raw Exception-/Outputlogging. Eigene Gruppe wird vor
    jedem möglichen I/O registriert. Die Source-Failurefixtures starten nur Python.
    """
    def exchange(self, argv, payload, secrets, *, timeout=15):
        sentinels=_sentinels(secrets)
        if not isinstance(argv,list) or not argv or not all(isinstance(x,str) and x and '\0' not in x for x in argv) \
                or not os.path.isabs(argv[0]) or not isinstance(payload,(bytes,bytearray)) or len(payload)>16384 \
                or type(timeout) not in (int,float) or not 0<timeout<=120:
            raise TransportFailure('INPUT')
        if any(marker in argument.encode() for argument in argv for marker in sentinels):
            raise TransportFailure('ARGUMENT_SECRET')
        # Kein fremder SIGCHLD-Reaper darf unseren einzigen Popen-Owner überholen.
        if signal.getsignal(signal.SIGCHLD) not in (signal.SIG_DFL,None):raise TransportFailure('INPUT')
        process=None;data=bytearray(payload);out=bytearray();err=bytearray()
        deadline=time.monotonic()+timeout
        try:
            process=subprocess.Popen(argv,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,
                env={'PATH':'/usr/bin:/bin','HOME':'/nonexistent','LC_ALL':'C'},
                shell=False,close_fds=True,start_new_session=True)
            with selectors.DefaultSelector() as selector:
                for stream,kind in [(process.stdout,'out'),(process.stderr,'err')]:
                    os.set_blocking(stream.fileno(),False);selector.register(stream,selectors.EVENT_READ,kind)
                position=0
                if data:
                    os.set_blocking(process.stdin.fileno(),False)
                    selector.register(process.stdin,selectors.EVENT_WRITE,'in')
                else:process.stdin.close()
                while selector.get_map():
                    remaining=deadline-time.monotonic()
                    if remaining<=0:raise TransportFailure('TIMEOUT')
                    events=selector.select(remaining)
                    if not events:raise TransportFailure('TIMEOUT')
                    for key,_ in events:
                        stream=key.fileobj
                        if key.data=='in':
                            try:position+=os.write(stream.fileno(),memoryview(data)[position:position+4096])
                            except BlockingIOError:continue
                            if position==len(data):
                                selector.unregister(stream);stream.close()  # explizites Eingabe-EOF
                            continue
                        buffer=out if key.data=='out' else err
                        try:chunk=os.read(stream.fileno(),min(4096,65537-len(buffer)))
                        except BlockingIOError:continue
                        if not chunk:selector.unregister(stream);continue
                        buffer.extend(chunk)
                        if len(buffer)>65536:raise TransportFailure('OUTPUT_LIMIT')
                remaining=deadline-time.monotonic()
                if remaining<=0:raise TransportFailure('TIMEOUT')
                try:process.wait(timeout=remaining)
                except subprocess.TimeoutExpired:raise TransportFailure('TIMEOUT') from None
            self._require_empty_group(process.pid)
            if any(marker in channel for channel in (out,err,out+err,err+out) for marker in sentinels):
                raise TransportFailure('LEAK')
            return PipeResult(process.returncode,bytes(out),bytes(err))
        except TransportFailure:
            if process is not None:self._finish_own(process)
            raise
        except BaseException as error:
            if process is not None:self._finish_own(process)
            if isinstance(error,(KeyboardInterrupt,SystemExit)):raise
            raise TransportFailure('OS_ERROR') from None
        finally:
            for i in range(len(data)):data[i]=0
            if process is not None:
                for stream in (process.stdin,process.stdout,process.stderr):
                    if stream is not None:stream.close()

    @staticmethod
    def _require_empty_group(pid):
        try:os.killpg(pid,0)
        except ProcessLookupError:return
        except OSError:pass
        raise TransportFailure('HANDLE_UNKNOWN',pid)

    def _finish_own(self, process):
        # Nur solange der einzige eigene Popen-Owner den Leader noch nicht gereapt hat:
        # kein Signal an einen nachträglich wiederverwendeten/fremden Gruppenhandle.
        try:
            if process.poll() is None:
                os.killpg(process.pid,signal.SIGTERM)
                try:process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if process.poll() is None:os.killpg(process.pid,signal.SIGKILL)
                    process.wait(timeout=2)
            self._require_empty_group(process.pid)
        except TransportFailure:raise
        except BaseException:raise TransportFailure('HANDLE_UNKNOWN',process.pid) from None


def _identity(value,length):
    if not isinstance(value,str) or not re.fullmatch('[a-f0-9]{'+str(length)+'}',value):
        raise TransportFailure('INPUT')
    return value


def api_start_arguments(nonce,image):
    """Nur Kanalfragment; noch keine freigegebene Host-/Auth-/Mount-/Netzkonfiguration."""
    _identity(nonce,32)
    if not isinstance(image,str) or not image.startswith('sha256:'):raise TransportFailure('INPUT')
    _identity(image[7:],64)
    return ['container','run','--pull=never','--interactive','--attach=stdin','--sig-proxy=false',
            '--restart=no','--log-driver=none','--name','flowzer-tt-api-'+nonce,image,'--secrets-stdin']


def migration_arguments(nonce,image):
    """Migration hängt beide Ausgabekanäle an und wartet damit auf den echten Containerexit."""
    args=api_start_arguments(nonce,image)
    args[args.index('--name')+1]='flowzer-tt-migrate-'+nonce
    args[args.index(image):args.index(image)]=['--attach=stdout','--attach=stderr']
    args[-1:]=['--migrate','--secrets-stdin']
    return args


def restart_pg_arguments(cid):
    """Nur Start eines schon separat eigentums-/konfigurationsgeprüften festen eigenen PG-CID."""
    return ['container','start',_identity(cid,64)]


if __name__=='__main__':
    print('FLOWZER_MANUAL_OPERATOR_SOURCE_ONLY')
