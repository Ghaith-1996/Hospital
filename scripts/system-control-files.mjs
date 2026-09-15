import { writeFile, rename } from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";

// Windows can briefly deny rename while the other harness process reads the old file.
// Retry only that filesystem contention; never expose partially written JSON to a reader.
export async function writeControlJson(directory, name, value) {
  const temporary = `${directory}/${name}.tmp`;
  const destination = `${directory}/${name}.json`;
  await writeFile(temporary, JSON.stringify(value));
  const deadline = Date.now() + 5_000;
  while (true) {
    try { await rename(temporary, destination); return; }
    catch (error) {
      if (!["EPERM", "EACCES", "EBUSY"].includes(error.code) || Date.now() >= deadline) throw error;
      await delay(20);
    }
  }
}
