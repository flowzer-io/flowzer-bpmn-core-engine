"""Testzweck: wirklicher Health-API-Vertrag, keine HTTP-/Docker-/DB-Aufrufe."""
import importlib.util,json,unittest
from pathlib import Path
s=importlib.util.spec_from_file_location('readiness',Path(__file__).with_name('manual_readiness.py'));m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
class ReadinessTests(unittest.TestCase):
    def fixture(self):return dict(successful=True,result=dict(status='Healthy',storage='Ready',details=dict(storageProvider='PostgreSQL (schema flowzer)',migrationState='UpToDate',pendingMigrationCount=0,expectedMigrationVersion=22,expressionEngine='Feel')))
    def parse(self,value=None,**changes):
        return m.readiness_fields(json.dumps(value or self.fixture()).encode(),**dict(dict(http_status=200,anonymous_status=401,owned=True),**changes))
    def test_contract_state_is_bound_to_current_health_producer(self):
        # Testzweck: kein erfundener Status in lauter gegenseitig bestätigenden Fakes.
        producer=Path(__file__).resolve().parents[3]/'src/WebApiEngine/Controller/HealthController.cs'
        self.assertIn('status.IsUpToDate ? "UpToDate" : "Pending"',producer.read_text())
        self.assertEqual(self.fixture()['result']['details']['migrationState'],'UpToDate')
    def test_actual_complete_current_postgres_feel_contract(self):
        # Testzweck: nur echte positive API-/ACL-Fakten ergeben das Sequenzproof.
        v=self.parse();self.assertEqual((v['head'],v['pending'],v['expression_engine'],v['anonymous_status']),(22,0,'Feel',401))
    def test_status_and_ownership_are_not_inferred_from_a_body(self):
        # Testzweck: Body allein beweist weder Containerhoheit noch Authfailclosed.
        for changes in [dict(http_status=302),dict(http_status=503),dict(anonymous_status=200),dict(anonymous_status=302),dict(owned=False),dict(http_status=True)]:
            with self.subTest(changes=changes),self.assertRaises(m.ReadinessFailure):self.parse(**changes)
    def test_pending_stale_head_and_expression_fallback_stop(self):
        # Testzweck: Migratorhead20/fehlendes natives FEEL darf nie Readiness22 ersetzen.
        for k,v in [('storageProvider','memory'),('migrationState','Pending'),('pendingMigrationCount',1),('pendingMigrationCount',False),('expectedMigrationVersion',20),('expressionEngine','Simple')]:
            d=self.fixture();d['result']['details'][k]=v
            with self.subTest(k=k),self.assertRaises(m.ReadinessFailure):self.parse(d)
    def test_missing_malformed_duplicate_or_oversize_body_has_fixed_error(self):
        # Testzweck: kein Fehlertext/privater Raw-Body darf den Parser verlassen.
        for raw in [b'RAW_PRIVATE',b'{"successful":true,"successful":false}',b'x'*65537,b'{}']:
            with self.subTest(size=len(raw)),self.assertRaises(m.ReadinessFailure) as e:m.readiness_fields(raw,http_status=200,anonymous_status=401,owned=True)
            self.assertEqual(str(e.exception),'FLOWZER_READINESS_UNPROVEN')
    def test_unknown_technical_details_are_not_returned(self):
        # Testzweck: positive Ausgabe enthält nur die sichere endliche Proofstruktur.
        d=self.fixture();d['result']['details']['private']='SYNTHETIC_PRIVATE'
        self.assertNotIn('SYNTHETIC_PRIVATE',repr(self.parse(d)))
if __name__=='__main__':unittest.main()
