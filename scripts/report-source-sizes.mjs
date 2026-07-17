import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";

const root = process.cwd();
const reviewBytes = 12 * 1024;
const warningBytes = 20 * 1024;
const reviewLines = 300;
const warningLines = 500;
const sourceExtensions = new Set([
  ".cs", ".css", ".html", ".js", ".jsx", ".mjs", ".nsi", ".php",
  ".ps1", ".ts", ".tsx", ".xaml"
]);
const excludedDirectories = new Set([
  ".git", ".vs", "artifacts", "bin", "coverage", "dist", "node_modules", "obj"
]);
const excludedPathPrefixes = [
  "apps/mobile-web/public/",
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
    const contents = await readFile(absolutePath, "utf8");
    const lineCount = countLines(contents);
    if (metadata.size > reviewBytes || lineCount > reviewLines) {
      files.push({ relativePath, size: metadata.size, lineCount });
    }
  }

  return files;
}

const reviewCandidates = (await collect(root)).sort((left, right) => {
  const leftScore = Math.max(left.size / reviewBytes, left.lineCount / reviewLines);
  const rightScore = Math.max(right.size / reviewBytes, right.lineCount / reviewLines);
  return rightScore - leftScore;
});

if (reviewCandidates.length === 0) {
  console.log("All actively maintained source files are at or below 12 KB and 300 lines.");
} else {
  console.log("Actively maintained source files above 12 KB or 300 lines (review only):");
  for (const file of reviewCandidates) {
    const marker = file.size > warningBytes || file.lineCount > warningLines ? "WARNING" : "review";
    console.log(
      `${(file.size / 1024).toFixed(1).padStart(6)} KB  ${String(file.lineCount).padStart(5)} lines  ${marker.padEnd(7)}  ${file.relativePath}`
    );
  }
}

function countLines(contents) {
  if (contents.length === 0) {
    return 0;
  }

  const newlineCount = contents.match(/\r\n|\r|\n/gu)?.length ?? 0;
  return newlineCount + (/(?:\r\n|\r|\n)$/u.test(contents) ? 0 : 1);
}
