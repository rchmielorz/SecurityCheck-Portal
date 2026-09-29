// Copies the SPA build (build/client) into the API's wwwroot so the API
// serves the UI from a single origin. Node fs keeps it platform-independent.
import { cpSync, existsSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = join(webRoot, "build", "client");
const target = resolve(webRoot, "..", "api", "wwwroot");

if (!existsSync(join(source, "index.html"))) {
  console.error(`copy-to-api: ${join(source, "index.html")} not found; run "react-router build" first.`);
  process.exit(1);
}

// The dev-only /styleguide route must not ship. routes.ts drops it when NODE_ENV is
// "production" (the default for "react-router build"); fail here if a build slipped through.
function findStyleguide(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      const hit = findStyleguide(path);
      if (hit) return hit;
    } else if (/.(html|js|css|json)$/.test(entry.name) && /styleguide/i.test(readFileSync(path, "utf8"))) {
      return path;
    }
  }
  return null;
}

const leaked = findStyleguide(source);
if (leaked) {
  console.error(`copy-to-api: dev-only styleguide found in ${leaked}; rebuild with NODE_ENV=production.`);
  process.exit(1);
}

rmSync(target, { recursive: true, force: true });
cpSync(source, target, { recursive: true });

console.log(`copy-to-api: copied ${source} -> ${target}`);
