// Copies the SPA build (build/client) into the API's wwwroot so the API
// serves the UI from a single origin. Node fs keeps it platform-independent.
import { cpSync, existsSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = join(webRoot, "build", "client");
const target = resolve(webRoot, "..", "api", "wwwroot");

if (!existsSync(join(source, "index.html"))) {
  console.error(`copy-to-api: ${join(source, "index.html")} not found; run "react-router build" first.`);
  process.exit(1);
}

rmSync(target, { recursive: true, force: true });
cpSync(source, target, { recursive: true });

console.log(`copy-to-api: copied ${source} -> ${target}`);
