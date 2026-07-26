# OpenCowork CodeGraph MCP

A local MCP stdio server that indexes source code with tree-sitter and exposes symbol
search, call graphs, impact analysis, indexed file browsing, and source context.

## Run without installing

Requires the .NET 10 SDK:

```bash
dnx OpenCowork.CodeGraph.Mcp@1.0.0 --project /absolute/path/to/project
```

MCP client configuration:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "dnx",
      "args": [
        "OpenCowork.CodeGraph.Mcp@1.0.0",
        "--project",
        "/absolute/path/to/project"
      ]
    }
  }
}
```

## Install as a .NET tool

```bash
dotnet tool install --global OpenCowork.CodeGraph.Mcp
opencowork-codegraph-mcp --project /absolute/path/to/project
```

`--project` is optional. The server falls back to `CODEGRAPH_PROJECT_PATH`, then the
process working directory. Set `CODEGRAPH_MCP_TOOLS` to a comma-separated tool allowlist;
without it, the advertised surface contains only `codegraph_explore`.

The first tool call automatically creates the local index. Large projects may return an
index-in-progress message; retry the call after indexing finishes.

## Package locally

```bash
dotnet pack OpenCowork.CodeGraph.Mcp/OpenCowork.CodeGraph.Mcp.csproj -c Release
```

The package is written to `artifacts/packages` at the repository root.

To publish from GitHub, configure the repository secret `NUGET_API_KEY`, then publish a
release whose tag matches the package version (for example, `v1.0.0`) or run the
`Publish NuGet MCP` workflow manually.

## License

MIT
