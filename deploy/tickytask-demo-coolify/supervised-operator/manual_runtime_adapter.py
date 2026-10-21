"""Konkrete Ports des vorhandenen manuellen Operators, standardmäßig inert.

Kein Installer/Secretresolver und keine Runtimefreigabe. Ein äußerer Rootguard
muss Ziel, Images/UIDs, eigene Netze/Daten, Source, Frist und jeden Versuch binden.
Import/CLI führt kein SSH, Docker oder SQL aus. Alle unbekannten Ausgänge sperren
weitere Ports; es gibt weder Adoption noch automatische Wiederholung/Löschung.
"""
from __future__ import annotations
import importlib.util,sys,json,re,uuid,ipaddress,tarfile,io,time
from pathlib import Path
from dataclasses import dataclass
from functools import wraps
from urllib.parse import urlsplit
for _name in ('manual_pg_adapter','manual_operator','manual_readiness'):
    if _name not in sys.modules:
        _spec=importlib.util.spec_from_file_location(_name,Path(__file__).with_name(_name+'.py'))
        _module=importlib.util.module_from_spec(_spec);sys.modules[_name]=_module;_spec.loader.exec_module(_module)
pg=sys.modules['manual_pg_adapter'];op=sys.modules['manual_operator'];ready=sys.modules['manual_readiness']


def require(value):
    if not value:raise op.PortFailure()


@dataclass(frozen=True)
class PublicSettings:
    """Nur nichtgeheime, separat von Root autorisierte Demo-Konfiguration.

    Ein gemeinsamer Realm gewährt keine gemeinsamen Rechte. Client-/Audience-/
    Gruppenbindungen kommen aus der eigenen Demoöffnung, nie aus fremden Defaults.
    """
    authority: str
    audience: str
    bff_client: str
    directory_client: str
    root_group: str
    proxy_cidr: str

    def environment(self):
        try:
            u=urlsplit(self.authority);net=ipaddress.IPv4Network(self.proxy_cidr,strict=True)
            require(u.scheme=='https' and u.hostname and u.netloc==u.hostname and not u.query and not u.fragment
                    and u.path=='/realms/TickyTask' and not u.username and str(net)==self.proxy_cidr and 24<=net.prefixlen<=30)
            require(all(type(v) is str and re.fullmatch('[a-zA-Z0-9._-]{1,128}',v) for v in [self.audience,self.bff_client,self.directory_client]))
            require(str(uuid.UUID(self.root_group))==self.root_group)
        except Exception:raise op.PortFailure() from None
        return {
            'ASPNETCORE_ENVIRONMENT':'Production','ASPNETCORE_URLS':'http://0.0.0.0:8080',
            'Storage__Provider':'PostgreSql','Storage__PostgreSql__Schema':'flowzer','Storage__PostgreSql__ApplyMigrationsOnStartup':'false',
            'Authentication__Scheme':'Bff','Authentication__JwtBearer__Authority':self.authority,
            'Authentication__JwtBearer__Audience':self.audience,'Authentication__JwtBearer__RequireHttpsMetadata':'true',
            'Authentication__JwtBearer__RequiredRole':'flowzer-access','Authentication__JwtBearer__Roles__Modeler':'flowzer-modeler',
            'Authentication__JwtBearer__Roles__Operator':'flowzer-operator','Authentication__JwtBearer__Roles__Worker':'flowzer-worker',
            'Authentication__JwtBearer__LegacyPermissiveRoles':'false','Authentication__Bff__ClientId':self.bff_client,
            'Authentication__Bff__Scopes__0':'openid','Authentication__Bff__Scopes__1':'profile',
            'Authentication__Bff__DataProtectionKeysPath':'/var/lib/flowzer/data-protection',
            'IdentityDirectory__Enabled':'true','IdentityDirectory__ServerUrl':self.authority.removesuffix('/realms/TickyTask'),
            'IdentityDirectory__Issuer':self.authority,'IdentityDirectory__Realm':'TickyTask','IdentityDirectory__ClientId':self.directory_client,
            'IdentityDirectory__RootGroupId':self.root_group,'FormEmbedding__Enabled':'true',
            'FormEmbedding__PublicOrigin':'https://flowzer.tickytask.de','FormEmbedding__AllowedHostOrigins__0':'https://demo.tickytask.de',
            'ForwardedHeaders__KnownNetworks__0':self.proxy_cidr,'ForwardedHeaders__ForwardLimit':'2',
            'Ai__AllowCloudProviders':'false','Ai__AllowLocalEndpoints':'false','AiExecution__Enabled':'false',
            'Connectors__Http__Enabled':'false','Connectors__Email__Enabled':'false','ServiceTaskWebhooks__Enabled':'false',
            'InboundTriggers__SecretKey':'','TickyTaskTicketActions__ApiClientId':'','Observability__Prometheus__Enabled':'false','TZ':'Europe/Berlin'}


