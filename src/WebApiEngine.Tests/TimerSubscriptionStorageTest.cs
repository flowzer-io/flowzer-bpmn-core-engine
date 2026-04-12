using FilesystemStorageSystem;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class TimerSubscriptionStorageTest
{
    [Test]
    public async Task AddAndGetTimerSubscriptions_ShouldPersistDefinitionAndInstanceTimers()
    {
        using var context = new TimerSubscriptionStorageTestContext();
        var instanceId = Guid.NewGuid();

        var definitionTimer = context.CreateTimerSubscription(
            TimerSubscriptionKind.ProcessStartEvent,
            processInstanceId: null,
            tokenId: null,
            flowNodeId: "StartEvent_Timer");
        var instanceTimer = context.CreateTimerSubscription(
            TimerSubscriptionKind.IntermediateCatchEvent,
            processInstanceId: instanceId,
            tokenId: Guid.NewGuid(),
            flowNodeId: "CatchEvent_Timer");

        await context.SubscriptionStorage.AddTimerSubscription(definitionTimer);
        await context.SubscriptionStorage.AddTimerSubscription(instanceTimer);

        var allTimers = (await context.SubscriptionStorage.GetAllTimerSubscriptions()).ToArray();
        var instanceTimers = (await context.SubscriptionStorage.GetTimerSubscriptions(instanceId)).ToArray();

        allTimers.Should().HaveCount(2);
        allTimers.Should().Contain(subscription => subscription.Id == definitionTimer.Id);
        allTimers.Should().Contain(subscription => subscription.Id == instanceTimer.Id);
        instanceTimers.Should().ContainSingle(subscription => subscription.Id == instanceTimer.Id);
    }

    [Test]
    public async Task RemoveOperations_ShouldDeleteMatchingTimerSubscriptionsOnly()
    {
        using var context = new TimerSubscriptionStorageTestContext();
        var firstInstanceId = Guid.NewGuid();
        var secondInstanceId = Guid.NewGuid();

        var definitionTimer = context.CreateTimerSubscription(
            TimerSubscriptionKind.ProcessStartEvent,
            processInstanceId: null,
            tokenId: null,
            flowNodeId: "StartEvent_Timer");
        var firstInstanceTimer = context.CreateTimerSubscription(
            TimerSubscriptionKind.IntermediateCatchEvent,
            processInstanceId: firstInstanceId,
            tokenId: Guid.NewGuid(),
            flowNodeId: "CatchEvent_Timer_1");
        var secondInstanceTimer = context.CreateTimerSubscription(
            TimerSubscriptionKind.IntermediateCatchEvent,
            processInstanceId: secondInstanceId,
            tokenId: Guid.NewGuid(),
            flowNodeId: "CatchEvent_Timer_2");

        await context.SubscriptionStorage.AddTimerSubscription(definitionTimer);
        await context.SubscriptionStorage.AddTimerSubscription(firstInstanceTimer);
        await context.SubscriptionStorage.AddTimerSubscription(secondInstanceTimer);

        await context.SubscriptionStorage.RemoveProcessTimerSubscriptionsByProcessInstanceId(firstInstanceId);
        var afterInstanceRemoval = (await context.SubscriptionStorage.GetAllTimerSubscriptions()).ToArray();
        afterInstanceRemoval.Should().HaveCount(2);
        afterInstanceRemoval.Should().NotContain(subscription => subscription.Id == firstInstanceTimer.Id);

        await context.SubscriptionStorage.RemoveAllProcessTimerSubscriptionsWithNoInstanceId(context.RelatedDefinitionId);
        var afterDefinitionRemoval = (await context.SubscriptionStorage.GetAllTimerSubscriptions()).ToArray();
        afterDefinitionRemoval.Should().ContainSingle(subscription => subscription.Id == secondInstanceTimer.Id);

        await context.SubscriptionStorage.RemoveTimerSubscription(secondInstanceTimer.Id);
        (await context.SubscriptionStorage.GetAllTimerSubscriptions()).Should().BeEmpty();
    }

    private sealed class TimerSubscriptionStorageTestContext : IDisposable
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;

        public TimerSubscriptionStorageTestContext()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-timer-storage-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _storageRoot);

            Storage = new Storage();
            SubscriptionStorage = Storage.SubscriptionStorage;
        }

        public Storage Storage { get; }
        public StorageSystem.IMessageSubscriptionStorage SubscriptionStorage { get; }
        public string RelatedDefinitionId { get; } = "definition-timer-storage";
        public Guid DefinitionId { get; } = Guid.NewGuid();

        public TimerSubscription CreateTimerSubscription(
            TimerSubscriptionKind kind,
            Guid? processInstanceId,
            Guid? tokenId,
            string flowNodeId)
        {
            return new TimerSubscription
            {
                DueAt = DateTime.UtcNow.AddMinutes(5),
                FlowNodeId = flowNodeId,
                Kind = kind,
                ProcessId = "Process_Timer",
                RelatedDefinitionId = RelatedDefinitionId,
                DefinitionId = DefinitionId,
                ProcessInstanceId = processInstanceId,
                TokenId = tokenId
            };
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);

            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }
}
