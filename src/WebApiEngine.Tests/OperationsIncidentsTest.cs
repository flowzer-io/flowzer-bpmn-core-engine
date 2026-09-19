using System.Reflection;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.Controller;
using WebApiEngine.Diagnostics;
using WebApiEngine.Shared;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Das Störungszentrum: Was ohne Eingriff liegen bleibt, steht an einer Stelle. Die Liste wird
/// abgeleitet und nicht geführt — es gibt keinen Zustand, den eine eigene Ablage noch ergänzen
/// könnte, wohl aber einen, den sie verpassen würde.
/// </summary>
[NonParallelizable]
public class OperationsIncidentsTest
{
    // Testzweck: Ein Auftrag ohne verbleibende Versuche erscheint mit allem, was der Betrieb zum
    // Handeln braucht: Workflow, Schritt, Auftragstyp, letzte Meldung und die Eingaben, die der
    // Korrekturdialog vorbelegt.
    [Test]
    public async Task Incidents_ShouldListAStalledJobWithItsStepAndItsInputs()
    {
        using var context = new IncidentContext();
        var job = await context.AddStalledJob("Endpunkt nicht erreichbar");

        var incidents = await context.GetIncidents();

        var incident = incidents.Should().ContainSingle().Subject;
        incident.Kind.Should().Be(OperationsIncidentKinds.JobExhausted);
        incident.JobId.Should().Be(job.Id);
        incident.JobType.Should().Be("zahlung");
        incident.InstanceId.Should().Be(job.ProcessInstanceId);
        incident.MetaDefinitionId.Should().Be("Definitions_Zahlung");
        incident.DefinitionId.Should().Be(job.DefinitionId);
        incident.DefinitionName.Should().Be("Zahlung");
        incident.FlowNodeId.Should().Be("ServiceTask_1");
        incident.FlowNodeName.Should().Be("Zahlung ausloesen");
        incident.Message.Should().Be("Endpunkt nicht erreichbar");
        incident.Since.Should().Be(job.CreatedAt);
        incident.ManualRetries.Should().Be(0);
        incident.Variables.Should().ContainKey("iban");
    }

    // Testzweck: Ein Auftrag mit verbleibenden Versuchen ist keine Störung. Er wartet nur, und
    // die Betriebsseite wäre unbrauchbar, wenn jede normale Wartezeit darin stünde.
    [Test]
    public async Task Incidents_ShouldIgnoreAJobThatStillHasAttempts()
    {
        using var context = new IncidentContext();
        await context.AddStalledJob("egal", retries: 2);

        (await context.GetIncidents()).Should().BeEmpty();
    }

    // Testzweck: Eine gescheiterte Instanz erscheint mit ihrer Begründung und dem Schritt, an
    // dem sie stehen geblieben ist — nicht mit dem Prozessknoten des Master-Tokens, der nur
    // „irgendwo in diesem Workflow" sagt.
    [Test]
    public async Task Incidents_ShouldListAFailedInstanceWithItsReasonAndItsStep()
    {
        using var context = new IncidentContext();
        var instance = await context.AddFailedInstance("Unhandled BPMN error 'BONITAET' at 'ServiceTask_1'.");

        var incidents = await context.GetIncidents();

        var incident = incidents.Should().ContainSingle().Subject;
        incident.Kind.Should().Be(OperationsIncidentKinds.InstanceFailed);
        incident.InstanceId.Should().Be(instance.InstanceId);
        incident.DefinitionName.Should().Be("Zahlung");
        incident.FlowNodeId.Should().Be("ServiceTask_1");
        incident.FlowNodeName.Should().Be("Zahlung ausloesen");
        incident.Message.Should().Be("Unhandled BPMN error 'BONITAET' at 'ServiceTask_1'.");
        incident.JobId.Should().BeNull();
        incident.JobType.Should().BeNull();
        incident.ManualRetries.Should().BeNull();
        incident.Variables.Should().BeNull();
    }

    // Testzweck: Beide Arten stehen in derselben Liste, neueste zuerst. Der Betrieb sieht damit
    // ohne Umschalten, was zuletzt passiert ist.
    [Test]
    public async Task Incidents_ShouldMergeBothKindsNewestFirst()
    {
        using var context = new IncidentContext();
        await context.AddStalledJob("alt", since: new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc));
        await context.AddFailedInstance("neu", failedAt: new DateTime(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc));

        var incidents = await context.GetIncidents();

