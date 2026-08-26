import { p256 } from "@noble/curves/nist.js";
import { hmac } from "@noble/hashes/hmac.js";
import { sha256 } from "@noble/hashes/sha2.js";
import { base64Url, decodeBase64Url, signPrivateKeyPayload } from "./pairingCredentials";

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const maximumPendingSends = 32;
const maximumPointerDelta = 5000;

interface PendingSend {
  plaintext: string;
  rawSend: (data: ArrayBuffer) => void;
  resolve: () => void;
  reject: (reason: unknown) => void;
}

export interface RelayKeyChallenge {
  type: "session.key.challenge";
  routeId: string;
  clientId: string;
  hostEphemeralPublicKey: string;
  nonce: string;
}

export interface PendingRelaySession {
  channel: RelayEncryptedChannel;
  hostIdentityPublicKey: string;
  transcript: Uint8Array;
  proof: { type: "session.key.proof"; clientEphemeralPublicKey: string; signature: string };
}

export type RelayEncryptedSend = (plaintext: string) => Promise<void>;

export function parseRelayKeyChallenge(value: unknown): RelayKeyChallenge | null {
  if (
    !isRecord(value) ||
    value.type !== "session.key.challenge" ||
    Object.keys(value).length !== 5 ||
    typeof value.routeId !== "string" ||
    !/^[A-Za-z0-9_-]{22}$/u.test(value.routeId) ||
    typeof value.clientId !== "string" ||
    value.clientId.length === 0 ||
    value.clientId.length > 128 ||
    typeof value.hostEphemeralPublicKey !== "string" ||
    !/^[A-Za-z0-9_-]{87}$/u.test(value.hostEphemeralPublicKey) ||
    typeof value.nonce !== "string" ||
    !/^[A-Za-z0-9_-]{43}$/u.test(value.nonce)
  ) {
    return null;
  }
  return value as unknown as RelayKeyChallenge;
}

export function beginRelaySession(
  challenge: RelayKeyChallenge,
  hostIdentityPublicKey: string,
  signingPrivateKey: string | null,
  storedSigner: (transcript: string) => string | null,
): PendingRelaySession | null {
  try {
    const ephemeral = p256.keygen();
    const clientEphemeralPublicKey = base64Url(p256.getPublicKey(ephemeral.secretKey, false));
    const transcriptText = [
      "voltura-air-relay-session-v1",
      challenge.routeId,
      challenge.clientId,
      hostIdentityPublicKey,
      challenge.hostEphemeralPublicKey,
      clientEphemeralPublicKey,
      challenge.nonce,
    ].join("\n");
    const transcript = encoder.encode(transcriptText);
    const signature = signingPrivateKey
      ? signPrivateKeyPayload(signingPrivateKey, transcript)
      : storedSigner(transcriptText);
    if (!signature) {
      return null;
    }
    const shared = p256
      .getSharedSecret(
        ephemeral.secretKey,
        decodeBase64Url(challenge.hostEphemeralPublicKey),
        false,
      )
      .slice(1, 33);
    return {
      channel: RelayEncryptedChannel.create(shared, transcript),
      hostIdentityPublicKey,
      transcript,
      proof: { type: "session.key.proof", clientEphemeralPublicKey, signature },
    };
  } catch {
    return null;
  }
}

export function verifyRelayHostAcceptance(pending: PendingRelaySession, value: unknown): boolean {
  if (
    !isRecord(value) ||
    value.type !== "session.key.accepted" ||
    Object.keys(value).length !== 2 ||
    typeof value.signature !== "string" ||
    !/^[A-Za-z0-9_-]{86}$/u.test(value.signature)
  ) {
    return false;
  }
  try {
    return p256.verify(
      decodeBase64Url(value.signature),
      pending.transcript,
      decodeBase64Url(pending.hostIdentityPublicKey),
      { lowS: false },
    );
  } catch {
    return false;
  }
}

export class RelayEncryptedChannel {
  private sendCounter = 0n;
  private receiveCounter = 0n;
  private readonly sendQueue: PendingSend[] = [];
  private sending = false;
  private receiveQueue = Promise.resolve();

