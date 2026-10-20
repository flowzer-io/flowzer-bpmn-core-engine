"""Source-Verträge für einen eigenen späteren Zweicontainerlauf; kein Liveadapter enthalten."""
from dataclasses import dataclass


class SmokeFailure(Exception):
    """Nur feste Kategorien, niemals Port-/Secret-/Exceptioninhalte."""


@dataclass(frozen=True)
class Plan:
    nonce: str
    pg_image: str
    api_image: str


def verify_bytes(output, secrets):
    import base64
    if len(output) > 65536 or not secrets or any(not isinstance(v, bytes) or not v for v in secrets):
        raise SmokeFailure('OUTPUT')
    for value in secrets:
        for _ in range(3):
            if value in output:
                raise SmokeFailure('OUTPUT')
            value = base64.b64encode(value)


def verify_output(result, secrets):
    """Kein Trunkieren: Exit, beide vollständigen Kanäle und alle Sentinelcodierungen sind Gates."""
    import base64
    if result['code'] != 0 or not result['eof'] or not result['terminal'] or result['timeout']:
        raise SmokeFailure('OUTPUT')
    verify_bytes(result['stdout'] + result['stderr'], secrets)


class SmokeRun:
    """Ein fester Versuch; ausschließlich verifizierte eigene IDs dürfen je gestoppt werden."""
    def __init__(self, ports, clock):
        self.ports, self.clock, self.used = ports, clock, False

    def run(self, plan, pg_password, fixture_input, secrets):
        import re
        if self.used:
            raise SmokeFailure('ONCE')
        self.used = True
        if not re.fullmatch('[a-f0-9]{32}', plan.nonce) or not re.fullmatch(
                'docker.io/library/postgres@sha256:[a-f0-9]{64}', plan.pg_image) or not re.fullmatch(
                r'(flowzer-smoke/api@sha256:|sha256:)[a-f0-9]{64}', plan.api_image):
            raise SmokeFailure('INPUT')
        if not 32 <= len(pg_password) <= 128 or b'\n' in pg_password or not 0 < len(fixture_input) <= 16384:
            raise SmokeFailure('INPUT')
        resource = self.ports.preflight()
        if resource['architecture'] != 'linux/amd64' or resource['cpus'] < 2 or resource['memory'] < 2 * 1024**3 \
                or resource['disk'] < 4 * 1024**3 or tuple(resource['images']) != (plan.pg_image, plan.api_image):
            raise SmokeFailure('RESOURCES')
        start = self.clock.now
        def remaining():
            value = 300000 - (self.clock.now - start)
            if not 0 < value <= 300000:
                raise SmokeFailure('TIMEOUT')
            return value
        volume, owned, unknown = 'flowzer-smoke-' + plan.nonce, [], False
        volume_owned = False
        def spec(role, image):
            return dict(nonce=plan.nonce, role=role, image=image, network='none', logDriver='none',
                        privileged=False, portBindings={}, hostMounts=[], socketVolume=volume,
                        memory=(256 if role == 'pg' else 768) * 1024**2, pids=(64 if role == 'pg' else 128), cpus=1)
        try:
            self.ports.create_volume(volume, plan.nonce)
            if self.ports.inspect_volume(volume) != dict(name=volume, nonce=plan.nonce):
                unknown = True
                raise SmokeFailure('OWNERSHIP')
            volume_owned = True
            cids = {}
            for role, image in [('pg', plan.pg_image), ('api', plan.api_image)]:
                expected = spec(role, image)
                try:
                    cid = self.ports.create(expected)
                    if not isinstance(cid, str) or not re.fullmatch('[a-f0-9]{64}', cid) \
                            or self.ports.inspect(cid) != expected:
                        raise SmokeFailure('OWNERSHIP')
                except Exception:
                    unknown = True
                    raise SmokeFailure('OWNERSHIP') from None
                owned.append((cid, expected)); cids[role] = cid
            data = pg_password + b'\n'
            sent = self.ports.start_pg(cids['pg'], data, remaining())
            if sent['written'] != len(data) or not sent['eof'] or sent['timeout']:
                raise SmokeFailure('INPUT')
            verify_bytes(sent['stdout'] + sent['stderr'], secrets)
            result = self.ports.run_fixture(cids['api'], fixture_input, remaining())
            verify_output(result, secrets)
            if result['stdout'] != b'FLOWZER_FIXTURE_OK\n' or result['stderr']:
                raise SmokeFailure('OUTPUT')
            remaining()
        except SmokeFailure:
            raise
        except Exception:
            raise SmokeFailure('UNKNOWN') from None
        finally:
            clean = not unknown
            bad_output = False
            for cid, expected in reversed(owned):
                try:
                    actual = self.ports.inspect(cid)
                    if actual is None:
                        continue
                    if actual != expected:
                        clean = False
                        continue
                    stopped = self.ports.stop(cid, 10000)
                    if not stopped['terminal'] or not stopped['eof'] or stopped['timeout']:
                        clean = False
                        continue
                    try:
                        verify_output(stopped, secrets)
                    except SmokeFailure:
                        bad_output = True
                    self.ports.remove(cid)
                    if self.ports.inspect(cid) is not None:
                        clean = False
                except Exception:
                    clean = False
            if clean and volume_owned:
                self.ports.remove_volume(volume)
                clean = self.ports.inspect_volume(volume) is None
            if not clean:
                raise SmokeFailure('CLEANUP') from None
            if bad_output:
                raise SmokeFailure('OUTPUT') from None


