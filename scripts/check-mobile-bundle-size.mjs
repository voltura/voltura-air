import { readdir, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const maximumRawJavaScriptBytes = 568 * 1024;
const maximumBrotliJavaScriptBytes = 136 * 1024;
const assetsDirectory = fileURLToPath(new URL("../apps/mobile-web/dist/assets/", import.meta.url));
const entries = await readdir(assetsDirectory, { withFileTypes: true });
const javaScriptFiles = entries
  .filter((entry) => entry.isFile() && entry.name.endsWith(".js"))
  .map((entry) => entry.name);

if (javaScriptFiles.length === 0) {
  throw new Error("The mobile bundle contains no JavaScript assets. Build the mobile client before checking its bundle.");
}

let rawBytes = 0;
let brotliBytes = 0;
for (const file of javaScriptFiles) {
  rawBytes += (await stat(path.join(assetsDirectory, file))).size;
  brotliBytes += (await stat(path.join(assetsDirectory, `${file}.br`))).size;
}

console.log(
  `Mobile JavaScript: ${formatKilobytes(rawBytes)} raw, ${formatKilobytes(brotliBytes)} Brotli ` +
  `(${javaScriptFiles.length} asset${javaScriptFiles.length === 1 ? "" : "s"}).`
);

const findings = [];
if (rawBytes > maximumRawJavaScriptBytes) {
  findings.push(`raw JavaScript exceeds ${formatKilobytes(maximumRawJavaScriptBytes)}`);
}
if (brotliBytes > maximumBrotliJavaScriptBytes) {
  findings.push(`Brotli JavaScript exceeds ${formatKilobytes(maximumBrotliJavaScriptBytes)}`);
}
if (findings.length > 0) {
  throw new Error(`Mobile bundle budget exceeded: ${findings.join("; ")}. Review ownership or deliberately revise the measured budget.`);
}

function formatKilobytes(bytes) {
  return `${(bytes / 1024).toFixed(1)} KB`;
}
