internal sealed record ErrorResult(string Error);
internal sealed record StatusResult(bool Ok, int Pid);
internal sealed record WorkerRouteDescriptor(
    string Method,
    string ExecutionMode,
    string ResultMode,
    string? LanePolicy);
internal sealed record WorkerRoutesResult(string[] Methods, WorkerRouteDescriptor[] Routes);
internal sealed record WorkerHelloResult(bool Ok, int Pid, int ProtocolVersion, string? AppVersion);

internal static class WorkerProtocol
{
    // Bump on incompatible frame/dispatch contract changes; the Electron
    // supervisor refuses to run against a mismatched worker binary.
    public const int Version = 2;
}
