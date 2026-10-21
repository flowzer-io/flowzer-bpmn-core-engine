"""Dünner persistenter Initial-/Restart-Caller über unveränderte Originalports.

Import und CLI sind inert. Nur ein separat physisch gebundener privater Rootcaller
liefert guard, exklusiven Versuch und RAMbundle; diese Bibliothek stellt keine
Berechtigung aus. Kein Pilot-Go, Secretresolver, Pull, Proxy-/Consoleinstaller,
automatischer Neustart oder Remote-Cleanup. Nicht gegen beliebigen Same-UID-Code
isoliert: die Source-/Transport-/Authoritybindung bleibt Aufgabe des Rootcallers.
"""
from __future__ import annotations

from dataclasses import dataclass
import importlib.util
from pathlib import Path
import re
import sys

if 'manual_runtime_adapter' not in sys.modules:
    _spec = importlib.util.spec_from_file_location('manual_runtime_adapter', Path(__file__).with_name('manual_runtime_adapter.py'))
    _module = importlib.util.module_from_spec(_spec)
    sys.modules[_spec.name] = _module
    _spec.loader.exec_module(_module)
rt = sys.modules['manual_runtime_adapter']
op = rt.op
pg = rt.pg


@dataclass(frozen=True)
class PersistentBinding:
    """Exakte nichtgeheime Versuchsdaten, keine persönlichen oder geratenen Defaults.

    pg bleibt beim Restart unverändert auf der registrierten Erstellung (Labels,
    Quell-SHA, Datenträger, Nonce). runtime bindet den neuen API-/Migratorversuch
    und die zuvor registrierte API. Netzwerk-/Hostkey-/Image-/Zeit-/Sourcebelege
    sind zusätzlich vor JEDEM Originaltransport im äußeren Rootguard erforderlich.
    """
    pg: pg.PgBinding
    runtime: rt.RuntimeBinding
    target: pg.SshTarget
    prior_pg_cid: str | None = None
    prior_api_cid: str | None = None

    def validate(self, mode):
        """Nur bestehende Verträge enger komponieren; keine neue Netz-/Issuerfreigabe."""
        if not isinstance(self.pg, pg.PgBinding) or not isinstance(self.runtime, rt.RuntimeBinding) \
                or not isinstance(self.target, pg.SshTarget):
            raise ValueError()
        self.pg.validate()
        self.runtime.validate()
        self.target.validate()
        if self.runtime.settings.authority != 'https://auth.tickytask.de/realms/TickyTask' \
                or self.pg.architecture != 'arm64' or self.pg.prepare_keyring is not True \
                or (self.runtime.uid, self.runtime.gid) != (self.pg.key_uid, self.pg.key_gid):
            raise ValueError()
        if any(value == 'NOT_ISSUED' for value in (self.runtime.settings.audience,
                self.runtime.settings.bff_client, self.runtime.settings.directory_client)):
            raise ValueError()
        if mode == 'initial':
            if self.prior_pg_cid is not None or self.prior_api_cid is not None \
                    or self.runtime.prior_api_nonce is not None or self.runtime.attempt_nonce != self.pg.nonce:
                raise ValueError()
        elif mode == 'restart':
            if not _cid(self.prior_pg_cid) or self.prior_api_cid is not None and not _cid(self.prior_api_cid) \
                    or self.runtime.prior_api_nonce is None \
                    or self.runtime.attempt_nonce in (self.runtime.prior_api_nonce, self.pg.nonce):
                raise ValueError()
        else:
            raise ValueError()


def _cid(value):
    return value if type(value) is str and re.fullmatch('[a-f0-9]{64}', value) else None


class _AttemptObservation:
    """Nur Provenienz am bestehenden Kanal/RAMclose, kein anderer Executor.

    Der Original-Portwrapper kann beim close den ersten Transportfehler maskieren.
    Deshalb wird dessen eigener numerischer Transport-PID bereits an derselben
    exchange-Grenze festgehalten. Nie PIDs aus Resolver-/Guard-/freien Exceptions
    übernehmen, keine Prozesssuche oder Signalisierung und keine Rohfehler halten.
    """
    def __init__(self, pipe):
        self.pipe = pipe
        self.own_pid = None
        self.unknown = False
        self.close_failed = False

    def exchange(self, *args, **kwargs):
        try:
            return self.pipe.exchange(*args, **kwargs)
        except BaseException as error:
            self.unknown = True
            if isinstance(error, pg.TransportFailure):
                value = error.own_pid
                if type(value) is int and value > 0:
                    self.own_pid = value
            raise

    def watch_close(self, original):
        # Auch PG.close läuft in internen Initial-/Bootstrap-Fehlerpfaden.
        # Beide bestehenden RAMclose-Grenzen durchreichen und Fehler merken.
        def close():
            try:
                return original()
            except BaseException:
                self.close_failed = True
                raise
        return close