@dataclass(frozen=True)
class RuntimeBinding:
    """Appimagequelle und frischer Operatorversuch bleiben ausdrücklich getrennte Pins."""
    api_image: str
    api_source: str
    network_id: str
    uid: int
    gid: int
    settings: PublicSettings
    network_labels: tuple[tuple[str,str],...]
    attempt_nonce: str
    prior_api_nonce: str|None=None
    allowed_network_peers: tuple[str,...]=()

    def validate(self):
        require(re.fullmatch('sha256:[a-f0-9]{64}',self.api_image) and re.fullmatch('[a-f0-9]{40}',self.api_source)
                and re.fullmatch('[a-f0-9]{64}',self.network_id) and re.fullmatch('[a-f0-9]{32}',self.attempt_nonce)
                and all(type(v) is int and 0<v<65536 for v in [self.uid,self.gid]))
        require(self.prior_api_nonce is None or re.fullmatch('[a-f0-9]{32}',self.prior_api_nonce))
        require(type(self.network_labels) is tuple and 1<=len(self.network_labels)<=16 and all(type(row) is tuple and len(row)==2 and all(type(v) is str and v and len(v)<=256 and not any(ord(c)<32 for c in v) for v in row) for row in self.network_labels))
        require(len(dict(self.network_labels))==len(self.network_labels))
        require(type(self.allowed_network_peers) is tuple and len(set(self.allowed_network_peers))==len(self.allowed_network_peers) and len(self.allowed_network_peers)<=2
                and all(type(cid) is str and re.fullmatch('[a-f0-9]{64}',cid) for cid in self.allowed_network_peers))
        self.settings.environment()


def port(method):
    """Jeder Fehler sperrt diesen Versuch und erhält einen eigenen UNKNOWN-Handle."""
    @wraps(method)
    def run(self,*args,**kwargs):
        try:
            require(self.state!='UNKNOWN');self.guard();return method(self,*args,**kwargs)
        except BaseException as error:
            self.state='UNKNOWN';self.close()
            if isinstance(error,(KeyboardInterrupt,SystemExit)):raise
            raise op.PortFailure(getattr(error,'own_pid',None) or getattr(error,'own_local_pid',None)) from None
    return run


