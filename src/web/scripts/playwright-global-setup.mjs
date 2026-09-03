import { spawn } from "node:child_process";
import http from "node:http";
import { rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";

const hostname = "127.0.0.1";
const port = 3101;
const serverUrl = `http://${hostname}:${port}`;
const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const pidFile = join(tmpdir(), "hospital-frontend-prototype-playwright-3101.pid");

function delay(milliseconds) {
  return new Promise((resolveDelay) => {
    setTimeout(resolveDelay, milliseconds);
  });
}

function isServerAvailable() {
  return new Promise((resolveAvailability) => {
    let settled = false;
    const finish = (available) => {
      if (settled) {
        return;
      }
      settled = true;
      resolveAvailability(available);
    };

    const request = http.get(serverUrl, (response) => {
      response.resume();
      finish(response.statusCode >= 200 && response.statusCode < 500);
    });

    request.on("error", () => finish(false));
    request.setTimeout(1_000, () => {
      request.destroy();
      finish(false);
    });
  });
}

export default async function globalSetup() {
  rmSync(pidFile, { force: true });

  if (await isServerAvailable()) {
    throw new Error(
      `${serverUrl} is already used. Refusing to reuse an existing server for frontend prototype E2E.`,
    );
  }

  const child = spawn(process.execPath, ["scripts/playwright-next-dev.mjs"], {
    cwd: webRoot,
    env: {
      ...process.env,
      BROWSER: "none",
      CRITICAL_ALERTS_API_URL: "",
      FORCE_COLOR: "1",
    },
    shell: false,
    stdio: "ignore",
    windowsHide: true,
  });

  writeFileSync(pidFile, `${child.pid}\n`, "utf8");

  let exitCode = null;
  child.once("exit", (code) => {
    exitCode = code;
  });
  child.unref();

  const deadline = Date.now() + 120_000;
  while (Date.now() < deadline) {
    if (await isServerAvailable()) {
      return;
    }
    if (exitCode !== null) {
      throw new Error(`Frontend prototype E2E server exited before startup with code ${exitCode}.`);
    }
    await delay(250);
  }

  throw new Error(`Timed out waiting 120000ms for ${serverUrl}.`);
}
