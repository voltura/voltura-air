import { existsSync, readdirSync } from "node:fs";
import { join, relative } from "node:path";

const roots = process.argv.slice(2);

if (roots.length === 0) {
  console.error("Usage: node scripts/verify-web-compression.mjs <web-root> [web-root...]");
  process.exit(1);
}

let missing = 0;
let verified = 0;

for (const root of roots) {
  if (!existsSync(root)) {
    console.error(`Missing web asset root: ${root}`);
    missing += 1;
    continue;
  }

  for (const file of findJavaScriptFiles(root)) {
    const brotliPath = `${file}.br`;
    const gzipPath = `${file}.gz`;
    const displayPath = relative(process.cwd(), file);
    const hasBrotli = existsSync(brotliPath);
    const hasGzip = existsSync(gzipPath);

    if (!hasBrotli || !hasGzip) {
      console.error(`${displayPath}: missing${hasBrotli ? "" : " .br"}${hasGzip ? "" : " .gz"}`);
      missing += 1;
      continue;
    }

    console.log(`${displayPath}: found .br and .gz`);
    verified += 1;
  }
}

if (verified === 0) {
  console.error("No JavaScript assets were found to verify.");
  process.exit(1);
}

if (missing > 0) {
  process.exit(1);
}

console.log(`Verified compressed assets for ${verified} JavaScript file(s).`);

function findJavaScriptFiles(directory) {
  const files = [];

  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...findJavaScriptFiles(fullPath));
      continue;
    }

    if (entry.isFile() && fullPath.endsWith(".js")) {
      files.push(fullPath);
    }
  }

  return files;
}
