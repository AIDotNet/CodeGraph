using System.Text.Json;

internal delegate ValueTask<WorkerResponse> WorkerMethodHandler(
    JsonElement parameters,
    WorkerRequestContext context);

internal sealed class WorkerDispatcher
{
    private readonly Dictionary<string, WorkerMethodHandler> handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkerRouteDescriptor> descriptors = new(StringComparer.Ordinal);

    public void Register(string method, Func<JsonElement, Task<WorkerResponse>> handler)
    {
        AddHandler(method, async (parameters, _) => await handler(parameters), Inline(method));
    }

    public void Register(string method, Func<JsonElement, WorkerResponse> handler)
    {
        AddHandler(method, (parameters, _) => ValueTask.FromResult(handler(parameters)), Inline(method));
    }

    public void Register(string method, Func<JsonElement, WorkerRequestContext, Task<WorkerResponse>> handler)
    {
        AddHandler(method, async (parameters, context) => await handler(parameters, context), Inline(method));
    }

    public void Register(string method, Func<JsonElement, WorkerRequestContext, WorkerResponse> handler)
    {
        AddHandler(
            method,
            (parameters, context) => ValueTask.FromResult(handler(parameters, context)),
            Inline(method));
    }

    public void RegisterJob(
        string method,
        Func<JsonElement, WorkerResponse> handler,
        string resultMode,
        string? lanePolicy)
    {
        AddHandler(
            method,
            (parameters, _) => ValueTask.FromResult(handler(parameters)),
            new WorkerRouteDescriptor(method, "job", resultMode, lanePolicy));
    }

    public void RegisterJob(
        string method,
        Func<JsonElement, Task<WorkerResponse>> handler,
        string resultMode,
        string? lanePolicy)
    {
        AddHandler(
            method,
            async (parameters, _) => await handler(parameters),
            new WorkerRouteDescriptor(method, "job", resultMode, lanePolicy));
    }

    public void RegisterJob(
        string method,
        Func<JsonElement, WorkerRequestContext, Task<WorkerResponse>> handler,
        string resultMode,
        string? lanePolicy)
    {
        AddHandler(
            method,
            async (parameters, context) => await handler(parameters, context),
            new WorkerRouteDescriptor(method, "job", resultMode, lanePolicy));
    }

    public async ValueTask<WorkerResponse> DispatchAsync(
        string method,
        JsonElement parameters,
        WorkerRequestContext context)
    {
        if (TryGetJobDescriptor(method, out _))
        {
            return WorkerResponse.Error(
                $"Background Job route '{method}' must be submitted through jobs/submit.");
        }

        return await DispatchCoreAsync(method, parameters, context);
    }

    public async ValueTask<WorkerResponse> DispatchJobAsync(
        string method,
        JsonElement parameters,
        WorkerRequestContext context)
    {
        if (!TryGetJobDescriptor(method, out _))
        {
            return WorkerResponse.Error($"Method is not registered as a background Job: {method}");
        }

        return await DispatchCoreAsync(method, parameters, context);
    }

    private async ValueTask<WorkerResponse> DispatchCoreAsync(
        string method,
        JsonElement parameters,
        WorkerRequestContext context)
    {
        if (!handlers.TryGetValue(method, out var handler))
        {
            return WorkerResponse.Error($"Unsupported method: {method}");
        }

        return await handler(parameters, context);
    }

    public string[] GetRegisteredMethods()
    {
        var methods = handlers.Keys.ToArray();
        Array.Sort(methods, StringComparer.Ordinal);
        return methods;
    }

    public WorkerRouteDescriptor[] GetRouteDescriptors()
    {
        var routes = descriptors.Values.ToArray();
        Array.Sort(routes, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Method, right.Method));
        return routes;
    }

    public bool TryGetJobDescriptor(string method, out WorkerRouteDescriptor descriptor)
    {
        if (descriptors.TryGetValue(method, out var candidate) &&
            string.Equals(candidate.ExecutionMode, "job", StringComparison.Ordinal))
        {
            descriptor = candidate;
            return true;
        }

        descriptor = null!;
        return false;
    }

    private static WorkerRouteDescriptor Inline(string method)
    {
        return new WorkerRouteDescriptor(method, "inline", "direct", null);
    }

    private void AddHandler(
        string method,
        WorkerMethodHandler handler,
        WorkerRouteDescriptor descriptor)
    {
        if (!handlers.TryAdd(method, handler))
        {
            throw new InvalidOperationException($"Duplicate worker method: {method}");
        }
        descriptors.Add(method, descriptor);
    }
}
