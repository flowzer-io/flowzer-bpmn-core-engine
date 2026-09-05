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
