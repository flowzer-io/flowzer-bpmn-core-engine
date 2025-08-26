using core_engine;

namespace core_engine.Tests;

public class InteractionTests
{
    [Fact]
    public void UserTask_Should_Create_Successfully()
    {
        // Act
        var userTask = new UserTask("Test Task", "node1", "form1");
        
        // Assert
        Assert.Equal("Test Task", userTask.Name);
        Assert.Equal("node1", userTask.NodeId);
        Assert.Equal("form1", userTask.FormId);
    }

    [Fact]
    public void ServiceTask_Should_Create_Successfully()
    {
        // Act
        var serviceTask = new ServiceTask("Service Task", "node2");
        
        // Assert
        Assert.Equal("Service Task", serviceTask.Name);
        Assert.Equal("node2", serviceTask.NodeId);
    }

    [Fact]
    public void CatchError_Should_Create_Successfully()
    {
        // Act
        var catchError = new CatchError("Error Handler", "node3");
        
        // Assert
        Assert.Equal("Error Handler", catchError.Name);
        Assert.Equal("node3", catchError.NodeId);
    }

    [Fact]
    public void Timer_Should_Create_Successfully()
    {
        // Act
        var timer = new Timer("Timer Task", "node4");
        
        // Assert
        Assert.Equal("Timer Task", timer.Name);
        Assert.Equal("node4", timer.NodeId);
    }
}