"""Sichere Ergebnisprojektion für den manuellen API-Readinessadapter, ohne I/O.

Verwendet den wirklichen Health-API-Vertrag. Containerhoheit muss der konkrete
Transportadapter separat prüfen; weder Body noch HTTP200 beweisen diese Hoheit.
Die Rückgabe ist direkt für ``manual_operator.ApiProof(**fields)`` bestimmt.
"""
import json
from typing import TypedDict


class ApiReadinessFields(TypedDict):
    """Nur endliche, sichere Felder für den bestehenden Sequenznachweis."""
    owned: bool
    head: int
    pending: int
    expression_engine: str
    anonymous_status: int
    provider: str


class ReadinessFailure(Exception):
    """Fester Fehler ohne Body, technische Exception oder vertrauliche Details."""
    def __init__(self):super().__init__('FLOWZER_READINESS_UNPROVEN')


def readiness_fields(body: bytes, *, http_status: int, anonymous_status: int, owned: bool) -> ApiReadinessFields:
    """Nur Healthy/UpToDate/PostgreSQL/Head22/FEEL und anonymer401 sind positiv.

    Kein Polling oder HTTPaufruf in dieser reinen Funktion. Der Transport muss
    schon vor Materialisierung auf höchstens64KiB begrenzen und darf keinem
    Redirect folgen. Eigentum kommt aus dessen separatem exaktem Own-Inspect.
    Unbekannte zusätzliche Healthdetails werden weder übernommen noch geloggt.
    """
    def unique(rows):
        result={}
        for key,value in rows:
            if key in result:raise ReadinessFailure()
            result[key]=value
        return result
    try:
        if type(body) is not bytes or len(body)>65536 or type(http_status) is not int or http_status!=200 \
                or type(anonymous_status) is not int or anonymous_status!=401 or owned is not True:
            raise ReadinessFailure()
        document=json.loads(body,object_pairs_hook=unique)
        result=document['result'];details=result['details']
        if document['successful'] is not True or result['status']!='Healthy' or result['storage']!='Ready':
            raise ReadinessFailure()
        fixed=dict(storageProvider='PostgreSQL (schema flowzer)',migrationState='UpToDate',
                   pendingMigrationCount=0,expectedMigrationVersion=22,expressionEngine='Feel')
        if any(type(details[key]) is not type(value) or details[key]!=value for key,value in fixed.items()):
            raise ReadinessFailure()
        return dict(owned=True,head=22,pending=0,expression_engine='Feel',anonymous_status=401,
                    provider='PostgreSQL (schema flowzer)')
    except Exception:raise ReadinessFailure() from None
