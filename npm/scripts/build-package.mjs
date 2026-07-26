import { spawn } from "node:child_process";
import { cp, mkdir, readdir, rm } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, "..", "..");
const sourceDirectory = path.join(repositoryRoot, "npm", "codegraph-mcp");
const artifactsDirectory = path.join(repositoryRoot, "artifacts", "npm");
const publishDirectory = path.join(artifactsDirectory, "dotnet-publish");
const packageDirectory = path.join(artifactsDirectory, "package");
const tarballDirectory = path.join(artifactsDirectory, "tarballs");
const supportedRuntimeDirectories = new Set([
  "linux-arm64",
  "linux-x64",
  "osx-arm64",
  "osx-x64",
  "win",
  "win-arm64",
  "win-x64"
]);
const unusedGrammarNames = [
  "agda",
  "css",
  "embedded-template",
  "html",
  "jsdoc",
  "json",
  "ocaml",
  "ocaml-type",
  "ql",
  "toml",
  "tsq",
  "verilog"
];

await rm(artifactsDirectory, { recursive: true, force: true });
await mkdir(publishDirectory, { recursive: true });

await run("dotnet", [
  "publish",
  "OpenCowork.CodeGraph.Mcp/OpenCowork.CodeGraph.Mcp.csproj",
  "-c",
  "Release",
  "--no-self-contained",
  "-o",
  publishDirectory,
  "/p:PublishAot=false",
  "/p:UseAppHost=false",
  "/p:DebugSymbols=false",
  "/p:DebugType=None"
]);

await mkdir(path.join(packageDirectory, "bin"), { recursive: true });
await mkdir(path.join(packageDirectory, ".mcp"), { recursive: true });
await cp(
  path.join(sourceDirectory, "package.json"),
  path.join(packageDirectory, "package.json")
);
await cp(
  path.join(sourceDirectory, "README.md"),
  path.join(packageDirectory, "README.md")
);
await cp(
  path.join(repositoryRoot, "LICENSE"),
  path.join(packageDirectory, "LICENSE")
);
await cp(
  path.join(sourceDirectory, "bin", "opencowork-codegraph-mcp.mjs"),
  path.join(packageDirectory, "bin", "opencowork-codegraph-mcp.mjs")
);
await cp(
  path.join(sourceDirectory, ".mcp", "server.json"),
  path.join(packageDirectory, ".mcp", "server.json")
);
await cp(publishDirectory, path.join(packageDirectory, "dist"), {
  recursive: true,
  filter: source => !source.endsWith(".pdb")
});

const runtimeRoot = path.join(packageDirectory, "dist", "runtimes");
for (const entry of await readdir(runtimeRoot, { withFileTypes: true })) {
  if (entry.isDirectory() && !supportedRuntimeDirectories.has(entry.name)) {
    await rm(path.join(runtimeRoot, entry.name), { recursive: true, force: true });
  }
}

for (const runtimeName of supportedRuntimeDirectories) {
  if (runtimeName === "win") {
    continue;
  }

  const nativeDirectory = path.join(runtimeRoot, runtimeName, "native");
  for (const entry of await readdir(nativeDirectory, { withFileTypes: true })) {
    const lowerName = entry.name.toLowerCase();
    if (entry.isFile() && unusedGrammarNames.some(
      grammar => lowerName.includes(`tree-sitter-${grammar}.`)
    )) {
      await rm(path.join(nativeDirectory, entry.name), { force: true });
    }
  }
}

await mkdir(tarballDirectory, { recursive: true });
console.error(`Staged npm package at ${packageDirectory}`);

function run(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: repositoryRoot,
      env: process.env,
      stdio: "inherit"
    });
    child.on("error", reject);
    child.on("exit", code => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`${command} exited with code ${code ?? "unknown"}`));
      }
    });
  });
}
