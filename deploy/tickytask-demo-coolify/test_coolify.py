"""Coolify-Demo-Verträge: nur Source und synthetische Compose-Interpolation, kein Daemon/Netz."""
import copy
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent
MASK = "__FLOWZER_NOT_FOR_THIS_SERVICE__"
ALLOWED = {
    "postgres": {"FLOWZER_DEMO_POSTGRES_ADMIN_PASSWORD"},
    "migrate": {"FLOWZER_DEMO_MIGRATION_CONNECTION_STRING"},
    "api": {"FLOWZER_DEMO_RUNTIME_CONNECTION_STRING", "FLOWZER_DEMO_BFF_CLIENT_SECRET", "FLOWZER_DEMO_DIRECTORY_CLIENT_SECRET"},
    "console": set(),
}
ALIASES = set().union(*ALLOWED.values())


def project_through_compose(model, omit=None):
    """Modelliert die zwei dokumentierten Coolify-Parser-Schritte plus echtes Compose.

    Kein vollständiger Coolify-Nachbau: nur zusätzliche Referenzalias-Injektion,
    Leerwertersetzung und gemeinsame env_file. Ausschließlich synthetische Werte,
    eigener HOME/DOCKER_CONFIG, kein SDK, Container, Secrethelper oder Netzwerk.
    """
    model = copy.deepcopy(model)
    required = set(re.findall(r"\$\{([A-Z0-9_]+):\?", json.dumps(model)))
    values = {key: "synthetic-" + key for key in required | ALIASES}
    values["FLOWZER_DEMO_TRUSTED_PROXY_CIDR"] = "172.30.77.0/24"  # Nur Testfixture, kein Hostvorschlag.
    if omit:
        del values[omit]
    with tempfile.TemporaryDirectory(prefix="flowzer-coolify-contract-") as directory:
        base = Path(directory)
        shared = base / "synthetic.env"
        shared.write_text("\n".join(f"{key}={values[key]}" for key in ALIASES if key in values))
        shared.chmod(0o600)
        home = base / "home"
        home.mkdir(mode=0o700)
        for service in model["services"].values():
            environment = service["environment"]
            for key, value in list(environment.items()):
                match = re.fullmatch(r"\$\{([A-Z0-9_]+)(?::[-?].*)?\}", value)
                if match and match[1] != key and match[1] in values:
                    environment[match[1]] = values[match[1]]
            for key, value in list(environment.items()):
                if value == "" and key in values:
                    environment[key] = values[key]
            service["env_file"] = [str(shared)]
        environment = {"PATH": os.environ["PATH"], "HOME": str(home),
                       "DOCKER_CONFIG": str(home), "DOCKER_HOST": "unix://" + str(base / "no-daemon.sock"), **values}
        result = subprocess.run(["docker", "compose", "--project-directory", directory,
                                 "--env-file", "/dev/null", "--profile", "operations", "-f", "-", "config", "--format", "json"],
                                input=json.dumps(model), text=True, capture_output=True, env=environment, timeout=30)
        if result.returncode:
            raise AssertionError("Synthetische Compose-Konfiguration abgewiesen: " + result.stderr)
        return json.loads(result.stdout)["services"], values