# Ab hier ausschließlich die später separat gebundenen realen Adapter. Beim Import passiert nichts.
class LiveClock:
    @property
    def now(self):
        import time
        return int(time.monotonic() * 1000)


class AttachedCommand:
    """Ein eigener Docker-CLI-Leader, nicht der Container: Pipes und Wartezeiten sind endlich."""
    def __init__(self, args, environment, payload, timeout_ms, secrets, commands):
        import os
        import selectors
        import subprocess
        self.os, self.selectors = os, selectors
        self.clock, self.secrets = LiveClock(), secrets
        self.deadline = self.clock.now + timeout_ms
        self.data, self.written = bytearray(payload), 0
        self.out, self.err = bytearray(), bytearray()
        self.input_done, self.closed, self.verified_closed = False, False, False
        self.process, self.selector = None, None
        # Vor jedem möglichen Start registrieren: auch fehlgeschlagener Cleanup verliert keinen eigenen Handle.
        commands.append(self)
        try:
            self.selector = selectors.DefaultSelector()
            self.process = subprocess.Popen(args, env=environment, stdin=subprocess.PIPE,
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0, close_fds=True, start_new_session=True)
            for stream, event, label in [(self.process.stdin, selectors.EVENT_WRITE, 'in'),
                                        (self.process.stdout, selectors.EVENT_READ, 'out'),
                                        (self.process.stderr, selectors.EVENT_READ, 'err')]:
                os.set_blocking(stream.fileno(), False)
                self.selector.register(stream, event, label)
            if not self.data:
                self._eof()
        except BaseException:
            self.close()
            raise SmokeFailure('SETUP') from None

    def _eof(self):
        self.selector.unregister(self.process.stdin)
        self.process.stdin.close()
        self.input_done = True
        self.data[:] = b'\0' * len(self.data)

    def tick(self, wait_ms=50):
        if self.clock.now >= self.deadline:
            raise SmokeFailure('TIMEOUT')
        for key, _ in self.selector.select(min(wait_ms, self.deadline - self.clock.now) / 1000):
            try:
                if key.data == 'in':
                    count = self.os.write(key.fd, self.data[self.written:])
                    if count <= 0: raise SmokeFailure('INPUT')
                    self.written += count
                    if self.written == len(self.data): self._eof()
                else:
                    chunk = self.os.read(key.fd, min(4096, 65537 - len(self.out) - len(self.err)))
                    if not chunk:
                        self.selector.unregister(key.fileobj); key.fileobj.close()
                    else:
                        target = self.out if key.data == 'out' else self.err
                        target.extend(chunk)
                        verify_bytes(bytes(self.out + self.err), self.secrets)
            except BlockingIOError:
                continue
        return not self.selector.get_map() and self.process.poll() is not None

    def result(self):
        if self.process.poll() is None or self.selector.get_map() or not self.input_done:
            raise SmokeFailure('OUTPUT')
        self.close()
        return dict(code=self.process.returncode, stdout=bytes(self.out), stderr=bytes(self.err),
                    eof=True, terminal=True, timeout=False)

    def close(self):
        import signal
        import subprocess
        if self.closed: return
        self.closed = True
        try:
            if self.process is not None and self.process.poll() is None:
                self.os.killpg(self.process.pid, signal.SIGTERM)
                try: self.process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if self.process.poll() is None: self.os.killpg(self.process.pid, signal.SIGKILL)
                    self.process.wait(timeout=2)
            if self.process is not None:
                try: self.os.killpg(self.process.pid, 0)
                except ProcessLookupError: pass
                else: raise SmokeFailure('CLEANUP')
        except BaseException:
            raise SmokeFailure('CLEANUP') from None
        finally:
            try:
                if self.selector is not None: self.selector.close()
                if self.process is not None:
                    for stream in [self.process.stdin, self.process.stdout, self.process.stderr]:
                        if not stream.closed: stream.close()
            finally:
                self.data[:] = b'\0' * len(self.data)
        self.verified_closed = True


