using System.Text.Json;

internal readonly struct WorkerModuleContext
{
    private readonly WorkerDispatcher dispatcher;

    public WorkerModuleContext(WorkerDispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    internal WorkerDispatcher Dispatcher => dispatcher;

    public void Register(string method, Func<JsonElement, Task<WorkerResponse>> handler)
    {
        dispatcher.Register(method, handler);
    }

    public void Register(string method, Func<JsonElement, WorkerResponse> handler)
    {
        dispatcher.Register(method, handler);
    }

    public void Register(string method, Func<JsonElement, WorkerRequestContext, Task<WorkerResponse>> handler)
    {
        dispatcher.Register(method, handler);
    }

    public void Register(string method, Func<JsonElement, WorkerRequestContext, WorkerResponse> handler)
    {
        dispatcher.Register(method, handler);
    }

    public void RegisterJob(
        string method,
        Func<JsonElement, WorkerResponse> handler,
        string resultMode = "result",
        string? lanePolicy = null)
    {
        dispatcher.RegisterJob(method, handler, resultMode, lanePolicy);
    }

    public void RegisterJob(
        string method,
        Func<JsonElement, Task<WorkerResponse>> handler,
        string resultMode = "result",
        string? lanePolicy = null)
    {
        dispatcher.RegisterJob(method, handler, resultMode, lanePolicy);
    }

    public void RegisterJob(
        string method,
        Func<JsonElement, WorkerRequestContext, Task<WorkerResponse>> handler,
        string resultMode = "result",
        string? lanePolicy = null)
    {
        dispatcher.RegisterJob(method, handler, resultMode, lanePolicy);
    }

    public string[] GetRegisteredMethods()
    {
        return dispatcher.GetRegisteredMethods();
    }

    public WorkerRouteDescriptor[] GetRouteDescriptors()
    {
        return dispatcher.GetRouteDescriptors();
    }
}