# Nur feste Metadaten-/Rechtequeries. Kein privater Testtable-/Probe-DMLwrite.
ACL_SQL=b"""-- ACL_RUNTIME_PROOF
SELECT current_user='flowzer_tt_demo'
 AND NOT pg_has_role('flowzer_tt_demo','flowzer_tt_demo_migration','MEMBER')
 AND (SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='flowzer_tt_demo')
 AND (SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication AND NOT rolbypassrls FROM pg_roles WHERE rolname='flowzer_tt_demo_migration')
 AND (SELECT pg_get_userbyid(datdba)='flowzer_tt_demo_migration' FROM pg_database WHERE datname='flowzer_tt_demo')
 -- ACL_DENY_DATABASE_DDL: keine alternative Schema-/TEMP-/Rollenerweiterung.
 AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member='flowzer_tt_demo'::regrole)
 AND NOT has_database_privilege('flowzer_tt_demo','flowzer_tt_demo','CREATE,TEMPORARY,CONNECT WITH GRANT OPTION')
 AND has_database_privilege('flowzer_tt_demo','flowzer_tt_demo','CONNECT')
 AND NOT EXISTS (SELECT 1 FROM pg_namespace n WHERE has_schema_privilege('flowzer_tt_demo',n.oid,'CREATE'))
 AND NOT has_schema_privilege('flowzer_tt_demo','flowzer','USAGE WITH GRANT OPTION')
 AND has_schema_privilege('flowzer_tt_demo','flowzer','USAGE') AND NOT has_schema_privilege('flowzer_tt_demo','flowzer','CREATE')
 AND has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','SELECT')
 AND NOT has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','INSERT')
 AND NOT has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','UPDATE')
 AND NOT has_table_privilege('flowzer_tt_demo','flowzer.schema_migrations','DELETE')
 -- ACL_DENY_ADDITIONAL_EFFECTIVE: PUBLIC/inherited roles/global defaults included.
 AND (SELECT count(DISTINCT a.privilege_type)=4 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) a
      WHERE d.defaclrole='flowzer_tt_demo_migration'::regrole AND d.defaclnamespace='flowzer'::regnamespace
      AND d.defaclobjtype='r' AND a.grantee='flowzer_tt_demo'::regrole
      AND a.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE'))
 -- ACL_DENY_PUBLIC_AND_OTHER_ROLES: explizite Isolation, nicht nur effektive Runtime-Rechte.
 AND NOT EXISTS (SELECT 1 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) a
      WHERE d.defaclrole='flowzer_tt_demo_migration'::regrole AND d.defaclnamespace='flowzer'::regnamespace
      AND d.defaclobjtype IN ('r','S') AND a.grantee NOT IN ('flowzer_tt_demo'::regrole,'flowzer_tt_demo_migration'::regrole))
 AND NOT EXISTS (SELECT 1 FROM pg_database d, LATERAL aclexplode(COALESCE(d.datacl,acldefault('d',d.datdba))) a
      WHERE d.datname='flowzer_tt_demo' AND a.grantee NOT IN ('flowzer_tt_demo'::regrole,'flowzer_tt_demo_migration'::regrole))
 AND NOT EXISTS (SELECT 1 FROM pg_namespace n, LATERAL aclexplode(COALESCE(n.nspacl,acldefault('n',n.nspowner))) a
      WHERE n.oid='flowzer'::regnamespace AND a.grantee NOT IN ('flowzer_tt_demo'::regrole,'flowzer_tt_demo_migration'::regrole))
 AND NOT EXISTS (SELECT 1 FROM pg_class c, LATERAL aclexplode(COALESCE(c.relacl,
          acldefault(CASE WHEN c.relkind='S' THEN 's'::"char" ELSE 'r'::"char" END,c.relowner))) a
      WHERE c.relnamespace='flowzer'::regnamespace AND c.relkind IN ('r','p','v','m','f','S')
      AND a.grantee NOT IN ('flowzer_tt_demo'::regrole,'flowzer_tt_demo_migration'::regrole))
 -- ACL_DENY_GLOBAL_SCOPE: auch erlaubte DMLrechte duerfen nicht global entstehen.
 AND NOT EXISTS (SELECT 1 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) a
      WHERE d.defaclrole='flowzer_tt_demo_migration'::regrole AND d.defaclnamespace=0 AND d.defaclobjtype IN ('r','S')
      AND CASE WHEN a.grantee=0 THEN true ELSE pg_has_role('flowzer_tt_demo',a.grantee,'USAGE') END)
 AND NOT EXISTS (SELECT 1 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) a
      WHERE d.defaclrole='flowzer_tt_demo_migration'::regrole AND d.defaclnamespace IN (0,'flowzer'::regnamespace)
      AND d.defaclobjtype IN ('r','S') AND CASE WHEN a.grantee=0 THEN true ELSE pg_has_role('flowzer_tt_demo',a.grantee,'USAGE') END
      AND (a.is_grantable OR (d.defaclobjtype='r' AND a.privilege_type NOT IN ('SELECT','INSERT','UPDATE','DELETE'))
           OR (d.defaclobjtype='S' AND a.privilege_type NOT IN ('USAGE','SELECT'))))
 AND (SELECT count(DISTINCT a.privilege_type)=2 FROM pg_default_acl d, LATERAL aclexplode(d.defaclacl) a
      WHERE d.defaclrole='flowzer_tt_demo_migration'::regrole AND d.defaclnamespace='flowzer'::regnamespace
      AND d.defaclobjtype='S' AND a.grantee='flowzer_tt_demo'::regrole
      AND a.privilege_type IN ('USAGE','SELECT'))
 AND NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.relnamespace='flowzer'::regnamespace AND c.relkind IN ('r','p','v','m','f')
      AND (has_table_privilege('flowzer_tt_demo',c.oid,'TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
           OR has_table_privilege('flowzer_tt_demo',c.oid,'SELECT WITH GRANT OPTION,INSERT WITH GRANT OPTION,UPDATE WITH GRANT OPTION,DELETE WITH GRANT OPTION')
           OR NOT has_table_privilege('flowzer_tt_demo',c.oid,'SELECT')
           OR (c.relname<>'schema_migrations' AND (NOT has_table_privilege('flowzer_tt_demo',c.oid,'INSERT')
                OR NOT has_table_privilege('flowzer_tt_demo',c.oid,'UPDATE') OR NOT has_table_privilege('flowzer_tt_demo',c.oid,'DELETE')))))
 AND NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.relnamespace='flowzer'::regnamespace AND c.relkind='S'
      AND (has_sequence_privilege('flowzer_tt_demo',c.oid,'UPDATE,USAGE WITH GRANT OPTION,SELECT WITH GRANT OPTION')
           OR NOT has_sequence_privilege('flowzer_tt_demo',c.oid,'USAGE') OR NOT has_sequence_privilege('flowzer_tt_demo',c.oid,'SELECT')));
"""
HISTORY_SQL=b"""-- HISTORY_22_PROOF
SELECT count(*)=22 AND min(version)=1 AND max(version)=22 FROM flowzer.schema_migrations;
"""
MOUNTS='"mounts":[{{range .Mounts}}{"type":{{json .Type}},"name":{{json .Name}},"destination":{{json .Destination}},"rw":{{json .RW}}},{{end}}null]'
# .Id erzwingt den dokumentierten Raw-JSON-Fallback: NanoCpus heißt im
# typisierten Go-SDK NanoCPUs, in der Docker-API jedoch NanoCpus. Nicht mischen.
APP_FMT='{"id":{{json .Id}},"name":{{json .Name}},"image":{{json .Image}},"labels":{{json .Config.Labels}},"user":{{json .Config.User}},"entrypoint":{{json .Config.Entrypoint}},"cmd":{{json .Config.Cmd}},"pidmode":{{json .HostConfig.PidMode}},"ipcmode":{{json .HostConfig.IpcMode}},"running":{{json .State.Running}},"exit":{{json .State.ExitCode}},"env":{{json .Config.Env}},"network":{{json .HostConfig.NetworkMode}},"networks":[{{range $name,$config := .NetworkSettings.Networks}}{"name":{{json $name}},"id":{{json $config.NetworkID}}},{{end}}null],"ports":{{json .HostConfig.PortBindings}},"log":{{json .HostConfig.LogConfig.Type}},"restart":{{json .HostConfig.RestartPolicy.Name}},"readonly":{{json .HostConfig.ReadonlyRootfs}},"privileged":{{json .HostConfig.Privileged}},"capdrop":{{json .HostConfig.CapDrop}},"capadd":{{json .HostConfig.CapAdd}},"security":{{json .HostConfig.SecurityOpt}},"memory":{{json .HostConfig.Memory}},"nanocpus":{{json .HostConfig.NanoCpus}},"pids":{{json .HostConfig.PidsLimit}},"tmpfs":{{json .HostConfig.Tmpfs}},'+MOUNTS+'}'