def validate_binding(raw, digest, nonce, now_unix):
    """Reine Schema-/Hashprüfung; Dateilesen und Hostinitialisierung geschehen erst danach."""
    import hashlib
    import json
    import re
    from pathlib import PurePosixPath
    if len(raw) > 16384 or hashlib.sha256(raw).hexdigest() != digest:
        raise SmokeFailure('BINDING')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise SmokeFailure('BINDING')
            result[key] = value
        return result
    try:
        value = json.loads(raw, object_pairs_hook=unique)
        keys = {'version', 'owner', 'nonce', 'expiresUnix', 'dockerPath', 'dockerSha256', 'dockerHost',
                'daemonId', 'pgImage', 'apiImage', 'apiSha256', 'feederSha256', 'nativePath', 'nativeSha256',
                'runtime', 'pgUid', 'apiUid', 'gid', 'expectedMigration'}
        if set(value) != keys or type(value['version']) is not int or value['version'] != 1 or value['owner'] != '01a11f64-c814-7f51-abdb-1b7462e0491e' \
                or value['nonce'] != nonce or not re.fullmatch('[a-f0-9]{32}', nonce):
            raise SmokeFailure('BINDING')
        if type(value['expiresUnix']) is not int or not now_unix < value['expiresUnix'] <= now_unix + 300:
            raise SmokeFailure('BINDING')
        for key in ['dockerSha256', 'apiSha256', 'feederSha256', 'nativeSha256']:
            if not re.fullmatch('[a-f0-9]{64}', value[key]): raise SmokeFailure('BINDING')
        for key in ['pgUid', 'apiUid', 'gid', 'expectedMigration']:
            if type(value[key]) is not int or not 0 < value[key] < (4097 if key == 'expectedMigration' else 2**31):
                raise SmokeFailure('BINDING')
        if not value['dockerHost'].startswith('unix:///') or not value['dockerPath'].startswith('/') \
                or not value['daemonId'] or not re.fullmatch(r'10\.0\.\d+', value['runtime']):
            raise SmokeFailure('BINDING')
        native = PurePosixPath(value['nativePath'])
        if not str(native).startswith('/app/') or '..' in native.parts or str(native) != value['nativePath'] \
                or not str(native).endswith('.so'):
            raise SmokeFailure('BINDING')
        return value
    except SmokeFailure: raise
    except Exception: raise SmokeFailure('BINDING') from None