  private constructor(
    private readonly sendKey: Uint8Array,
    private readonly receiveKey: Uint8Array,
    private readonly sendNoncePrefix: Uint8Array,
    private readonly receiveNoncePrefix: Uint8Array,
    private readonly transcriptHash: Uint8Array,
    private readonly sendDirection: number,
    private readonly receiveDirection: number,
  ) {}

  static create(secret: Uint8Array, transcript: Uint8Array): RelayEncryptedChannel {
    const transcriptHash = sha256(transcript);
    const prk = hmac(sha256, transcriptHash, secret);
    const material = expand(prk, encoder.encode("voltura-air-relay-session-keys-v1"), 72);
    return new RelayEncryptedChannel(
      material.slice(32, 64),
      material.slice(0, 32),
      material.slice(68, 72),
      material.slice(64, 68),
      transcriptHash,
      2,
      1,
    );
  }

  static createHostForConformance(
    secret: Uint8Array,
    transcript: Uint8Array,
  ): RelayEncryptedChannel {
    const transcriptHash = sha256(transcript);
    const prk = hmac(sha256, transcriptHash, secret);
    const material = expand(prk, encoder.encode("voltura-air-relay-session-keys-v1"), 72);
    return new RelayEncryptedChannel(
      material.slice(0, 32),
      material.slice(32, 64),
      material.slice(64, 68),
      material.slice(68, 72),
      transcriptHash,
      1,
      2,
    );
  }

  send(rawSend: (data: ArrayBuffer) => void, plaintext: string): Promise<void> {
    return new Promise((resolve, reject) => {
      const tail = this.sendQueue.at(-1);
      const merged = tail ? mergeRelativeMovement(tail.plaintext, plaintext) : null;
      if (tail && merged) {
        tail.plaintext = merged;
        resolve();
        return;
      }

      if (this.sendQueue.length >= maximumPendingSends) {
        reject(new Error("Relay encrypted send queue is full."));
        return;
      }

      this.sendQueue.push({ plaintext, rawSend, resolve, reject });
      this.drainSendQueue();
    });
  }

  private drainSendQueue(): void {
    if (this.sending) {
      return;
    }
    this.sending = true;
    void this.runSendQueue();
  }

  private async runSendQueue(): Promise<void> {
    try {
      while (this.sendQueue.length > 0) {
        const pending = this.sendQueue.shift()!;
        try {
          const frame = await this.encrypt(encoder.encode(pending.plaintext));
          pending.rawSend(copyBuffer(frame));
          pending.resolve();
        } catch (error) {
          pending.reject(error);
          for (const queued of this.sendQueue.splice(0)) {
            queued.reject(error);
          }
          break;
        }
      }
    } finally {
      this.sending = false;
      if (this.sendQueue.length > 0) {
        this.drainSendQueue();
      }
    }
  }

  decryptText(value: unknown): Promise<string | null> {
    if (!(value instanceof ArrayBuffer)) {
      return Promise.resolve(null);
    }
    const frame = new Uint8Array(value.slice(0));
    const result = this.receiveQueue.then(() => this.decryptFrame(frame));
    this.receiveQueue = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }

  private async decryptFrame(frame: Uint8Array): Promise<string | null> {
    if (frame.length < 26 || frame[0] !== 1 || frame[1] !== this.receiveDirection) {
      return null;
    }
    const counter = readCounter(frame.subarray(2, 10));
    if (counter !== this.receiveCounter + 1n) {
      return null;
    }
    try {
      const key = await crypto.subtle.importKey(
        "raw",
        copyBuffer(this.receiveKey),
        "AES-GCM",
        false,
        ["decrypt"],
      );
      const plaintext = await crypto.subtle.decrypt(
        {
          name: "AES-GCM",
          iv: copyBuffer(nonce(this.receiveNoncePrefix, counter)),
          additionalData: copyBuffer(concat(this.transcriptHash, frame.subarray(0, 10))),
          tagLength: 128,
        },
        key,
        copyBuffer(frame.subarray(10)),
      );
      this.receiveCounter = counter;
      return decoder.decode(plaintext);
    } catch {
      return null;
    }
  }

