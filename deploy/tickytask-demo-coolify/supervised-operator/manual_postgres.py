"""Gebundene initiale PostgreSQL-STDIN-Pakete, ohne Prozess-/Datei-/Netz-I/O.

Nur für einen separat bewiesenen frischen eigenen Cluster, nie beim Restart.
Der konkrete Adapter muss ALLE Pakete vor seinem ersten Ressourcenwrite bauen.
Das ist kein freier SQL-Executor und keine Freigabe für private DB-Aufrufe.
"""
import hashlib
import unicodedata

SQL01_SHA256='4257e0a2d62a216dbaf7c969b0e6988742a1dd92418e112d4c7459d61e02e00f'
SQL02_SHA256='ba76969b8e9843452adec96e4a575e74f85c13df1ab3fed92a8b1c232d5f392f'


class BootstrapFailure(Exception):
    """Fester Eingabefehler; keine Secrets, SQLfragmente oder Treiberfehler."""
    def __init__(self):super().__init__('FLOWZER_INITIAL_BOOTSTRAP_INPUT')


def psql_initial_arguments() -> list[str]:
    """psql ohne TTY/rc/ENV-Passwort; Passwortzeile und SQL kommen über stdin.

    Der spätere Dockeradapter muss zusätzlich -i ohne -t, log-driver=none,
    restart=no, eigene Image-/Socket-/UIDbindung und begrenzte Kanäle erzwingen.
    PostgreSQL17 simple_prompt fällt ohne /dev/tty auf stdin zurück. Tatsächliche
    Prompt-/Mehrfachconnect-/EOFabnahme erfolgt vor Secrets auf isoliertem Linux;
    ein Sourcevertrag ersetzt diesen Nachweis ausdrücklich nicht.
    """
    return ['--no-psqlrc','--no-readline','--set','ON_ERROR_STOP=1',
            '--set','VERBOSITY=terse','--set','SHOW_CONTEXT=never',
            '--host','/socket','--username','postgres','--dbname','postgres','--password','--file','-']


def initial_psql_payload(*, admin: str, migration: str, runtime: str, sql01: bytes, sql02: bytes) -> bytearray:
    r"""Originale reviewte Rollen-/ACLskripte inline, kein persistierter Include.

    Passwortwerte sind ausschließlich quotierte SQLdaten bei explizitem
    standard_conforming_strings=on. Adminpasswortzeilen werden nur von psqls Passwortprompts gelesen,
    NICHT vom SQLparser. -W erzwingt auch bei \connect einen neuen Prompt. Der Aufrufer muss das gesamte
    Paket und alle eigenen Lieferpuffer nach Benutzung leeren; immutable Python-
    Strings lassen sich damit nicht nachweislich zeroisieren.
    """
    try:
        for value in (admin,migration,runtime):
            if type(value) is not str or not value.strip() or len(value)<16 \
                    or any(unicodedata.category(c) in ('Cc','Cs') for c in value) \
                    or len(value.encode('utf-16-le'))//2>4096:raise BootstrapFailure()
        for script,pin in ((sql01,SQL01_SHA256),(sql02,SQL02_SHA256)):
            if type(script) is not bytes or hashlib.sha256(script).hexdigest()!=pin:raise BootstrapFailure()
        include=b'\\ir 02-laufzeitrechte.sql'
        if sql01.count(include)!=1:raise BootstrapFailure()
        body=sql01.replace(include,sql02)
        # psql17 do_connect fragt bei TRI_YES (-W) nochmals, unabhängig davon,
        # ob Benutzer/Host gleich bleiben. Die nächste physische stdin-Zeile
        # gehört daher dem Prompt, nicht dem nachfolgenden SQLparser.
        connect=b'\n\\connect :datenbank\n'
        if body.count(connect)!=1:raise BootstrapFailure()
        body=body.replace(connect,connect+admin.encode('utf-8')+b'\n')
        prefix=(admin+'\n\\set datenbank flowzer_tt_demo\n\\set migrationsrolle flowzer_tt_demo_migration\n'
                '\\set laufzeitrolle flowzer_tt_demo\n\\set schema flowzer\n').encode('utf-8')
        # Nach dem skripteigenen \connect erneut setzen: keine Sessionannahme.
        passwords='\nSET standard_conforming_strings = on;\n'
        for role,value in [('flowzer_tt_demo_migration',migration),('flowzer_tt_demo',runtime)]:
            passwords+="ALTER ROLE "+role+" PASSWORD '"+value.replace("'","''")+"';\n"
        result=bytearray(prefix+body+passwords.encode('utf-8'))
        if len(result)>16384:
            result[:]=b'\0'*len(result);raise BootstrapFailure()
        return result
    except Exception:raise BootstrapFailure() from None
