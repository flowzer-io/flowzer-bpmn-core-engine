"""Hermetische Verträge der separaten Dev01-Demokonfiguration; kein Docker/Netz/Hoststart."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent


class DemoComposeTests(unittest.TestCase):
    def setUp(self):
        self.model = json.loads((ROOT / "compose.json").read_text())

    def test_only_four_owned_services_and_no_host_ports(self):
        # Testzweck: Keine fremden TT-/Proxy-Dienste übernehmen oder neue Hostports öffnen.
        self.assertEqual(set(self.model["services"]), {"postgres", "migrate", "api", "console"})
        self.assertTrue(all("ports" not in s and "build" not in s for s in self.model["services"].values()))

    def test_only_verified_api_console_digests(self):
        # Testzweck: Keine beweglichen Tags und kein erneuter Imagebau für die Installation.
        services = self.model["services"]
        self.assertEqual(services["api"]["image"], "ghcr.io/flowzer-io/flowzer-api@sha256:9994c7e309fab08f649b21cba44b13249c1eb28d474bcb4a73aa0a7a56552273")
        self.assertEqual(services["migrate"]["image"], services["api"]["image"])
        self.assertEqual(services["console"]["image"], "ghcr.io/flowzer-io/flowzer-console@sha256:fa5f1b80adcc8088108d6f0d639d66106fa8f2b4b4651d1ae2e069b838f7618a")
        self.assertIn("postgres:17@sha256:${FLOWZER_DEMO_POSTGRES_SHA256:?", services["postgres"]["image"])

    def test_database_is_internal_and_not_shared_with_proxy(self):
        # Testzweck: PostgreSQL bleibt ausschließlich im eigenen internen Datenbanknetz.
        self.assertTrue(self.model["networks"]["database"]["internal"])
        self.assertEqual(self.model["services"]["postgres"]["networks"], ["database"])
        self.assertEqual(self.model["services"]["console"]["networks"], ["edge"])
        self.assertEqual(set(self.model["services"]["api"]["networks"]), {"database", "edge"})
        self.assertNotIn("external", self.model["networks"]["edge"])
        self.assertIn("FLOWZER_DEMO_EDGE_CIDR:?", self.model["networks"]["edge"]["ipam"]["config"][0]["subnet"])

    def test_existing_gateway_is_used_without_socket_or_proxy_volumes(self):
        # Testzweck: Kein zusätzlicher Gateway und keine Docker-/Zertifikatsrechte im Appcontainer.
        text = json.dumps(self.model)
        self.assertNotIn("docker.sock", text)
        self.assertNotIn("nginx-certs", text)
        self.assertNotIn("ttnext-demo", text)
        env = self.model["services"]["console"]["environment"]
        self.assertEqual(env["VIRTUAL_HOST"], "flowzer.tickytask.de")
        self.assertEqual(env["LETSENCRYPT_HOST"], env["VIRTUAL_HOST"])
        self.assertEqual(env["FLOWZER_API_UPSTREAM"], "api:8080")

    def test_auth_and_directory_are_explicit_and_scoped(self):
        # Testzweck: Gemeinsamer Realm mit zwingender Demo-Wurzel, keine permissiven Authdefaults.
        env = self.model["services"]["api"]["environment"]
        self.assertEqual(env["Authentication__Scheme"], "Bff")
        self.assertEqual(env["Authentication__JwtBearer__RequireHttpsMetadata"], "true")
        self.assertEqual(env["Authentication__JwtBearer__LegacyPermissiveRoles"], "false")
        self.assertEqual(env["IdentityDirectory__Realm"], "TickyTask")
        self.assertIn("ROOT_GROUP_ID:?", env["IdentityDirectory__RootGroupId"])
        self.assertIn("FLOWZER_DEMO_EDGE_CIDR:?", env["ForwardedHeaders__KnownNetworks__0"])

    def test_embedding_has_exact_origins_and_no_relaxation(self):
        # Testzweck: Renderer-/API-Origins bleiben exakt auf den einen TT-Demohost gebunden.
        api = self.model["services"]["api"]["environment"]
        console = self.model["services"]["console"]["environment"]
        self.assertEqual(api["FormEmbedding__PublicOrigin"], "https://flowzer.tickytask.de")
        self.assertEqual(api["FormEmbedding__AllowedHostOrigins__0"], "https://demo.tickytask.de")
        self.assertEqual(console["FLOWZER_EMBED_API_ORIGIN"], api["FormEmbedding__PublicOrigin"])
        self.assertEqual(console["FLOWZER_EMBED_HOST_ORIGINS"], api["FormEmbedding__AllowedHostOrigins__0"])

    def test_only_api_has_bff_and_directory_credentials(self):
        # Testzweck: Console/PG/Migrator bekommen keine BFF-/Directorysecrets oder freie env_file.
        for name, service in self.model["services"].items():
            self.assertNotIn("env_file", service)
            if name != "api":
                self.assertNotIn("Authentication__Bff__ClientSecret", service.get("environment", {}))
                self.assertNotIn("IdentityDirectory__ClientSecret", service.get("environment", {}))
        self.assertNotIn("Storage__PostgreSql__MigrationConnectionString", self.model["services"]["api"]["environment"])
        self.assertIn("FLOWZER_DEMO_POSTGRES_ADMIN_PASSWORD:?", self.model["services"]["postgres"]["environment"]["POSTGRES_PASSWORD"])
        self.assertNotIn("secrets", self.model)

    def test_migration_is_explicit_and_keyring_persistent(self):
        # Testzweck: Kein automatisches DDL beim API-Start, eigener persistenter Keyring.
        self.assertEqual(self.model["services"]["migrate"]["profiles"], ["operations"])
        self.assertEqual(self.model["services"]["migrate"]["command"], ["--migrate"])
        self.assertEqual(self.model["services"]["api"]["volumes"], ["keyring:/var/lib/flowzer/data-protection"])
        self.assertEqual(set(self.model["volumes"]), {"postgres_data", "keyring"})

    def test_external_effects_and_ticket_worker_phase_remain_closed(self):
        # Testzweck: Der erste Human-Task-Durchstich aktiviert keine fremden Effekte/Ticketaktionen.
        env = self.model["services"]["api"]["environment"]
        for key in ["Ai__AllowCloudProviders", "Ai__AllowLocalEndpoints", "AiExecution__Enabled", "Connectors__Http__Enabled", "Connectors__Email__Enabled", "ServiceTaskWebhooks__Enabled"]:
            self.assertEqual(env[key], "false")
        self.assertEqual(env["TickyTaskTicketActions__ApiClientId"], "")

    def test_caps_are_explicit_not_a_claim_of_capacity(self):
        # Testzweck: Ressourcenobergrenzen sind feste Kandidatenwerte, keine Hostreservebehauptung.
        self.assertEqual(self.model["services"]["api"]["mem_limit"], "768m")
        self.assertEqual(self.model["services"]["postgres"]["mem_limit"], "512m")
        self.assertEqual(self.model["services"]["console"]["mem_limit"], "128m")
        self.assertEqual(self.model["services"]["migrate"]["mem_limit"], "1024m")
        for name, expected in {"postgres": 0.5, "api": 1.0, "console": 0.25, "migrate": 1.0}.items():
            self.assertEqual(self.model["services"][name]["cpus"], expected)


if __name__ == "__main__":
    unittest.main()