class RuntimePorts:
    """Kleine konkrete Verdrahtung des vorhandenen ManualOperator, kein eigener Ablauf.

    `deliver=lambda: ports.accept_bundle(resolve_once())` liefert erst nach observe.
    Der Resolver bleibt außerhalb dieser öffentlichen Source und hat keine ENV-
    oder Dateifallbacks. Jede Benutzung benötigt einen neuen gebundenen Versuch.
    """
    def __init__(self,binding,runtime,target,guard,*,mode='initial',pipe=None,prior_pg_cid=None,prior_api_cid=None):
        runtime.validate();require(mode in ('initial','restart') and callable(guard))
        require(runtime.uid==binding.key_uid and runtime.gid==binding.key_gid and binding.prepare_keyring is True)
        if mode=='restart':
            require(re.fullmatch('[a-f0-9]{64}',prior_pg_cid or '') and runtime.prior_api_nonce is not None
                    and runtime.attempt_nonce!=runtime.prior_api_nonce)
            require(prior_api_cid is None or re.fullmatch('[a-f0-9]{64}',prior_api_cid))
        else:require(prior_pg_cid is None and prior_api_cid is None)
        self.pg=pg.PgBootstrapAdapter(binding,target,guard,pipe);self.r=runtime;self.guard=guard;self.mode=mode
        self.prior_pg=prior_pg_cid;self.prior_api=prior_api_cid;self.state='NEW';self.bundle=None;self.canaries=[]
        self.expected_api=None;self.expected_migration=None;self.sql_packets={};self.image_env=None;self.image_labels=None;self.api_cid=None
        self.env=runtime.settings.environment()

    def _raw(self,args,payload=b'',timeout=60):
        self.guard()
        result=self.pg.pipe.exchange(self.pg.target.argv(args),payload,self.canaries,timeout=timeout)
        self.guard();require(type(result.exit_code) is int and type(result.stdout) is bytes and type(result.stderr) is bytes)
        return result

    def _call(self,args,payload=b'',*,timeout=60):
        result=self._raw(args,payload,timeout=timeout);require(result.exit_code==0 and not result.stderr);return result.stdout

    def _labels(self,nonce=None):return dict(self.pg.labels,**{'flowzer.tt.demo.nonce':nonce or self.r.attempt_nonce})
    def _name(self,role,nonce=None):return self.pg.b.namespace+'_'+role+'_'+(nonce or self.r.attempt_nonce)
    def _environment(self,role):
        values=self.env if role=='api' else {k:self.env[k] for k in ['ASPNETCORE_ENVIRONMENT','Storage__Provider','Storage__PostgreSql__Schema','Storage__PostgreSql__ApplyMigrationsOnStartup','TZ']}
        merged=dict(v.split('=',1) for v in self.image_env)
        merged.update(values);return merged

    def _image(self):
        fmt='{"id":{{json .ID}},"os":{{json .Os}},"arch":{{json .Architecture}},"revision":{{json (index .Config.Labels "org.opencontainers.image.revision")}},"labels":{{json .Config.Labels}},"env":{{json .Config.Env}}}'
        v=json.loads(self._call(['image','inspect',self.r.api_image,'--format',fmt]))
        require(v['id']==self.r.api_image and v['os']=='linux' and v['arch']==self.pg.b.architecture and v['revision']==self.r.api_source)
        require(type(v['env']) is list and all(type(x) is str and '=' in x for x in v['env']))
        base=dict(x.split('=',1) for x in v['env']);require(len(base)==len(v['env']))
        # Nur deklarierte Standardimagewerte; keine konkurrierenden Secret-/Loaderprovider.
        require(set(base)<= {'PATH','APP_UID','ASPNET_VERSION','DOTNET_VERSION','DOTNET_RUNNING_IN_CONTAINER','ASPNETCORE_HTTP_PORTS','ASPNETCORE_URLS','FLOWZER_STORAGE_ROOT'})
        # Docker create übernimmt Image-Labels zusätzlich zu den Runlabels.
        # Vollständige unveränderliche Imageconfig binden, niemals nur Owner-Subset
        # akzeptieren oder beliebige Containerlabels nachträglich übernehmen.
        labels=v['labels'];require(type(labels) is dict and 1<=len(labels)<=32)
        require(all(type(k) is str and 0<len(k)<=256 and type(value) is str and len(value)<=4096 for k,value in labels.items()))
        require(labels.get('org.opencontainers.image.revision')==self.r.api_source
                and not any(k in self.pg.labels or k.startswith('flowzer.tt.demo.') for k in labels))
        self.image_labels=dict(labels)
        self.image_env=tuple(v['env'])

    def _network(self,*,timeout=60):
        fmt='{"id":{{json .Id}},"driver":{{json .Driver}},"internal":{{json .Internal}},"subnets":[{{range .IPAM.Config}}{{json .Subnet}},{{end}}null],"labels":{{json .Labels}},"peers":[{{range $cid,$peer := .Containers}}{{json $cid}},{{end}}null]}'
        v=json.loads(self._call(['network','inspect',self.r.network_id,'--format',fmt],timeout=timeout))
        # Keine geratenen Coolify-/Compose-Labelnamen: vollständige tatsächliche Rootbindung vergleichen.
        require(v['id']==self.r.network_id and v['driver']=='bridge' and v['internal'] is False
                and v['subnets']==[self.r.settings.proxy_cidr,None]
                and v['labels']==dict(self.r.network_labels))
        peers=v['peers'];require(type(peers) is list and peers[-1:]==[None] and all(type(cid) is str and re.fullmatch('[a-f0-9]{64}',cid) for cid in peers[:-1]))
        expected=set(self.r.allowed_network_peers)|({self.api_cid} if self.api_cid else set())
        require(len(peers[:-1])==len(set(peers[:-1])) and set(peers[:-1])==expected)

    def _keyring(self):
        fmt='{"name":{{json .Name}},"driver":{{json .Driver}},"labels":{{json .Labels}}}'
        v=json.loads(self._call(['volume','inspect',self.pg.keys,'--format',fmt]))
        require(v==dict(name=self.pg.keys,driver='local',labels=self.pg.labels))

    @port
    def observe(self):
        """Ziel/Image/Netz und gesamten erwarteten eigenen Bestand vor Secretlieferung lesen."""
        require(self.state=='NEW')
        # stdin-only CID/EOF-Kanal ist versionsspezifisch: Docker29 wartet weiter
        # auf Containerexit. Unbekannte/neue Clients VOR Secret-/Ressourcenwrites sperren.
        require(json.loads(self._call(['version','--format','{{json .Client.Version}}']))=='27.5.1')
        self._image();self._network()
        for role in ['api','migrate']:
            require(not self._call(['ps','-a','--filter','name=^/'+self._name(role)+'$','--format','{{.ID}}']).strip())
        if self.mode=='initial':
            for cmd in [['volume','ls','--filter','name=^'+self.pg.b.namespace+'_','--format','{{.Name}}'],['ps','-a','--filter','name=^/'+self.pg.b.namespace+'_','--format','{{.ID}}']]:require(not self._call(cmd).strip())
            value=op.Observation(True,'ABSENT','ABSENT','ABSENT')
        else:
            # Unbekannte/alte Hilfscontainer werden nicht still übernommen. Vor
            # Wiederanlauf muss deren separat bestätigtes Cleanup abgeschlossen sein.
            ids=self._call(['ps','-a','--no-trunc','--filter','name=^/'+self.pg.b.namespace+'_','--format','{{.ID}}']).decode('ascii').splitlines()
            require(len(ids)==len(set(ids)) and set(ids)=={x for x in [self.prior_pg,self.prior_api] if x})
            names=self._call(['volume','ls','--filter','name=^'+self.pg.b.namespace+'_','--format','{{.Name}}']).decode('ascii').splitlines()
            require(len(names)==3 and set(names)=={self.pg.data,self.pg.socket,self.pg.keys})
            self.pg.pg_cid=self.prior_pg;self.pg.verify_existing_pg();self._keyring()
            for name in [self.pg.data,self.pg.socket]:
                fmt='{"name":{{json .Name}},"driver":{{json .Driver}},"labels":{{json .Labels}}}'
                require(json.loads(self._call(['volume','inspect',name,'--format',fmt]))==dict(name=name,driver='local',labels=self.pg.labels))
            # docker cp liest auch bei gestopptem eigenen Container. Nur PG_VERSION
            # in einem begrenzten RAMtar, kein Helpercontainer/Datei-/Secretwrite.
            raw=self._call(['container','cp',self.prior_pg+':/var/lib/postgresql/data/PG_VERSION','-'])
            with tarfile.open(fileobj=io.BytesIO(raw),mode='r:') as archive:
                items=archive.getmembers();require(len(items)==1 and items[0].name=='PG_VERSION' and items[0].isfile() and items[0].size==3)
                require(archive.extractfile(items[0]).read(4)==b'17\n')
            if self.prior_api:self._inspect_app(self.prior_api,'api',False,self.r.prior_api_nonce)
            value=op.Observation(True,'OWNED','INITIALIZED','EXITED' if self.prior_api else 'ABSENT')
        self.state='OBSERVED';return value

    @port
    def accept_bundle(self,bundle):
        """Frische einmalige Lieferung binden und ALLE Rollenpakete vor Ressourcenwrites prüfen."""
        require(self.state=='OBSERVED' and isinstance(bundle,op.SecretBundle));bundle.validate(self.mode)
        self.bundle=bundle
        # Wiederverwendung exakt derselben Paketbuilder wie ManualOperator, nicht eigener Secretvertrag.
        self.expected_migration=op._packet('migrate',{'migrationConnectionString':op._connection(bundle.migration,'flowzer_tt_demo_migration')})
        self.expected_api=op._packet('api',{'runtimeConnectionString':op._connection(bundle.runtime,'flowzer_tt_demo'),'bffClientSecret':bundle.bff,'directoryClientSecret':bundle.directory})
        values=[v for v in [bundle.admin,bundle.migration,bundle.runtime,bundle.bff,bundle.directory] if v is not None]
        for v in values:
            self.canaries.extend([v.encode(),v.replace('"','""').encode(),json.dumps(v,ensure_ascii=False)[1:-1].encode()])
        # Beide festen SQLkanäle brauchen dasselbe 16KiB-UTF8budget wie der
        # Transport. Vor dem ersten Ressourcenwrite bauen, auch beim Wiederanlauf.
        for sql in (ACL_SQL,HISTORY_SQL):
            packet=bytearray(bundle.runtime.encode()+b'\n'+sql)
            self.sql_packets[sql]=packet;require(len(packet)<=16384)
        # Identischer Canaryvergleich wie BoundedPipe, ohne Prozess-/Netzstart.
        # Die feste psql-Pfadzeichenfolge kann selbst ein formal gültiges Secret sein.
        sentinels=sys.modules['manual_transport']._sentinels(self.canaries)
        for args in [self._sql_arguments('0'*64),self._run('migrate'),self._run('api')]:
            require(not any(marker in arg.encode() for arg in self.pg.target.argv(args) for marker in sentinels))
        self.state='DELIVERED';return bundle

    @port
    def initialize(self,bundle):
        """Nur initial: bestehende geprüfte PGinitialisierung samt Keyring verwenden."""
        require(self.mode=='initial' and self.state=='DELIVERED' and bundle is self.bundle)
        d=Path(__file__).resolve().parents[2]/'postgresql'
        self.pg.initialize(admin=bundle.admin,migration=bundle.migration,runtime=bundle.runtime,
                           sql01=(d/'01-datenbank-und-rollen.sql').read_bytes(),sql02=(d/'02-laufzeitrechte.sql').read_bytes())
        self._keyring();self.state='INITIALIZED';return 0

    @port
    def pg_start(self):
        """Initialer PGstart oder ausdrücklich eigener vorhandener CID ohne Reinitialisierung."""
        require(self.state==('INITIALIZED' if self.mode=='initial' else 'DELIVERED'))
        if self.mode=='initial':self.pg.pg_start()
        else:self.pg.restart_existing_pg()
        self.pg.verify_existing_pg(running=True);self.state='PG_STARTED';return 0

    @port
    def bootstrap(self,bundle):
        """Nur initial die gebundenen Rollen-/ACLskripte über den geprüften PGport ausführen."""
        require(self.mode=='initial' and self.state=='PG_STARTED' and bundle is self.bundle)
        self.pg.bootstrap();self.state='BOOTSTRAPPED';return 0

    def _sql_arguments(self,cid):
        return ['container','exec','--interactive','--user',str(self.pg.b.pg_uid)+':'+str(self.pg.b.pg_gid),cid,
              '/usr/lib/postgresql/17/bin/psql','--no-psqlrc','--no-readline','--quiet','--tuples-only','--no-align',
              '--set','ON_ERROR_STOP=1','--set','VERBOSITY=terse','--set','SHOW_CONTEXT=never','--host','/socket',
              '--username','flowzer_tt_demo','--dbname','flowzer_tt_demo','--password','--file','-']
    def _sql(self,sql):
        require(sql in self.sql_packets)
        self.pg.verify_existing_pg(running=True)
        result=self._raw(self._sql_arguments(self.pg.pg_cid),self.sql_packets[sql])
        require(result.exit_code==0 and result.stdout.strip()==b't' and result.stderr in (b'Password: ',b'Password: \n'))

    @port
    def pg_proof(self):
        """Least-privilege und lesende Historie mit tatsächlichem Runtime-Login prüfen."""
        require(self.state==('BOOTSTRAPPED' if self.mode=='initial' else 'PG_STARTED'))
        self._sql(ACL_SQL);self.state='PG_PROVED';return op.RoleProof(True,True,True)

    def _run(self,role):
        args=['container','run','--pull=never','--name',self._name(role),'--network',self.r.network_id if role=='api' else 'none',
              '--user',str(self.r.uid)+':'+str(self.r.gid),'--log-driver=none','--restart=no','--read-only','--cap-drop=ALL',
              '--security-opt=no-new-privileges=true','--ipc=private','--memory='+('768m' if role=='api' else '1024m'),'--cpus=1','--pids-limit=128',
              '--tmpfs','/tmp:rw,nosuid,nodev,noexec,size=64m,mode=1777','--interactive','--attach=stdin','--sig-proxy=false']
        if role=='migrate':args+=['--attach=stdout','--attach=stderr']
        for k,v in self._labels().items():args+=['--label',k+'='+v]
        for k,v in self._environment(role).items():args+=['--env',k+'='+v]
        args+=['--mount','type=volume,source='+self.pg.socket+',target=/socket,readonly']
        if role=='api':args+=['--mount','type=volume,source='+self.pg.keys+',target=/var/lib/flowzer/data-protection']
        return args+[self.r.api_image]+(['--migrate'] if role=='migrate' else [])+['--secrets-stdin']

    def _inspect_app(self,cid_or_name,role,running,nonce=None,*,timeout=60):
        # Docker27: leerer PidMode ist privat; das Literal 'private' ist ungueltig.
        v=json.loads(self._call(['container','inspect',cid_or_name,'--format',APP_FMT],timeout=timeout));require(type(v) is dict)
        require(re.fullmatch('[a-f0-9]{64}',v['id']) and (v['id']==cid_or_name or cid_or_name==self._name(role,nonce)))
        require(v['name']=='/'+self._name(role,nonce) and v['image']==self.r.api_image and v['labels']==dict(self.image_labels,**self._labels(nonce))
                and v['user']==str(self.r.uid)+':'+str(self.r.gid) and type(v['running']) is bool and v['running'] is running
                and type(v['exit']) is int and (role!='migrate' or v['exit']==0)
                and v['entrypoint']==['dotnet','WebApiEngine.dll'] and v['cmd']==(['--migrate','--secrets-stdin'] if role=='migrate' else ['--secrets-stdin'])
                and v['pidmode']=='' and v['ipcmode']=='private')
        require(v['network']==(self.r.network_id if role=='api' else 'none') and v['log']=='none' and v['restart']=='no'
                and v['ports'] in ({},None) and v['readonly'] is True and v['privileged'] is False
                and v['capdrop']==['ALL'] and v['capadd'] in ([],None) and v['security']==['no-new-privileges=true']
                and type(v['memory']) is int and v['memory']==(768 if role=='api' else 1024)*1024*1024
                and type(v['nanocpus']) is int and v['nanocpus']==1000000000 and type(v['pids']) is int and v['pids']==128
                and v['tmpfs']=={'/tmp':'rw,nosuid,nodev,noexec,size=64m,mode=1777'})
        networks=v['networks'];require(type(networks) is list and networks[-1:]==[None])
        if role=='api':
            require(len(networks)==2 and set(networks[0])=={'name','id'} and type(networks[0]['name']) is str and networks[0]['name']
                    and networks[0]['id']==self.r.network_id)
        else:
            require(networks==[None] or len(networks)==2 and set(networks[0])=={'name','id'} and networks[0]['name']=='none'
                    and type(networks[0]['id']) is str and (networks[0]['id']=='' or re.fullmatch('[a-f0-9]{64}',networks[0]['id'])))
        env=v['env'];require(type(env) is list and all(type(x) is str and '=' in x for x in env))
        parsed=dict(x.split('=',1) for x in env);require(len(parsed)==len(env) and parsed==self._environment(role))
        expected=[dict(type='volume',name=self.pg.socket,destination='/socket',rw=False)]
        if role=='api':expected.append(dict(type='volume',name=self.pg.keys,destination='/var/lib/flowzer/data-protection',rw=True))
        mounts=v['mounts'];require(type(mounts) is list and mounts[-1:]==[None] and len(mounts)==len(expected)+1)
        require(all(type(row) is dict and type(row.get('rw')) is bool for row in mounts[:-1]))
        require(sorted(mounts[:-1],key=lambda x:x.get('name',''))==sorted(expected,key=lambda x:x['name']))
        return v['id']

    @port
    def migrate(self,packet):
        """Ein terminaler Migratorlauf, dann tatsächliche Historie exakt001..022 lesen."""
        require(self.state=='PG_PROVED' and packet==self.expected_migration);self.pg.verify_existing_pg(running=True);self._image()
        self._call(self._run('migrate'),packet)
        self._inspect_app(self._name('migrate'),'migrate',False)
        self._sql(ACL_SQL);self._sql(HISTORY_SQL);self.state='MIGRATED';return op.MigrationProof(0,22,0)

    @port
    def api_start(self,packet):
        """Ein APIversuch mit frischer stdin-Lieferung, noch kein Readinesserfolg."""
        require(self.state=='MIGRATED' and packet==self.expected_api);self._network();self._keyring();self._image()
        raw=self._call(self._run('api'),packet);cid=raw.decode('ascii').strip();require(re.fullmatch('[a-f0-9]{64}',cid))
        self.api_cid=cid;self._inspect_app(cid,'api',True);self._network();self.state='API_STARTED';return cid

    def _http(self,path,timeout):
        args=['container','exec','--user',str(self.r.uid)+':'+str(self.r.gid),self.api_cid,'wget','--quiet','--server-response',
              '--max-redirect=0','--tries=1','--timeout=3','--output-document=-','http://127.0.0.1:8080'+path]
        result=self._raw(args,timeout=timeout)
        statuses=re.findall(rb'(?m)^\s*HTTP/[0-9.]+ ([0-9]{3})[^\r\n]*\r?$',result.stderr)
        require(len(statuses)<=1)
        if not statuses:
            require(result.exit_code==4);return 0,b''
        # GNU Wget meldet echte401-Challenges mit Exit6, nicht generischem8.
        # Der Exit allein ist nie Authbeleg: Status und Clientexit müssen exakt passen.
        status=int(statuses[0]);require((status,result.exit_code) in ((200,0),(401,6),(503,8)))
        return status,result.stdout

    @port
    def api_proof(self,cid):
        """Eigentum/Config und Readiness plus anonyme401 begrenzt tatsächlich prüfen."""
        require(self.state=='API_STARTED' and cid==self.api_cid);until=time.monotonic()+30
        while True:
            left=until-time.monotonic();require(left>0);self._inspect_app(cid,'api',True,timeout=left)
            left=until-time.monotonic();require(left>0)
            status,body=self._http('/health/ready',min(5,left))
            if status==200:break
            require(status in (0,503));time.sleep(min(.2,max(0,until-time.monotonic())))
        left=until-time.monotonic();require(left>0);self._inspect_app(cid,'api',True,timeout=left)
        left=until-time.monotonic();require(left>0);self._network(timeout=left)
        left=until-time.monotonic();require(left>0)
        anonymous,_=self._http('/Definition',min(5,left))
        proof=op.ApiProof(**ready.readiness_fields(body,http_status=status,anonymous_status=anonymous,owned=True))
        self.state='COMPLETE';self.close();return proof

    def close(self):
        """Nur eigene RAMpuffer leeren, keine entfernten Stop-/Remove-/Rollbackaktionen."""
        self.pg.close()
        for packet in [self.expected_api,self.expected_migration,*self.sql_packets.values()]:
            if packet is not None:packet[:]=b'\0'*len(packet)
        self.expected_api=None;self.expected_migration=None;self.sql_packets.clear();self.canaries.clear();self.bundle=None


if __name__=='__main__':print('FLOWZER_RUNTIME_ADAPTER_SOURCE_ONLY')
