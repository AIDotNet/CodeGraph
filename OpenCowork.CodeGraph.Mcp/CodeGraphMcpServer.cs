using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

// Standard MCP adapter for the dedicated stdio executable. The CodeGraph core
// remains the source of truth for tool visibility and execution; this class only
// maps its DTOs to the official MCP C# SDK types and supplies a default projectPath.
//
// stdout is transport-owned. Host logs are forced to stderr so a diagnostic can
// never corrupt the newline-delimited JSON-RPC stream.
internal static class CodeGraphMcpServer
{
    private const string ServerName = "OpenCowork.CodeGraph";
    private const string ServerVersion = "1.0.0";
    private const string UnindexedInstructions =
        "# CodeGraph\n\n"
        + "CodeGraph builds a symbol graph of the configured project. The first tool call "
        + "automatically starts indexing; on a large project, retry the call if indexing is "
        + "still in progress. Prefer `codegraph_explore` over Read/Grep once the index is ready.";

    public static async Task RunAsync(
        string projectRoot,
        Stream? input = null,
        Stream? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        projectRoot = Path.GetFullPath(projectRoot);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
            options.LogToStandardErrorThreshold = LogLevel.Trace);

        var mcp = builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = ServerName,
                    Title = "OpenCowork CodeGraph",
                    Version = ServerVersion
                };
                options.ServerInstructions = CodeGraphEngine.IsInitialized(projectRoot)
                    ? CodeGraphInstructions.Indexed
                    : UnindexedInstructions;
            })
            .WithListToolsHandler((_, _) =>
                ValueTask.FromResult(ListTools(projectRoot)))
            .WithCallToolHandler((request, _) =>
                ValueTask.FromResult(CallTool(projectRoot, request.Params)));

        if (input is null && output is null)
        {
            mcp.WithStdioServerTransport();
        }
        else if (input is not null && output is not null)
        {
            mcp.WithStreamServerTransport(input, output);
        }
        else
        {
            throw new ArgumentException("MCP input and output streams must be supplied together.");
        }

        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ListToolsResult ListTools(string projectRoot)
    {
        using var args = ToolArguments(arguments: null, projectRoot);
        var definitions = CodeGraphToolHandler.ToolsList(args.RootElement).Tools;
        return new ListToolsResult
        {
            Tools = definitions.Select(ToMcpTool).ToList()
        };
    }

    private static Tool ToMcpTool(CodeGraphToolDefinition definition)
    {
        return new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = JsonSerializer.SerializeToElement(
                definition.InputSchema,
                CodeGraphJsonContext.Default.CodeGraphToolInputSchema),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = definition.Annotations.ReadOnlyHint,
                DestructiveHint = definition.Annotations.DestructiveHint,
                IdempotentHint = definition.Annotations.IdempotentHint,
                OpenWorldHint = definition.Annotations.OpenWorldHint
            }
        };
    }

    private static CallToolResult CallTool(string projectRoot, CallToolRequestParams? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return ToolError("A tool name is required.");
        }

        using var args = ToolArguments(request.Arguments, projectRoot);
        var result = request.Name switch
        {
            "codegraph_explore" => CodeGraphToolHandler.Explore(args.RootElement),
            "codegraph_search" => CodeGraphToolHandler.Search(args.RootElement),
            "codegraph_node" => CodeGraphToolHandler.Node(args.RootElement),
            "codegraph_callers" => CodeGraphToolHandler.Callers(args.RootElement),
            "codegraph_callees" => CodeGraphToolHandler.Callees(args.RootElement),
            "codegraph_impact" => CodeGraphToolHandler.Impact(args.RootElement),
            "codegraph_files" => CodeGraphToolHandler.Files(args.RootElement),
            "codegraph_status" => CodeGraphToolHandler.Status(args.RootElement),
            _ => null
        };

        return result is null
            ? ToolError($"Unknown CodeGraph tool: {request.Name}")
            : new CallToolResult
            {
                Content = [new TextContentBlock { Text = result.Text }],
                IsError = result.IsError
            };
    }

    private static CallToolResult ToolError(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true
    };

    // Convert the SDK's argument dictionary to the raw JsonElement consumed by the
    // existing handlers. An explicit projectPath/workingFolder always wins; otherwise
    // the server's configured project is appended without mutating the client payload.
    private static JsonDocument ToolArguments(
        IDictionary<string, JsonElement>? arguments,
        string projectRoot)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            var hasProject = false;
            if (arguments is not null)
            {
                foreach (var (name, value) in arguments)
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                    hasProject |= string.Equals(name, "projectPath", StringComparison.Ordinal)
                        || string.Equals(name, "workingFolder", StringComparison.Ordinal);
                }
            }

            if (!hasProject)
            {
                writer.WriteString("projectPath", projectRoot);
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }
}
