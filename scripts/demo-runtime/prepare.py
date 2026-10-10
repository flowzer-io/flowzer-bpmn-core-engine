#!/usr/bin/env python3
"""Offline-Vorbereitung des temporären Ressourcenrigs, kein Runtime-/Dispatchpfad.

Nur eine Wegwerfkopie des unveränderten CF-Harness wird angepasst. Die Original-
Assertions bleiben erhalten; Secrets, Rohkonfigurationen und URLs sind keine Messwerte.
"""
from decimal import Decimal, ROUND_CEILING
from pathlib import Path
import re
import subprocess

SOURCE = 'cf082ccd9a165429774de9aab907d3583832279c'
TREE = '38362a4d81f72ed1550c926883b5bb7f28ac50b5'
IMAGES = {
    'api': 'ghcr.io/flowzer-io/flowzer-tt-demo-api@sha256:9eb9a8bdeb624d94352529a7af132bcafdddecdffd3dd1372195e2ee3d3130ad',
    'console': 'ghcr.io/flowzer-io/flowzer-tt-demo-console@sha256:e6b9f55a439212c4a99eec584672163af03f24e2bc52a4b59e11b50584e87467',
    'db': 'postgres@sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24',
    'keycloak': 'quay.io/keycloak/keycloak@sha256:82a77884f3af238beab1e7afd63b5f530e1b5c0590bd7aa60b40a40463e29b2c',
    'tls': 'caddy@sha256:f2a1290d0463aad60660d4ec134943f183ee2a5f6c3eb7bf32dd984f2f020772',
    'certs': 'alpine/openssl@sha256:3f25da71f70eba788067daac3f3df03bd1de7a7c52ed89fa93b94ad2c92d986b',
}
# Schutzlimits eines zu prüfenden Messvorschlags, keine abgenommenen Mindestbedarfe.
BUDGETS = {'api': (512, '0.5'), 'db': (256, '0.4'), 'console': (64, '0.1'),
    'keycloak': (768, '1.0'), 'tls': (64, '0.1'), 'certs': (32, '0.1'), 'migrate': (512, '0.5')}


def require(condition):
    """Geschlossener Fehler ohne Rohantworten oder Pfad-/Identitätsinhalte."""
    if not condition:
        raise ValueError('Isolierte Ressourcenbindung nicht bestätigt.')


def project_for(context):
    """Namespace erst nach genauer Repo-/Ref-/Hosted-/SHA-/Erstlaufprüfung vergeben."""
    require(type(context) is dict and set(context) == {'repository','ref','event','attempt',
        'run_id','sha','confirmed_sha','runner_environment','runner_os','runner_arch'}
        and all(type(value) is str for value in context.values()))
    require(context['repository'] == 'flowzer-io/flowzer-bpmn-core-engine'
        and context['ref'] == 'refs/heads/codex/flowzer-runtime-calibration-pilot'
        and context['event'] == 'workflow_dispatch' and context['attempt'] == '1'
        and context['runner_environment'] == 'github-hosted'
        and context['runner_os'] == 'Linux' and context['runner_arch'] == 'X64')
    require(re.fullmatch('[0-9a-f]{40}', context['sha']) is not None
        and context['confirmed_sha'] == context['sha']
        and re.fullmatch('[1-9][0-9]{0,19}', context['run_id']) is not None)
    return 'flowzer-runtime-' + context['run_id'] + '-a1'


def memory_bytes(value, approximate=False):
    """Docker-Stats-Einheiten ausdrücklich parsen, kein eval oder freie Logprojektion."""
    match = re.fullmatch(r'([0-9]+(?:\.[0-9]+)?)\s*(B|kB|MB|GB|KiB|MiB|GiB)', value)
    require(match is not None)
    units = {'B':1, 'kB':1000, 'MB':10**6, 'GB':10**9, 'KiB':1024, 'MiB':1024**2, 'GiB':1024**3}
    result = Decimal(match[1]) * units[match[2]]
    require(result <= 2**44 and (approximate or result == result.to_integral_value()))
    return int(result.to_integral_value(rounding=ROUND_CEILING))


