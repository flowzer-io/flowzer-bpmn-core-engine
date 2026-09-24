-- Fixture des Upgrade-/Restore-Rigs (R2b): Schema flowzer auf Migrationsstand 016.
--
-- Erzeugt von tests/upgrade-restore/fixtures/make-schema-016.sh am 2026-09-24 mit der API
-- aus Commit 92d85573f8e215eceee3c1d280575cd41bfedbff (Release #320), PostgreSQL 17.11,
-- pg_dump (PostgreSQL) 17.11; pg_dump --schema=flowzer --no-owner --no-privileges.
-- Inhalt: Formular UpgradeApproval, Workflows Upgrade_Review, Upgrade_Service, Upgrade_Timer
-- (tests/upgrade-restore/bpmn) und je eine wartende Instanz:
--   Aufgabe  0640c341-b335-43ac-b531-afe1f8d5f714
--   Auftrag  de7d0363-2c55-4c9b-adba-bd536c4efc16
--   Timer    88be15a3-d902-4170-9043-28e482c9f5fe (Frist abgelaufen, Scheduler war aus)
-- Keine Rollen, Rechte oder Passwoerter: Eigentuemerin wird, wer einspielt (im Rig die
-- Migrationsrolle); die Laufzeitrechte setzt danach deploy/postgresql/02-laufzeitrechte.sql.
--
--
-- PostgreSQL database dump
--

\restrict VtM4zVDT1prjutoNMKGh6sQsKCNJLShSyJYZ0hChzwGoe93ckqaYrD4Tsa1hgpw

-- Dumped from database version 17.11
-- Dumped by pg_dump version 17.11

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: flowzer; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA flowzer;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: ai_connection_revisions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.ai_connection_revisions (
    id uuid NOT NULL,
    revision bigint NOT NULL,
    body text NOT NULL,
    CONSTRAINT ai_connection_revisions_revision_check CHECK ((revision > 0))
);


--
-- Name: ai_connections; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.ai_connections (
    id uuid NOT NULL,
    name text NOT NULL,
    revision bigint NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    secret_reference text NOT NULL,
    body text NOT NULL,
    CONSTRAINT ai_connections_revision_check CHECK ((revision > 0))
);


--
-- Name: ai_runs; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.ai_runs (
    id uuid NOT NULL,
    process_instance_id uuid NOT NULL,
    token_id uuid NOT NULL,
    status smallint NOT NULL,
    attempt integer NOT NULL,
    maximum_attempts integer NOT NULL,
    revision bigint NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    next_attempt_at timestamp with time zone,
    lease_owner text,
    lease_expires_at timestamp with time zone,
    provider_call_started_at timestamp with time zone,
    output_json text,
    result_model text,
    input_tokens integer,
    output_tokens integer,
    total_tokens integer,
    failure_code text,
    body text NOT NULL,
    CONSTRAINT ai_runs_attempt_check CHECK ((attempt >= 0)),
    CONSTRAINT ai_runs_input_tokens_check CHECK (((input_tokens IS NULL) OR (input_tokens >= 0))),
    CONSTRAINT ai_runs_lease_complete CHECK ((((lease_owner IS NULL) AND (lease_expires_at IS NULL)) OR ((lease_owner IS NOT NULL) AND (lease_expires_at IS NOT NULL)))),
    CONSTRAINT ai_runs_maximum_attempts_check CHECK (((maximum_attempts >= 1) AND (maximum_attempts <= 100))),
    CONSTRAINT ai_runs_output_tokens_check CHECK (((output_tokens IS NULL) OR (output_tokens >= 0))),
    CONSTRAINT ai_runs_revision_check CHECK ((revision > 0)),
    CONSTRAINT ai_runs_status_check CHECK (((status >= 0) AND (status <= 7))),
    CONSTRAINT ai_runs_total_tokens_check CHECK (((total_tokens IS NULL) OR (total_tokens >= 0)))
);


--
-- Name: definition_binaries; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.definition_binaries (
    id uuid NOT NULL,
    xml text NOT NULL
);


--
-- Name: definitions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.definitions (
    id uuid NOT NULL,
    definition_id text NOT NULL,
    is_active boolean NOT NULL,
    version_major integer NOT NULL,
    version_minor integer NOT NULL,
    saved_on timestamp with time zone NOT NULL,
    body text NOT NULL
);


--
-- Name: form_authoring_drafts; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.form_authoring_drafts (
    form_id uuid NOT NULL,
    revision bigint NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    body text NOT NULL,
    CONSTRAINT form_authoring_drafts_revision_check CHECK ((revision > 0))
);


--
-- Name: form_folders; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.form_folders (
    id uuid NOT NULL,
    parent_id uuid,
    name text NOT NULL,
    body text NOT NULL
);


--
-- Name: form_metadata; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.form_metadata (
    form_id uuid NOT NULL,
    body text NOT NULL
);


--
-- Name: form_section_authoring_drafts; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.form_section_authoring_drafts (
    section_id uuid NOT NULL,
    revision bigint NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    body text NOT NULL,
    CONSTRAINT form_section_authoring_drafts_revision_check CHECK ((revision > 0))
);


--
-- Name: form_section_metadata; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.form_section_metadata (
    section_id uuid NOT NULL,
    name text NOT NULL,
    body text NOT NULL
);


--
-- Name: form_section_versions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.form_section_versions (
    id uuid NOT NULL,
    section_id uuid NOT NULL,
    version_major integer NOT NULL,
    version_minor integer NOT NULL,
    body text NOT NULL
);


--
-- Name: forms; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.forms (
    id uuid NOT NULL,
    form_id uuid NOT NULL,
    version_major integer NOT NULL,
    version_minor integer NOT NULL,
    body text NOT NULL
);


--
-- Name: idempotency_records; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.idempotency_records (
    scope_hash text NOT NULL,
    request_hash text NOT NULL,
    operation text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    is_completed boolean NOT NULL,
    process_instance_id uuid
);


--
-- Name: identity_directory_state; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.identity_directory_state (
    singleton boolean DEFAULT true NOT NULL,
    active_snapshot text,
    sync_status text,
    CONSTRAINT identity_directory_state_singleton_check CHECK (singleton)
);


--
-- Name: instances; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.instances (
    instance_id uuid NOT NULL,
    meta_definition_id text NOT NULL,
    is_finished boolean NOT NULL,
    body text NOT NULL
);


--
-- Name: message_subscriptions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.message_subscriptions (
    id uuid NOT NULL,
    related_definition_id text NOT NULL,
    process_instance_id uuid,
    message_name text NOT NULL,
    correlation_key text,
    body text NOT NULL
);


--
-- Name: meta_definitions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.meta_definitions (
    definition_id text NOT NULL,
    body text NOT NULL
);


--
-- Name: runtime_node_events; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.runtime_node_events (
    id uuid NOT NULL,
    process_instance_id uuid NOT NULL,
    definition_id uuid NOT NULL,
    token_id uuid NOT NULL,
    flow_node_id text NOT NULL,
    node_state smallint NOT NULL,
    correlation_id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    body text NOT NULL
);


--
-- Name: schema_migrations; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.schema_migrations (
    version integer NOT NULL,
    name text NOT NULL,
    applied_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: service_task_jobs; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.service_task_jobs (
    id uuid NOT NULL,
    type text NOT NULL,
    process_instance_id uuid NOT NULL,
    token_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    locked_until timestamp with time zone,
    locked_by text,
    retry_at timestamp with time zone,
    retries integer NOT NULL,
    last_error text,
    body text NOT NULL
);


--
-- Name: service_task_webhooks; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.service_task_webhooks (
    id uuid NOT NULL,
    type text NOT NULL,
    body text NOT NULL
);


--
-- Name: signal_subscriptions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.signal_subscriptions (
    id uuid NOT NULL,
    related_definition_id text NOT NULL,
    process_instance_id uuid,
    signal_name text NOT NULL,
    body text NOT NULL
);


--
-- Name: timer_subscriptions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.timer_subscriptions (
    id uuid NOT NULL,
    related_definition_id text NOT NULL,
    process_instance_id uuid,
    due_at timestamp with time zone NOT NULL,
    body text NOT NULL
);


--
-- Name: user_task_assignment_events; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.user_task_assignment_events (
    id uuid NOT NULL,
    user_task_id uuid NOT NULL,
    revision bigint NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    body text NOT NULL,
    process_instance_id uuid,
    CONSTRAINT user_task_assignment_events_revision_check CHECK ((revision > 0))
);


--
-- Name: user_task_deadlines; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.user_task_deadlines (
    user_task_id uuid NOT NULL,
    revision bigint NOT NULL,
    next_check_at timestamp with time zone,
    body text NOT NULL,
    CONSTRAINT user_task_deadlines_revision_check CHECK ((revision > 0))
);


--
-- Name: user_task_drafts; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.user_task_drafts (
    user_task_id uuid NOT NULL,
    owner_key text NOT NULL,
    owner_user_id uuid NOT NULL,
    token_id uuid NOT NULL,
    process_instance_id uuid NOT NULL,
    definition_id uuid NOT NULL,
    revision bigint NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    body text NOT NULL,
    CONSTRAINT user_task_drafts_owner_key_check CHECK ((length(owner_key) = 64)),
    CONSTRAINT user_task_drafts_revision_check CHECK ((revision > 0))
);


--
-- Name: user_task_notification_reads; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.user_task_notification_reads (
    notification_id uuid NOT NULL,
    owner_key character(64) NOT NULL,
    read_at timestamp with time zone NOT NULL
);


--
-- Name: user_task_notifications; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.user_task_notifications (
    id uuid NOT NULL,
    user_task_id uuid NOT NULL,
    kind text NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    deduplication_key text NOT NULL,
    body text NOT NULL
);


--
-- Name: user_task_subscriptions; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.user_task_subscriptions (
    id uuid NOT NULL,
    related_definition_id text NOT NULL,
    process_instance_id uuid,
    body text NOT NULL
);


--
-- Name: user_task_work_states; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.user_task_work_states (
    user_task_id uuid NOT NULL,
    revision bigint NOT NULL,
    body text NOT NULL,
    CONSTRAINT user_task_work_states_revision_check CHECK ((revision > 0))
);


--
-- Name: workflow_folders; Type: TABLE; Schema: flowzer; Owner: -
--

CREATE TABLE flowzer.workflow_folders (
    id uuid NOT NULL,
    parent_id uuid,
    name text NOT NULL,
    body text NOT NULL
);


--
-- Data for Name: ai_connection_revisions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.ai_connection_revisions (id, revision, body) FROM stdin;
\.


--
-- Data for Name: ai_connections; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.ai_connections (id, name, revision, updated_at, secret_reference, body) FROM stdin;
\.


--
-- Data for Name: ai_runs; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.ai_runs (id, process_instance_id, token_id, status, attempt, maximum_attempts, revision, created_at, updated_at, next_attempt_at, lease_owner, lease_expires_at, provider_call_started_at, output_json, result_model, input_tokens, output_tokens, total_tokens, failure_code, body) FROM stdin;
\.


--
-- Data for Name: definition_binaries; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.definition_binaries (id, xml) FROM stdin;
44e805a6-ac34-4340-be88-c07fbeb0bcfe	<?xml version="1.0" encoding="UTF-8"?>\n<!-- Upgrade-/Restore-Rig: wartende Benutzeraufgabe mit gebundenem Formular (UpgradeApproval). -->\n<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"\n                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"\n                  id="Upgrade_Review" targetNamespace="http://bpmn.io/schema/bpmn">\n  <bpmn:process id="Process_UpgradeReview" name="Upgrade Freigabe" isExecutable="true">\n    <bpmn:startEvent id="StartEvent_1"><bpmn:outgoing>Flow_1</bpmn:outgoing></bpmn:startEvent>\n    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="UserTask_1" />\n    <bpmn:userTask id="UserTask_1" name="Freigeben">\n      <bpmn:extensionElements><zeebe:formDefinition formKey="UpgradeApproval" /></bpmn:extensionElements>\n      <bpmn:incoming>Flow_1</bpmn:incoming><bpmn:outgoing>Flow_2</bpmn:outgoing>\n    </bpmn:userTask>\n    <bpmn:sequenceFlow id="Flow_2" sourceRef="UserTask_1" targetRef="EndEvent_1" />\n    <bpmn:endEvent id="EndEvent_1"><bpmn:incoming>Flow_2</bpmn:incoming></bpmn:endEvent>\n  </bpmn:process>\n</bpmn:definitions>\n
ce3cda8a-41c5-494d-84e4-aa8ee1622dcb	<?xml version="1.0" encoding="UTF-8"?>\n<!-- Upgrade-/Restore-Rig: wartender Auftrag fuer einen externen Worker (Typ upgrade-zahlung). -->\n<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"\n                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"\n                  id="Upgrade_Service" targetNamespace="http://bpmn.io/schema/bpmn">\n  <bpmn:process id="Process_UpgradeService" name="Upgrade Zahlung" isExecutable="true">\n    <bpmn:startEvent id="StartEvent_1"><bpmn:outgoing>Flow_1</bpmn:outgoing></bpmn:startEvent>\n    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />\n    <bpmn:serviceTask id="ServiceTask_1" name="Zahlung ausloesen">\n      <bpmn:extensionElements><zeebe:taskDefinition type="upgrade-zahlung" retries="3" /></bpmn:extensionElements>\n      <bpmn:incoming>Flow_1</bpmn:incoming><bpmn:outgoing>Flow_2</bpmn:outgoing>\n    </bpmn:serviceTask>\n    <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />\n    <bpmn:endEvent id="EndEvent_1"><bpmn:incoming>Flow_2</bpmn:incoming></bpmn:endEvent>\n  </bpmn:process>\n</bpmn:definitions>\n
fab1063a-1547-4978-8c05-e3fa270d744a	<?xml version="1.0" encoding="UTF-8"?>\n<!-- Upgrade-/Restore-Rig: wartender Zwischen-Timer. Die kurze Frist ist Absicht: Der Rig laesst\n     den Timer-Scheduler zunaechst aus, der Timer wartet also, obwohl er bereits faellig ist, und\n     feuert beim ersten Start mit eingeschaltetem Scheduler. -->\n<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"\n                  id="Upgrade_Timer" targetNamespace="http://bpmn.io/schema/bpmn">\n  <bpmn:process id="Process_UpgradeTimer" name="Upgrade Frist" isExecutable="true">\n    <bpmn:startEvent id="StartEvent_1"><bpmn:outgoing>Flow_1</bpmn:outgoing></bpmn:startEvent>\n    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="TimerCatch_1" />\n    <bpmn:intermediateCatchEvent id="TimerCatch_1" name="Frist">\n      <bpmn:incoming>Flow_1</bpmn:incoming><bpmn:outgoing>Flow_2</bpmn:outgoing>\n      <bpmn:timerEventDefinition id="TimerDefinition_1">\n        <bpmn:timeDuration>PT2S</bpmn:timeDuration>\n      </bpmn:timerEventDefinition>\n    </bpmn:intermediateCatchEvent>\n    <bpmn:sequenceFlow id="Flow_2" sourceRef="TimerCatch_1" targetRef="EndEvent_1" />\n    <bpmn:endEvent id="EndEvent_1"><bpmn:incoming>Flow_2</bpmn:incoming></bpmn:endEvent>\n  </bpmn:process>\n</bpmn:definitions>\n
\.


--
-- Data for Name: definitions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.definitions (id, definition_id, is_active, version_major, version_minor, saved_on, body) FROM stdin;
44e805a6-ac34-4340-be88-c07fbeb0bcfe	Upgrade_Review	t	1	0	2026-09-24 11:50:18.949084+00	{"Id":"44e805a6-ac34-4340-be88-c07fbeb0bcfe","DefinitionId":"Upgrade_Review","PreviousGuid":null,"Hash":"5DEF4D0D283CBEA00D76366B80D8C5D5E00DD6B59A97DF3EA9F6D6F34B824EB0","SavedByUser":"5a1e0b2c-4d3f-4e21-9a87-0c6b2d4e8f10","SavedOn":"2026-09-24T11:50:18.9490842Z","DeployedByUser":null,"DeployedOn":"2026-09-24T11:50:19.0056481Z","Version":{"Major":1,"Minor":0},"IsActive":true,"FormBindings":{"UpgradeApproval":{"Id":"7a31cabb-c300-483f-bb8d-b46b834d3f6f","FormId":"dad83a8c-6672-41e3-8237-e84c1f9002e1","Version":"0.1","FormData":"{\\"components\\":[{\\"label\\":\\"Bemerkung\\",\\"key\\":\\"bemerkung\\",\\"type\\":\\"textfield\\",\\"input\\":true}]}","ValidationProfile":"flowzer.forms/1"}},"AiTaskBindings":{}}
ce3cda8a-41c5-494d-84e4-aa8ee1622dcb	Upgrade_Service	t	1	0	2026-09-24 11:50:19.094605+00	{"Id":"ce3cda8a-41c5-494d-84e4-aa8ee1622dcb","DefinitionId":"Upgrade_Service","PreviousGuid":null,"Hash":"5702CD2B4DEAD25BC874B78E7D0AE3EEBD61DF4B137FE054957C5E1C34DFD709","SavedByUser":"5a1e0b2c-4d3f-4e21-9a87-0c6b2d4e8f10","SavedOn":"2026-09-24T11:50:19.0946052Z","DeployedByUser":null,"DeployedOn":"2026-09-24T11:50:19.0989345Z","Version":{"Major":1,"Minor":0},"IsActive":true,"FormBindings":{},"AiTaskBindings":{}}
fab1063a-1547-4978-8c05-e3fa270d744a	Upgrade_Timer	t	1	0	2026-09-24 11:50:19.17845+00	{"Id":"fab1063a-1547-4978-8c05-e3fa270d744a","DefinitionId":"Upgrade_Timer","PreviousGuid":null,"Hash":"121327CB640C16B9A6B5CBDD8B36106ACCEAD0574B00647F3D0D9FFCC0CB25E9","SavedByUser":"5a1e0b2c-4d3f-4e21-9a87-0c6b2d4e8f10","SavedOn":"2026-09-24T11:50:19.1784503Z","DeployedByUser":null,"DeployedOn":"2026-09-24T11:50:19.1833218Z","Version":{"Major":1,"Minor":0},"IsActive":true,"FormBindings":{},"AiTaskBindings":{}}
\.


--
-- Data for Name: form_authoring_drafts; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.form_authoring_drafts (form_id, revision, updated_at, body) FROM stdin;
\.


--
-- Data for Name: form_folders; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.form_folders (id, parent_id, name, body) FROM stdin;
\.


--
-- Data for Name: form_metadata; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.form_metadata (form_id, body) FROM stdin;
dad83a8c-6672-41e3-8237-e84c1f9002e1	{"FormId":"dad83a8c-6672-41e3-8237-e84c1f9002e1","Name":"UpgradeApproval","FolderId":null}
\.


--
-- Data for Name: form_section_authoring_drafts; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.form_section_authoring_drafts (section_id, revision, updated_at, body) FROM stdin;
\.


--
-- Data for Name: form_section_metadata; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.form_section_metadata (section_id, name, body) FROM stdin;
\.


--
-- Data for Name: form_section_versions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.form_section_versions (id, section_id, version_major, version_minor, body) FROM stdin;
\.


--
-- Data for Name: forms; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.forms (id, form_id, version_major, version_minor, body) FROM stdin;
7a31cabb-c300-483f-bb8d-b46b834d3f6f	dad83a8c-6672-41e3-8237-e84c1f9002e1	0	1	{"Id":"7a31cabb-c300-483f-bb8d-b46b834d3f6f","FormId":"dad83a8c-6672-41e3-8237-e84c1f9002e1","Version":{"Major":0,"Minor":1},"FormData":"{\\"components\\":[{\\"label\\":\\"Bemerkung\\",\\"key\\":\\"bemerkung\\",\\"type\\":\\"textfield\\",\\"input\\":true}]}"}
\.


--
-- Data for Name: idempotency_records; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.idempotency_records (scope_hash, request_hash, operation, created_at, expires_at, is_completed, process_instance_id) FROM stdin;
\.


--
-- Data for Name: identity_directory_state; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.identity_directory_state (singleton, active_snapshot, sync_status) FROM stdin;
\.


--
-- Data for Name: instances; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.instances (instance_id, meta_definition_id, is_finished, body) FROM stdin;
0640c341-b335-43ac-b531-afe1f8d5f714	Upgrade_Review	f	{"InstanceId":"0640c341-b335-43ac-b531-afe1f8d5f714","metaDefinitionId":"Upgrade_Review","DefinitionId":"44e805a6-ac34-4340-be88-c07fbeb0bcfe","ProcessId":"Process_UpgradeReview","Tokens":[{"Id":"a4e2f8cd-b64b-4650-9dcb-81a2ad473c91","ProcessInstanceId":"dbcfce1b-18df-47df-aff2-e7e7365da712","CurrentBaseElement":{"$type":"BPMN.Process.Process, FlowzerBPMN","Id":"Process_UpgradeReview","Documentations":null,"ExtensionDefinitions":null,"FlowElements":[{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Events.EndEvent, FlowzerBPMN","InputSet":null,"DataInputs":null,"DataInputAssociations":null,"EventDefinitions":null,"InputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"EndEvent_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Common.SequenceFlow, FlowzerBPMN","IsImmediate":false,"SourceRef":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Common.SequenceFlow, FlowzerBPMN","IsImmediate":false,"SourceRef":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.Events.EndEvent, FlowzerBPMN","InputSet":null,"DataInputs":null,"DataInputAssociations":null,"EventDefinitions":null,"InputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"EndEvent_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_2","Documentations":null,"ExtensionDefinitions":null}],"ProcessType":0,"IsExecutable":true,"IsClosed":false,"CorrelationSubscriptions":null,"Resources":null,"Supports":null,"LaneSet":null,"Properties":null,"Monitoring":null,"Auditing":null,"Name":"Upgrade Freigabe","IoSpecification":null,"IoBindings":null,"SupportedInterfaceRefs":null,"FlowzerProcessHash":null,"DefinitionsId":"Upgrade_Review","FlowzerUserTaskForms":[]},"CurrentFlowNode":null,"ActiveBoundaryEvents":[],"State":1,"StartTime":"2026-09-24T11:50:19.2389002Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":{},"OutputData":null,"CompletedByUserId":null,"Initiator":{"Issuer":"urn:flowzer:development","Subject":"5a1e0b2c-4d3f-4e21-9a87-0c6b2d4e8f10"},"ParentTokenId":null,"LastStateChangeTime":"2026-09-24T11:50:19.2389437Z"},{"Id":"9ba243a2-c122-4d08-9463-ee14b36b972f","ProcessInstanceId":"dbcfce1b-18df-47df-aff2-e7e7365da712","CurrentBaseElement":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":4,"StartTime":"2026-09-24T11:50:19.2398878Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"a4e2f8cd-b64b-4650-9dcb-81a2ad473c91","LastStateChangeTime":"2026-09-24T11:50:19.2439931Z"},{"Id":"12d7a944-0546-4136-8867-e43335f0582b","ProcessInstanceId":"dbcfce1b-18df-47df-aff2-e7e7365da712","CurrentBaseElement":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":1,"StartTime":"2026-09-24T11:50:19.2453192Z","PreviousToken":{"Id":"9ba243a2-c122-4d08-9463-ee14b36b972f","ProcessInstanceId":"dbcfce1b-18df-47df-aff2-e7e7365da712","CurrentBaseElement":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":4,"StartTime":"2026-09-24T11:50:19.2398878Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"a4e2f8cd-b64b-4650-9dcb-81a2ad473c91","LastStateChangeTime":"2026-09-24T11:50:19.2439931Z"},"LastSequenceFlow":{"IsImmediate":false,"SourceRef":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_1","Documentations":null,"ExtensionDefinitions":null},"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"a4e2f8cd-b64b-4650-9dcb-81a2ad473c91","LastStateChangeTime":"2026-09-24T11:50:19.2473965Z"}],"IsFinished":false,"State":2,"MessageSubscriptionCount":0,"SignalSubscriptionCount":0,"UserTaskSubscriptionCount":1,"ServiceSubscriptionCount":0,"Migrations":[]}
de7d0363-2c55-4c9b-adba-bd536c4efc16	Upgrade_Service	f	{"InstanceId":"de7d0363-2c55-4c9b-adba-bd536c4efc16","metaDefinitionId":"Upgrade_Service","DefinitionId":"ce3cda8a-41c5-494d-84e4-aa8ee1622dcb","ProcessId":"Process_UpgradeService","Tokens":[{"Id":"cfba1ad7-081f-4a71-879e-4bd77b740a85","ProcessInstanceId":"6f382ee5-b595-4e62-8d6b-12d1c2b0c01a","CurrentBaseElement":{"$type":"BPMN.Process.Process, FlowzerBPMN","Id":"Process_UpgradeService","Documentations":null,"ExtensionDefinitions":null,"FlowElements":[{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Activities.ServiceTask, FlowzerBPMN","Implementation":"upgrade-zahlung","FlowzerAiTask":null,"FlowzerRetries":3,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Zahlung ausloesen","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"ServiceTask_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Events.EndEvent, FlowzerBPMN","InputSet":null,"DataInputs":null,"DataInputAssociations":null,"EventDefinitions":null,"InputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"EndEvent_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Common.SequenceFlow, FlowzerBPMN","IsImmediate":false,"SourceRef":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.Activities.ServiceTask, FlowzerBPMN","Implementation":"upgrade-zahlung","FlowzerAiTask":null,"FlowzerRetries":3,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Zahlung ausloesen","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"ServiceTask_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Common.SequenceFlow, FlowzerBPMN","IsImmediate":false,"SourceRef":{"$type":"BPMN.Activities.ServiceTask, FlowzerBPMN","Implementation":"upgrade-zahlung","FlowzerAiTask":null,"FlowzerRetries":3,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Zahlung ausloesen","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"ServiceTask_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.Events.EndEvent, FlowzerBPMN","InputSet":null,"DataInputs":null,"DataInputAssociations":null,"EventDefinitions":null,"InputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"EndEvent_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_2","Documentations":null,"ExtensionDefinitions":null}],"ProcessType":0,"IsExecutable":true,"IsClosed":false,"CorrelationSubscriptions":null,"Resources":null,"Supports":null,"LaneSet":null,"Properties":null,"Monitoring":null,"Auditing":null,"Name":"Upgrade Zahlung","IoSpecification":null,"IoBindings":null,"SupportedInterfaceRefs":null,"FlowzerProcessHash":null,"DefinitionsId":"Upgrade_Service","FlowzerUserTaskForms":[]},"CurrentFlowNode":null,"ActiveBoundaryEvents":[],"State":1,"StartTime":"2026-09-24T11:50:19.3852927Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":{},"OutputData":null,"CompletedByUserId":null,"Initiator":{"Issuer":"urn:flowzer:development","Subject":"5a1e0b2c-4d3f-4e21-9a87-0c6b2d4e8f10"},"ParentTokenId":null,"LastStateChangeTime":"2026-09-24T11:50:19.3852928Z"},{"Id":"2f2f4082-019d-4061-b0b1-c4b7c793a91b","ProcessInstanceId":"6f382ee5-b595-4e62-8d6b-12d1c2b0c01a","CurrentBaseElement":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":4,"StartTime":"2026-09-24T11:50:19.385301Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"cfba1ad7-081f-4a71-879e-4bd77b740a85","LastStateChangeTime":"2026-09-24T11:50:19.3862432Z"},{"Id":"25c29700-8e4d-47ab-a27a-c7bcaaaa1e9e","ProcessInstanceId":"6f382ee5-b595-4e62-8d6b-12d1c2b0c01a","CurrentBaseElement":{"$type":"BPMN.Activities.ServiceTask, FlowzerBPMN","Implementation":"upgrade-zahlung","FlowzerAiTask":null,"FlowzerRetries":3,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Zahlung ausloesen","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"ServiceTask_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Activities.ServiceTask, FlowzerBPMN","Implementation":"upgrade-zahlung","FlowzerAiTask":null,"FlowzerRetries":3,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Zahlung ausloesen","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"ServiceTask_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":1,"StartTime":"2026-09-24T11:50:19.3862578Z","PreviousToken":{"Id":"2f2f4082-019d-4061-b0b1-c4b7c793a91b","ProcessInstanceId":"6f382ee5-b595-4e62-8d6b-12d1c2b0c01a","CurrentBaseElement":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":4,"StartTime":"2026-09-24T11:50:19.385301Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"cfba1ad7-081f-4a71-879e-4bd77b740a85","LastStateChangeTime":"2026-09-24T11:50:19.3862432Z"},"LastSequenceFlow":{"IsImmediate":false,"SourceRef":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.Activities.ServiceTask, FlowzerBPMN","Implementation":"upgrade-zahlung","FlowzerAiTask":null,"FlowzerRetries":3,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Zahlung ausloesen","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"ServiceTask_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_1","Documentations":null,"ExtensionDefinitions":null},"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"cfba1ad7-081f-4a71-879e-4bd77b740a85","LastStateChangeTime":"2026-09-24T11:50:19.3866679Z"}],"IsFinished":false,"State":2,"MessageSubscriptionCount":0,"SignalSubscriptionCount":0,"UserTaskSubscriptionCount":0,"ServiceSubscriptionCount":1,"Migrations":[]}
88be15a3-d902-4170-9043-28e482c9f5fe	Upgrade_Timer	f	{"InstanceId":"88be15a3-d902-4170-9043-28e482c9f5fe","metaDefinitionId":"Upgrade_Timer","DefinitionId":"fab1063a-1547-4978-8c05-e3fa270d744a","ProcessId":"Process_UpgradeTimer","Tokens":[{"Id":"85cddf5f-f9d8-4a5e-9224-48a0db3de52d","ProcessInstanceId":"10513d96-aed6-42f6-8f21-85577e7972dc","CurrentBaseElement":{"$type":"BPMN.Process.Process, FlowzerBPMN","Id":"Process_UpgradeTimer","Documentations":null,"ExtensionDefinitions":null,"FlowElements":[{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Flowzer.Events.FlowzerIntermediateTimerCatchEvent, FlowzerBPMN","TimerType":2,"TimerDefinition":{"TimeDate":null,"TimeCycle":null,"TimeDuration":{"Body":"PT2S"}},"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"Frist","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"TimerCatch_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Events.EndEvent, FlowzerBPMN","InputSet":null,"DataInputs":null,"DataInputAssociations":null,"EventDefinitions":null,"InputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"EndEvent_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Common.SequenceFlow, FlowzerBPMN","IsImmediate":false,"SourceRef":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.Flowzer.Events.FlowzerIntermediateTimerCatchEvent, FlowzerBPMN","TimerType":2,"TimerDefinition":{"TimeDate":null,"TimeCycle":null,"TimeDuration":{"Body":"PT2S"}},"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"Frist","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"TimerCatch_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_1","Documentations":null,"ExtensionDefinitions":null},{"$type":"BPMN.Common.SequenceFlow, FlowzerBPMN","IsImmediate":false,"SourceRef":{"$type":"BPMN.Flowzer.Events.FlowzerIntermediateTimerCatchEvent, FlowzerBPMN","TimerType":2,"TimerDefinition":{"TimeDate":null,"TimeCycle":null,"TimeDuration":{"Body":"PT2S"}},"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"Frist","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"TimerCatch_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.Events.EndEvent, FlowzerBPMN","InputSet":null,"DataInputs":null,"DataInputAssociations":null,"EventDefinitions":null,"InputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"EndEvent_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_2","Documentations":null,"ExtensionDefinitions":null}],"ProcessType":0,"IsExecutable":true,"IsClosed":false,"CorrelationSubscriptions":null,"Resources":null,"Supports":null,"LaneSet":null,"Properties":null,"Monitoring":null,"Auditing":null,"Name":"Upgrade Frist","IoSpecification":null,"IoBindings":null,"SupportedInterfaceRefs":null,"FlowzerProcessHash":null,"DefinitionsId":"Upgrade_Timer","FlowzerUserTaskForms":[]},"CurrentFlowNode":null,"ActiveBoundaryEvents":[],"State":1,"StartTime":"2026-09-24T11:50:19.4399746Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":{},"OutputData":null,"CompletedByUserId":null,"Initiator":{"Issuer":"urn:flowzer:development","Subject":"5a1e0b2c-4d3f-4e21-9a87-0c6b2d4e8f10"},"ParentTokenId":null,"LastStateChangeTime":"2026-09-24T11:50:19.4399747Z"},{"Id":"9d07bbaf-2038-45ea-bbf7-91b3360022a2","ProcessInstanceId":"10513d96-aed6-42f6-8f21-85577e7972dc","CurrentBaseElement":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":4,"StartTime":"2026-09-24T11:50:19.4399888Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"85cddf5f-f9d8-4a5e-9224-48a0db3de52d","LastStateChangeTime":"2026-09-24T11:50:19.4405838Z"},{"Id":"4530d6cf-5c5f-41a8-bce1-d0cd96b2f084","ProcessInstanceId":"10513d96-aed6-42f6-8f21-85577e7972dc","CurrentBaseElement":{"$type":"BPMN.Flowzer.Events.FlowzerIntermediateTimerCatchEvent, FlowzerBPMN","TimerType":2,"TimerDefinition":{"TimeDate":null,"TimeCycle":null,"TimeDuration":{"Body":"PT2S"}},"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"Frist","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"TimerCatch_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Flowzer.Events.FlowzerIntermediateTimerCatchEvent, FlowzerBPMN","TimerType":2,"TimerDefinition":{"TimeDate":null,"TimeCycle":null,"TimeDuration":{"Body":"PT2S"}},"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"Frist","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"TimerCatch_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":1,"StartTime":"2026-09-24T11:50:19.440593Z","PreviousToken":{"Id":"9d07bbaf-2038-45ea-bbf7-91b3360022a2","ProcessInstanceId":"10513d96-aed6-42f6-8f21-85577e7972dc","CurrentBaseElement":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":4,"StartTime":"2026-09-24T11:50:19.4399888Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"85cddf5f-f9d8-4a5e-9224-48a0db3de52d","LastStateChangeTime":"2026-09-24T11:50:19.4405838Z"},"LastSequenceFlow":{"IsImmediate":false,"SourceRef":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.Flowzer.Events.FlowzerIntermediateTimerCatchEvent, FlowzerBPMN","TimerType":2,"TimerDefinition":{"TimeDate":null,"TimeCycle":null,"TimeDuration":{"Body":"PT2S"}},"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"Frist","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"TimerCatch_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_1","Documentations":null,"ExtensionDefinitions":null},"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"85cddf5f-f9d8-4a5e-9224-48a0db3de52d","LastStateChangeTime":"2026-09-24T11:50:19.440987Z"}],"IsFinished":false,"State":2,"MessageSubscriptionCount":0,"SignalSubscriptionCount":0,"UserTaskSubscriptionCount":0,"ServiceSubscriptionCount":0,"Migrations":[]}
\.


--
-- Data for Name: message_subscriptions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.message_subscriptions (id, related_definition_id, process_instance_id, message_name, correlation_key, body) FROM stdin;
\.


--
-- Data for Name: meta_definitions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.meta_definitions (definition_id, body) FROM stdin;
Upgrade_Review	{"DefinitionId":"Upgrade_Review","Name":"Upgrade_Review","Description":"Upgrade-/Restore-Rig (R2b)","FolderId":null}
Upgrade_Service	{"DefinitionId":"Upgrade_Service","Name":"Upgrade_Service","Description":"Upgrade-/Restore-Rig (R2b)","FolderId":null}
Upgrade_Timer	{"DefinitionId":"Upgrade_Timer","Name":"Upgrade_Timer","Description":"Upgrade-/Restore-Rig (R2b)","FolderId":null}
\.


--
-- Data for Name: runtime_node_events; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.runtime_node_events (id, process_instance_id, definition_id, token_id, flow_node_id, node_state, correlation_id, occurred_at, body) FROM stdin;
58fd4f9c-a93d-b5c8-e7e7-67d61a0062ef	0640c341-b335-43ac-b531-afe1f8d5f714	44e805a6-ac34-4340-be88-c07fbeb0bcfe	9ba243a2-c122-4d08-9463-ee14b36b972f	StartEvent_1	4	ef4bcc50-0653-4075-80a0-498ccc306de4	2026-09-24 11:50:19.243993+00	{"Id":"58fd4f9c-a93d-b5c8-e7e7-67d61a0062ef","ProcessInstanceId":"0640c341-b335-43ac-b531-afe1f8d5f714","DefinitionId":"44e805a6-ac34-4340-be88-c07fbeb0bcfe","TokenId":"9ba243a2-c122-4d08-9463-ee14b36b972f","FlowNodeId":"StartEvent_1","State":4,"CorrelationId":"ef4bcc50-0653-4075-80a0-498ccc306de4","OccurredAtUtc":"2026-09-24T11:50:19.2439931+00:00"}
cb208ce8-9d3a-0c00-b4b0-0a8062c31db9	0640c341-b335-43ac-b531-afe1f8d5f714	44e805a6-ac34-4340-be88-c07fbeb0bcfe	12d7a944-0546-4136-8867-e43335f0582b	UserTask_1	1	ef4bcc50-0653-4075-80a0-498ccc306de4	2026-09-24 11:50:19.247396+00	{"Id":"cb208ce8-9d3a-0c00-b4b0-0a8062c31db9","ProcessInstanceId":"0640c341-b335-43ac-b531-afe1f8d5f714","DefinitionId":"44e805a6-ac34-4340-be88-c07fbeb0bcfe","TokenId":"12d7a944-0546-4136-8867-e43335f0582b","FlowNodeId":"UserTask_1","State":1,"CorrelationId":"ef4bcc50-0653-4075-80a0-498ccc306de4","OccurredAtUtc":"2026-09-24T11:50:19.2473965+00:00"}
06e2a1cc-bee4-f2d3-1755-bfe344de34c1	de7d0363-2c55-4c9b-adba-bd536c4efc16	ce3cda8a-41c5-494d-84e4-aa8ee1622dcb	2f2f4082-019d-4061-b0b1-c4b7c793a91b	StartEvent_1	4	d4fb3d6f-711e-4648-b3b7-d5f19f50155c	2026-09-24 11:50:19.386243+00	{"Id":"06e2a1cc-bee4-f2d3-1755-bfe344de34c1","ProcessInstanceId":"de7d0363-2c55-4c9b-adba-bd536c4efc16","DefinitionId":"ce3cda8a-41c5-494d-84e4-aa8ee1622dcb","TokenId":"2f2f4082-019d-4061-b0b1-c4b7c793a91b","FlowNodeId":"StartEvent_1","State":4,"CorrelationId":"d4fb3d6f-711e-4648-b3b7-d5f19f50155c","OccurredAtUtc":"2026-09-24T11:50:19.3862432+00:00"}
a7192622-3032-8c92-a15a-b2de193c5aa2	de7d0363-2c55-4c9b-adba-bd536c4efc16	ce3cda8a-41c5-494d-84e4-aa8ee1622dcb	25c29700-8e4d-47ab-a27a-c7bcaaaa1e9e	ServiceTask_1	1	d4fb3d6f-711e-4648-b3b7-d5f19f50155c	2026-09-24 11:50:19.386667+00	{"Id":"a7192622-3032-8c92-a15a-b2de193c5aa2","ProcessInstanceId":"de7d0363-2c55-4c9b-adba-bd536c4efc16","DefinitionId":"ce3cda8a-41c5-494d-84e4-aa8ee1622dcb","TokenId":"25c29700-8e4d-47ab-a27a-c7bcaaaa1e9e","FlowNodeId":"ServiceTask_1","State":1,"CorrelationId":"d4fb3d6f-711e-4648-b3b7-d5f19f50155c","OccurredAtUtc":"2026-09-24T11:50:19.3866679+00:00"}
4cf3d6ea-505c-0b36-28c5-38c32bace659	88be15a3-d902-4170-9043-28e482c9f5fe	fab1063a-1547-4978-8c05-e3fa270d744a	9d07bbaf-2038-45ea-bbf7-91b3360022a2	StartEvent_1	4	baa842c0-800e-4002-a2b4-bc9fdf28b1f4	2026-09-24 11:50:19.440583+00	{"Id":"4cf3d6ea-505c-0b36-28c5-38c32bace659","ProcessInstanceId":"88be15a3-d902-4170-9043-28e482c9f5fe","DefinitionId":"fab1063a-1547-4978-8c05-e3fa270d744a","TokenId":"9d07bbaf-2038-45ea-bbf7-91b3360022a2","FlowNodeId":"StartEvent_1","State":4,"CorrelationId":"baa842c0-800e-4002-a2b4-bc9fdf28b1f4","OccurredAtUtc":"2026-09-24T11:50:19.4405838+00:00"}
0884ea71-5321-6949-91e7-686a25f5aefd	88be15a3-d902-4170-9043-28e482c9f5fe	fab1063a-1547-4978-8c05-e3fa270d744a	4530d6cf-5c5f-41a8-bce1-d0cd96b2f084	TimerCatch_1	1	baa842c0-800e-4002-a2b4-bc9fdf28b1f4	2026-09-24 11:50:19.440987+00	{"Id":"0884ea71-5321-6949-91e7-686a25f5aefd","ProcessInstanceId":"88be15a3-d902-4170-9043-28e482c9f5fe","DefinitionId":"fab1063a-1547-4978-8c05-e3fa270d744a","TokenId":"4530d6cf-5c5f-41a8-bce1-d0cd96b2f084","FlowNodeId":"TimerCatch_1","State":1,"CorrelationId":"baa842c0-800e-4002-a2b4-bc9fdf28b1f4","OccurredAtUtc":"2026-09-24T11:50:19.440987+00:00"}
\.


--
-- Data for Name: schema_migrations; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.schema_migrations (version, name, applied_at) FROM stdin;
1	001_initial	2026-09-24 11:50:12.23091+00
2	002_service_tasks	2026-09-24 11:50:12.23091+00
3	003_workflow_folders	2026-09-24 11:50:12.23091+00
4	004_idempotency	2026-09-24 11:50:12.23091+00
5	005_identity_directory	2026-09-24 11:50:12.23091+00
6	006_user_task_drafts	2026-09-24 11:50:12.23091+00
7	007_user_task_lifecycle	2026-09-24 11:50:12.23091+00
8	008_user_task_deadlines	2026-09-24 11:50:12.23091+00
9	009_form_authoring_drafts	2026-09-24 11:50:12.23091+00
10	010_process_history_task_lifecycle	2026-09-24 11:50:12.23091+00
11	011_form_sections	2026-09-24 11:50:12.23091+00
12	012_runtime_node_events	2026-09-24 11:50:12.23091+00
13	013_ai_connections	2026-09-24 11:50:12.23091+00
14	014_ai_runs	2026-09-24 11:50:12.23091+00
15	015_ai_connection_revisions	2026-09-24 11:50:12.23091+00
16	016_form_library	2026-09-24 11:50:12.23091+00
\.


--
-- Data for Name: service_task_jobs; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.service_task_jobs (id, type, process_instance_id, token_id, created_at, locked_until, locked_by, retry_at, retries, last_error, body) FROM stdin;
ada83e53-52a6-4dcd-ad8a-5f8d89be4fd5	upgrade-zahlung	de7d0363-2c55-4c9b-adba-bd536c4efc16	25c29700-8e4d-47ab-a27a-c7bcaaaa1e9e	2026-09-24 11:50:19.388424+00	\N	\N	\N	3	\N	{"Id":"ada83e53-52a6-4dcd-ad8a-5f8d89be4fd5","Type":"upgrade-zahlung","Name":"Zahlung ausloesen","TokenId":"25c29700-8e4d-47ab-a27a-c7bcaaaa1e9e","FlowNodeId":"ServiceTask_1","ProcessInstanceId":"de7d0363-2c55-4c9b-adba-bd536c4efc16","MetaDefinitionId":"Upgrade_Service","DefinitionId":"ce3cda8a-41c5-494d-84e4-aa8ee1622dcb","ProcessId":"Process_UpgradeService","CreatedAt":"2026-09-24T11:50:19.3884244Z","LockedUntil":null,"LockedBy":null,"Retries":3,"RetryAt":null,"LastErrorMessage":null,"Variables":{}}
\.


--
-- Data for Name: service_task_webhooks; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.service_task_webhooks (id, type, body) FROM stdin;
\.


--
-- Data for Name: signal_subscriptions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.signal_subscriptions (id, related_definition_id, process_instance_id, signal_name, body) FROM stdin;
\.


--
-- Data for Name: timer_subscriptions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.timer_subscriptions (id, related_definition_id, process_instance_id, due_at, body) FROM stdin;
cd6094e9-0665-486f-9d84-4c560071c538	Upgrade_Timer	88be15a3-d902-4170-9043-28e482c9f5fe	2026-09-24 11:50:21.440987+00	{"Id":"cd6094e9-0665-486f-9d84-4c560071c538","DueAt":"2026-09-24T11:50:21.440987Z","FlowNodeId":"TimerCatch_1","Kind":1,"ProcessId":"Process_UpgradeTimer","RelatedDefinitionId":"Upgrade_Timer","DefinitionId":"fab1063a-1547-4978-8c05-e3fa270d744a","ProcessInstanceId":"88be15a3-d902-4170-9043-28e482c9f5fe","TokenId":"4530d6cf-5c5f-41a8-bce1-d0cd96b2f084","RemainingOccurrences":null}
\.


--
-- Data for Name: user_task_assignment_events; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.user_task_assignment_events (id, user_task_id, revision, occurred_at, body, process_instance_id) FROM stdin;
\.


--
-- Data for Name: user_task_deadlines; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.user_task_deadlines (user_task_id, revision, next_check_at, body) FROM stdin;
d0609421-9f16-49b8-b4f4-e5f5fc66af9a	1	\N	{"UserTaskId":"d0609421-9f16-49b8-b4f4-e5f5fc66af9a","Revision":1,"ActivatedAtUtc":"2026-09-24T11:50:19.2453192+00:00","RawDueDate":null,"RawFollowUpDate":null,"ScheduleState":"none","Status":"none","DueAtUtc":null,"FollowUpAtUtc":null,"ReminderAtUtc":[],"EscalationAtUtc":null,"EmittedMilestones":[],"NextCheckAtUtc":null,"PolicyVersion":"default-v1","UpdatedAtUtc":"2026-09-24T11:50:19.2453192+00:00"}
\.


--
-- Data for Name: user_task_drafts; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.user_task_drafts (user_task_id, owner_key, owner_user_id, token_id, process_instance_id, definition_id, revision, updated_at, body) FROM stdin;
\.


--
-- Data for Name: user_task_notification_reads; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.user_task_notification_reads (notification_id, owner_key, read_at) FROM stdin;
\.


--
-- Data for Name: user_task_notifications; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.user_task_notifications (id, user_task_id, kind, occurred_at, deduplication_key, body) FROM stdin;
\.


--
-- Data for Name: user_task_subscriptions; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.user_task_subscriptions (id, related_definition_id, process_instance_id, body) FROM stdin;
d0609421-9f16-49b8-b4f4-e5f5fc66af9a	Upgrade_Review	0640c341-b335-43ac-b531-afe1f8d5f714	{"Id":"d0609421-9f16-49b8-b4f4-e5f5fc66af9a","Name":"Freigeben","Token":{"Id":"12d7a944-0546-4136-8867-e43335f0582b","ProcessInstanceId":"dbcfce1b-18df-47df-aff2-e7e7365da712","CurrentBaseElement":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":1,"StartTime":"2026-09-24T11:50:19.2453192Z","PreviousToken":{"Id":"9ba243a2-c122-4d08-9463-ee14b36b972f","ProcessInstanceId":"dbcfce1b-18df-47df-aff2-e7e7365da712","CurrentBaseElement":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"CurrentFlowNode":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"ActiveBoundaryEvents":[],"State":4,"StartTime":"2026-09-24T11:50:19.2398878Z","PreviousToken":null,"LastSequenceFlow":null,"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"a4e2f8cd-b64b-4650-9dcb-81a2ad473c91","LastStateChangeTime":"2026-09-24T11:50:19.2439931Z"},"LastSequenceFlow":{"IsImmediate":false,"SourceRef":{"$type":"BPMN.Events.StartEvent, FlowzerBPMN","FlowzerFormKey":null,"OutputSet":null,"DataOutputs":null,"DataOutputAssociations":null,"EventDefinition":null,"OutputMappings":null,"Escalations":null,"Properties":null,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"StartEvent_1","Documentations":null,"ExtensionDefinitions":null},"TargetRef":{"$type":"BPMN.HumanInteraction.UserTask, FlowzerBPMN","Implementation":"UpgradeApproval","Renderings":[],"FlowzerAssignee":null,"FlowzerCandidateGroups":null,"FlowzerCandidateUsers":null,"FlowzerAssignmentMode":0,"FlowzerDirectoryAssigneeUserId":null,"FlowzerDirectoryCandidateUserIds":[],"FlowzerDirectoryCandidateGroupIds":[],"FlowzerDueDate":null,"FlowzerFollowUpDate":null,"FlowzerPriority":null,"InputMappings":null,"OutputMappings":null,"IsForCompensation":false,"StartQuantity":0,"CompletionQuantity":0,"IoSpecification":null,"DataInputAssociations":null,"DataOutputAssociations":null,"Properties":null,"DefaultId":null,"Resources":null,"LoopCharacteristics":null,"BoundaryEvents":null,"Name":"Freigeben","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"UserTask_1","Documentations":null,"ExtensionDefinitions":null},"ConditionExpression":null,"FlowzerCondition":null,"FlowzerIsDefault":false,"Name":"","Auditing":null,"Monitoring":null,"CategoryValueRefs":null,"Id":"Flow_1","Documentations":null,"ExtensionDefinitions":null},"Variables":null,"OutputData":null,"CompletedByUserId":null,"Initiator":null,"ParentTokenId":"a4e2f8cd-b64b-4650-9dcb-81a2ad473c91","LastStateChangeTime":"2026-09-24T11:50:19.2473965Z"},"UserCandidates":[],"UserGroups":[],"CurrenAssignedUser":null,"ProcessInstanceId":"0640c341-b335-43ac-b531-afe1f8d5f714","MetaDefinitionId":"Upgrade_Review","DefinitionId":"44e805a6-ac34-4340-be88-c07fbeb0bcfe","ProcessId":"Process_UpgradeReview","Assignee":null,"CandidateUsers":[],"CandidateGroups":[],"AssignmentMode":0,"DirectoryAssigneeUserId":null,"DirectoryCandidateUserIds":[],"DirectoryCandidateGroupIds":[]}
\.


--
-- Data for Name: user_task_work_states; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.user_task_work_states (user_task_id, revision, body) FROM stdin;
\.


--
-- Data for Name: workflow_folders; Type: TABLE DATA; Schema: flowzer; Owner: -
--

COPY flowzer.workflow_folders (id, parent_id, name, body) FROM stdin;
\.


--
-- Name: ai_connection_revisions ai_connection_revisions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.ai_connection_revisions
    ADD CONSTRAINT ai_connection_revisions_pkey PRIMARY KEY (id, revision);


--
-- Name: ai_connections ai_connections_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.ai_connections
    ADD CONSTRAINT ai_connections_pkey PRIMARY KEY (id);


--
-- Name: ai_runs ai_runs_instance_token_unique; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.ai_runs
    ADD CONSTRAINT ai_runs_instance_token_unique UNIQUE (process_instance_id, token_id);


--
-- Name: ai_runs ai_runs_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.ai_runs
    ADD CONSTRAINT ai_runs_pkey PRIMARY KEY (id);


--
-- Name: definition_binaries definition_binaries_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.definition_binaries
    ADD CONSTRAINT definition_binaries_pkey PRIMARY KEY (id);


--
-- Name: definitions definitions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.definitions
    ADD CONSTRAINT definitions_pkey PRIMARY KEY (id);


--
-- Name: form_authoring_drafts form_authoring_drafts_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_authoring_drafts
    ADD CONSTRAINT form_authoring_drafts_pkey PRIMARY KEY (form_id);


--
-- Name: form_folders form_folders_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_folders
    ADD CONSTRAINT form_folders_pkey PRIMARY KEY (id);


--
-- Name: form_metadata form_metadata_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_metadata
    ADD CONSTRAINT form_metadata_pkey PRIMARY KEY (form_id);


--
-- Name: form_section_authoring_drafts form_section_authoring_drafts_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_section_authoring_drafts
    ADD CONSTRAINT form_section_authoring_drafts_pkey PRIMARY KEY (section_id);


--
-- Name: form_section_metadata form_section_metadata_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_section_metadata
    ADD CONSTRAINT form_section_metadata_pkey PRIMARY KEY (section_id);


--
-- Name: form_section_versions form_section_versions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_section_versions
    ADD CONSTRAINT form_section_versions_pkey PRIMARY KEY (id);


--
-- Name: form_section_versions form_section_versions_section_id_version_major_version_mino_key; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_section_versions
    ADD CONSTRAINT form_section_versions_section_id_version_major_version_mino_key UNIQUE (section_id, version_major, version_minor);


--
-- Name: forms forms_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.forms
    ADD CONSTRAINT forms_pkey PRIMARY KEY (id);


--
-- Name: idempotency_records idempotency_records_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.idempotency_records
    ADD CONSTRAINT idempotency_records_pkey PRIMARY KEY (scope_hash);


--
-- Name: identity_directory_state identity_directory_state_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.identity_directory_state
    ADD CONSTRAINT identity_directory_state_pkey PRIMARY KEY (singleton);


--
-- Name: instances instances_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.instances
    ADD CONSTRAINT instances_pkey PRIMARY KEY (instance_id);


--
-- Name: message_subscriptions message_subscriptions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.message_subscriptions
    ADD CONSTRAINT message_subscriptions_pkey PRIMARY KEY (id);


--
-- Name: meta_definitions meta_definitions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.meta_definitions
    ADD CONSTRAINT meta_definitions_pkey PRIMARY KEY (definition_id);


--
-- Name: runtime_node_events runtime_node_events_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.runtime_node_events
    ADD CONSTRAINT runtime_node_events_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: service_task_jobs service_task_jobs_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.service_task_jobs
    ADD CONSTRAINT service_task_jobs_pkey PRIMARY KEY (id);


--
-- Name: service_task_webhooks service_task_webhooks_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.service_task_webhooks
    ADD CONSTRAINT service_task_webhooks_pkey PRIMARY KEY (id);


--
-- Name: signal_subscriptions signal_subscriptions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.signal_subscriptions
    ADD CONSTRAINT signal_subscriptions_pkey PRIMARY KEY (id);


--
-- Name: timer_subscriptions timer_subscriptions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.timer_subscriptions
    ADD CONSTRAINT timer_subscriptions_pkey PRIMARY KEY (id);


--
-- Name: user_task_assignment_events user_task_assignment_events_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_assignment_events
    ADD CONSTRAINT user_task_assignment_events_pkey PRIMARY KEY (id);


--
-- Name: user_task_assignment_events user_task_assignment_events_user_task_id_revision_key; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_assignment_events
    ADD CONSTRAINT user_task_assignment_events_user_task_id_revision_key UNIQUE (user_task_id, revision);


--
-- Name: user_task_deadlines user_task_deadlines_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_deadlines
    ADD CONSTRAINT user_task_deadlines_pkey PRIMARY KEY (user_task_id);


--
-- Name: user_task_drafts user_task_drafts_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_drafts
    ADD CONSTRAINT user_task_drafts_pkey PRIMARY KEY (user_task_id, owner_key);


--
-- Name: user_task_notification_reads user_task_notification_reads_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_notification_reads
    ADD CONSTRAINT user_task_notification_reads_pkey PRIMARY KEY (notification_id, owner_key);


--
-- Name: user_task_notifications user_task_notifications_deduplication_key_key; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_notifications
    ADD CONSTRAINT user_task_notifications_deduplication_key_key UNIQUE (deduplication_key);


--
-- Name: user_task_notifications user_task_notifications_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_notifications
    ADD CONSTRAINT user_task_notifications_pkey PRIMARY KEY (id);


--
-- Name: user_task_subscriptions user_task_subscriptions_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_subscriptions
    ADD CONSTRAINT user_task_subscriptions_pkey PRIMARY KEY (id);


--
-- Name: user_task_work_states user_task_work_states_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_work_states
    ADD CONSTRAINT user_task_work_states_pkey PRIMARY KEY (user_task_id);


--
-- Name: workflow_folders workflow_folders_pkey; Type: CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.workflow_folders
    ADD CONSTRAINT workflow_folders_pkey PRIMARY KEY (id);


--
-- Name: ai_connections_name_unique_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE UNIQUE INDEX ai_connections_name_unique_idx ON flowzer.ai_connections USING btree (lower(name));


--
-- Name: ai_runs_claim_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX ai_runs_claim_idx ON flowzer.ai_runs USING btree (status, next_attempt_at, created_at) WHERE (status = ANY (ARRAY[0, 2, 3]));


--
-- Name: ai_runs_expired_lease_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX ai_runs_expired_lease_idx ON flowzer.ai_runs USING btree (lease_expires_at) WHERE (status = ANY (ARRAY[1, 4]));


--
-- Name: definitions_definition_id_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX definitions_definition_id_idx ON flowzer.definitions USING btree (definition_id);


--
-- Name: definitions_definition_version_uidx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE UNIQUE INDEX definitions_definition_version_uidx ON flowzer.definitions USING btree (definition_id, version_major, version_minor);


--
-- Name: form_authoring_drafts_updated_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX form_authoring_drafts_updated_idx ON flowzer.form_authoring_drafts USING btree (updated_at);


--
-- Name: form_folders_parent_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX form_folders_parent_idx ON flowzer.form_folders USING btree (parent_id);


--
-- Name: form_section_authoring_drafts_updated_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX form_section_authoring_drafts_updated_idx ON flowzer.form_section_authoring_drafts USING btree (updated_at);


--
-- Name: form_section_versions_section_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX form_section_versions_section_idx ON flowzer.form_section_versions USING btree (section_id, version_major, version_minor);


--
-- Name: forms_form_id_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX forms_form_id_idx ON flowzer.forms USING btree (form_id);


--
-- Name: forms_form_version_uidx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE UNIQUE INDEX forms_form_version_uidx ON flowzer.forms USING btree (form_id, version_major, version_minor);


--
-- Name: idempotency_records_expires_at_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX idempotency_records_expires_at_idx ON flowzer.idempotency_records USING btree (expires_at);


--
-- Name: instances_is_finished_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX instances_is_finished_idx ON flowzer.instances USING btree (is_finished);


--
-- Name: message_subscriptions_instance_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX message_subscriptions_instance_idx ON flowzer.message_subscriptions USING btree (process_instance_id);


--
-- Name: message_subscriptions_name_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX message_subscriptions_name_idx ON flowzer.message_subscriptions USING btree (message_name);


--
-- Name: runtime_node_events_instance_time_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX runtime_node_events_instance_time_idx ON flowzer.runtime_node_events USING btree (process_instance_id, occurred_at, token_id, id);


--
-- Name: service_task_jobs_available_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX service_task_jobs_available_idx ON flowzer.service_task_jobs USING btree (type, locked_until, retry_at, created_at);


--
-- Name: service_task_jobs_instance_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX service_task_jobs_instance_idx ON flowzer.service_task_jobs USING btree (process_instance_id);


--
-- Name: service_task_jobs_token_uidx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE UNIQUE INDEX service_task_jobs_token_uidx ON flowzer.service_task_jobs USING btree (token_id);


--
-- Name: service_task_webhooks_type_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX service_task_webhooks_type_idx ON flowzer.service_task_webhooks USING btree (type);


--
-- Name: signal_subscriptions_instance_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX signal_subscriptions_instance_idx ON flowzer.signal_subscriptions USING btree (process_instance_id);


--
-- Name: timer_subscriptions_due_at_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX timer_subscriptions_due_at_idx ON flowzer.timer_subscriptions USING btree (due_at);


--
-- Name: user_task_assignment_events_instance_time_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX user_task_assignment_events_instance_time_idx ON flowzer.user_task_assignment_events USING btree (process_instance_id, occurred_at, user_task_id, revision) WHERE (process_instance_id IS NOT NULL);


--
-- Name: user_task_assignment_events_task_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX user_task_assignment_events_task_idx ON flowzer.user_task_assignment_events USING btree (user_task_id, revision);


--
-- Name: user_task_deadlines_next_check_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX user_task_deadlines_next_check_idx ON flowzer.user_task_deadlines USING btree (next_check_at) WHERE (next_check_at IS NOT NULL);


--
-- Name: user_task_drafts_updated_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX user_task_drafts_updated_idx ON flowzer.user_task_drafts USING btree (updated_at);


--
-- Name: user_task_notifications_task_time_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX user_task_notifications_task_time_idx ON flowzer.user_task_notifications USING btree (user_task_id, occurred_at DESC);


--
-- Name: user_task_subscriptions_instance_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX user_task_subscriptions_instance_idx ON flowzer.user_task_subscriptions USING btree (process_instance_id);


--
-- Name: workflow_folders_parent_idx; Type: INDEX; Schema: flowzer; Owner: -
--

CREATE INDEX workflow_folders_parent_idx ON flowzer.workflow_folders USING btree (parent_id);


--
-- Name: form_authoring_drafts form_authoring_drafts_form_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_authoring_drafts
    ADD CONSTRAINT form_authoring_drafts_form_id_fkey FOREIGN KEY (form_id) REFERENCES flowzer.form_metadata(form_id) ON DELETE CASCADE;


--
-- Name: form_folders form_folders_parent_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_folders
    ADD CONSTRAINT form_folders_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES flowzer.form_folders(id) ON DELETE RESTRICT;


--
-- Name: form_section_authoring_drafts form_section_authoring_drafts_section_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_section_authoring_drafts
    ADD CONSTRAINT form_section_authoring_drafts_section_id_fkey FOREIGN KEY (section_id) REFERENCES flowzer.form_section_metadata(section_id) ON DELETE CASCADE;


--
-- Name: form_section_versions form_section_versions_section_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.form_section_versions
    ADD CONSTRAINT form_section_versions_section_id_fkey FOREIGN KEY (section_id) REFERENCES flowzer.form_section_metadata(section_id) ON DELETE RESTRICT;


--
-- Name: user_task_deadlines user_task_deadlines_user_task_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_deadlines
    ADD CONSTRAINT user_task_deadlines_user_task_id_fkey FOREIGN KEY (user_task_id) REFERENCES flowzer.user_task_subscriptions(id) ON DELETE CASCADE;


--
-- Name: user_task_drafts user_task_drafts_user_task_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_drafts
    ADD CONSTRAINT user_task_drafts_user_task_id_fkey FOREIGN KEY (user_task_id) REFERENCES flowzer.user_task_subscriptions(id) ON DELETE CASCADE;


--
-- Name: user_task_notification_reads user_task_notification_reads_notification_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_notification_reads
    ADD CONSTRAINT user_task_notification_reads_notification_id_fkey FOREIGN KEY (notification_id) REFERENCES flowzer.user_task_notifications(id) ON DELETE CASCADE;


--
-- Name: user_task_notifications user_task_notifications_user_task_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_notifications
    ADD CONSTRAINT user_task_notifications_user_task_id_fkey FOREIGN KEY (user_task_id) REFERENCES flowzer.user_task_subscriptions(id) ON DELETE CASCADE;


--
-- Name: user_task_work_states user_task_work_states_user_task_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.user_task_work_states
    ADD CONSTRAINT user_task_work_states_user_task_id_fkey FOREIGN KEY (user_task_id) REFERENCES flowzer.user_task_subscriptions(id) ON DELETE CASCADE;


--
-- Name: workflow_folders workflow_folders_parent_id_fkey; Type: FK CONSTRAINT; Schema: flowzer; Owner: -
--

ALTER TABLE ONLY flowzer.workflow_folders
    ADD CONSTRAINT workflow_folders_parent_id_fkey FOREIGN KEY (parent_id) REFERENCES flowzer.workflow_folders(id) ON DELETE RESTRICT;


--
-- PostgreSQL database dump complete
--

\unrestrict VtM4zVDT1prjutoNMKGh6sQsKCNJLShSyJYZ0hChzwGoe93ckqaYrD4Tsa1hgpw

