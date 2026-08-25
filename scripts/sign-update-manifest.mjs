import { createHash, createPrivateKey, createPublicKey, sign, verify } from "node:crypto";
import { readFile, stat, writeFile } from "node:fs/promises";
import path from "node:path";

const [version, publishRoot] = process.argv.slice(2);
if (!/^\d+\.\d+\.\d+$/u.test(version ?? "") || !publishRoot) throw new Error("Usage: node scripts/sign-update-manifest.mjs VERSION PUBLISH_ROOT");
const keyPath = process.env.VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH;
if (!keyPath) throw new Error("VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH is required to sign update manifests.");
const configuredPassphrase = process.env.VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE;
async function readPassphrase() {
  if (!process.stdin.isTTY) throw new Error("An authorized interactive terminal is required for the update signing passphrase.");
  process.stdout.write("Update signing passphrase: ");
  process.stdin.setRawMode(true);
  process.stdin.resume();
  return await new Promise((resolve) => {
    let value = "";
    process.stdin.on("data", (chunk) => {
      const character = chunk.toString("utf8");
      if (character === "\r" || character === "\n") {
        process.stdin.setRawMode(false); process.stdin.pause(); process.stdout.write("\n"); resolve(value);
      } else if (character === "\u0003") {
        process.stdin.setRawMode(false); process.exitCode = 130; resolve("");
      } else if (character === "\u007f" || character === "\b") value = value.slice(0, -1);
      else value += character;
    });
  });
}
const passphrase = configuredPassphrase === undefined ? await readPassphrase() : configuredPassphrase;
if (!passphrase.trim()) throw new Error("No update signing passphrase was provided.");
const installers = [
  `VolturaAir-Setup-${version}-win-x64.exe`,
  `VolturaAir-Setup-${version}-win-x64-full.exe`
];
const assets = [];
for (const name of installers) {
  const file = path.join(publishRoot, name);
  const details = await stat(file);
  assets.push({ name, size: details.size, sha256: createHash("sha256").update(await readFile(file)).digest("hex") });
}
const bytes = Buffer.from(JSON.stringify({ schema: 1, version, assets }), "utf8");
const privateKey = createPrivateKey({ key: await readFile(keyPath, "utf8"), passphrase });
const signature = sign("sha256", bytes, { key: privateKey, padding: 6, saltLength: 32 });
const publicKey = createPublicKey(await readFile(path.resolve("apps/windows-host/Features/Updates/update-signing-public.pem"), "utf8"));
if (!verify("sha256", bytes, { key: publicKey, padding: 6, saltLength: 32 }, signature)) throw new Error("Update manifest does not verify with the host public key.");
await writeFile(path.join(publishRoot, `VolturaAir-Update-${version}.json`), bytes);
await writeFile(path.join(publishRoot, `VolturaAir-Update-${version}.sig`), signature);
