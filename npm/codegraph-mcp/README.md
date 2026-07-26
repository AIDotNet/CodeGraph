# OpenCowork CodeGraph MCP

Code intelligence over MCP: symbol search, source context, callers/callees, call paths,
impact analysis, and indexed file browsing.

## Requirements

- Node.js 18 or later
- .NET 10 runtime
- macOS, Linux, or Windows on x64/arm64

## Run with npx

```bash
npx -y @aidotnet/codegraph-mcp@1.0.0 \
  --project /absolute/path/to/project
```

MCP client configuration:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "npx",
      "args": [
        "-y",
        "@aidotnet/codegraph-mcp@1.0.0",
        "--project",
        "/absolute/path/to/project"
      ]
    }
  }
}
```

`--project` is optional. The server falls back to `CODEGRAPH_PROJECT_PATH`, then the
process working directory. Set `CODEGRAPH_MCP_TOOLS` to a comma-separated tool allowlist;
without it, the advertised surface contains only `codegraph_explore`.

The first tool call automatically creates the local index. Large projects may return an
index-in-progress message; retry after indexing finishes.

## License

MIT
