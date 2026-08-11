using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading.Channels;

/// <summary>
/// Independent, one-way Event IPC. Writes are buffered behind a bounded in-memory wake queue;
/// the durable outbox remains authoritative. Backpressure or disconnect on this socket never
/// blocks Control IPC and never marks the worker unhealthy.
/// </summary>
internal sealed class LocalIpcWorkerEventServer
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);
    private const int QueueCapacity = 1024;
    private const int MaxFrameBytes = 256 * 1024 * 1024;
    private const long MaxBufferedBytes = 16L * 1024 * 1024;

    private readonly string address;
    private readonly Channel<ReadOnlyMemory<byte>> queue = Channel.CreateBounded<ReadOnlyMemory<byte>>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            // Publish uses TryWrite exclusively. Wait mode makes TryWrite return false
            // when the queue is full, so the durable outbox does not mark a dropped
            // batch as in-flight. No caller waits for queue capacity.
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private long droppedFrames;
    private long bufferedBytes;
    // Retained across Event socket reconnects. A partial/failed write must be
    // retried before later queued frames or ACK-through could skip a sequence.
    private ReadOnlyMemory<byte>? pendingFrame;

    public LocalIpcWorkerEventServer(string address)
    {
        this.address = address;
    }

    public ValueTask PublishAsync(
        WorkerMessagePackEvent messagePackEvent,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        TryPublish(messagePackEvent);
        return ValueTask.CompletedTask;
    }

    public bool TryPublish(WorkerMessagePackEvent messagePackEvent)
    {
        if (messagePackEvent.Payload.IsEmpty)
        {
            return true;
        }
        if (messagePackEvent.Payload.Length > MaxFrameBytes)
        {
            LogDropped(messagePackEvent);
            return false;
        }

        if (!TryReserveBytes(messagePackEvent.Payload.Length))
        {
            LogDropped(messagePackEvent);
            return false;
        }

        if (!queue.Writer.TryWrite(messagePackEvent.Payload))
        {
            ReleaseBytes(messagePackEvent.Payload.Length);
            LogDropped(messagePackEvent);
            return false;
        }

        return true;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        return OperatingSystem.IsWindows()
            ? RunNamedPipeAsync(cancellationToken)
            : RunUnixSocketAsync(cancellationToken);
    }

    private async Task RunNamedPipeAsync(CancellationToken cancellationToken)
    {
        var pipeName = address.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase)
            ? address[@"\\.\pipe\".Length..]
            : address;

        WorkerLog.Info("event server listening transport=named-pipe");
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.Out,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                WorkerLog.Debug("event client connected transport=named-pipe");
                await PumpAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                WorkerLog.Warn($"event connection reset error={ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task RunUnixSocketAsync(CancellationToken cancellationToken)
    {
        TryDeleteSocketFile(address);
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(address));
        listener.Listen(backlog: 1);
        WorkerLog.Info("event server listening transport=unix-domain-socket");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptAsync(cancellationToken);
                    await using var stream = new NetworkStream(client, ownsSocket: false);
                    WorkerLog.Debug("event client connected transport=unix-domain-socket");
                    await PumpAsync(stream, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    WorkerLog.Warn($"event connection reset error={ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            TryDeleteSocketFile(address);
        }
    }

    private async Task PumpAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            pendingFrame ??= await queue.Reader.ReadAsync(cancellationToken);
            var pendingLength = pendingFrame.Value.Length;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(WriteTimeout);
            try
            {
                await MessagePackFrameProtocol.WriteFrameAsync(stream, pendingFrame.Value, deadline.Token);
                ReleaseBytes(pendingLength);
                pendingFrame = null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Event IPC write exceeded {WriteTimeout.TotalSeconds:0}s; reconnecting Event IPC.");
            }
        }
    }

    private bool TryReserveBytes(int length)
    {
        while (true)
        {
            var current = Volatile.Read(ref bufferedBytes);
            // One oversized frame may make progress when the queue is otherwise empty.
            if (current > 0 && current + length > MaxBufferedBytes)
            {
                return false;
            }
            if (Interlocked.CompareExchange(ref bufferedBytes, current + length, current) == current)
            {
                return true;
            }
        }
    }

    private void ReleaseBytes(int length)
    {
        Interlocked.Add(ref bufferedBytes, -length);
    }

    private void LogDropped(WorkerMessagePackEvent messagePackEvent)
    {
        var dropped = Interlocked.Increment(ref droppedFrames);
        WorkerLog.Warn(
            $"event ipc wake queue full; durable replay required " +
            $"event={messagePackEvent.EventName} bytes={messagePackEvent.Payload.Length} " +
            $"bufferedBytes={Volatile.Read(ref bufferedBytes)} dropped={dropped}");
    }

    private static void TryDeleteSocketFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Bind will report an actionable error when best-effort cleanup was insufficient.
        }
    }
}
