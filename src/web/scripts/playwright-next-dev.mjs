import { createServer } from "node:http";
import { rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import next from "next";

const hostname = "127.0.0.1";
const port = 3101;
const pidFile = join(tmpdir(), "hospital-frontend-prototype-playwright-3101.pid");
const app = next({ dev: true, dir: process.cwd(), hostname, port });
const handle = app.getRequestHandler();

let server;

async function shutdown() {
  rmSync(pidFile, { force: true });

  if (!server) {
    process.exit(0);
    return;
  }

  server.closeAllConnections?.();
  server.closeIdleConnections?.();
  server.close(async () => {
    try {
      await app.close();
    } finally {
      process.exit(0);
    }
  });
}

process.once("SIGINT", shutdown);
process.once("SIGTERM", shutdown);
process.once("SIGHUP", shutdown);
process.once("disconnect", shutdown);

app
  .prepare()
  .then(() => {
    writeFileSync(pidFile, `${process.pid}\n`, "utf8");
    server = createServer((request, response) => {
      handle(request, response);
    });
    server.listen(port, hostname, () => {
      console.log(`Playwright Next dev server ready at http://${hostname}:${port}`);
    });
  })
  .catch((error) => {
    console.error(error);
    process.exit(1);
  });
