"""Kleine einmalige Initial-/Restart-Sequenz mit injizierten, noch nicht live gebundenen Ports.

Ports sind vertrauenswürdige lokale Adapter, niemals Browser-/HTTP-Eingaben.
Der Sourceaufruf ist kein Runtime-Go. Kein Import/CLI startet SSH, Docker oder DB.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import json
import re
import unicodedata


class OperatorFailure(Exception):
    """Endliche Phase plus unbestätigter CIDkandidat; kein Rohfehler oder Reparaturauftrag."""
    PHASES={'INPUT','ONCE','PREFLIGHT','INITIALIZE','PG_START','BOOTSTRAP','PG_PROOF','MIGRATION','API_START','API_PROOF'}

    def __init__(self,phase,candidate_cid=None,own_local_pid=None):
        self.phase=phase if phase in self.PHASES else 'INPUT'
        self.candidate_cid=candidate_cid
        self.own_local_pid=own_local_pid if type(own_local_pid) is int and own_local_pid>0 else None
        super().__init__('FLOWZER_OPERATOR_'+self.phase+'_UNKNOWN')


class PortFailure(Exception):
    """Der vertrauenswürdige Adapter reicht nur einen eigenen ungeklärten lokalen Handle weiter.

    Kein freies Exception-/Outputfeld. Dieser Beleg ist keine Erlaubnis, einen PID
    später zu signalisieren; das bleibt ausschließlich Aufgabe seines Besitzers.
    """
    def __init__(self,own_local_pid=None):
        self.own_local_pid=own_local_pid if type(own_local_pid) is int and own_local_pid>0 else None
        super().__init__('FLOWZER_PORT_UNKNOWN')


@dataclass(frozen=True)
class Observation:
    """Adaptervorprüfung aller eigenen Ziel-/Image-/Mount-/Keyring-/Socketbindungen.

    resources=ABSENT nur bei belegter Abwesenheit ALLER neuen Ressourcen, OWNED
    nur bei vollständigem Vergleich der exakt vorher registrierten Bestandsressourcen.
    Diese lokalen Portwerte sind weder Signatur noch Ersatz für Backendautorisierung.
    """
    target_bound: bool
    resources: str
    pg_data: str
    prior_api: str


@dataclass(frozen=True)
class RoleProof:
    """Getrennte Migrationsrolle; Runtime exakt DML/kein DDL und Historie nur lesbar."""
    separated: bool
    runtime_least_privilege: bool
    history_read_only: bool


@dataclass(frozen=True)
class MigrationProof:
    exit_code: int
    head: int
    pending: int
    terminal: bool=True
    output_eof: bool=True


@dataclass(frozen=True)
class ApiProof:
    owned: bool
    head: int
    pending: int
    expression_engine: str
    anonymous_status: int
    provider: str='PostgreSQL (schema flowzer)'


@dataclass(frozen=True)
class SecretBundle:
    """Nur pro manueller Lieferung im RAM; Strings sind nicht sicher zeroisierbar."""
    admin: str|None=field(repr=False)
    migration: str=field(repr=False)
    runtime: str=field(repr=False)
    bff: str=field(repr=False)
    directory: str=field(repr=False)

    def validate(self,mode):
        values=[self.migration,self.runtime,self.bff,self.directory]
        if mode=='initial':values.append(self.admin)
        elif self.admin is not None:raise OperatorFailure('INPUT')
        if any(not _valid_value(v) or len(v)<16 for v in values):raise OperatorFailure('INPUT')


@dataclass(frozen=True)
class SequenceProof:
    """Nur Port-Sequenzabschluss, keine Host-/Runtimeattestierung ohne echte gebundene Adapter."""
    api_cid: str


def _connection(password,role):
    # ADO.NET: quotierte Werte verdoppeln innere Anführungszeichen. Keine freien
    # Host-/Datenbank-/Benutzerwerte aus dem Secretpaket; Rollen sind feste Aufruferkonstanten.
    return 'Host=/socket;Database=flowzer_tt_demo;Username='+role+';Password="'+password.replace('"','""')+'"'


def _valid_value(value):
    """Derselbe finale Stringvertrag wie .NET: UTF16-Codeeinheiten und char.IsControl.

    Ungepaarte Surrogate sind kein gültiges UTF8-JSON. C1-Controls dürfen nicht erst
    im API-Bootstrap nach schon erfolgtem PG-/Rollenwrite zurückgewiesen werden.
    """
    return (isinstance(value,str) and bool(value.strip())
            and not any(unicodedata.category(c) in ('Cc','Cs') for c in value)
            and len(value.encode('utf-16-le'))//2<=4096)


def _packet(role,values):
    # Entscheidend ist die gerenderte DSN mit Prefix/Quoting, nicht das rohe Passwort.
    if not all(_valid_value(v) for v in values.values()):raise OperatorFailure('INPUT')
    value=bytearray(json.dumps({'version':1,'role':role,'values':values},ensure_ascii=False).encode())
    if len(value)>16384:
        for i in range(len(value)):value[i]=0
        raise OperatorFailure('INPUT')
    return value


class ManualOperator:
    """Ein Versuch, kein Retry/Force-Cleanup. Fremde/unklare Bestände blockieren vor Secretlieferung.

    Die kleinen Ports müssen vor privater Nutzung durch echte sichere SSH/Docker-
    Adapter implementiert/geprüft werden. Aktuelle Tests ersetzen ALLE Außenränder.
    """
    def __init__(self):self.used=False

    def run(self,mode,ports,deliver):
        if self.used:raise OperatorFailure('ONCE')
        self.used=True
        if mode not in ('initial','restart'):raise OperatorFailure('INPUT')
        phase='PREFLIGHT';candidate=None;packets=[]
        try:
            observed=ports.observe()
            if not isinstance(observed,Observation) or observed.target_bound is not True:
                raise OperatorFailure(phase)
            if mode=='initial':
                if (observed.resources,observed.pg_data,observed.prior_api)!=('ABSENT','ABSENT','ABSENT'):
                    raise OperatorFailure(phase)
            elif observed.resources!='OWNED' or observed.pg_data!='INITIALIZED' or observed.prior_api not in ('ABSENT','EXITED'):
                raise OperatorFailure(phase)
            # Frische Lieferung erst nach positiver Vorprüfung; keine ENV-/Datei-/CLIhilfe.
            phase='INPUT';bundle=deliver()
            if not isinstance(bundle,SecretBundle):raise OperatorFailure(phase)
            bundle.validate(mode)
            migration=_packet('migrate',{'migrationConnectionString':_connection(bundle.migration,'flowzer_tt_demo_migration')})
            packets.append(migration)
            api=_packet('api',{'runtimeConnectionString':_connection(bundle.runtime,'flowzer_tt_demo'),
                'bffClientSecret':bundle.bff,'directoryClientSecret':bundle.directory})
            packets.append(api)
            if mode=='initial':
                phase='INITIALIZE';self._zero_exit(ports.initialize(bundle),phase)
            phase='PG_START';self._zero_exit(ports.pg_start(),phase)
            if mode=='initial':
                phase='BOOTSTRAP';self._zero_exit(ports.bootstrap(bundle),phase)
            phase='PG_PROOF';role=ports.pg_proof()
            if not isinstance(role,RoleProof) or any(x is not True for x in
                    (role.separated,role.runtime_least_privilege,role.history_read_only)):
                raise OperatorFailure(phase)
            phase='MIGRATION';migration_result=ports.migrate(migration)
            if not isinstance(migration_result,MigrationProof) or type(migration_result.exit_code) is not int \
                    or type(migration_result.head) is not int or type(migration_result.pending) is not int \
                    or (migration_result.exit_code,migration_result.head,migration_result.pending)!=(0,22,0) \
                    or migration_result.terminal is not True or migration_result.output_eof is not True:
                raise OperatorFailure(phase)
            phase='API_START';candidate=ports.api_start(api)
            if not isinstance(candidate,str) or not re.fullmatch('[a-f0-9]{64}',candidate):
                candidate=None;raise OperatorFailure(phase)
            phase='API_PROOF';ready=ports.api_proof(candidate)
            if not isinstance(ready,ApiProof) or ready.owned is not True \
                    or type(ready.head) is not int or type(ready.pending) is not int or type(ready.anonymous_status) is not int \
                    or (ready.head,ready.pending,ready.expression_engine,ready.anonymous_status,ready.provider) \
                    !=(22,0,'Feel',401,'PostgreSQL (schema flowzer)'):
                raise OperatorFailure(phase,candidate)
            return SequenceProof(candidate)
        except PortFailure as error:
            raise OperatorFailure(phase,candidate,error.own_local_pid) from None
        except (KeyboardInterrupt,SystemExit):raise
        except BaseException:
            # Keine Cleanup-/Adoptionsaktionen: ein unbekannter Ausgang bleibt unbekannt.
            raise OperatorFailure(phase,candidate) from None
        finally:
            for packet in packets:
                for i in range(len(packet)):packet[i]=0

    @staticmethod
    def _zero_exit(value,phase):
        if type(value) is not int or value!=0:raise OperatorFailure(phase)


if __name__=='__main__':
    print('FLOWZER_MANUAL_OPERATOR_SOURCE_ONLY')
