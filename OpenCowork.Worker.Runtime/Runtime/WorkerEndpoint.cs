internal sealed record WorkerEndpoint(string ControlAddress, string EventAddress, string HostId)
{
    public static WorkerEndpoint Parse(string[] args)
    {
        var controlAddress = ReadArgument(args, "--control-ipc");
        var eventAddress = ReadArgument(args, "--event-ipc");
        var hostId = ReadArgument(args, "--host-id");

        if (string.IsNullOrWhiteSpace(controlAddress))
        {
            throw new ArgumentException(
                "Native worker requires --control-ipc <unix-socket-path|named-pipe-path>.");
        }

        if (string.IsNullOrWhiteSpace(eventAddress))
        {
            throw new ArgumentException(
                "Native worker requires --event-ipc <unix-socket-path|named-pipe-path>.");
        }

        if (string.IsNullOrWhiteSpace(hostId))
        {
            throw new ArgumentException("Native worker requires --host-id <stable-client-id>.");
        }

        if (string.Equals(controlAddress, eventAddress, StringComparison.Ordinal))
        {
            throw new ArgumentException("Control IPC and Event IPC endpoints must be different.");
        }

        return new WorkerEndpoint(controlAddress, eventAddress, hostId);
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new ArgumentException($"Missing value for {name}.");
            }

            return args[i + 1].Trim();
        }

        return null;
    }
}