class CoolifyDemoContracts(unittest.TestCase):
    def setUp(self):
        self.model = json.loads((ROOT / "compose.json").read_text())

    def test_only_own_four_services_no_hostports_builds_or_manual_routes(self):
        # Testzweck: Neuer isolierter Stack, keine bestehenden Container/Proxyregeln übernehmen.
        self.assertEqual(set(self.model["services"]), set(ALLOWED))
        for service in self.model["services"].values():
            for key in ("ports", "build", "container_name", "labels", "env_file"):
                self.assertNotIn(key, service)
        self.assertNotIn("name", self.model)

    def test_digest_pins_reuse_existing_images_and_verified_pg17_index(self):
        # Testzweck: Keine beweglichen Tags, kein neuer Publisher und keine emulierte Zielplattform.
        services = self.model["services"]
        self.assertEqual(services["api"]["image"], "ghcr.io/flowzer-io/flowzer-api@sha256:9994c7e309fab08f649b21cba44b13249c1eb28d474bcb4a73aa0a7a56552273")
        self.assertEqual(services["migrate"]["image"], services["api"]["image"])
        self.assertEqual(services["console"]["image"], "ghcr.io/flowzer-io/flowzer-console@sha256:fa5f1b80adcc8088108d6f0d639d66106fa8f2b4b4651d1ae2e069b838f7618a")
        self.assertEqual(services["postgres"]["image"], "docker.io/library/postgres:17@sha256:2d2b8998d31037bf721cfdf764d76ba74171b4fab3431b7f72c27c56ddbdf9e3")
        self.assertTrue(all("platform" not in service for service in services.values()))

    def test_new_resource_network_and_volumes_not_shared_destination(self):
        # Testzweck: Coolify besitzt die neuen UUID-Namen; kein globales/fremdes Netz oder Volume.
        self.assertEqual(self.model["networks"], {"default": {}})
        self.assertEqual(self.model["volumes"], {"postgres_data": {}, "keyring": {}})
        for service in self.model["services"].values():
            self.assertEqual(service["networks"], ["default"])
        self.assertEqual(self.model["services"]["postgres"]["volumes"], ["postgres_data:/var/lib/postgresql/data"])
        self.assertEqual(self.model["services"]["api"]["volumes"], ["keyring:/var/lib/flowzer/data-protection"])

    def test_all_five_secret_aliases_have_nonempty_masks(self):
        # Testzweck: Shared env_file darf auch den neuen PG-Adminzugang nicht fremd verteilen.
        for service in self.model["services"].values():
            for alias in ALIASES:
                self.assertEqual(service["environment"][alias], MASK)

    def test_actual_compose_and_synthetic_coolify_keep_service_secret_boundaries(self):
        # Testzweck: Alias-Injektion/Leerwertersetzung plus echtes Compose prüfen alle 20 Grenzen.
        services, values = project_through_compose(self.model)
        for name, service in services.items():
            environment = service["environment"]
            for alias in ALIASES:
                self.assertEqual(environment[alias], values[alias] if alias in ALLOWED[name] else MASK)
                if alias not in ALLOWED[name]:
                    self.assertNotIn(values[alias], environment.values())
        self.assertEqual(services["postgres"]["environment"]["POSTGRES_PASSWORD"], values["FLOWZER_DEMO_POSTGRES_ADMIN_PASSWORD"])
        self.assertEqual(services["migrate"]["environment"]["Storage__PostgreSql__MigrationConnectionString"], values["FLOWZER_DEMO_MIGRATION_CONNECTION_STRING"])
        api = services["api"]["environment"]
        self.assertEqual(api["Storage__PostgreSql__ConnectionString"], values["FLOWZER_DEMO_RUNTIME_CONNECTION_STRING"])
        self.assertEqual(api["Authentication__Bff__ClientSecret"], values["FLOWZER_DEMO_BFF_CLIENT_SECRET"])
        self.assertEqual(api["IdentityDirectory__ClientSecret"], values["FLOWZER_DEMO_DIRECTORY_CLIENT_SECRET"])
        self.assertNotIn("Storage__PostgreSql__MigrationConnectionString", api)

    def test_empty_foreign_mask_really_leaks_in_modeled_coolify_parser(self):
        # Testzweck: Negativfixture beweist, warum eine leere Maskierung keine Alternative ist.
        model = copy.deepcopy(self.model)
        alias = "FLOWZER_DEMO_POSTGRES_ADMIN_PASSWORD"
        model["services"]["console"]["environment"][alias] = ""
        services, values = project_through_compose(model)
        self.assertEqual(services["console"]["environment"][alias], values[alias])

    def test_missing_runtime_credential_is_rejected(self):
        # Testzweck: Kein verdeckter Runtimezugang aus fremder ENV/Datei bei fehlender Pflichtvariable.
        with self.assertRaisesRegex(AssertionError, "FLOWZER_DEMO_RUNTIME_CONNECTION_STRING"):
            project_through_compose(self.model, omit="FLOWZER_DEMO_RUNTIME_CONNECTION_STRING")

    def test_missing_proxy_network_is_rejected_before_runtime(self):
        # Testzweck: Ohne tatsächlich gebundenen eigenen Netz-CIDR bleibt der API-Start geschlossen.
        with self.assertRaisesRegex(AssertionError, "FLOWZER_DEMO_TRUSTED_PROXY_CIDR"):
            project_through_compose(self.model, omit="FLOWZER_DEMO_TRUSTED_PROXY_CIDR")

    def test_strict_identity_origins_and_new_network_trust(self):
        # Testzweck: Keine Übernahme fremder Realmrechte oder pauschaler Proxy-Vertrauensnetze.
        env = self.model["services"]["api"]["environment"]
        self.assertEqual(env["Authentication__Scheme"], "Bff")
        self.assertEqual(env["Authentication__JwtBearer__RequireHttpsMetadata"], "true")
        self.assertEqual(env["Authentication__JwtBearer__LegacyPermissiveRoles"], "false")
        self.assertEqual(env["IdentityDirectory__Realm"], "TickyTask")
        self.assertIn("ROOT_GROUP_ID:?", env["IdentityDirectory__RootGroupId"])
        self.assertIn("FLOWZER_DEMO_TRUSTED_PROXY_CIDR:?", env["ForwardedHeaders__KnownNetworks__0"])
        self.assertEqual(env["ForwardedHeaders__ForwardLimit"], "2")
        self.assertEqual(env["FormEmbedding__PublicOrigin"], "https://flowzer.tickytask.de")
        self.assertEqual(env["FormEmbedding__AllowedHostOrigins__0"], "https://demo.tickytask.de")
        console = self.model["services"]["console"]["environment"]
        self.assertEqual(console["FLOWZER_EMBED_API_ORIGIN"], env["FormEmbedding__PublicOrigin"])
        self.assertEqual(console["FLOWZER_EMBED_HOST_ORIGINS"], env["FormEmbedding__AllowedHostOrigins__0"])

    def test_manual_migration_and_effect_gates_stay_closed(self):
        # Testzweck: Explizite DDL-Identität, kein automatischer Start von Ticket-/Außeneffekten.
        services = self.model["services"]
        self.assertEqual(services["migrate"]["profiles"], ["operations"])
        self.assertEqual(services["migrate"]["command"], ["--migrate"])
        self.assertEqual(services["migrate"]["restart"], "no")
        env = services["api"]["environment"]
        for key in ("Ai__AllowCloudProviders", "Ai__AllowLocalEndpoints", "AiExecution__Enabled", "Connectors__Http__Enabled", "Connectors__Email__Enabled", "ServiceTaskWebhooks__Enabled"):
            self.assertEqual(env[key], "false")
        self.assertEqual(env["TickyTaskTicketActions__ApiClientId"], "")
        self.assertEqual(env["InboundTriggers__SecretKey"], "")

    def test_all_resource_caps_remain_explicit(self):
        # Testzweck: Kein Verlust der festen CPU-/RAM-Obergrenzen beim Coolify-Umzug.
        for name, memory, cpu in (("postgres", "512m", .5), ("api", "768m", 1), ("console", "128m", .25), ("migrate", "1024m", 1)):
            self.assertEqual(self.model["services"][name]["mem_limit"], memory)
            self.assertEqual(self.model["services"][name]["cpus"], cpu)


if __name__ == "__main__":
    unittest.main()
