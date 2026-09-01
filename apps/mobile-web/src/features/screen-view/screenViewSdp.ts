export function hasExpectedScreenMedia(sdp: string, direction: "sendonly" | "recvonly"): boolean {
  const sections: { kind: string; lines: string[] }[] = [];
  for (const rawLine of sdp.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (line.startsWith("m=")) {
      sections.push({ kind: line.slice(2).split(" ", 1)[0] ?? "", lines: [line] });
    } else if (line && sections.length > 0) {
      sections.at(-1)?.lines.push(line);
    }
  }
  const video = sections.filter((section) => section.kind === "video");
  const audio = sections.filter((section) => section.kind === "audio");
  const application = sections.filter((section) => section.kind === "application");
  return (
    sections.length === 3 &&
    video.length === 1 &&
    audio.length === 1 &&
    application.length === 1 &&
    hasExactCodec(video[0]!, "102", "H264/90000", direction) &&
    hasExactCodec(audio[0]!, "111", "opus/48000/2", direction) &&
    hasExpectedDataChannel(application[0]!)
  );
}

function hasExpectedDataChannel(section: { lines: string[] }): boolean {
  const media = section.lines[0]?.split(/\s+/) ?? [];
  return (
    media.length === 4 &&
    media[1] !== "0" &&
    media[2]?.toLowerCase() === "udp/dtls/sctp" &&
    media[3]?.toLowerCase() === "webrtc-datachannel"
  );
}

function hasExactCodec(
  section: { lines: string[] },
  payloadType: string,
  codec: string,
  direction: "sendonly" | "recvonly",
): boolean {
  const media = section.lines[0]?.split(/\s+/) ?? [];
  const mappings = section.lines.filter((line) => line.startsWith("a=rtpmap:"));
  const directions = section.lines.filter((line) =>
    ["a=sendrecv", "a=sendonly", "a=recvonly", "a=inactive"].includes(line),
  );
  const prefix = `a=rtpmap:${payloadType} `;
  return (
    media.length === 4 &&
    media[1] !== "0" &&
    media[3] === payloadType &&
    mappings.length === 1 &&
    mappings[0]?.startsWith(prefix) === true &&
    mappings[0]?.slice(prefix.length).toLowerCase() === codec.toLowerCase() &&
    directions.length === 1 &&
    directions[0] === `a=${direction}`
  );
}