@dataclass(frozen=True)
class PersistentResult:
    """Minimaler sicherer Checkpoint, keine Installations-/IdP-/HTTPS-/TT-Abnahme.

    CIDs sind bei STOP nur bekannte unbestätigte Kandidaten. PRESERVE bedeutet
    ausschließlich: dieser Caller versuchte keine entfernte Löschung; kein Beleg,
    dass unbekannte entfernte Ressourcen vollständig existieren oder gesund sind.
    """
    result: str
    phase: str
    pg_cid: str | None = None
    api_cid: str | None = None
    own_local_pid: int | None = None
    close_failed: bool = False
    retry_allowed: bool = False
    data_policy: str = 'PRESERVE_NO_REMOTE_CLEANUP'


class PersistentCaller:
    """Ein manueller Versuch; nur Sequenzabschluss ist READY, jeder Fehler ist STOP.

    claim_once ist der vorhandene private/dauerhafte exklusive Rootversuch, nicht
    ein hier ausgestelltes Permit. Er muss exakt True erst nach erfolgreichem
    Claim zurückgeben und darf nach Fehler/UNKNOWN nicht erneut geöffnet werden.
    Der äußere guard bindet exakt dieses Binding, Originalsource und Transport.
    pipe ist zwingend explizit, niemals ein heimlicher alternativer Livekanal.
    """
    def __init__(self):
        self.used = False

    def run(self, mode, binding, *, guard, claim_once, deliver, pipe):
        """Vorprüfung → einmalige Lieferung → Originalsequenz → ausschließlich RAMclose.

        Kein finally-Remote-Cleanup wie im synthetischen ARM64-Piloten. Auch nach
        Tastaturabbruch/Closefehler bleiben entfernte Zustände zur manuellen Klärung
        erhalten; sichere eigene UNKNOWN-PIDs werden nur gemeldet, nicht signalisiert.
        """
        if self.used:
            return PersistentResult('STOP_UNKNOWN', 'ONCE')
        self.used = True
        ports = None
        phase = 'BINDING'
        result = 'STOP_UNKNOWN'
        observation = None
        api_cid = None
        try:
            if not isinstance(binding, PersistentBinding) or not all(callable(value) for value in
                    (guard, claim_once, deliver, getattr(pipe, 'exchange', None))):
                raise ValueError()
            binding.validate(mode)
            guard()
            phase = 'ONCE'
            if claim_once() is not True:
                raise ValueError()
            phase = 'BINDING'
            observation = _AttemptObservation(pipe)
            ports = rt.RuntimePorts(binding.pg, binding.runtime, binding.target, guard,
                mode=mode, pipe=observation, prior_pg_cid=binding.prior_pg_cid, prior_api_cid=binding.prior_api_cid)
            # Derselbe Originalclose läuft auch intern im @port-Fehlerpfad.
            # Nur Fehlerstatus beobachten; Rückgabe/Exception unverändert durchreichen.
            ports.pg.close = observation.watch_close(ports.pg.close)
            ports.close = observation.watch_close(ports.close)
            phase = 'PREFLIGHT'
            proof = op.ManualOperator().run(mode, ports, lambda: ports.accept_bundle(deliver()))
            api_cid = _cid(proof.api_cid)
            if api_cid is None:
                raise ValueError()
            result, phase = 'READY', 'COMPLETE'
        except BaseException as error:
            # Keine freie Exception/Outputs im Resultat, einschließlich Interrupt.
            if isinstance(error, op.OperatorFailure):
                phase = error.phase
        finally:
            if ports is not None:
                try:
                    ports.close()
                except BaseException:
                    pass  # Bereits am Originalclose beobachtet; niemals Remote-Reparatur.
        close_failed = observation.close_failed if observation is not None else False
        own_pid = observation.own_pid if observation is not None else None
        if close_failed or observation is not None and observation.unknown:
            result = 'STOP_UNKNOWN'
            if phase == 'COMPLETE':
                phase = 'CLOSE' if close_failed else 'PREFLIGHT'
        pg_cid = _cid(ports.pg.pg_cid) if ports is not None else None
        api_cid = api_cid or (_cid(ports.api_cid) if ports is not None else None)
        return PersistentResult(result, phase, pg_cid, api_cid, own_pid, close_failed)


if __name__ == '__main__':
    # Keine --execute-/ENV-/Dateiparameter können die private Rootöffnung ersetzen.
    print('FLOWZER_PERSISTENT_SOURCE_ONLY')
