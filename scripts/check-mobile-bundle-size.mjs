import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const maximumRawJavaScriptBytes = 568 * 1024;
const maximumBrotliJavaScriptBytes = 136 * 1024;
const distDirectory = fileURLToPath(new URL("../apps/mobile-web/dist/", import.meta.url));
const assetsDirectory = fileURLToPath(new URL("../apps/mobile-web/dist/assets/", import.meta.url));
const indexHtml = await readFile(path.join(distDirectory, "index.html"), "utf8");
const entries = await readdir(assetsDirectory, { withFileTypes: true });
const javaScriptFiles = entries
  .filter((entry) => entry.isFile() && entry.name.endsWith(".js"))
  .map((entry) => entry.name);
const initialJavaScriptFiles = Array.from(
  indexHtml.matchAll(/<script\b[^>]*\bsrc="\/assets\/([^"]+\.js)"/gu),
  (match) => match[1]
);

if (javaScriptFiles.length === 0) {
  throw new Error("The mobile bundle contains no JavaScript assets. Build the mobile client before checking its bundle.");
}

if (initialJavaScriptFiles.length === 0) {
  throw new Error("The mobile bundle index references no initial JavaScript assets.");
}

const initialSizes = await sumJavaScriptSizes(initialJavaScriptFiles);
const totalSizes = await sumJavaScriptSizes(javaScriptFiles);
console.log(
  `Mobile initial JavaScript: ${formatKilobytes(initialSizes.rawBytes)} raw, ${formatKilobytes(initialSizes.brotliBytes)} Brotli ` +
  `(${initialJavaScriptFiles.length} asset${initialJavaScriptFiles.length === 1 ? "" : "s"}). ` +
  `Total emitted JavaScript: ${formatKilobytes(totalSizes.rawBytes)} raw, ${formatKilobytes(totalSizes.brotliBytes)} Brotli ` +
  `(${javaScriptFiles.length} asset${javaScriptFiles.length === 1 ? "" : "s"}).`
);

const findings = [];
if (initialSizes.rawBytes > maximumRawJavaScriptBytes) {
  findings.push(`raw JavaScript exceeds ${formatKilobytes(maximumRawJavaScriptBytes)}`);
}
if (initialSizes.brotliBytes > maximumBrotliJavaScriptBytes) {
  findings.push(`Brotli JavaScript exceeds ${formatKilobytes(maximumBrotliJavaScriptBytes)}`);
}
if (findings.length > 0) {
  throw new Error(`Mobile initial bundle budget exceeded: ${findings.join("; ")}. Review ownership or deliberately revise the measured budget.`);
}

async function sumJavaScriptSizes(files) {
  let rawBytes = 0;
  let brotliBytes = 0;
  for (const file of files) {
    rawBytes += (await stat(path.join(assetsDirectory, file))).size;
    brotliBytes += (await stat(path.join(assetsDirectory, `${file}.br`))).size;
  }

  return { brotliBytes, rawBytes };
}

function formatKilobytes(bytes) {
  return `${(bytes / 1024).toFixed(1)} KB`;
}
