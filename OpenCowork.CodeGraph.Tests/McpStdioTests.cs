using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Xunit;

public sealed class McpStdioTests
{
    [Fact]
    public async Task Stdio_Initializes_ListsTools_AndReportsUnknownTool()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"codegraph-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        try
        {
            var toServer = new Pipe();
            var fromServer = new Pipe();
            await using var serverInput = toServer.Reader.AsStream();
            await using var serverOutput = fromServer.Writer.AsStream();
            await using var clientOutput = toServer.Writer.AsStream();
            await using var clientInput = fromServer.Reader.AsStream();
            using var responseReader = new StreamReader(clientInput, Encoding.UTF8, leaveOpen: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var serverTask = CodeGraphMcpServer.RunAsync(projectRoot, serverInput, serverOutput, timeout.Token);
            await WriteMessage(
                clientOutput,
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{" +
                    "\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"codegraph-tests\",\"version\":\"1.0\"}}}",
                timeout.Token);

            var responses = new[]
            {
                await ReadResponse(responseReader, timeout.Token)
            };
            await WriteMessage(
                clientOutput,
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                timeout.Token);
            await WriteMessage(
                clientOutput,
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}",
                timeout.Token);
            await WriteMessage(
                clientOutput,
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{" +
                    "\"name\":\"codegraph_missing\",\"arguments\":{}}}",
                timeout.Token);
            responses =
            [
                responses[0],
                await ReadResponse(responseReader, timeout.Token),
                await ReadResponse(responseReader, timeout.Token)
            ];

            await clientOutput.DisposeAsync();
            await serverTask;
            try
            {
                var initialize = FindResponse(responses, 1).RootElement.GetProperty("result");
                Assert.Equal("OpenCowork.CodeGraph", initialize.GetProperty("serverInfo").GetProperty("name").GetString());
                Assert.Equal("1.0.0", initialize.GetProperty("serverInfo").GetProperty("version").GetString());
                Assert.True(initialize.GetProperty("capabilities").TryGetProperty("tools", out _));
                Assert.Contains(
                    "automatically starts indexing",
                    initialize.GetProperty("instructions").GetString(),
                    StringComparison.Ordinal);

                var tools = FindResponse(responses, 2).RootElement
                    .GetProperty("result")
                    .GetProperty("tools");
                var explore = Assert.Single(tools.EnumerateArray());
                Assert.Equal("codegraph_explore", explore.GetProperty("name").GetString());
                Assert.True(explore.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
                Assert.Equal("object", explore.GetProperty("inputSchema").GetProperty("type").GetString());

                var unknown = FindResponse(responses, 3).RootElement.GetProperty("result");
                Assert.True(unknown.GetProperty("isError").GetBoolean());
                Assert.Contains(
                    "Unknown CodeGraph tool",
                    unknown.GetProperty("content")[0].GetProperty("text").GetString(),
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public void NuGetMcpManifest_IdentifiesTheDotnetToolAndStdioTransport()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "McpPackage", "server.json");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = manifest.RootElement;

        Assert.Equal("com.aidotnet/codegraph", root.GetProperty("name").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());

        var package = Assert.Single(root.GetProperty("packages").EnumerateArray());
        Assert.Equal("nuget", package.GetProperty("registryType").GetString());
        Assert.Equal("OpenCowork.CodeGraph.Mcp", package.GetProperty("identifier").GetString());
        Assert.Equal(root.GetProperty("version").GetString(), package.GetProperty("version").GetString());
        Assert.Equal("stdio", package.GetProperty("transport").GetProperty("type").GetString());

        var projectArgument = Assert.Single(package.GetProperty("packageArguments").EnumerateArray());
        Assert.Equal("--project", projectArgument.GetProperty("name").GetString());
        Assert.Equal("filepath", projectArgument.GetProperty("format").GetString());
    }

    private static async Task WriteMessage(Stream stream, string json, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(json + '\n'), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponse(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        Assert.NotNull(line);
        return JsonDocument.Parse(line);
    }

    private static JsonDocument FindResponse(JsonDocument[] responses, int id) =>
        Assert.Single(
            responses,
            response => response.RootElement.GetProperty("id").GetInt32() == id);
}