  private async encrypt(plaintext: Uint8Array): Promise<Uint8Array> {
    const counter = ++this.sendCounter;
    const header = new Uint8Array(10);
    header[0] = 1;
    header[1] = this.sendDirection;
    writeCounter(header.subarray(2), counter);
    const key = await crypto.subtle.importKey("raw", copyBuffer(this.sendKey), "AES-GCM", false, [
      "encrypt",
    ]);
    const encrypted = new Uint8Array(
      await crypto.subtle.encrypt(
        {
          name: "AES-GCM",
          iv: copyBuffer(nonce(this.sendNoncePrefix, counter)),
          additionalData: copyBuffer(concat(this.transcriptHash, header)),
          tagLength: 128,
        },
        key,
        copyBuffer(plaintext),
      ),
    );
    return concat(header, encrypted);
  }
}

function mergeRelativeMovement(currentText: string, nextText: string): string | null {
  try {
    const current = JSON.parse(currentText) as unknown;
    const next = JSON.parse(nextText) as unknown;
    if (
      !isRelativeMovement(current) ||
      !isRelativeMovement(next) ||
      current.type !== next.type ||
      current.inputContext !== next.inputContext ||
      (current.seq !== undefined && next.seq !== undefined)
    ) {
      return null;
    }

    const dx = current.dx + next.dx;
    const dy = current.dy + next.dy;
    if (
      !Number.isSafeInteger(dx) ||
      !Number.isSafeInteger(dy) ||
      Math.abs(dx) > maximumPointerDelta ||
      Math.abs(dy) > maximumPointerDelta
    ) {
      return null;
    }

    return JSON.stringify({
      type: current.type,
      ...(current.seq !== undefined || next.seq !== undefined
        ? { seq: current.seq ?? next.seq }
        : {}),
      ...(current.inputContext === undefined ? {} : { inputContext: current.inputContext }),
      dx,
      dy,
    });
  } catch {
    return null;
  }
}

function isRelativeMovement(value: unknown): value is {
  type: "pointer.move" | "pointer.wheel";
  seq?: number;
  inputContext?: string;
  dx: number;
  dy: number;
} {
  if (!isRecord(value)) {
    return false;
  }
  const expectedFieldCount =
    3 + (value.seq === undefined ? 0 : 1) + (value.inputContext === undefined ? 0 : 1);
  return (
    Object.keys(value).length === expectedFieldCount &&
    (value.type === "pointer.move" || value.type === "pointer.wheel") &&
    (value.seq === undefined || Number.isSafeInteger(value.seq)) &&
    (value.inputContext === undefined || typeof value.inputContext === "string") &&
    Number.isSafeInteger(value.dx) &&
    Number.isSafeInteger(value.dy)
  );
}

function expand(prk: Uint8Array, info: Uint8Array, length: number): Uint8Array {
  const output = new Uint8Array(length);
  let previous = new Uint8Array();
  let offset = 0;
  for (let block = 1; offset < length; block += 1) {
    previous = hmac(sha256, prk, concat(previous, info, new Uint8Array([block])));
    const count = Math.min(previous.length, length - offset);
    output.set(previous.subarray(0, count), offset);
    offset += count;
  }
  return output;
}

function nonce(prefix: Uint8Array, counter: bigint): Uint8Array {
  const value = new Uint8Array(12);
  value.set(prefix);
  writeCounter(value.subarray(4), counter);
  return value;
}
function writeCounter(target: Uint8Array, value: bigint): void {
  new DataView(target.buffer, target.byteOffset, 8).setBigUint64(0, value);
}
function readCounter(value: Uint8Array): bigint {
  return new DataView(value.buffer, value.byteOffset, 8).getBigUint64(0);
}
function concat(...values: Uint8Array[]): Uint8Array {
  const result = new Uint8Array(values.reduce((total, value) => total + value.length, 0));
  let offset = 0;
  for (const value of values) {
    result.set(value, offset);
    offset += value.length;
  }
  return result;
}
function copyBuffer(value: Uint8Array): ArrayBuffer {
  return Uint8Array.from(value).buffer;
}
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
