using ServiceTask = BPMN.Activities.ServiceTask;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Jobs;

namespace WebApiEngine.Tests;

/// <summary>Eigener Job und echte Engine-Koordinaten, aber hermetisches Provider-/Storage-I/O.</summary>
public sealed class ServiceTaskInitiatorAccessTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private static readonly Guid Worker = Guid.Parse("A1B2C3D4-0000-4000-8000-000000000001");

    // Testzweck: Die gespeicherte Master-Identität und konfigurierte API-Rolle werden
    // live geprüft, niemals Worker, Prozessvariablen oder ein vom Client gewünschter User.
    [TestCase("tt.ticket.read", true)]
    [TestCase("tt.ticket.create", false)]
    [TestCase("tt.ticket.close", true)]
    [TestCase("tt.ticket.delegate", true)]
    public async Task Check_ShouldBindTheCurrentInitiatorToTheOwnedJob(string type, bool allowed)
    {
        var context = new Context(type); context.Reader.Allowed = allowed;
        var outcome = await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None);
        outcome.Status.Should().Be(ServiceTaskInitiatorAccessStatus.Ok); outcome.Access.Should().NotBeNull();
        var proof = outcome.Access!;
        proof.Allowed.Should().Be(allowed); proof.JobId.Should().Be(context.Job.Id);
        proof.ProcessInstanceId.Should().Be(context.Instance.InstanceId); proof.MetaDefinitionId.Should().Be(context.Job.MetaDefinitionId);
        proof.DefinitionId.Should().Be(context.Job.DefinitionId); proof.TokenId.Should().Be(context.Job.TokenId);
        proof.FlowNodeId.Should().Be(context.Job.FlowNodeId); proof.Type.Should().Be(type);
        proof.InitiatorIssuer.Should().Be(Issuer); proof.InitiatorSubject.Should().Be("m1");
        context.Reader.Requests.Should().Equal((new AuthenticatedSubject(Issuer, "m1"), "flowzer-api", "access"));
        context.Provider.OpenContexts.Should().Be(0); context.Provider.Openings.Should().Be(2);
        context.Job.Retries.Should().Be(3); context.Job.LastErrorMessage.Should().BeNull();
        context.Provider.Commits.Should().Be(0);
    }

    // Testzweck: Fremde Lease, abgearbeiteter Token, falsche Engine-Version, fehlende oder
    // mehrdeutige Initiatoren und historische/technische Starts liefern keine Providerprofile.
    [TestCase("missing", ServiceTaskInitiatorAccessStatus.NotFound)]
    [TestCase("foreign-worker", ServiceTaskInitiatorAccessStatus.LeaseLost)]
    [TestCase("foreign-person", ServiceTaskInitiatorAccessStatus.LeaseLost)]
    [TestCase("expired", ServiceTaskInitiatorAccessStatus.LeaseLost)]
    [TestCase("other-type", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("finished", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("terminating", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("missing-instance", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("wrong-instance", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("wrong-version", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("wrong-definition", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("wrong-process", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("wrong-node", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("finished-token", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("duplicate-token", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("missing-initiator", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("duplicate-master", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("wrong-issuer", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    public async Task Check_ShouldCloseBeforeProviderIoOnInvalidContext(string variant, ServiceTaskInitiatorAccessStatus expected)
    {
        var context = new Context(); context.Change(variant);
        var outcome = await context.Service.CheckAsync(variant == "missing" ? Guid.NewGuid() : context.Job.Id,
            variant == "foreign-person" ? Guid.NewGuid() : Worker, variant == "foreign-worker" ? "worker-b" : "worker-a", CancellationToken.None);
        outcome.Status.Should().Be(expected); outcome.Access.Should().BeNull(); context.Reader.Requests.Should().BeEmpty();
    }

    // Testzweck: Während Keycloak-I/O gibt es keine offene Storage-Transaktion. Verlust
    // oder Umbinden des Jobs, Aufgabenabbruch und Identitätswechsel entwerten auch ein Ja.
    [TestCase("expired", ServiceTaskInitiatorAccessStatus.LeaseLost)]
    [TestCase("foreign-worker", ServiceTaskInitiatorAccessStatus.LeaseLost)]
    [TestCase("finished", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("wrong-version", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("changed-job-version", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("changed-subject", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    [TestCase("changed-type", ServiceTaskInitiatorAccessStatus.InvalidContext)]
    public async Task Check_ShouldRevalidateAfterProviderIo(string variant, ServiceTaskInitiatorAccessStatus expected)
    {
        var context = new Context(); context.Reader.DuringRead = () =>
        { context.Provider.OpenContexts.Should().Be(0); context.Change(variant); };
        var outcome = await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None);
        outcome.Status.Should().Be(expected); outcome.Access.Should().BeNull(); context.Reader.Requests.Should().HaveCount(1);
        context.Provider.Commits.Should().Be(0);
    }

    // Testzweck: Technische Quellfehler sind geschlossen, aber kein bestätigter Entzug,
    // keine Retrybudget-Senkung und kein Leak der ursprünglichen HTTP-Exception.
    [Test]
    public async Task Check_ShouldTreatSourceFailureAsUnavailableNotDenied()
    {
        var context = new Context(); context.Reader.Error = new KeycloakAdminClientException(KeycloakAdminClientFailureKind.Authorization,
            "PRIVATE_HTTP_INPUT", new Exception("token-private"));
        var outcome = await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None);
        outcome.Status.Should().Be(ServiceTaskInitiatorAccessStatus.Unavailable); outcome.Access.Should().BeNull();
        outcome.ToString().Should().NotContain("PRIVATE_HTTP_INPUT").And.NotContain("token-private"); context.Job.Retries.Should().Be(3);
    }

    // Testzweck: Fehlende Authentifizierung oder leere/permissive RequiredRole dürfen
    // nicht zu einem versteckten realmweiten Worker-Zugangsbeweis werden.
    [TestCase("none")]
    [TestCase("no-role")]
    [TestCase("no-audience")]
    public async Task Check_ShouldRejectPermissiveAuthentication(string variant)
    {
        var context = new Context();
        if (variant == "none") context.Authentication.Scheme = "None";
        if (variant == "no-role") context.Authentication.JwtBearer.RequiredRole = "";
        if (variant == "no-audience") context.Authentication.JwtBearer.Audience = "";
        var outcome = await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None);
        outcome.Status.Should().Be(ServiceTaskInitiatorAccessStatus.Unavailable); outcome.Access.Should().BeNull();
        context.Reader.Requests.Should().BeEmpty(); context.Provider.Openings.Should().Be(0);
    }

    // Testzweck: Caller-Abbruch wird nicht in Denied/Unavailable umgedeutet.
    [Test]
    public async Task Check_ShouldPreserveCancellation()
    {
        var context = new Context(); using var cancellation = new CancellationTokenSource();
        context.Reader.DuringRead = cancellation.Cancel;
        var error = await FluentActions.Awaiting(() => context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        error.Which.CancellationToken.Should().Be(cancellation.Token); context.Provider.OpenContexts.Should().Be(0);
    }

    private sealed class Context
    {
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        public InMemoryServiceTaskStorage Jobs { get; } = new();
        public Reader Reader { get; } = new();
        public FlowzerAuthenticationOptions Authentication { get; } = new()
        { Scheme = "JwtBearer", JwtBearer = new() { Authority = Issuer, Audience = "flowzer-api", RequiredRole = "access" } };
        public ServiceTaskJob Job { get; }
        public ProcessInstanceInfo Instance { get; }
        public Provider Provider { get; }
        public ServiceTaskInitiatorAccessService Service { get; }
        public Context(string type = "tt.ticket.read")
        {
            var instanceId = Guid.NewGuid(); var masterId = Guid.NewGuid();
            var master = new Token { Id = masterId, ProcessInstanceId = instanceId,
                CurrentBaseElement = new BPMN.Process.Process { Id = "process", DefinitionsId = "definition" }, ActiveBoundaryEvents = [], Initiator = new(Issuer, "m1") };
            var task = new Token { Id = Guid.NewGuid(), ParentTokenId = masterId, ProcessInstanceId = instanceId,
                CurrentBaseElement = new ServiceTask { Id = "node", Name = "Node", Implementation = type }, ActiveBoundaryEvents = [], State = FlowNodeState.Active };
            Instance = new() { InstanceId = instanceId, metaDefinitionId = "definition", DefinitionId = Guid.NewGuid(), ProcessId = "process",
                Tokens = [master, task], IsFinished = false, State = ProcessInstanceState.Waiting,
                MessageSubscriptionCount = 0, SignalSubscriptionCount = 0, UserTaskSubscriptionCount = 0, ServiceSubscriptionCount = 1 };
            Job = new() { Id = Guid.NewGuid(), Type = type, Name = "PRIVATE_WORKFLOW_NAME", TokenId = task.Id, FlowNodeId = "node", ProcessInstanceId = instanceId,
                MetaDefinitionId = Instance.metaDefinitionId, DefinitionId = Instance.DefinitionId, ProcessId = "process",
                LockedBy = ServiceTaskJobService.BuildLockOwner(Worker, "worker-a"), LockedUntil = Time.GetUtcNow().UtcDateTime.AddMinutes(5), Retries = 3 };
            Jobs.SaveJob(Job).GetAwaiter().GetResult();
            Provider = new(Jobs, Instance); Service = new(Provider, Reader, Authentication, Time);
        }
        public void Change(string variant)
        {
            switch (variant)
            {
                case "expired": Time.Advance(TimeSpan.FromMinutes(6)); break;
                case "other-type": case "changed-type": Job.Type = "payment"; break;
                case "foreign-worker": Job.LockedBy = ServiceTaskJobService.BuildLockOwner(Worker, "other"); break;
                case "finished": Instance.IsFinished = true; break;
                case "terminating": Instance.State = ProcessInstanceState.Terminating; break;
                case "missing-instance": Provider.MissingInstance = true; break;
                case "wrong-instance": Instance.InstanceId = Guid.NewGuid(); break;
                case "wrong-version": Instance.DefinitionId = Guid.NewGuid(); break;
                case "changed-job-version": Job.DefinitionId = Instance.DefinitionId = Guid.NewGuid(); break;
                case "wrong-definition": Instance.metaDefinitionId = "foreign"; break;
                case "wrong-process": Instance.ProcessId = "foreign"; break;
                case "wrong-node": Job.FlowNodeId = "foreign"; break;
                case "finished-token": Instance.Tokens[1].State = FlowNodeState.Completed; break;
                case "duplicate-token": Instance.Tokens.Add(Instance.Tokens[1]); break;
                case "missing-initiator": Instance.Tokens[0].Initiator = null; break;
                case "duplicate-master": Instance.Tokens.Add(Instance.Tokens[0]); break;
                case "wrong-issuer": Instance.Tokens[0].Initiator = new("https://other.invalid/realms/flowzer", "m1"); break;
                case "changed-subject": Instance.Tokens[0].Initiator = new(Issuer, "other"); break;
            }
        }
    }
    private sealed class Reader : IKeycloakSubjectAccessReader
    {
        public List<(AuthenticatedSubject Identity, string Client, string Role)> Requests { get; } = [];
        public bool Allowed { get; set; } = true;
        public Action? DuringRead { get; set; }
        public Exception? Error { get; set; }
        public Task<bool> HasCurrentAccessAsync(AuthenticatedSubject identity, string clientId, string requiredRole, CancellationToken cancellationToken)
        {
            Requests.Add((identity, clientId, requiredRole)); DuringRead?.Invoke(); cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null) throw Error; return Task.FromResult(Allowed);
        }
    }
    private sealed class Provider(InMemoryServiceTaskStorage jobs, ProcessInstanceInfo instance) : ITransactionalStorageProvider
    {
        public int OpenContexts { get; private set; }
        public int Openings { get; private set; }
        public int Commits { get; private set; }
        public bool MissingInstance { get; set; }
        public ITransactionalStorage GetTransactionalStorage() { OpenContexts++; Openings++; return new Storage(this, jobs, instance); }
        private sealed class Storage(Provider owner, InMemoryServiceTaskStorage jobs, ProcessInstanceInfo instance) : ITransactionalStorage
        {
            public IServiceTaskStorage ServiceTaskStorage => jobs;
            public IInstanceStorage InstanceStorage { get; } = new Instances(owner, instance);
            public IDefinitionStorage DefinitionStorage => throw new NotSupportedException();
            public IFolderStorage FolderStorage => throw new NotSupportedException();
            public IMessageSubscriptionStorage SubscriptionStorage => throw new NotSupportedException();
            public IFormStorage FormStorage => throw new NotSupportedException();
            public void CommitChanges() => owner.Commits++;
            public void RollbackTransaction() => throw new NotSupportedException();
            public void Dispose() => owner.OpenContexts--;
        }
        private sealed class Instances(Provider owner, ProcessInstanceInfo instance) : IInstanceStorage
        {
            public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) => owner.MissingInstance
                ? throw new FileNotFoundException() : Task.FromResult(instance);
            public Task AddOrUpdateInstance(ProcessInstanceInfo value) => throw new NotSupportedException();
            public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => throw new NotSupportedException();
            public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => throw new NotSupportedException();
        }
    }
}
