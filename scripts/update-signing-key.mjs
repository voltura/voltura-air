import { createPrivateKey, createPublicKey, randomBytes, sign, verify } from "node:crypto";
import { readFileSync } from "node:fs";
import path from "node:path";

export function validateUpdateSigningKey(root, environment = process.env, read = readFileSync) {
  if (!environment.VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH)
    throw new Error("The update signing key path is missing.");
  if (!environment.VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE?.trim())
    throw new Error("The update signing passphrase is missing.");
  try {
    const pem = read(environment.VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH, "utf8");
    if (!pem.includes("ENCRYPTED PRIVATE KEY") && !pem.includes("Proc-Type: 4,ENCRYPTED"))
      throw new Error();
    const privateKey = createPrivateKey({
      key: pem,
      passphrase: environment.VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE,
    });
    const publicKey = createPublicKey(
      read(path.join(root, "apps/windows-host/Features/Updates/update-signing-public.pem"), "utf8"),
    );
    const challenge = randomBytes(32);
    const signature = sign("sha256", challenge, { key: privateKey, padding: 6, saltLength: 32 });
    if (!verify("sha256", challenge, { key: publicKey, padding: 6, saltLength: 32 }, signature))
      throw new Error();
    return privateKey;
  } catch {
    throw new Error(
      "The encrypted signing key could not be opened or does not match the host public key. Check the key file and passphrase.",
    );
  }
}
