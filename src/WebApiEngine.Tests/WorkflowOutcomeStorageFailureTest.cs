using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>Technische Lesefehler folgen dem bestehenden Fehlervertrag und werden niemals fachliche Entscheidungen.</summary>
[NonParallelizable]
public sealed class WorkflowOutcomeStorageFailureTest
{
    // Testzweck: Definition-/Binary-I/O-Fehler werden nicht verschluckt und nicht als Approved/Rejected ausgegeben.
    [TestCase(false)]
    [TestCase(true)]
    public async Task TechnicalStorageReadFailure_ShouldPropagateWithoutDecision(bool failBinary)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var definitions = DispatchProxy.Create<IDefinitionStorage, DefinitionReadFaultProxy>();
        var proxy = (DefinitionReadFaultProxy)definitions;
        proxy.Target = context.Demo.Storage.DefinitionStorage;
        proxy.FailBinary = failBinary;
        var projector = new WorkflowOutcomeProjector(new DefinitionOnlyStorage(definitions), Options.Create(context.Options));
        Func<Task> read = async () => _ = await projector.ProjectAsync(await context.Demo.InstanceAsync());
        await read.Should().ThrowAsync<IOException>().WithMessage("synthetic storage failure");
    }

    /// <summary>Ausschließlich synthetischer Lesefehler, keine Transport-/Providerabkürzung.</summary>
    public class DefinitionReadFaultProxy : DispatchProxy
    {
        internal IDefinitionStorage Target { get; set; } = null!;
        internal bool FailBinary { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IDefinitionStorage.GetBinary) && FailBinary)
                return Task.FromException<string>(new IOException("synthetic storage failure"));
            if (targetMethod.Name == nameof(IDefinitionStorage.GetDefinitionById) && !FailBinary)
                return Task.FromException<BpmnDefinition>(new IOException("synthetic storage failure"));
            return targetMethod.Invoke(Target, args);
        }
    }

    private sealed class DefinitionOnlyStorage(IDefinitionStorage definitions) : IStorageSystem
    {
        public IDefinitionStorage DefinitionStorage => definitions;
        public IFolderStorage FolderStorage => throw new NotSupportedException();
        public IMessageSubscriptionStorage SubscriptionStorage => throw new NotSupportedException();
        public IInstanceStorage InstanceStorage => throw new NotSupportedException();
        public IFormStorage FormStorage => throw new NotSupportedException();
        public IServiceTaskStorage ServiceTaskStorage => throw new NotSupportedException();
    }
}