def stats_row(service, cpu, memory, pids):
    """Whitelist-Messwert, bewusst ohne Containername, URL, Env, Kommando oder Fehlertext."""
    require(service in BUDGETS and re.fullmatch(r'[0-9]+(?:\.[0-9]+)?%', cpu) is not None
        and re.fullmatch('[0-9]{1,7}', pids) is not None)
    parts = memory.split(' / ')
    require(len(parts) == 2)
    used, limit = memory_bytes(parts[0], approximate=True), memory_bytes(parts[1])
    require(limit == BUDGETS[service][0] * 1024**2 and used <= limit and Decimal(cpu[:-1]) <= 10000)
    return dict(service=service, cpu_percent=float(cpu[:-1]), memory_bytes_approx=used, limit_bytes=limit, pids=int(pids))


def replace_once(text, old, new, count=1):
    """Harness-Drift ist ein Gate; nicht mit tolerantem Regex über neue Schutzregeln hinweggehen."""
    require(text.count(old) == count)
    return text.replace(old, new)


def adapt_compose(text, project):
    """Keine Builds/Tags, nur sechs feste Digests für sieben eigene Dienste und ein internes Netz."""
    text = replace_once(text, 'name: flowzer-installation-auth\n', 'name: ' + project + '\n')
    text = replace_once(text, 'x-api-build: &api-build\n  context: ../..\n  dockerfile: Dockerfile.api\n\n', '')
    text = replace_once(text, '    build: *api-build\n', '', 2)
    text = replace_once(text, '    build:\n      context: ../..\n      dockerfile: Dockerfile.console\n', '')
    originals = {'api': ('flowzer-installation-auth/api:local', 2),
        'console': ('flowzer-installation-auth/console:local', 1), 'db': ('postgres:17-alpine', 1),
        'keycloak': ('quay.io/keycloak/keycloak:26.7.4', 1), 'tls': ('caddy:2', 1),
        'certs': ('alpine/openssl:3.5.8', 1)}
    for service, (original, count) in originals.items():
        text = replace_once(text, '    image: ' + original + '\n', '    image: ' + IMAGES[service] + '\n', count)
    # Startanker binden den Dienst, nicht zufällige image-Vorkommen in Kommentaren.
    for service, (mib, cpus) in BUDGETS.items():
        text = replace_once(text, '\n  ' + service + ':\n', '\n  ' + service + ':\n'
            + f'    platform: linux/amd64\n    mem_limit: {mib}m\n    memswap_limit: {mib}m\n    cpus: "{cpus}"\n'
            + f'    labels:\n      io.flowzer.runtime.owner: "{project}"\n')
    text = replace_once(text, 'networks:\n  default:\n', 'networks:\n  default:\n    internal: true\n'
        + f'    labels:\n      io.flowzer.runtime.owner: "{project}"\n')
    for volume in ['ca-public','tls-material','db-data','keyring']:
        text = replace_once(text, f'  {volume}:\n', f'  {volume}:\n    labels:\n      io.flowzer.runtime.owner: "{project}"\n')
    return text


def adapt_compose_helper(text, project):
    """Genau den Original-Check-config-Oneoff in der Kopie bis zum eigenen Exit-/OOM-Audit erhalten."""
    text = replace_once(text, "const PROJECT_NAME = 'flowzer-installation-auth';", f"const PROJECT_NAME = '{project}';")
    old = "  const result = spawnSync('docker', ['compose', '-p', PROJECT_NAME, '-f', COMPOSE_FILE, ...args], {"
    new = "  // Nur dieser feste eigene Oneoff bleibt bis zum Ressourcen-/Exit-Audit erhalten.\n" \
        + "  const budgetCheck = JSON.stringify(args) === '[\"run\",\"--rm\",\"--no-deps\",\"-T\",\"api\",\"--check-config\"]';\n" \
        + "  const ownedArgs = budgetCheck ? args.filter(value => value !== '--rm') : args;\n" \
        + "  const result = spawnSync('docker', ['compose', '-p', PROJECT_NAME, '-f', COMPOSE_FILE, ...ownedArgs], {"
    return replace_once(text, old, new)


