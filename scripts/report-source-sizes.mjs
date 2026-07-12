import { readdir, stat } from "node:fs/promises";
import path from "node:path";

const root = process.cwd();
const targetBytes = 20 * 1024;
const warningBytes = 25 * 1024;
const sourceExtensions = new Set([
  ".cs", ".css", ".html", ".js", ".jsx", ".mjs", ".nsi", ".php",
  ".ps1", ".ts", ".tsx", ".xaml"
]);
const excludedDirectories = new Set([
  ".git", ".vs", "artifacts", "bin", "coverage", "dist", "node_modules", "obj"
]);
const excludedPathPrefixes = [
  "apps/mobile-web/public/",
  "apps/windows-host/wwwroot/",
  "docs/site/assets/",
  "installer/assets/"
];

async function collect(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    if (entry.isDirectory() && excludedDirectories.has(entry.name)) {
      continue;
    }

    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collect(absolutePath));
      continue;
    }

    const relativePath = path.relative(root, absolutePath).split(path.sep).join("/");
    if (!sourceExtensions.has(path.extname(entry.name).toLowerCase()) ||
        excludedPathPrefixes.some((prefix) => relativePath.startsWith(prefix)) ||
        /(?:^|\/)(?:[^/]+\.g\.cs|[^/]+\.generated\.[^/]+)$/.test(relativePath)) {
      continue;
    }

    const metadata = await stat(absolutePath);
    if (metadata.size > targetBytes) {
      files.push({ relativePath, size: metadata.size });
    }
  }

  return files;
}

const oversized = (await collect(root)).sort((left, right) => right.size - left.size);

if (oversized.length === 0) {
  console.log("All actively maintained source files are at or below 20 KB.");
} else {
  console.log("Actively maintained source files above 20 KB (report only):");
  for (const file of oversized) {
    const marker = file.size > warningBytes ? "WARNING >25 KB" : "review";
    console.log(`${(file.size / 1024).toFixed(1).padStart(6)} KB  ${marker.padEnd(14)}  ${file.relativePath}`);
  }
}
