# CodeGraph

A code-structure index and query engine for large repositories, built as an AOT-compiled
.NET sidecar. It parses a project with tree-sitter, stores symbols/edges in SQLite, and
exposes a graph query surface (search, callers, callees, call paths, context packs) over a
length-prefixed MessagePack IPC protocol and a dedicated MCP stdio executable.

Extracted from [OpenCowork](https://github.com/AIDotNet/OpenCowork), where it is consumed as
a git submodule.

## Layout

| Project | Purpose |
| --- | --- |
| `OpenCowork.Worker.Runtime` | Shared worker runtime: split Control/Event IPC transport, dispatch, host builder, `SystemModule`. Single source of truth for worker protocol v2. |
| `OpenCowork.CodeGraph.Core` | The engine — scanning, extraction, storage, traversal, query, and the `codegraph/*` module + MCP tool surface. |
| `OpenCowork.CodeGraph.Worker` | Thin AOT executable hosting `SystemModule` + `CodeGraphModule`. |
| `OpenCowork.CodeGraph.Mcp` | Dedicated AOT executable exposing the CodeGraph tools as a standard MCP stdio server. |
| `OpenCowork.CodeGraph.Tests` | xUnit suite over Core. |

## Build

```bash
dotnet build CodeGraph.slnx -c Release
dotnet test  CodeGraph.slnx -c Release
```

AOT publish of the sidecar binary:

```bash
dotnet publish OpenCowork.CodeGraph.Worker/OpenCowork.CodeGraph.Worker.csproj \
  -c Release -r osx-arm64 -o out /p:PublishAot=true /p:StripSymbols=true

dotnet publish OpenCowork.CodeGraph.Mcp/OpenCowork.CodeGraph.Mcp.csproj \
  -c Release -r osx-arm64 -o out-mcp /p:PublishAot=true /p:StripSymbols=true
```

Requires the .NET 10 SDK. Supported RIDs: `osx-arm64`, `osx-x64`, `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64`.

## Running

```bash
# Self-test: proves SQLite FTS5 + the tree-sitter binding in this binary, then exits.
./OpenCowork.CodeGraph.Worker

# IPC worker mode.
./OpenCowork.CodeGraph.Worker \
  --control-ipc <control-endpoint> \
  --event-ipc <event-endpoint> \
  --host-id <stable-client-id>
```

Control and Event endpoints must be different. Both use four-byte-length-prefixed MessagePack.
Control carries requests, responses, health checks, and cancellation; Event carries one-way
progress. A blocked Event consumer therefore cannot block `worker/ping`. The standalone CodeGraph
worker keeps its CodeGraph routes inline; when CodeGraph is source-linked into
`OpenCowork.Native.Worker`, those long routes are registered with the Native Worker's durable Job
queue.

### MCP over stdio

`OpenCowork.CodeGraph.Mcp` is a separate executable dedicated to standard MCP stdio. It
uses the official MCP C# SDK, keeps stdout reserved for JSON-RPC, and exits when the client
closes stdin.

```bash
./OpenCowork.CodeGraph.Mcp --project /absolute/path/to/project
```

`--project` is optional. The default project is resolved in this order:
`--project`, `CODEGRAPH_PROJECT_PATH`, then the process working directory. A tool call may
still override it with its `projectPath` argument.

Example MCP client configuration:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "/absolute/path/to/OpenCowork.CodeGraph.Mcp",
      "args": ["--project", "/absolute/path/to/project"]
    }
  }
}
```

The advertised tools honor `CODEGRAPH_MCP_TOOLS`; when it is unset, the default MCP
surface contains only `codegraph_explore`.

### npm / npx package

The independent npm project under `npm/` packages the framework-dependent .NET MCP server
with native tree-sitter assets for macOS, Linux, and Windows on x64/arm64. The Node launcher
selects the current platform assets and starts the server over stdio; it does not rewrite
the MCP as JavaScript. End users need Node.js 18 or later and the .NET 10 runtime.

Run the published package without installing it globally:

```bash
npx -y @aidotnet/codegraph-mcp@1.0.0 \
  --project /absolute/path/to/project
```

Example MCP client configuration:

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

Build, inspect, and publish the npm package from this repository:

```bash
npm run build --prefix npm
npm run pack --prefix npm
npm publish artifacts/npm/tarballs/aidotnet-codegraph-mcp-1.0.0.tgz \
  --access public --otp <one-time-code>
```

For CI publishing, create a granular npm access token with read/write access to
`@aidotnet/codegraph-mcp` and **Bypass 2FA** enabled, save it as the repository secret
`NPM_TOKEN`, and run the `Publish npm MCP` workflow. Release tags must match all .NET, npm,
and MCP manifest versions.

### NuGet / dnx package

The MCP project packs as both a `DotnetTool` and an `McpServer`, including its MCP registry
manifest and cross-platform tree-sitter native assets:

```bash
dotnet pack OpenCowork.CodeGraph.Mcp/OpenCowork.CodeGraph.Mcp.csproj -c Release
dnx OpenCowork.CodeGraph.Mcp@1.0.0 --project /absolute/path/to/project
```

The `.nupkg` is written to `artifacts/packages`. To publish it after configuring a NuGet
API key:

```bash
dotnet nuget push artifacts/packages/OpenCowork.CodeGraph.Mcp.1.0.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY"
```

For CI publishing, add `NUGET_API_KEY` as a repository secret and run the
`Publish NuGet MCP` workflow. Release tags must match the project and manifest version.

### tree-sitter grammars

The engine resolves grammar shared libraries at startup from a `grammars/` directory beside
the executable, or from `OPEN_COWORK_CODEGRAPH_GRAMMARS_DIR`. Grammars are **not** committed
here — they come from the `TreeSitter.DotNet` NuGet package's RID-specific native assets.
The consuming application is responsible for staging them next to the published binary
(OpenCowork does this in `scripts/publish-native-worker.mjs` via `scripts/codegraph-grammar-manifest.mjs`).

## Consuming from OpenCowork

`OpenCowork.Worker.Runtime` is shared by two binaries: the `OpenCowork.CodeGraph.Worker` in
this repo (via `ProjectReference`) and OpenCowork's `OpenCowork.Native.Worker` (which
source-links `Runtime/`, `Hosting/`, `Contracts/` and `Modules/SystemModule.cs` directly, so
both speak an identical protocol). Renaming or moving anything under those paths is a
cross-repo change — update OpenCowork's `OpenCowork.Native.Worker.csproj` in the same pass.

## Conventions

Global namespace, all-internal types, `InternalsVisibleTo` between the assemblies. Sources
are heavily commented with design rationale; read the header block of a file before changing
it.

## License

MIT — see [LICENSE](LICENSE).
