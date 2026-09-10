using BPMN.Common;
using BPMN.HumanInteraction;
using BPMN.Process;
using FluentAssertions;
using Model;
using Newtonsoft.Json;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Schutzmechanismen der PostgreSQL-Ablage, die ohne Datenbank pruefbar sind:
/// Typ-Allowlist beim Deserialisieren und die Validierung des Schemanamens.
/// </summary>
[TestFixture]
public class PostgreSqlStorageGuardsTest
{
    // Testzweck: Ein manipulierter $type-Wert in der Datenbank darf keine fremde Klasse instanziieren.
    [Test]
    public void SerializerRejectsTypesOutsideTheKnownAssemblies()
    {
        // Ein manipulierter $type-Wert darf keine fremde Klasse instanziieren.
        const string hostile = "{\"$type\":\"System.Diagnostics.Process, System.Diagnostics.Process\",\"StartInfo\":{}}";

        var act = () => StorageJson.Deserialize<object>(hostile);

        // Newtonsoft verpackt den Binder-Fehler; entscheidend ist, dass der Typ nie aufgeloest wird.
        act.Should().Throw<JsonSerializationException>()
            .Which.InnerException.Should().BeOfType<JsonSerializationException>()
            .Which.Message.Should().Contain("not allowed");
    }

    // Testzweck: Die Typ-Allowlist laesst die polymorphen BPMN-Elemente in Tokens weiterhin durch.
    [Test]
    public void SerializerRoundTripsPolymorphicFlowzerTypes()
    {
        // Instanzen tragen BPMN-Elemente polymorph in Tokens; die Allowlist muss diese Typen durchlassen.
        var instanceId = Guid.NewGuid();
        var userTask = new UserTask { Id = "task", Name = "Aufgabe", Implementation = "Approval" };
        var process = new Process { Id = "Process_1", Name = "P", DefinitionsId = "D", IsExecutable = true, FlowElements = [userTask] };
        var instance = new ProcessInstanceInfo
        {
            InstanceId = instanceId, metaDefinitionId = "catalog-1", DefinitionId = Guid.NewGuid(), ProcessId = "Process_1",
            Tokens = [new Token { ProcessInstanceId = instanceId, CurrentBaseElement = process, ActiveBoundaryEvents = [], State = FlowNodeState.Active },
                      new Token { ProcessInstanceId = instanceId, CurrentBaseElement = userTask, ActiveBoundaryEvents = [], State = FlowNodeState.Active }],
            State = ProcessInstanceState.Waiting, IsFinished = false,
            MessageSubscriptionCount = 0, SignalSubscriptionCount = 0, UserTaskSubscriptionCount = 1, ServiceSubscriptionCount = 0
        };

        var copy = StorageJson.Deserialize<ProcessInstanceInfo>(StorageJson.Serialize(instance));

        copy.Tokens.Should().HaveCount(2);
        copy.Tokens.Last().CurrentBaseElement.Should().BeOfType<UserTask>().Which.Name.Should().Be("Aufgabe");
    }

    // Testzweck: Konkrete Service-Task-Auftragsdokumente kommen ohne CLR-Typmetadaten aus und
    // behalten die Daten, die der PostgreSQL-Adapter außerhalb des Token-Objektgraphen benötigt.
    [Test]
    public void ConcreteServiceTaskJobDocumentRoundTripsWithoutTypeMetadata()
    {
        var job = new ServiceTaskJob
        {
            Id = Guid.NewGuid(),
            Type = "payment",
            Name = "Zahlung auslösen",
            TokenId = Guid.NewGuid(),
            FlowNodeId = "ServiceTask_Payment",
            ProcessInstanceId = Guid.NewGuid(),
            MetaDefinitionId = "payments",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "PaymentProcess",
            CreatedAt = DateTime.UtcNow,
            Retries = 3,
            Variables = new System.Dynamic.ExpandoObject()
        };

        var json = StorageJson.SerializeConcrete(job);
        var restored = StorageJson.DeserializeConcrete<ServiceTaskJob>(json);

        json.Should().NotContain("$type");
        restored.Id.Should().Be(job.Id);
        restored.TokenId.Should().Be(job.TokenId);
        restored.FlowNodeId.Should().Be(job.FlowNodeId);
        restored.Type.Should().Be(job.Type);
        restored.Variables.Should().NotBeNull();
    }

