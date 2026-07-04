export type PairingFailureReason =
  | "qr-unreadable"
  | "qr-not-pairing-link"
  | "expired-token"
  | "stale-token"
  | "missing-token"
  | "invalid-token"
  | "host-unreachable"
  | "host-rejected-device"
  | "device-revoked"
  | "protocol-version-mismatch"
  | "unknown";

export type PairingFeedback = {
  body: string;
  diagnosticCode?: string;
  hints: string[];
  primaryLabel: string;
  reason?: PairingFailureReason;
  severity: "info" | "warning" | "error";
  showRecoveryActions: boolean;
  title: string;
};

const defaultPairingFeedback: PairingFeedback = {
  title: "Pair this app",
  body: "Scan the QR code shown on your PC.",
  hints: [],
  primaryLabel: "Take photo of QR code",
  severity: "info",
  showRecoveryActions: false
};

export function getPairingFeedback(message: string, activePcUnavailable = false): PairingFeedback {
  const normalizedMessage = message.trim();
  if (activePcUnavailable) {
    return {
      title: "PC not available",
      body: "Could not reach the PC. Make sure Voltura Air is running, both devices are on the same Wi-Fi/LAN, and Windows Firewall allows the host.",
      diagnosticCode: "VAIR-PAIR-HOST-UNREACHABLE",
      hints: [
        "Check that Voltura Air is still running on the PC.",
        "Check that the phone or tablet is on the same Wi-Fi/LAN as the PC.",
        "If you changed network, IP address, or port, click New code on the PC and scan again.",
        "If Windows Firewall asks, allow Voltura Air on private networks."
      ],
      primaryLabel: "Try reconnect",
      reason: "host-unreachable",
      severity: "error",
      showRecoveryActions: true
    };
  }

  if (/could not read the qr code/i.test(normalizedMessage)) {
    return {
      title: "QR code unreadable",
      body: "The photo did not contain a readable QR code. Retake the photo closer to the PC screen, keep the QR code sharp, or generate a new code on the PC.",
      diagnosticCode: "VAIR-PAIR-QR-UNREADABLE",
      hints: [
        "Zoom in on the QR code before taking the photo.",
        "Avoid glare or motion blur.",
        "Use New code on the PC if the current code may be old."
      ],
      primaryLabel: "Take photo again",
      reason: "qr-unreadable",
      severity: "warning",
      showRecoveryActions: true
    };
  }

  if (/no voltura air pairing link/i.test(normalizedMessage)) {
    return {
      title: "Not a Voltura Air QR code",
      body: "That QR code does not contain a Voltura Air pairing link. Scan the QR code from the Voltura Air Connect screen on the PC.",
      diagnosticCode: "VAIR-PAIR-QR-NOT-LINK",
      hints: ["Open Voltura Air on the PC and use the QR code on the Connect screen."],
      primaryLabel: "Scan Voltura Air QR",
      reason: "qr-not-pairing-link",
      severity: "warning",
      showRecoveryActions: true
    };
  }

  if (/pairing code expired|qr code expired/i.test(normalizedMessage)) {
    return feedbackForReason("expired-token");
  }

  const rejectedReason = parseRejectedReason(normalizedMessage);
  if (rejectedReason) {
    return feedbackForRejectedReason(rejectedReason);
  }

  if (/confirm the device name/i.test(normalizedMessage)) {
    return {
      ...defaultPairingFeedback,
      title: "Confirm this device",
      body: normalizedMessage,
      primaryLabel: "Pair"
    };
  }

  if (/connecting/i.test(normalizedMessage)) {
    return {
      ...defaultPairingFeedback,
      title: "Connecting to PC",
      body: normalizedMessage,
      primaryLabel: "Scan new QR code"
    };
  }

  return normalizedMessage ? { ...defaultPairingFeedback, body: normalizedMessage } : defaultPairingFeedback;
}

export function buildPairingDiagnostics(message: string, activePcUnavailable = false, diagnosticCode?: string): string {
  const feedback = getPairingFeedback(message, activePcUnavailable);
  const diagnostics = {
    state: activePcUnavailable ? "host-unavailable" : feedback.reason ? "pairing-failed" : "pairing",
    reason: feedback.reason ?? null,
    diagnosticCode: diagnosticCode ?? feedback.diagnosticCode ?? null,
    message: message.trim(),
    pageUrl: safeLocationHref(),
    platform: navigator.userAgent,
    displayMode: getDisplayMode(),
    timestamp: new Date().toISOString()
  };

  return JSON.stringify(diagnostics, null, 2);
}

export function normalizeManualHostInput(value: string, fallbackUrl: string): string | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  try {
    if (/^\d{1,5}$/.test(trimmed)) {
      const port = Number.parseInt(trimmed, 10);
      if (port <= 0 || port > 65535) {
        return null;
      }

      const fallback = new URL(fallbackUrl);
      fallback.port = String(port);
      return fallback.origin;
    }

    const candidate = /^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed) ? trimmed : `http://${trimmed}`;
    const url = new URL(candidate);
    if (url.protocol !== "http:" && url.protocol !== "https:") {
      return null;
    }

    return url.toString();
  } catch {
    return null;
  }
}

