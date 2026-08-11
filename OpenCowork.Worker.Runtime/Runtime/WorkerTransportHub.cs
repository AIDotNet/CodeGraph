using System.Text.Json;

/// <summary>
/// Transport-neutral bridge used by durable/background work. A job must never retain the
/// request that submitted it, because request cancellation and Event IPC availability have
/// different lifetimes from the job itself.
/// </summary>
internal static class WorkerTransportHub
{
    private static readonly object Sync = new();
    private static Func<string, Action<Utf8JsonWriter>, CancellationToken, ValueTask>?
        controlPublisher;
    private static Func<WorkerMessagePackEvent, CancellationToken, ValueTask>?
        eventPublisher;
    private static Func<WorkerMessagePackEvent, bool>? eventTryPublisher;
    private static CancellationToken workerCancellationToken;

    public static void ConfigureWorkerCancellation(CancellationToken cancellationToken)
    {
        lock (Sync)
        {
            workerCancellationToken = cancellationToken;
        }
    }

    public static void SetControlPublisher(
        Func<string, Action<Utf8JsonWriter>, CancellationToken, ValueTask> publisher)
    {
        lock (Sync)
        {
            controlPublisher = publisher;
        }
    }

    public static void ClearControlPublisher()
    {
        lock (Sync)
        {
            controlPublisher = null;
        }
    }

    public static void SetEventPublisher(
        Func<WorkerMessagePackEvent, CancellationToken, ValueTask> publisher,
        Func<WorkerMessagePackEvent, bool> tryPublisher)
    {
        lock (Sync)
        {
            eventPublisher = publisher;
            eventTryPublisher = tryPublisher;
        }
    }

    public static void ClearEventPublisher()
    {
        lock (Sync)
        {
            eventPublisher = null;
            eventTryPublisher = null;
        }
    }

    public static WorkerRequestContext CreateBackgroundContext(CancellationToken cancellationToken)
    {
        CancellationToken lifetime;
        lock (Sync)
        {
            lifetime = workerCancellationToken;
        }

        return new WorkerRequestContext(
            PublishBackgroundEventAsync,
            PublishEventAsync,
            cancellationToken,
            lifetime);
    }

    public static ValueTask PublishEventAsync(
        WorkerMessagePackEvent messagePackEvent,
        CancellationToken cancellationToken = default)
    {
        Func<WorkerMessagePackEvent, CancellationToken, ValueTask>? publisher;
        lock (Sync)
        {
            publisher = eventPublisher;
        }

        return publisher is null
            ? ValueTask.CompletedTask
            : publisher(messagePackEvent, cancellationToken);
    }

    public static bool TryPublishEvent(WorkerMessagePackEvent messagePackEvent)
    {
        Func<WorkerMessagePackEvent, bool>? publisher;
        lock (Sync)
        {
            publisher = eventTryPublisher;
        }

        return publisher?.Invoke(messagePackEvent) ?? false;
    }

    private static ValueTask PublishControlAsync(
        string eventName,
        Action<Utf8JsonWriter> writeParameters,
        CancellationToken cancellationToken)
    {
        Func<string, Action<Utf8JsonWriter>, CancellationToken, ValueTask>? publisher;
        lock (Sync)
        {
            publisher = controlPublisher;
        }

        if (publisher is null)
        {
            throw new IOException(
                $"Control IPC is unavailable while publishing worker event '{eventName}'.");
        }

        return publisher(eventName, writeParameters, cancellationToken);
    }

    private static ValueTask PublishBackgroundEventAsync(
        string eventName,
        Action<Utf8JsonWriter> writeParameters,
        CancellationToken cancellationToken)
    {
        // Reverse RPC belongs to Control IPC: the host must answer it even when
        // Event IPC is disconnected. All one-way progress/output belongs to Event IPC.
        if (eventName is "agent/reverse-request" or "agent/reverse-cancel")
        {
            return PublishControlAsync(eventName, writeParameters, cancellationToken);
        }

        var payload = MessagePackFrameProtocol.EncodeEvent(eventName, writeParameters);
        return PublishEventAsync(
            new WorkerMessagePackEvent(eventName, payload),
            cancellationToken);
    }

}
