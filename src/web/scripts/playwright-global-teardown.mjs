import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";

const pidFile = join(tmpdir(), "hospital-frontend-prototype-playwright-3101.pid");

export default async function globalTeardown() {
  if (!existsSync(pidFile)) {
    return;
  }

  const pid = Number.parseInt(readFileSync(pidFile, "utf8"), 10);
  rmSync(pidFile, { force: true });

  if (!Number.isInteger(pid) || pid <= 0 || pid === process.pid) {
    return;
  }

  try {
    if (process.platform === "win32") {
      execFileSync("taskkill.exe", ["/pid", String(pid), "/T", "/F"], {
        stdio: "ignore",
        windowsHide: true,
      });
    } else {
      process.kill(pid, "SIGTERM");
    }
  } catch {
    // Playwright usually kills the child process first. This handles Windows
    // shells that leave the owned Next process alive after the suite.
  }
}
