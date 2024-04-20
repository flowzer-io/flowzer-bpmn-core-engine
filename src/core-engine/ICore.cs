namespace core_engine;
/// <summary>
/// The interface for the actual BPMN execution implementation.
/// It should be independent of whether it runs in a continuously running application,
/// a MicroService, an Azure/AWS Function, or a test.
/// Data from external services (Webhook/UserTask/Timing control, etc.) are "injected" into the Core
/// via the <see cref="Execute"/> method.
/// The execution of the process continues until all tokens are at an activity waiting for external interactions.
/// </summary>
public interface ICore
{
    /// <summary>
    /// Loads the BPMN model data into the Core, which is required for the execution of the process.
    /// This method is called before the execution of the process begins.
    /// </summary>
    /// <param name="process">the BPMN process data</param>
    /// <returns>A task that represents the asynchronous load operation.</returns>
    public Task LoadModel(BPMN.Process.Process process);
    
    /// <summary>
    /// Retrieves the starting points or initial service subscriptions (InteractionRequests) of the diagram.
    /// </summary>
    /// <returns>Returns the initial service subscriptions (InteractionRequests) of the diagram.</returns>
    public Task<InteractionRequest[]> GetInitialInteractionRequests();
    
    /// <summary>
    /// Submits an event into the Core to start or continue the execution of the process,
    /// until all tokens are at an activity waiting for external interactions.
    /// </summary>
    /// <param name="interactionResult">The data resulting from the interaction</param>
    /// <returns>The result of the execution</returns>
    public Task<ExecutionResult> Execute(InteractionResult interactionResult);

    /// <summary>
    /// Loads the current state of the process instance.
    /// </summary>
    /// <param name="processInstanceState">The state of the process instance</param>
    /// <returns>A task that represents the asynchronous load operation.</returns>
    public Task LoadInstance(ProcessInstanceState processInstanceState);
}