function feedbackForRejectedReason(reason: string): PairingFeedback {
  if (reason === "invalid-token") {
    return feedbackForReason("invalid-token");
  }

  if (reason === "expired-token") {
    return feedbackForReason("expired-token");
  }

  if (reason === "stale-token" || reason === "used-token" || reason === "token-already-used") {
    return feedbackForReason("stale-token");
  }

  if (reason === "missing-token") {
    return feedbackForReason("missing-token");
  }

  if (reason === "device-revoked" || reason === "secret-revoked") {
    return feedbackForReason("device-revoked");
  }

  if (reason === "protocol-version-mismatch") {
    return feedbackForReason("protocol-version-mismatch");
  }

  if (reason === "pair-first") {
    return {
      title: "Pairing required",
      body: "The PC rejected the request because the first message was not a pairing request. Scan a fresh QR code and try again.",
      diagnosticCode: "VAIR-PAIR-PAIR-FIRST",
      hints: ["Scan a fresh QR code from the PC Connect screen."],
      primaryLabel: "Scan new QR code",
      reason: "host-rejected-device",
      severity: "error",
      showRecoveryActions: true
    };
  }

  return {
    title: "Pairing failed",
    body: `The PC rejected the pairing request. Diagnostic code: ${diagnosticCodeForReason(reason)}.`,
    diagnosticCode: diagnosticCodeForReason(reason),
    hints: [
      "Click New code on the PC and scan again.",
      "Check that both devices are on the same Wi-Fi/LAN.",
      "Copy diagnostics if the problem continues."
    ],
    primaryLabel: "Scan new QR code",
    reason: "unknown",
    severity: "error",
    showRecoveryActions: true
  };
}

function feedbackForReason(reason: PairingFailureReason): PairingFeedback {
  switch (reason) {
    case "expired-token":
      return {
        title: "QR code expired",
        body: "This QR code expired. Click New code on the PC and scan the fresh QR code.",
        diagnosticCode: "VAIR-PAIR-EXPIRED-TOKEN",
        hints: ["Pairing QR codes are short-lived for safety.", "Use the latest QR code shown on the PC."],
        primaryLabel: "Scan new QR code",
        reason,
        severity: "warning",
        showRecoveryActions: true
      };
    case "stale-token":
      return {
        title: "QR code already used",
        body: "This QR code was already used or replaced. Click New code on the PC and scan the new QR code.",
        diagnosticCode: "VAIR-PAIR-STALE-TOKEN",
        hints: ["Only the latest unused pairing QR code can pair a device.", "Use New code on the PC before scanning again."],
        primaryLabel: "Scan new QR code",
        reason,
        severity: "warning",
        showRecoveryActions: true
      };
    case "missing-token":
      return {
        title: "Pairing code missing",
        body: "This link does not contain a pairing code. Scan the QR code shown by Voltura Air on the PC.",
        diagnosticCode: "VAIR-PAIR-MISSING-TOKEN",
        hints: ["Open the Connect screen on the PC and scan its QR code."],
        primaryLabel: "Scan QR code",
        reason,
        severity: "warning",
        showRecoveryActions: true
      };
    case "invalid-token":
      return {
        title: "Pairing code invalid",
        body: "This pairing code is not valid anymore. Click New code on the PC and scan again.",
        diagnosticCode: "VAIR-PAIR-INVALID-TOKEN",
        hints: ["This can happen if the QR code is old, from another PC, or already replaced."],
        primaryLabel: "Scan new QR code",
        reason,
        severity: "warning",
        showRecoveryActions: true
      };
    case "device-revoked":
      return {
        title: "Device disconnected",
        body: "This device was disconnected from the PC. Scan a new QR code to pair it again.",
        diagnosticCode: "VAIR-PAIR-DEVICE-REVOKED",
        hints: ["Open Devices on the PC to confirm whether this phone or tablet was removed."],
        primaryLabel: "Scan new QR code",
        reason,
        severity: "error",
        showRecoveryActions: true
      };
    case "protocol-version-mismatch":
      return {
        title: "App version mismatch",
        body: "The mobile app and PC host do not speak the same pairing protocol. Refresh the mobile app from the PC and try again.",
        diagnosticCode: "VAIR-PAIR-PROTOCOL-MISMATCH",
        hints: ["Open Settings in the mobile app and use Refresh app, then scan a fresh QR code."],
        primaryLabel: "Scan new QR code",
        reason,
        severity: "error",
        showRecoveryActions: true
      };
    default:
      return feedbackForRejectedReason(reason);
  }
}

function parseRejectedReason(message: string): string | null {
  const match = /pairing rejected:\s*([a-z0-9._-]+)/i.exec(message);
  return match?.[1].toLowerCase() ?? null;
}

function diagnosticCodeForReason(reason: string): string {
  const normalized = reason.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").toUpperCase();
  return `VAIR-PAIR-${normalized || "UNKNOWN"}`;
}

function safeLocationHref(): string {
  try {
    return window.location.href;
  } catch {
    return "";
  }
}

function getDisplayMode(): "browser" | "installed" | "unknown" {
  if (window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true) {
    return "installed";
  }

  if (window.matchMedia("(display-mode: browser)").matches) {
    return "browser";
  }

  return "unknown";
}
