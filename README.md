# CodeGraph

A code-structure index and query engine for large repositories, built as an AOT-compiled
.NET sidecar. It parses a project with tree-sitter, stores symbols/edges in SQLite, and
exposes a graph query surface (search, callers, callees, call paths, context packs) over a
length-prefixed MessagePack IPC protocol.

Extracted from [OpenCowork](https://github.com/AIDotNet/OpenCowork), where it is consumed as
a git submodule.

## Layout

| Project | Purpose |
| --- | --- |
| `OpenCowork.Worker.Runtime` | Shared worker runtime: IPC transport, dispatch, host builder, `SystemModule`. Single source of truth for the worker protocol. |
| `OpenCowork.CodeGraph.Core` | The engine — scanning, extraction, storage, traversal, query, and the `codegraph/*` module + MCP tool surface. |
| `OpenCowork.CodeGraph.Worker` | Thin AOT executable hosting `SystemModule` + `CodeGraphModule`. |
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
```

Requires the .NET 10 SDK. Supported RIDs: `osx-arm64`, `osx-x64`, `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64`.

## Running

```bash
# Self-test: proves SQLite FTS5 + the tree-sitter binding in this binary, then exits.
./OpenCowork.CodeGraph.Worker

# IPC worker mode.
./OpenCowork.CodeGraph.Worker --ipc <endpoint>
```

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
