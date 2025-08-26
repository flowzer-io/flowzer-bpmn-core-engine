using core_engine;

namespace core_engine.Tests;

public class CoreEngineTests
{
    [Fact]
    public void EventData_Should_Create_With_Required_Properties()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var nodeId = "test-node-1";

        // Act
        var eventData = new EventData
        {
            BpmnNodeId = nodeId,
            InstanceId = instanceId
        };

        // Assert
        Assert.Equal(nodeId, eventData.BpmnNodeId);
        Assert.Equal(instanceId, eventData.InstanceId);
    }

    [Fact]
    public void Instance_Should_Create_With_Required_Properties()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var instanceData = new Dictionary<string, object> { { "key", "value" } };
        var tokens = new Token[] { };
        var interactions = new Interaction[] { };

        // Act
        var instance = new Instance
        {
            InstanceId = instanceId,
            InstanceData = instanceData,
            Tokens = tokens,
            PossibleInteractions = interactions
        };

        // Assert
        Assert.Equal(instanceId, instance.InstanceId);
        Assert.Equal(instanceData, instance.InstanceData);
        Assert.Equal(tokens, instance.Tokens);
        Assert.Equal(interactions, instance.PossibleInteractions);
    }

    [Fact]
    public void Token_Should_Create_With_Required_Properties()
    {
        // Arrange
        var tokenId = Guid.NewGuid();
        var time = DateTime.UtcNow;
        var nodeId = "test-node-1";

        // Act
        var token = new Token
        {
            Id = tokenId,
            Time = time,
            BpmnNodeId = nodeId
        };

        // Assert
        Assert.Equal(tokenId, token.Id);
        Assert.Equal(time, token.Time);
        Assert.Equal(nodeId, token.BpmnNodeId);
    }

    [Fact]
    public void Subscription_Should_Create_With_Required_Properties()
    {
        // Arrange
        var serviceId = "test-service";
        var nodeId = "test-node-1";
        var instanceId = Guid.NewGuid();

        // Act
        var subscription = new Subscription
        {
            ServiceId = serviceId,
            BpmnNodeId = nodeId,
            InstanceId = instanceId
        };

        // Assert
        Assert.Equal(serviceId, subscription.ServiceId);
        Assert.Equal(nodeId, subscription.BpmnNodeId);
        Assert.Equal(instanceId, subscription.InstanceId);
    }

    [Fact]
    public void EventResult_Should_Create_With_Required_Properties()
    {
        // Arrange
        var subscriptions = new Subscription[] { };

        // Act
        var eventResult = new EventResult
        {
            ServiceSubscriptions = subscriptions,
            IsDone = false
        };

        // Assert
        Assert.Equal(subscriptions, eventResult.ServiceSubscriptions);
        Assert.False(eventResult.IsDone);
    }
}