class DockerPorts:
    """Fester Linux-/Dockeradapter ohne Pull/Login/Build/Hostmount, nur die gebundene neue Nonce."""
    def __init__(self, binding, binding_sha, home, secrets):
        import hashlib
        import os
        import sys
        from pathlib import Path
        self.b, self.binding_sha, self.home, self.secrets = binding, binding_sha, home, secrets
        self.pg, self.api_result, self.created = None, None, []
        self.expected, self.cleanup_until = {}, None
        self.image_ids, self.image_env, self.commands = {}, {}, []
        import time
        self.utc = time.time
        if sys.platform != 'linux': raise SmokeFailure('BINDING')
        binary = Path(binding['dockerPath'])
        if not binary.is_absolute() or not binary.is_file() or binary.stat().st_size > 64 * 1024**2:
            raise SmokeFailure('BINDING')
        with binary.open('rb') as source:
            if hashlib.file_digest(source, 'sha256').hexdigest() != binding['dockerSha256']:
                raise SmokeFailure('BINDING')
        self.env = {'PATH': '/usr/bin:/bin', 'HOME': str(home), 'DOCKER_CONFIG': str(home / 'docker'), 'TMPDIR': str(home)}
        self.base = [str(binary), '--host', binding['dockerHost']]
        self.clock = LiveClock()

    def _fresh(self):
        if int(self.utc()) >= self.b['expiresUnix']:
            raise SmokeFailure('BINDING')

    def _run(self, args, missing=None):
        import json
        limit = 30000 if self.cleanup_until is None else self.cleanup_until - self.clock.now
        if limit <= 0: raise SmokeFailure('CLEANUP')
        child = AttachedCommand(self.base + args, self.env, b'', min(limit, 30000), self.secrets, self.commands)
        try:
            while not child.tick():
                if self.pg is not None and self.pg.process.poll() is None: self.pg.tick(0)
            result = child.result()
            if result['code']:
                # Nur exakte bekannte Not-found-Texte sind Abwesenheitsbelege; Rechte-/Netzfehler bleiben UNKNOWN.
                if missing and not result['stdout'].strip(b' \n[]') and result['stderr'].decode().strip() in missing:
                    return None
                raise SmokeFailure('DOCKER')
            return result['stdout']
        finally: child.close()

    def preflight(self):
        import json
        import shutil
        from pathlib import Path
        self._fresh()
        info = json.loads(self._run(['info', '--format', '{{json .}}']))
        if info['ID'] != self.b['daemonId'] or info['OSType'] != 'linux' or info['Architecture'] not in ['x86_64', 'amd64']:
            raise SmokeFailure('BINDING')
        for image in [self.b['pgImage'], self.b['apiImage']]:
            data = json.loads(self._run(['image', 'inspect', image]))[0]
            if data['Os'] != 'linux' or data['Architecture'] != 'amd64': raise SmokeFailure('BINDING')
            self.image_ids[image] = data['Id']
            inherited = dict(x.split('=', 1) for x in data['Config'].get('Env', []))
            allowed = {'PATH', 'LANG', 'LC_ALL', 'TZ', 'APP_UID', 'ASPNETCORE_HTTP_PORTS',
                       'DOTNET_RUNNING_IN_CONTAINER', 'DOTNET_VERSION', 'ASPNET_VERSION',
                       'GOSU_VERSION', 'PG_MAJOR', 'PG_VERSION', 'PGDATA'}
            if set(inherited) - allowed or sum(len(k) + len(v) for k, v in inherited.items()) > 4096:
                raise SmokeFailure('BINDING')
            self.image_env[image] = inherited
        memory = int(next(line.split()[1] for line in Path('/proc/meminfo').read_text().splitlines()
                          if line.startswith('MemAvailable:'))) * 1024
        return dict(architecture='linux/amd64', cpus=info['NCPU'], memory=min(memory, info['MemTotal']),
                    disk=shutil.disk_usage(self.home).free, images=(self.b['pgImage'], self.b['apiImage']))

    def _volume_options(self):
        return {'type': 'tmpfs', 'device': 'tmpfs',
                'o': f"uid={self.b['pgUid']},gid={self.b['gid']},mode=0770,size=1048576"}

    def create_volume(self, name, nonce):
        self._fresh()
        # Docker-create ist idempotent. Altbestand darf selbst mit passenden Labels nie übernommen werden.
        # Der koordinierte Slot muss zusätzlich exklusiven Writerbesitz für diese frische Nonce garantieren.
        if self.inspect_volume(name) is not None: raise SmokeFailure('OWNERSHIP')
        args = ['volume', 'create', '--driver', 'local', '--label', 'flowzer-smoke.nonce=' + nonce]
        for key, value in self._volume_options().items(): args += ['--opt', key + '=' + value]
        try: self._run(args + [name])
        except Exception:
            self.inspect_volume(name)  # Einziger Readback, niemals zweites Create.
            raise SmokeFailure('OWNERSHIP') from None
        return name

    def inspect_volume(self, name):
        import json
        raw = self._run(['volume', 'inspect', name], [f'Error response from daemon: get {name}: no such volume'])
        if raw is None: return None
        item = json.loads(raw)[0]
        if item['Driver'] != 'local' or item['Options'] != self._volume_options(): raise SmokeFailure('OWNERSHIP')
        return dict(name=item['Name'], nonce=item['Labels'].get('flowzer-smoke.nonce'))

    @staticmethod
    def _pg_command():
        return "initdb -D /var/lib/postgresql/data -U fixture_owner --auth-local=scram-sha-256 --auth-host=scram-sha-256 --pwfile=/dev/stdin && exec postgres -D /var/lib/postgresql/data -c listen_addresses= -c unix_socket_directories=/flowzer-socket -c unix_socket_permissions=0770 -c logging_collector=off -c log_statement=none -c log_min_error_statement=panic"

    def _api_environment(self):
        return {'FLOWZER_SMOKE_BOUND_NONCE': self.b['nonce'], 'FLOWZER_SMOKE_BINDING_SHA': self.binding_sha,
                'FLOWZER_SMOKE_BOUND_UNTIL': str(self.b['expiresUnix']), 'FLOWZER_SMOKE_RUNTIME': self.b['runtime'],
                'FLOWZER_SMOKE_NATIVE_FILE': self.b['nativePath'], 'FLOWZER_SMOKE_NATIVE_SHA': self.b['nativeSha256']}

    def create(self, spec):
        self._fresh()
        name = 'flowzer-smoke-' + spec['nonce'] + '-' + spec['role']
        uid = self.b['pgUid'] if spec['role'] == 'pg' else self.b['apiUid']
        args = ['create', '--name', name, '--pull', 'never', '--interactive', '--network', 'none', '--log-driver', 'none',
                '--label', 'flowzer-smoke.nonce=' + spec['nonce'], '--label', 'flowzer-smoke.role=' + spec['role'],
                '--memory', str(spec['memory']), '--pids-limit', str(spec['pids']), '--cpus', '1',
                '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--user', f"{uid}:{self.b['gid']}",
                '--mount', 'type=volume,source=' + spec['socketVolume'] + ',target=/flowzer-socket']
        if spec['role'] == 'pg':
            args += ['--tmpfs', f"/var/lib/postgresql/data:rw,noexec,nosuid,size=134217728,uid={uid},gid={self.b['gid']}",
                     '--entrypoint', '/bin/sh', spec['image'], '-c',
                     self._pg_command()]
        else:
            args += ['--read-only', '--tmpfs', f"/tmp:rw,noexec,nosuid,size=67108864,uid={uid},gid={self.b['gid']}"]
            env = self._api_environment()
            for key, value in env.items(): args += ['--env', key + '=' + value]  # Ausschließlich nichtgeheime Bindungen.
            args += ['--entrypoint', '/usr/share/dotnet/dotnet', spec['image'], '/app/SmokeFeeder.dll',
                     '--bound-fixture', spec['nonce'], self.b['apiSha256'], self.b['feederSha256']]
        try: cid = self._run(args).decode().strip()
        except Exception:
            # Unsicherer Writeausgang bleibt ein Fehler, auch wenn die exakte eigene Nonce nun sichtbar wäre.
            self._run(['container', 'inspect', name], [f'Error: No such container: {name}'])
            raise SmokeFailure('OWNERSHIP') from None
        import re
        if not re.fullmatch('[a-f0-9]{64}', cid) or cid in self.expected:
            raise SmokeFailure('OWNERSHIP')
        self.expected[cid] = dict(spec); self.created.append(cid)
        return cid

    def inspect(self, cid):
        import json
        raw = self._run(['container', 'inspect', cid], [f'Error: No such container: {cid}', f'Error: No such object: {cid}'])
        if raw is None: return None
        value = json.loads(raw)[0]; config, host = value['Config'], value['HostConfig']
        expected = self.expected[cid]; role = expected['role']
        environment = dict(x.split('=', 1) for x in config['Env'])
        expected_environment = dict(self.image_env[expected['image']])
        if role == 'api': expected_environment.update(self._api_environment())
        if environment != expected_environment or len(config['Env']) != len(environment) \
                or value['Image'] != self.image_ids[expected['image']]:
            raise SmokeFailure('OWNERSHIP')
        command = (['/app/SmokeFeeder.dll', '--bound-fixture', self.b['nonce'], self.b['apiSha256'], self.b['feederSha256']]
                   if role == 'api' else None)
        if role == 'api' and (config['Entrypoint'] != ['/usr/share/dotnet/dotnet'] or config['Cmd'] != command):
            raise SmokeFailure('OWNERSHIP')
        uid = self.b['pgUid'] if role == 'pg' else self.b['apiUid']
        pg_cmd = ['-c', self._pg_command()]
        expected_tmpfs = ({'/var/lib/postgresql/data': f"rw,noexec,nosuid,size=134217728,uid={uid},gid={self.b['gid']}"}
                          if role == 'pg' else {'/tmp': f"rw,noexec,nosuid,size=67108864,uid={uid},gid={self.b['gid']}"})
        if host['Tmpfs'] != expected_tmpfs or (role == 'pg' and
                (config['Entrypoint'] != ['/bin/sh'] or config['Cmd'] != pg_cmd)):
            raise SmokeFailure('OWNERSHIP')
        volumes = [m for m in value['Mounts'] if m['Type'] == 'volume']
        if value['Id'] != cid or config['User'] != f"{uid}:{self.b['gid']}" or host.get('Binds') \
                or len(volumes) != 1 or volumes[0]['Name'] != expected['socketVolume'] \
                or volumes[0]['Destination'] != '/flowzer-socket' or not volumes[0]['RW'] \
                or any(m['Type'] not in ['volume', 'tmpfs'] for m in value['Mounts']) \
                or host['PidMode'] or host['IpcMode'] not in ['private', ''] or host.get('PublishAllPorts') \
                or host['Memory'] != expected['memory'] or host['PidsLimit'] != expected['pids'] \
                or host['NanoCpus'] != 1000000000 or host['ReadonlyRootfs'] != (role == 'api') \
                or host.get('CapAdd') or host['CapDrop'] != ['ALL'] \
                or not any(x in host['SecurityOpt'] for x in ['no-new-privileges', 'no-new-privileges:true']):
            raise SmokeFailure('OWNERSHIP')
        return dict(expected, nonce=config['Labels'].get('flowzer-smoke.nonce'), role=config['Labels'].get('flowzer-smoke.role'),
                    image=config['Image'], network=host['NetworkMode'], logDriver=host['LogConfig']['Type'],
                    privileged=host['Privileged'], portBindings=host['PortBindings'] or {})

    def start_pg(self, cid, payload, remaining_ms):
        self._fresh()
        self.pg = AttachedCommand(self.base + ['start', '--attach', '--interactive', cid], self.env,
                                  payload, remaining_ms, self.secrets, self.commands)
        while not self.pg.input_done: self.pg.tick()
        return dict(written=self.pg.written, eof=self.pg.input_done, timeout=False,
                    stdout=bytes(self.pg.out), stderr=bytes(self.pg.err))

    def run_fixture(self, cid, payload, remaining_ms):
        self._fresh()
        child = AttachedCommand(self.base + ['start', '--attach', '--interactive', cid], self.env,
                                payload, remaining_ms, self.secrets, self.commands)
        try:
            while not child.tick():
                if self.pg is not None: self.pg.tick(0)
            self.api_result = child.result()
            return self.api_result
        finally: child.close()

    def stop(self, cid, remaining_ms):
        if self.cleanup_until is None:
            self.cleanup_until = self.clock.now + 20000
            if self.pg is not None: self.pg.deadline = self.cleanup_until
        self._run(['stop', '--time', '5', cid])
        if self.expected[cid]['role'] == 'api':
            return self.api_result or dict(code=1, stdout=b'', stderr=b'', eof=False, terminal=False, timeout=False)
        if self.pg is None:
            return dict(code=0, stdout=b'', stderr=b'', eof=True, terminal=True, timeout=False)
        self.pg.deadline = min(self.cleanup_until, self.clock.now + remaining_ms)
        try:
            while not self.pg.tick(): pass
            return self.pg.result()
        finally: self.pg.close()

    def remove(self, cid):
        self._run(['rm', cid])  # Kein --force, kein Löschen eines unbelegten/noch laufenden Containers.

    def remove_volume(self, name):
        self._run(['volume', 'rm', name])