    // Testzweck: Konkrete Webhook-Dokumente bleiben ohne CLR-Typmetadaten lesbar, damit
    // gespeicherte Ziel- und Sicherheitsinformationen nicht an polymorphe JSON-Bindung koppeln.
    [Test]
    public void ConcreteServiceTaskWebhookDocumentRoundTripsWithoutTypeMetadata()
    {
        var webhook = new ServiceTaskWebhook
        {
            Id = Guid.NewGuid(),
            Type = "payment",
            Url = new Uri("https://worker.example.test/jobs"),
            Secret = "test-secret",
            Description = "Payment worker",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid(),
            ConsecutiveFailures = 2,
            LastAttemptAt = DateTime.UtcNow,
            LastError = "Timeout"
        };

        var json = StorageJson.SerializeConcrete(webhook);
        var restored = StorageJson.DeserializeConcrete<ServiceTaskWebhook>(json);

        json.Should().NotContain("$type");
        restored.Id.Should().Be(webhook.Id);
        restored.Url.Should().Be(webhook.Url);
        restored.Secret.Should().Be(webhook.Secret);
        restored.LastError.Should().Be(webhook.LastError);
    }

    // Testzweck: Der Fallback für historische Auftragskörper liest ausschließlich die
    // verschachtelte BPMN-ID und wertet ein vorhandenes $type-Feld nie als CLR-Typ aus.
    [Test]
    public void LegacyFlowNodeIdReaderIgnoresClrTypeMetadata()
    {
        const string legacyBody = """
            {
              "$type": "System.Diagnostics.Process, System.Diagnostics.Process",
              "Token": {
                "CurrentBaseElement": {
                  "$type": "System.Diagnostics.Process, System.Diagnostics.Process",
                  "Id": "ServiceTask_Legacy"
                }
              }
            }
            """;

        var flowNodeId = StorageJson.ReadLegacyString(legacyBody, "Token", "CurrentBaseElement", "Id");

        flowNodeId.Should().Be("ServiceTask_Legacy");
    }

    // Testzweck: Der historische Fallback akzeptiert nur eine Zeichenkette als BPMN-ID und
    // verwirft strukturierte Nutzlasten statt sie implizit in eine Kennung umzuwandeln.
    [Test]
    public void LegacyFlowNodeIdReaderRejectsNonStringValues()
    {
        const string legacyBody = """
            { "Token": { "CurrentBaseElement": { "Id": { "value": "not-an-id" } } } }
            """;

        var flowNodeId = StorageJson.ReadLegacyString(legacyBody, "Token", "CurrentBaseElement", "Id");

        flowNodeId.Should().BeNull();
    }

    // Testzweck: Der Schemaname wird als schlichter Bezeichner validiert; alles andere wird abgelehnt.
    [TestCase("flowzer", true)]
    [TestCase("_intern", true)]
    [TestCase("flowzer_2", true)]
    [TestCase("1flowzer", false)]
    [TestCase("pg_flowzer", false)]
    [TestCase("Flowzer", false)]
    [TestCase("flowzer\"; DROP SCHEMA public", false)]
    [TestCase("", false)]
    public void SchemaNameIsValidatedAsAPlainIdentifier(string schema, bool valid)
    {
        var options = new PostgreSqlStorageOptions { ConnectionString = "Host=localhost;Database=x", Schema = schema };

        var act = () => options.Validate();

        if (valid)
            act.Should().NotThrow();
        else
            act.Should().Throw<InvalidOperationException>().WithMessage("*Schema*");
    }
}
