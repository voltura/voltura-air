import { describe, expect, it } from "vitest";
import { getPairingFeedback } from "./pairingFeedback";

describe("getPairingFeedback", () => {
  it("maps stale pairing tokens to a used QR code message", () => {
    const feedback = getPairingFeedback("Pairing rejected: stale-token");

    expect(feedback.reason).toBe("stale-token");
    expect(feedback.title).toBe("QR code already used");
    expect(feedback.diagnosticCode).toBe("VAIR-PAIR-STALE-TOKEN");
    expect(feedback.primaryLabel).toBe("Take photo of new QR code");
    expect(feedback.showRecoveryActions).toBe(true);
  });

  it("maps legacy missing-token UI text to the missing token reason", () => {
    const feedback = getPairingFeedback("Scan Living room PC's pairing QR to pair this app.");

    expect(feedback.reason).toBe("missing-token");
    expect(feedback.title).toBe("Pairing code missing");
    expect(feedback.diagnosticCode).toBe("VAIR-PAIR-MISSING-TOKEN");
  });

  it("maps legacy invalid-token UI text to the invalid token reason", () => {
    const feedback = getPairingFeedback("Pairing code expired. Scan a new QR code.");

    expect(feedback.reason).toBe("invalid-token");
    expect(feedback.title).toBe("Pairing code invalid");
    expect(feedback.diagnosticCode).toBe("VAIR-PAIR-INVALID-TOKEN");
  });

  it("maps unavailable hosts to LAN and firewall guidance", () => {
    const feedback = getPairingFeedback("PC is currently not available. Retrying...", true);

    expect(feedback.reason).toBe("host-unreachable");
    expect(feedback.title).toBe("PC not available");
    expect(feedback.body).toContain("same Wi-Fi/LAN");
    expect(feedback.body).toContain("Windows Firewall");
  });

  it("maps unreadable QR photos to a retryable QR error", () => {
    const feedback = getPairingFeedback("Could not read the QR code. Try zooming in.");

    expect(feedback.reason).toBe("qr-unreadable");
    expect(feedback.title).toBe("QR code unreadable");
    expect(feedback.primaryLabel).toBe("Take another photo of QR code");
  });

  it("keeps unknown host rejection reasons diagnosable", () => {
    const feedback = getPairingFeedback("Pairing rejected: strange-new-reason");

    expect(feedback.reason).toBe("unknown");
    expect(feedback.title).toBe("Pairing failed");
    expect(feedback.diagnosticCode).toBe("VAIR-PAIR-STRANGE-NEW-REASON");
  });

  it("maps rate-limited pairing attempts to wait-and-rescan guidance", () => {
    const feedback = getPairingFeedback("Pairing rejected: rate-limited");

    expect(feedback.reason).toBe("rate-limited");
    expect(feedback.title).toBe("Too many pairing attempts");
    expect(feedback.body).toContain("temporarily blocked");
    expect(feedback.diagnosticCode).toBe("VAIR-PAIR-RATE-LIMITED");
  });

  it("maps invalid pairing messages to refresh guidance", () => {
    const feedback = getPairingFeedback("Pairing rejected: invalid-message");

    expect(feedback.reason).toBe("invalid-message");
    expect(feedback.title).toBe("Pairing request invalid");
    expect(feedback.body).toContain("expected format");
    expect(feedback.diagnosticCode).toBe("VAIR-PAIR-INVALID-MESSAGE");
  });
});