def main(args):
    """Keine Defaults/Discovery: genau Hash+Nonce einer frischen expliziten Rootöffnung nötig."""
    import hashlib
    import json
    import os
    from pathlib import Path
    import secrets as random
    import tempfile
    import time
    if len(args) != 4 or args[0] != '--bound-run':
        print('FLOWZER_SMOKE_SOURCE_ONLY')
        return 78
    ports, binding, directory = None, None, None
    try:
        path = Path(args[1])
        if not path.is_absolute() or path.stat().st_uid != os.getuid() or path.stat().st_mode & 0o022:
            raise SmokeFailure('BINDING')
        with path.open('rb') as source: raw = source.read(16385)
        binding = validate_binding(raw, args[2], args[3], int(time.time()))
        values = [random.token_hex(24) for _ in range(5)]
        if len(set(values)) != 5: raise SmokeFailure('INPUT')
        payload = json.dumps(dict(version=1, role='fixture', nonce=binding['nonce'], expectedMigration=binding['expectedMigration'],
            values=dict(zip(['adminPassword', 'migrationPassword', 'runtimePassword', 'bffClientSecret', 'directoryClientSecret'], values)))).encode()
        directory = tempfile.mkdtemp(prefix='flowzer-smoke-parent-')
        ports = DockerPorts(binding, args[2], Path(directory), [x.encode() for x in values])
        SmokeRun(ports, LiveClock()).run(Plan(binding['nonce'], binding['pgImage'], binding['apiImage']),
                                      values[0].encode(), payload, [x.encode() for x in values])
        if not all(command.verified_closed for command in ports.commands): raise SmokeFailure('CLEANUP')
        import shutil
        shutil.rmtree(directory)  # Nur nach komplett belegtem eigenem Abschluss, niemals im UNKNOWN-Finally.
        print('FLOWZER_SMOKE_PARENT_OK')
        return 0
    except BaseException:
        # Nur sichere eigene Handles, keine Rohfehler/Umgebung/Pakete. Keine Erfolgsausgabe nach UNKNOWN.
        print(json.dumps(dict(result='REJECTED', nonce=binding['nonce'] if binding else None,
                              cids=ports.created if ports else [], home=directory, cleanup='CHECK_REQUIRED')))
        return 2


if __name__ == '__main__':
    import sys
    raise SystemExit(main(sys.argv[1:]))