def adapt_http_helper(text):
    """Nur der Export beobachtet dieselbe Discovery-Anfrage; originaler HTTP-/API-Code bleibt gleich.

    Keine neue Hilfsdatei, keine zweite Anfrage: Der bestehende sichere Reporter
    hält ausschließlich feste Zahlen/Booleans. Token-/Admin-/API-Aufrufe umgehen ihn.
    """
    old="module.exports = { apiRequest, decodeJwtPayload, httpRequest, testCaCertificate, waitFor };"
    new="module.exports = { apiRequest, decodeJwtPayload, httpRequest: (...args) => " \
        + "require('./safe-reporter').discoveryRequest(httpRequest, require('./constants').ISSUER, args), testCaCertificate, waitFor };"
    return replace_once(text,old,new)


def prepare(source, target, context):
    """Nur git-versionierte Fixtures in einen noch nicht existierenden, externen Ordner kopieren.

    Aufrufer muss vor einem späteren Runtime-Lauf Quellen-CI, Publish-Receipts und
    Registrydigests frisch bestätigen. Diese Funktion attestiert keine Live-Messung.
    """
    project = project_for(context)  # Vor jedem Datei-/Prozesszugriff.
    source, target = Path(source).resolve(), Path(target)
    require(not target.exists() and not target.is_symlink() and not target.resolve().is_relative_to(source))
    def git(*args):
        return subprocess.check_output(['git','-C',str(source),*args], stderr=subprocess.DEVNULL, timeout=10)
    # Nur Fixtures werden gelesen, kein neuer Produktbuild: Tool-Commits dürfen HEAD
    # ändern, die tatsächlich kopierten Dateien müssen weiterhin exakt CF entsprechen.
    require(git('rev-parse',SOURCE+'^{tree}').decode().strip() == TREE)
    require(not git('diff',SOURCE,'--','tests/installation-auth','deploy/postgresql'))
    # List/Modes und Inhalt stammen aus demselben unveränderlichen CF-Tree,
    # niemals aus möglicherweise assume-unchanged/skip-worktree-markierten Worktreebytes.
    entries = git('ls-tree','-r','-z',SOURCE,'--','tests/installation-auth','deploy/postgresql').decode().split('\0')[:-1]
    require(len(entries) == 25 and len(set(entries)) == 25)
    contents, modes = {}, {}
    for entry in entries:
        metadata, name = entry.split('\t',1)
        mode, kind, digest = metadata.split(' ')
        require(mode in {'100644','100755'} and kind == 'blob'
            and re.fullmatch('[0-9a-f]{40}',digest) is not None
            and name.startswith(('tests/installation-auth/','deploy/postgresql/'))
            and not Path(name).is_absolute() and '..' not in Path(name).parts)
        contents[name] = git('cat-file','blob',digest)
        modes[name] = 0o755 if mode == '100755' else 0o644
    auth = 'tests/installation-auth/'
    contents[auth+'compose.yml'] = adapt_compose(contents[auth+'compose.yml'].decode(), project).encode()
    contents[auth+'support/compose.js'] = adapt_compose_helper(contents[auth+'support/compose.js'].decode(),project).encode()
    contents[auth+'support/http.js'] = adapt_http_helper(contents[auth+'support/http.js'].decode()).encode()
    config = contents[auth+'playwright.config.js'].decode()
    config = replace_once(config, "reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],",
        "reporter: [[require.resolve('./support/safe-reporter')]],")
    config = replace_once(config, "trace: 'retain-on-failure',", "trace: 'off',\n    video: 'off',\n    serviceWorkers: 'block',")
    config = replace_once(config, "screenshot: 'only-on-failure'", "screenshot: 'off'")
    contents[auth+'playwright.config.js'] = config.encode()
    for name in list(contents):
        if name.startswith(auth+'specs/') and name.endswith('.spec.js'):
            contents[name] = replace_once(contents[name].decode(),
                "require('@playwright/test')", "require('../support/restricted-test')").encode()
    # Zusätzliche Fixture/Reporter verändern keine fachliche Assertion der Original-Specs.
    for helper in ['safe-reporter.js','restricted-test.js','loopback-proxy.js']:
        contents[auth+'support/'+helper] = Path(__file__).with_name(helper).read_bytes()
    target.mkdir()
    for name, content in contents.items():
        path = target / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        # Insbesondere PostgreSQL-Init-Hooks dürfen nicht durch verlorenes +x
        # vom eigenen Prozess in ein gesourctes Entrypointskript verwandelt werden.
        path.chmod(modes.get(name, 0o644))
    return dict(project=project, source_sha=SOURCE, source_tree=TREE, images=IMAGES.copy(), runtime_executed=False)
