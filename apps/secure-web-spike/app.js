(() => {
  "use strict";

  const maximumSdpLength = 32 * 1024;
  const requiredDirectBitrate = 12_000_000;
  class IceGatheringTimeoutError extends Error {}
  class SignalHttpError extends Error {
    constructor(status, message) {
      super(message);
      this.status = status;
    }
  }
  const tokenPattern = /^[A-Za-z0-9_-]{43}$/;
  const elements = Object.fromEntries([
    "preview", "camera", "transport", "prepare", "start", "stop", "overall", "environment", "capture",
    "video-settings", "send-settings", "ice", "route", "detail"
  ].map((id) => [id, document.getElementById(id)]));

  let room = "";
  let keyBytes = null;
  let offer = null;
  let peer = null;
  let sender = null;
  let stream = null;
  let pendingStream = null;
  let answerPosted = false;
  let answerSubmissionConfirmed = false;
  let statsTimer = 0;
  let previousOutbound = null;
  let camerasReady = false;
  let transportLost = false;
  const captureGeneration = window.createCaptureGeneration(() => document.hidden);

  const setOverall = (text, kind) => {
    elements.overall.textContent = text;
    elements.overall.className = `pill ${kind}`;
  };

  const describeEnvironment = () => {
    const userAgent = navigator.userAgent;
    const standalone = navigator.standalone === true || matchMedia("(display-mode: standalone)").matches;
    if (standalone) return "iPhone Home Screen web app";
    if (/FBAN|FBAV|MessengerForiOS/i.test(userAgent)) return "Messenger in-app browser";
    if (/CriOS/i.test(userAgent)) return "Chrome on iPhone";
    if (/FxiOS/i.test(userAgent)) return "Firefox on iPhone";
    if (/Safari/i.test(userAgent)) return "Safari on iPhone";
    return "iPhone browser (unidentified)";
  };

  const base64UrlToBytes = (value) => {
    const padded = value.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((value.length + 3) % 4);
    const binary = atob(padded);
    return Uint8Array.from(binary, (character) => character.charCodeAt(0));
  };

  const bytesToBase64Url = (value) => {
    let binary = "";
    for (const byte of value) binary += String.fromCharCode(byte);
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  };

  const importKey = () => crypto.subtle.importKey("raw", keyBytes, "AES-GCM", false, ["encrypt", "decrypt"]);

  const decryptEnvelope = async (envelope) => {
    if (envelope?.v !== 1 || typeof envelope.iv !== "string" || typeof envelope.ciphertext !== "string") {
      throw new Error("The signaling envelope is invalid.");
    }
    try {
      const plaintext = await crypto.subtle.decrypt(
        { name: "AES-GCM", iv: base64UrlToBytes(envelope.iv), additionalData: new TextEncoder().encode(room) },
        await importKey(),
        base64UrlToBytes(envelope.ciphertext));
      return JSON.parse(new TextDecoder().decode(plaintext));
    } catch {
      throw new Error("The signaling key is wrong or the offer was altered.");
    }
  };

  const encryptEnvelope = async (value) => {
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const ciphertext = await crypto.subtle.encrypt(
      { name: "AES-GCM", iv, additionalData: new TextEncoder().encode(room) },
      await importKey(),
      new TextEncoder().encode(JSON.stringify(value)));
    return { v: 1, iv: bytesToBase64Url(iv), ciphertext: bytesToBase64Url(new Uint8Array(ciphertext)) };
  };

  const signal = async (payload) => {
    const response = await fetch("signal.php", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      cache: "no-store",
      credentials: "same-origin",
      body: JSON.stringify(payload)
    });
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.ok) {
      throw new SignalHttpError(response.status, result?.error || `Signaling failed with HTTP ${response.status}.`);
    }
    return result;
  };

  const submitAnswer = async (payload) => {
    let ambiguousFailure = false;
    for (let attempt = 0; attempt < 3; ++attempt) {
      try {
        await signal({ op: "set_answer", room, payload });
        return true;
      } catch (error) {
        if (error instanceof SignalHttpError && error.status < 500) {
          if (ambiguousFailure && error.status === 404) return false;
          throw error;
        }
        ambiguousFailure = true;
        if (attempt < 2) await new Promise((resolve) => setTimeout(resolve, 250));
      }
    }
    return false;
  };

  const isRelayCandidate = (candidate) => candidate?.type === "relay" || /\styp\s+relay(?:\s|$)/.test(candidate?.candidate || "");

  const hasOnlyRelayCandidates = (sdp) => {
    const candidates = sdp.split(/\r?\n/).filter((line) => line.startsWith("a=candidate:"));
    return candidates.length > 0 && candidates.every((line) => /\styp\s+relay(?:\s|$)/.test(line));
  };

  const waitForIceGathering = (connection, allowSettledRelayCandidates = false) => {
    if (connection.iceGatheringState === "complete") return Promise.resolve();
    return new Promise((resolve, reject) => {
      let relaySettleTimeout;
      const cleanup = () => {
        clearTimeout(gatheringTimeout);
        clearTimeout(relaySettleTimeout);
        connection.removeEventListener("icegatheringstatechange", onState);
        connection.removeEventListener("icecandidate", onCandidate);
      };
      const finishWithRelayCandidates = () => {
        if (!hasOnlyRelayCandidates(connection.localDescription?.sdp || "")) return;
        cleanup();
        resolve();
      };
      const scheduleRelaySettle = () => {
        if (!allowSettledRelayCandidates) return;
        clearTimeout(relaySettleTimeout);
        relaySettleTimeout = setTimeout(finishWithRelayCandidates, 350);
      };
      const onState = () => {
        if (connection.iceGatheringState === "complete") {
          cleanup();
          resolve();
        }
      };
      const onCandidate = (event) => {
        if (isRelayCandidate(event.candidate)) scheduleRelaySettle();
      };
      connection.addEventListener("icegatheringstatechange", onState);
      connection.addEventListener("icecandidate", onCandidate);
      const gatheringTimeout = setTimeout(() => {
        if (allowSettledRelayCandidates && hasOnlyRelayCandidates(connection.localDescription?.sdp || "")) {
          finishWithRelayCandidates();
          return;
        }
        cleanup();
        reject(new IceGatheringTimeoutError("WebRTC candidate gathering timed out."));
      }, 10000);
      if (hasOnlyRelayCandidates(connection.localDescription?.sdp || "")) scheduleRelaySettle();
    });
  };

  const describeCandidate = (candidate) => {
    if (!candidate) return "unavailable";
    const address = candidate.address || candidate.ip || "private address";
    return `${candidate.candidateType || "unknown"} ${candidate.protocol || "unknown"} ${address}${candidate.port ? `:${candidate.port}` : ""}`;
  };

  const updateRoute = async () => {
    if (!peer) return;
    const stats = await peer.getStats();
    let pair = null;
    stats.forEach((report) => {
      if (report.type === "transport" && report.selectedCandidatePairId) pair = stats.get(report.selectedCandidatePairId) || pair;
      if (!pair && report.type === "candidate-pair" && report.state === "succeeded" && (report.selected || report.nominated)) pair = report;
    });
    if (pair) elements.route.textContent = `${describeCandidate(stats.get(pair.localCandidateId))} ↔ ${describeCandidate(stats.get(pair.remoteCandidateId))}`;
  };

  const updateOutbound = async () => {
    if (!sender) return;
    const stats = await sender.getStats();
    let outbound = null;
    stats.forEach((report) => {
      if (report.type === "outbound-rtp" && report.kind === "video" && !report.isRemote) outbound = report;
    });
    if (!outbound) return;
    let bitrate = "bitrate unavailable";
    if (previousOutbound && Number.isFinite(outbound.bytesSent) && Number.isFinite(outbound.timestamp)) {
      const elapsed = outbound.timestamp - previousOutbound.timestamp;
      const bytes = outbound.bytesSent - previousOutbound.bytesSent;
      if (elapsed > 0 && bytes >= 0) bitrate = `${(bytes * 8 / elapsed / 1000).toFixed(2)} Mbps`;
    }
    previousOutbound = { bytesSent: outbound.bytesSent, timestamp: outbound.timestamp };
    const dimensions = Number.isFinite(outbound.frameWidth) && Number.isFinite(outbound.frameHeight)
      ? `${outbound.frameWidth}×${outbound.frameHeight}`
      : "dimensions unavailable";
    const fps = Number.isFinite(outbound.framesPerSecond) ? `${outbound.framesPerSecond.toFixed(1)} fps` : "fps unavailable";
    const limitation = outbound.qualityLimitationReason ? `; limit ${outbound.qualityLimitationReason}` : "";
    elements["send-settings"].textContent = `${dimensions} at ${fps}; ${bitrate}${limitation}`;
  };

  const startOutboundStats = () => {
    clearInterval(statsTimer);
    previousOutbound = null;
    updateOutbound().catch(() => {});
    statsTimer = setInterval(() => updateOutbound().catch(() => {}), 1000);
  };

  const stopCapture = async (reason = "stopped") => {
    captureGeneration.invalidate();
    const previous = stream;
    const pending = pendingStream;
    stream = null;
    pendingStream = null;
    if (previous) previous.getTracks().forEach((track) => track.stop());
    if (pending && pending !== previous) pending.getTracks().forEach((track) => track.stop());
    elements.preview.srcObject = null;
    elements.capture.textContent = reason;
    elements["video-settings"].textContent = "not active";
    elements["send-settings"].textContent = "not active";
    clearInterval(statsTimer);
    statsTimer = 0;
    previousOutbound = null;
    elements.stop.disabled = true;
    elements.start.disabled = !offer || !camerasReady;
    if (peer && !["failed", "closed"].includes(peer.connectionState)) setOverall("Waiting", "pending");
    if (sender) await sender.replaceTrack(null).catch(() => {});
  };

  const markTransportLost = () => {
    if (transportLost) return;
    captureGeneration.invalidate();
    transportLost = true;
    const previous = stream;
    const pending = pendingStream;
    stream = null;
    pendingStream = null;
    if (sender) sender.replaceTrack(null).catch(() => {});
    if (previous) previous.getTracks().forEach((track) => track.stop());
    if (pending && pending !== previous) pending.getTracks().forEach((track) => track.stop());
    elements.preview.srcObject = null;
    elements.capture.textContent = "stopped; transport lost";
    elements["video-settings"].textContent = "not active";
    elements["send-settings"].textContent = "not active";
    clearInterval(statsTimer);
    statsTimer = 0;
    previousOutbound = null;
    elements.start.disabled = true;
    elements.stop.disabled = true;
    setOverall("Transport lost", "bad");
    elements.detail.textContent = "iOS closed the original WebRTC peer. Stop the host and open the URL from a new host run; this spike does not replace or renegotiate peers.";
  };

  const refreshCameras = async (selectedId, assertCurrent = () => {}) => {
    const devices = (await navigator.mediaDevices.enumerateDevices()).filter((device) => device.kind === "videoinput");
    assertCurrent();
    elements.camera.replaceChildren(...devices.map((device, index) => {
      const option = document.createElement("option");
      option.value = device.deviceId;
      option.textContent = device.label || `Camera ${index + 1}`;
      option.selected = device.deviceId === selectedId;
      return option;
    }));
    return devices;
  };

  const prepareCameras = async () => {
    elements.prepare.disabled = true;
    elements.detail.textContent = "Requesting camera permission. No video will be sent.";
    try {
      const permissionStream = await navigator.mediaDevices.getUserMedia({ audio: false, video: true });
      const selectedId = permissionStream.getVideoTracks()[0]?.getSettings().deviceId || "";
      permissionStream.getTracks().forEach((track) => track.stop());
      const devices = await refreshCameras(selectedId);
      if (devices.length === 0) throw new Error("No cameras are available.");
      camerasReady = true;
      elements.camera.disabled = false;
      elements.start.disabled = !offer;
      elements.capture.textContent = "permission granted; not streaming";
      elements.detail.textContent = "Select a camera, then start the webcam.";
      setOverall("Ready", "good");
    } catch (error) {
      camerasReady = false;
      elements.prepare.disabled = false;
      elements.detail.textContent = error instanceof Error ? error.message : String(error);
      setOverall("Failed", "bad");
    }
  };

  const acquireTrack = async (assertCurrent) => {
    const deviceId = elements.camera.value;
    const candidate = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: {
        width: { exact: 1920 },
        height: { exact: 1080 },
        frameRate: { ideal: 30, min: 28 },
        ...(deviceId ? { deviceId: { exact: deviceId } } : { facingMode: { ideal: "environment" } })
      }
    });
    try {
      assertCurrent();
      const track = candidate.getVideoTracks()[0];
      if (!track) throw new Error("The selected camera returned no video track.");
      const settings = track.getSettings();
      if (settings.width !== 1920 || settings.height !== 1080 || typeof settings.frameRate !== "number" || settings.frameRate < 28 || settings.frameRate > 31) {
        throw new Error(`The camera delivered ${settings.width || "?"}×${settings.height || "?"} at ${settings.frameRate || "?"} fps; 1920×1080 at approximately 30 fps is required.`);
      }
      track.contentHint = "detail";
      await refreshCameras(settings.deviceId, assertCurrent);
      assertCurrent();
      elements["video-settings"].textContent = `${settings.width}×${settings.height} at ${settings.frameRate.toFixed(1)} fps`;
      return { candidate, track };
    } catch (error) {
      candidate.getTracks().forEach((item) => item.stop());
      throw error;
    }
  };

  const createPeer = async (track, assertCurrent, generation) => {
    const transport = elements.transport.value;
    const relay = transport === "relay";
    const connection = new RTCPeerConnection({
      iceServers: Array.isArray(offer.iceServers) ? offer.iceServers : [],
      iceTransportPolicy: relay ? "relay" : "all",
      bundlePolicy: "max-bundle",
      rtcpMuxPolicy: "require"
    });
    let localSender = null;
    let relayCandidateCount = 0;
    let lastIceErrorCode = null;
    elements.transport.disabled = true;
    try {
      connection.addEventListener("icecandidate", (event) => {
        if (isRelayCandidate(event.candidate)) relayCandidateCount += 1;
      });
      connection.addEventListener("icecandidateerror", (event) => {
        lastIceErrorCode = event.errorCode;
      });
      connection.oniceconnectionstatechange = () => {
        if (peer !== connection) return;
        elements.ice.textContent = connection.iceConnectionState;
        if (["connected", "completed"].includes(connection.iceConnectionState)) updateRoute().catch(() => {});
        if (connection.iceConnectionState === "disconnected") setOverall("Transport interrupted", "pending");
        if (["failed", "closed"].includes(connection.iceConnectionState)) markTransportLost();
      };
      connection.onconnectionstatechange = () => {
        if (peer !== connection) return;
        if (connection.connectionState === "connected") setOverall("Streaming", "good");
        if (["failed", "closed"].includes(connection.connectionState)) markTransportLost();
      };

      await connection.setRemoteDescription({ type: "offer", sdp: offer.sdp });
      assertCurrent();
      const transceiver = connection.getTransceivers().find((item) => item.receiver?.track?.kind === "video");
      if (!transceiver) throw new Error("The host offer contains no video receiver.");
      transceiver.direction = "sendonly";
      localSender = transceiver.sender;
      await localSender.replaceTrack(track);
      assertCurrent();
      const maximumBitrate = relay ? offer.maximumBitrate : requiredDirectBitrate;
      if (!Number.isInteger(maximumBitrate) || maximumBitrate <= 0) throw new Error("The host did not provide a valid video bitrate.");
      const parameters = localSender.getParameters();
      parameters.encodings = parameters.encodings?.length ? parameters.encodings : [{}];
      parameters.encodings[0].maxBitrate = maximumBitrate;
      parameters.encodings[0].maxFramerate = 30;
      parameters.encodings[0].scaleResolutionDownBy = 1;
      parameters.degradationPreference = "maintain-resolution";
      await localSender.setParameters(parameters);
      assertCurrent();
      const answer = await connection.createAnswer();
      assertCurrent();
      if (!/^a=rtpmap:\d+ H264\/90000\r?$/m.test(answer.sdp || "")) throw new Error("The iPhone browser did not negotiate H.264.");
      await connection.setLocalDescription(answer);
      assertCurrent();
      await waitForIceGathering(connection, relay);
      assertCurrent();
      const sdp = connection.localDescription?.sdp;
      if (!sdp || sdp.length > maximumSdpLength) throw new Error("The complete browser answer is missing or too large.");
      if (relay && !hasOnlyRelayCandidates(sdp)) {
        throw new Error(`Relay candidate gathering failed (relay candidates: ${relayCandidateCount}, ICE error: ${lastIceErrorCode ?? "none"}).`);
      }
      const encryptedAnswer = await encryptEnvelope({ type: "answer", sdp, transport });
      assertCurrent();
      answerSubmissionConfirmed = await submitAnswer(encryptedAnswer);
      const captureStillCurrent = await window.retainSubmittedPeer(
        connection,
        localSender,
        () => captureGeneration.isCurrent(generation),
        (retainedConnection, retainedSender) => {
          peer = retainedConnection;
          sender = retainedSender;
          answerPosted = true;
        });
      elements.ice.textContent = connection.iceConnectionState;
      if (!answerSubmissionConfirmed) {
        elements.detail.textContent = "The answer response was lost; retaining the original peer while the host consumes it.";
      }
      return captureStillCurrent;
    } catch (error) {
      connection.close();
      elements.transport.disabled = false;
      throw error;
    }
  };

  const startCapture = async () => {
    if (!camerasReady || !elements.camera.value) return;
    if (transportLost || peer && (["failed", "closed"].includes(peer.connectionState) || ["failed", "closed"].includes(peer.iceConnectionState))) {
      markTransportLost();
      return;
    }
    elements.start.disabled = true;
    elements.detail.textContent = "Requesting camera permission.";
    if (pendingStream) pendingStream.getTracks().forEach((track) => track.stop());
    pendingStream = null;
    const generation = captureGeneration.begin();
    const assertCurrent = () => captureGeneration.assertCurrent(generation);
    let acquired = null;
    try {
      acquired = await acquireTrack(assertCurrent);
      assertCurrent();
      pendingStream = acquired.candidate;
      if (!peer) {
        const captureStillCurrent = await createPeer(acquired.track, assertCurrent, generation);
        if (!captureStillCurrent) throw new Error("Capture start was cancelled.");
      }
      else {
        await sender.replaceTrack(acquired.track);
        assertCurrent();
      }
      if (transportLost) throw new Error("The original WebRTC peer is no longer usable.");
      if (stream) stream.getTracks().forEach((track) => track.stop());
      const activatedStream = acquired.candidate;
      acquired.track.addEventListener("ended", () => {
        if (stream === activatedStream) stopCapture("camera track ended");
      }, { once: true });
      stream = activatedStream;
      pendingStream = null;
      acquired = null;
      elements.preview.srcObject = stream;
      elements.capture.textContent = "active";
      startOutboundStats();
      elements.stop.disabled = false;
      elements.detail.textContent = answerPosted
        ? answerSubmissionConfirmed
          ? "Video is flowing on the original peer."
          : "The answer response was lost; retaining the original peer while it connects."
        : "Connecting the original peer.";
      setOverall(answerPosted ? "Streaming" : "Connecting", answerPosted ? "good" : "pending");
    } catch (error) {
      if (acquired) {
        acquired.candidate.getTracks().forEach((track) => track.stop());
        if (pendingStream === acquired.candidate) pendingStream = null;
      }
      if (!captureGeneration.isCurrent(generation)) return;
      await stopCapture("failed");
      elements.detail.textContent = error instanceof Error ? error.message : String(error);
      if (transportLost) {
        elements.start.disabled = true;
        setOverall("Transport lost", "bad");
      } else {
        setOverall("Failed", "bad");
      }
    }
  };

  const initialize = async () => {
    elements.environment.textContent = describeEnvironment();
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia || typeof RTCPeerConnection !== "function") {
      throw new Error("A secure context with camera and WebRTC support is required.");
    }
    const [roomToken, keyToken, extra] = location.hash.slice(1).split(".");
    if (extra !== undefined || !tokenPattern.test(roomToken || "") || !tokenPattern.test(keyToken || "")) {
      throw new Error("The room or encryption key in this URL is invalid.");
    }
    room = roomToken;
    keyBytes = base64UrlToBytes(keyToken);
    const result = await signal({ op: "get_offer", room });
    offer = await decryptEnvelope(result.payload);
    if (offer?.type !== "offer" || typeof offer.sdp !== "string" || offer.sdp.length > maximumSdpLength || !offer.sdp.startsWith("v=0")) {
      throw new Error("The decrypted host offer is invalid.");
    }
    if (offer.relayRequired && !offer.relayAvailable) throw new Error("The host requires Relay but supplied no usable Relay configuration.");
    if (offer.relayAvailable) {
      const relayOption = elements.transport.querySelector('option[value="relay"]');
      relayOption.disabled = false;
      relayOption.textContent = "Relay";
    }
    if (offer.relayRequired) {
      const directOption = elements.transport.querySelector('option[value="direct"]');
      directOption.disabled = true;
      elements.transport.value = "relay";
    }
    elements.prepare.disabled = false;
    elements.detail.textContent = "Allow camera access, select a camera, then start explicitly.";
    setOverall("Ready", "good");
  };

  elements.prepare.addEventListener("click", prepareCameras);
  elements.start.addEventListener("click", startCapture);
  elements.stop.addEventListener("click", () => stopCapture("stopped by user"));
  elements.camera.addEventListener("change", () => { if (stream) startCapture(); });
  document.addEventListener("visibilitychange", () => { if (document.hidden) stopCapture("page hidden"); });
  window.addEventListener("pagehide", () => { stopCapture("page hidden"); });

  initialize().catch((error) => {
    elements.detail.textContent = error instanceof Error ? error.message : String(error);
    setOverall("Failed", "bad");
  });
})();
