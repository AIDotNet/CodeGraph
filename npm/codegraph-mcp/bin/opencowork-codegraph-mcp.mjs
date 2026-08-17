#!/usr/bin/env node

import { spawn } from 'node:child_process'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const serverAssembly = path.join(packageRoot, 'dist', 'OpenCowork.CodeGraph.Mcp.dll')
const child = spawn('dotnet', [serverAssembly, ...process.argv.slice(2)], {
  env: process.env,
  stdio: 'inherit',
  windowsHide: true
})
const forwardedSignals = ['SIGINT', 'SIGTERM', 'SIGHUP']
const signalHandlers = new Map(
  forwardedSignals.map((signal) => [signal, () => child.kill(signal)])
)
let spawnFailed = false

for (const [signal, handler] of signalHandlers) {
  process.on(signal, handler)
}

child.once('error', (error) => {
  spawnFailed = true
  console.error(`[opencowork-codegraph-mcp] Could not start dotnet: ${error.message}`)
})

child.once('close', (code, signal) => {
  for (const [forwardedSignal, handler] of signalHandlers) {
    process.off(forwardedSignal, handler)
  }

  if (spawnFailed) {
    process.exitCode = 1
    return
  }

  if (signal) {
    process.kill(process.pid, signal)
    return
  }

  process.exitCode = code ?? 1
})