        incidents.Select(incident => incident.Kind).Should().Equal(
            OperationsIncidentKinds.InstanceFailed,
            OperationsIncidentKinds.JobExhausted);
    }

    // Testzweck: Der Diagnose-Schnappschuss trägt die beiden Zähler, damit die Betriebsseite die
    // Kachel zeigen kann, ohne die Eingaben aller liegen gebliebenen Aufträge mitzuladen.
    [Test]
    public async Task Diagnostics_ShouldCarryTheIncidentCounters()
    {
        using var context = new IncidentContext();
        await context.AddStalledJob("liegt");
        await context.AddStalledJob("liegt auch");
        await context.AddStalledJob("wartet noch", retries: 1);
        await context.AddFailedInstance("gescheitert");

        var action = await context.Controller.GetDiagnostics();

        var payload = ((OkObjectResult)action.Result!).Value
            .Should().BeOfType<ApiStatusResult<OperationsDiagnosticsDto>>().Subject;
        payload.Result!.Incidents.JobExhausted.Should().Be(2);
        payload.Result.Incidents.InstanceFailed.Should().Be(1);
    }

    // Testzweck: Die Störungsliste zeigt Prozessdaten und den Zustand fremder Instanzen; sie
    // gehört deshalb wie die übrige Diagnose der Betriebsrolle und lockert die Klassenregel nicht.
    [Test]
    public void Incidents_ShouldRequireTheOperatorRole()
    {
        var method = typeof(OperationsController).GetMethod(nameof(OperationsController.GetIncidents))!;

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull("der Endpunkt darf die Klassenregel nicht lockern");
        typeof(OperationsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy
            .Should().Be(FlowzerPolicies.Operator);
    }

    private sealed class IncidentContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;
        private readonly Storage _storage;
        private readonly Guid _definitionId = Guid.NewGuid();

        public IncidentContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-incidents-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);

            _storage = new Storage();
            _storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = "Definitions_Zahlung",
                Name = "Zahlung"
            }).GetAwaiter().GetResult();

            Controller = new OperationsController(
                _storage,
                new StubHostEnvironment(),
                new TimerSchedulerDiagnosticsState(),
                Options.Create(new FlowzerObservabilityOptions()),
                new WebApiEngine.Persistence.FlowzerStorageOptions(),
                NullLogger<OperationsController>.Instance);
        }

        public OperationsController Controller { get; }

        public async Task<OperationsIncidentDto[]> GetIncidents()
        {
            var action = await Controller.GetIncidents();
            var payload = ((OkObjectResult)action.Result!).Value
                .Should().BeOfType<ApiStatusResult<OperationsIncidentDto[]>>().Subject;
            return payload.Result!;
        }

        public async Task<ServiceTaskJob> AddStalledJob(
            string errorMessage,
            int retries = 0,
            DateTime? since = null)
        {
            var variables = new Variables();
            ((IDictionary<string, object?>)variables)["iban"] = "DE00000000000000000000";

            var job = new ServiceTaskJob
            {
                Id = Guid.NewGuid(),
                Type = "zahlung",
                Name = "Zahlung ausloesen",
                TokenId = Guid.NewGuid(),
                FlowNodeId = "ServiceTask_1",
                ProcessInstanceId = Guid.NewGuid(),
                MetaDefinitionId = "Definitions_Zahlung",
                DefinitionId = _definitionId,
                ProcessId = "Process_Zahlung",
                CreatedAt = since ?? new DateTime(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc),
                Retries = retries,
                LastErrorMessage = errorMessage,
                Variables = variables
            };

            await _storage.ServiceTaskStorage.SaveJob(job);
            return job;
        }

        public async Task<ProcessInstanceInfo> AddFailedInstance(string failureReason, DateTime? failedAt = null)
        {
            var instanceId = Guid.NewGuid();
            var moment = failedAt ?? new DateTime(2026, 9, 19, 11, 0, 0, DateTimeKind.Utc);
            var masterToken = new Token
            {
                ProcessInstanceId = instanceId,
                CurrentBaseElement = new BPMN.Process.Process
                {
                    Id = "Process_Zahlung",
                    DefinitionsId = "Definitions_Zahlung"
                },
                ActiveBoundaryEvents = [],
                State = FlowNodeState.Failed,
                LastStateChangeTime = moment
            };
            var stepToken = new Token
            {
                ProcessInstanceId = instanceId,
                ParentTokenId = masterToken.Id,
                CurrentBaseElement = new BPMN.Activities.ServiceTask
                {
                    Id = "ServiceTask_1",
                    Name = "Zahlung ausloesen",
                    Implementation = "zahlung"
                },
                ActiveBoundaryEvents = [],
                State = FlowNodeState.Failed,
                LastStateChangeTime = moment
            };

            var instance = new ProcessInstanceInfo
            {
                InstanceId = instanceId,
                metaDefinitionId = "Definitions_Zahlung",
                DefinitionId = _definitionId,
                ProcessId = "Process_Zahlung",
                Tokens = [masterToken, stepToken],
                IsFinished = true,
                State = ProcessInstanceState.Failed,
                FailureReason = failureReason,
                MessageSubscriptionCount = 0,
                SignalSubscriptionCount = 0,
                UserTaskSubscriptionCount = 0,
                ServiceSubscriptionCount = 0
            };

            await _storage.InstanceStorage.AddOrUpdateInstance(instance);
            return instance;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _originalRoot);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Flowzer.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
