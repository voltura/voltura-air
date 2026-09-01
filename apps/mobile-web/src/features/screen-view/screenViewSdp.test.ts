import { describe, expect, it } from "vitest";
import { hasExpectedScreenMedia } from "./screenViewSdp";

const offer =
  "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\na=rtpmap:102 H264/90000\r\na=sendonly\r\n" +
  "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=sendonly\r\n" +
  "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n";

describe("screen SDP contract", () => {
  it("accepts exactly H.264 video, stereo Opus audio, and the event channel", () => {
    expect(hasExpectedScreenMedia(offer, "sendonly")).toBe(true);
    expect(hasExpectedScreenMedia(offer.replaceAll("a=sendonly", "a=recvonly"), "recvonly")).toBe(
      true,
    );
  });

  it("rejects missing, extra, wrong-codec, and wrong-direction media", () => {
    expect(
      hasExpectedScreenMedia(offer.replace(/m=audio[\s\S]*?(?=m=application)/, ""), "sendonly"),
    ).toBe(false);
    expect(hasExpectedScreenMedia(`${offer}m=audio 9 UDP/TLS/RTP/SAVPF 111\r\n`, "sendonly")).toBe(
      false,
    );
    expect(hasExpectedScreenMedia(offer.replace("opus/48000/2", "PCMU/8000"), "sendonly")).toBe(
      false,
    );
    expect(hasExpectedScreenMedia(offer.replace("a=sendonly", "a=recvonly"), "sendonly")).toBe(
      false,
    );
    expect(
      hasExpectedScreenMedia(offer.replace("m=application 9", "m=application 0"), "sendonly"),
    ).toBe(false);
    expect(
      hasExpectedScreenMedia(offer.replace("UDP/DTLS/SCTP", "UDP/TLS/RTP/SAVPF"), "sendonly"),
    ).toBe(false);
    expect(hasExpectedScreenMedia(offer.replace("webrtc-datachannel", "5000"), "sendonly")).toBe(
      false,
    );
  });
});
