// Test harness only. Owns every worker it starts; no application control endpoint.
import { spawn } from "node:child_process";
import { once } from "node:events";
import { readFile, appendFile } from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";
import path from "node:path";
import { createWriteStream } from "node:fs";
import { writeControlJson } from "./system-control-files.mjs";

const [directory, dotnet, workerDll] = process.argv.slice(2);
if (!directory || !dotnet || !workerDll || process.env.DOTNET_ENVIRONMENT !== "Test"
    || process.env.SimulationDispatch__Enabled !== "true" || process.env.SimulationEscalation__Enabled !== "true") {
  throw new Error("The worker supervisor requires the isolated Test harness with both simulations enabled.");
}
const children = [];
let generation = 0;
async function stop() {
  for (const child of children) {
    if (child.exitCode !== null || child.signalCode !== null) continue;
    const exited = once(child, "exit");
    child.kill("SIGTERM");
    await Promise.race([exited, delay(10_000, undefined, { ref: false })]);
    if (child.exitCode === null && child.signalCode === null) {
      child.kill("SIGKILL");
      await Promise.race([exited, delay(5_000, undefined, { ref: false })]);
    }
    if (child.exitCode === null && child.signalCode === null) throw new Error("Owned worker did not exit.");
  }
}
async function start(count) {
  await stop();
  for (let i = 0; i < count; i++) {
    const child = spawn(dotnet, [workerDll], { windowsHide: true, stdio: ["ignore", "pipe", "pipe"], env: process.env });
    await once(child, "spawn");
    children.push(child);
    const output = createWriteStream(path.join(directory, `worker-${child.pid}.log`));
    child.stdout.pipe(output, { end: false });
    child.stderr.pipe(output, { end: false });
    child.once("close", () => output.end());
    // PID ledger is written before acknowledging the transition and survives supervisor failure.
    await appendFile(path.join(directory, "owned-pids.txt"), `${child.pid}\n`);
  }
  generation++;
  await delay(500);
  if (children.filter(c => c.exitCode === null && c.signalCode === null).length !== count) throw new Error("Worker startup failed.");
}
async function status(id, error) {
  const value = { id, generation, pids: children.filter(c => c.exitCode === null && c.signalCode === null).map(c => c.pid),
    dispatchEnabled: true, escalationEnabled: true, error };
  await writeControlJson(directory, "status", value);
}
let lastId;
try {
  await start(1);
  await status("ready");
  while (true) {
    let request;
    try { request = JSON.parse(await readFile(path.join(directory, "request.json"), "utf8")); }
    catch (error) { if (error.code !== "ENOENT") throw error; }
    if (request && request.id !== lastId) {
      lastId = request.id;
      if (!["stop", "one", "two", "shutdown"].includes(request.command)) throw new Error("Unknown worker command.");
      if (request.command === "stop" || request.command === "shutdown") await stop();
      else await start(request.command === "two" ? 2 : 1);
      await status(lastId);
      if (request.command === "shutdown") break;
    }
    await delay(50);
  }
} catch (error) {
  await status(lastId ?? "startup", String(error));
  process.exitCode = 1;
} finally {
  await stop();
}
