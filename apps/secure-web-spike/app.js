(() => {
  "use strict";

  const maximumSdpLength = 32 * 1024;
  const roomPattern = /^[A-Za-z0-9_-]{43}$/;
  const elements = Object.fromEntries([
    "overall-state", "secure-context", "webrtc-support", "motion-support",
    "signaling-state", "ice-state", "channel-state", "send-test", "enable-motion",
    "permission-state", "room", "browser", "candidate-pair", "last-sent",
    "last-received", "sensor-count", "sensor-values"
  ].map((id) => [id, document.getElementById(id)]));

  let peer = null;
  let channel = null;
  let sensorCount = 0;
  let lastSensorSend = 0;

  const setOverall = (text, kind) => {
    elements["overall-state"].textContent = text;
    elements["overall-state"].className = `pill ${kind}`;
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
      throw new Error(result?.error || `Signaling failed with HTTP ${response.status}`);
    }
    return result;
  };

  const waitForIceGathering = (connection, timeoutMs = 20000) => new Promise((resolve, reject) => {
    if (connection.iceGatheringState === "complete") {
      resolve();
      return;
    }
    const timeout = setTimeout(() => {
      connection.removeEventListener("icegatheringstatechange", changed);
      reject(new Error("ICE gathering did not complete in time."));
    }, timeoutMs);
    function changed() {
      if (connection.iceGatheringState !== "complete") return;
      clearTimeout(timeout);
      connection.removeEventListener("icegatheringstatechange", changed);
      resolve();
    }
    connection.addEventListener("icegatheringstatechange", changed);
  });

  const describeCandidate = (candidate) => {
    if (!candidate) return "unavailable";
    const address = candidate.address || candidate.ip || "unknown-address";
    const port = candidate.port ? `:${candidate.port}` : "";
    const protocol = candidate.protocol || "unknown-protocol";
    const type = candidate.candidateType || "unknown-type";
    return `${type} ${protocol} ${address}${port}`;
  };

  const updateSelectedRoute = async () => {
    if (!peer) return;
    try {
      const stats = await peer.getStats();
      let selectedPair = null;
      stats.forEach((report) => {
        if (report.type === "transport" && report.selectedCandidatePairId) {
          selectedPair = stats.get(report.selectedCandidatePairId) || selectedPair;
        }
      });
      if (!selectedPair) {
        stats.forEach((report) => {
          if (report.type === "candidate-pair" && report.state === "succeeded" && (report.selected || report.nominated)) {
            selectedPair = report;
          }
        });
      }
      if (!selectedPair) return;
      const local = stats.get(selectedPair.localCandidateId);
      const remote = stats.get(selectedPair.remoteCandidateId);
      elements["candidate-pair"].textContent = `local ${describeCandidate(local)} ↔ remote ${describeCandidate(remote)}`;
    } catch (error) {
      elements["candidate-pair"].textContent = `Stats unavailable: ${error.message}`;
    }
  };

  const receiveChannelMessage = async (event) => {
    let text;
    if (typeof event.data === "string") text = event.data;
    else if (event.data instanceof Blob) text = await event.data.text();
    else text = new TextDecoder().decode(event.data);
    elements["last-received"].textContent = text.slice(0, 500);
  };

  const attachChannel = (dataChannel) => {
    channel = dataChannel;
    channel.onopen = () => {
      elements["channel-state"].textContent = "open";
      elements["send-test"].disabled = false;
      setOverall("Connected", "good");
      updateSelectedRoute();
    };
    channel.onclose = () => {
      elements["channel-state"].textContent = "closed";
      elements["send-test"].disabled = true;
      setOverall("Closed", "bad");
    };
    channel.onerror = () => {
      elements["channel-state"].textContent = "error";
      setOverall("Channel error", "bad");
    };
    channel.onmessage = receiveChannelMessage;
  };

  const send = (payload, label) => {
    if (!channel || channel.readyState !== "open") return false;
    const json = JSON.stringify(payload);
    channel.send(json);
    elements["last-sent"].textContent = label || json;
    return true;
  };

  const sensorPayload = (event, type) => {
    if (type === "orientation") {
      return { type, alpha: event.alpha, beta: event.beta, gamma: event.gamma, absolute: event.absolute, sentAt: Date.now() };
    }
    const rotation = event.rotationRate;
    const acceleration = event.accelerationIncludingGravity;
    return {
      type,
      rotationRate: rotation ? { alpha: rotation.alpha, beta: rotation.beta, gamma: rotation.gamma } : null,
      acceleration: acceleration ? { x: acceleration.x, y: acceleration.y, z: acceleration.z } : null,
      interval: event.interval,
      sentAt: Date.now()
    };
  };

  const handleSensor = (event, type) => {
    const payload = sensorPayload(event, type);
    elements["sensor-values"].textContent = JSON.stringify(payload).slice(0, 500);
    const now = performance.now();
    if (now - lastSensorSend < 50) return;
    lastSensorSend = now;
    if (send(payload, `${type} update ${sensorCount + 1}`)) {
      sensorCount += 1;
      elements["sensor-count"].textContent = String(sensorCount);
    }
  };

  const requestSensorPermission = async (constructorName) => {
    const constructor = window[constructorName];
    if (!constructor || typeof constructor.requestPermission !== "function") return "not-required";
    return constructor.requestPermission();
  };

  elements["send-test"].addEventListener("click", () => {
    send({ type: "test", message: "Hello from iPhone Safari", sentAt: Date.now() }, "Test message");
  });

  elements["enable-motion"].addEventListener("click", async () => {
    elements["enable-motion"].disabled = true;
    try {
      const results = await Promise.all([
        requestSensorPermission("DeviceMotionEvent"),
        requestSensorPermission("DeviceOrientationEvent")
      ]);
      if (results.some((result) => result === "denied")) throw new Error("Sensor permission was denied.");

      let subscribed = false;
      if ("DeviceMotionEvent" in window) {
        window.addEventListener("devicemotion", (event) => handleSensor(event, "motion"));
        subscribed = true;
      }
      if ("DeviceOrientationEvent" in window) {
        window.addEventListener("deviceorientation", (event) => handleSensor(event, "orientation"));
        subscribed = true;
      }
      if (!subscribed) throw new Error("This browser exposes no motion or orientation event API.");
      elements["permission-state"].textContent = `Sensor permission enabled (${results.join(", ")}).`;
      elements["enable-motion"].textContent = "Motion sensors enabled";
    } catch (error) {
      elements["permission-state"].textContent = error.message;
      elements["enable-motion"].disabled = false;
      setOverall("Sensor issue", "bad");
    }
  });

  const start = async () => {
    const room = location.hash.slice(1);
    elements["room"].textContent = room || "Missing";
    elements["secure-context"].textContent = String(window.isSecureContext);
    elements["webrtc-support"].textContent = typeof RTCPeerConnection === "function" ? "available" : "unavailable";
    const motionApis = ["DeviceMotionEvent", "DeviceOrientationEvent"].filter((name) => name in window);
    elements["motion-support"].textContent = motionApis.length ? motionApis.join(" + ") : "unavailable";
    elements["browser"].textContent = navigator.userAgent;

    if (!window.isSecureContext) throw new Error("The page is not a browser secure context.");
    if (typeof RTCPeerConnection !== "function") throw new Error("WebRTC is unavailable in this browser.");
    if (!roomPattern.test(room)) throw new Error("The room fragment is missing or invalid.");

    elements["signaling-state"].textContent = "Retrieving offer";
    const offerResult = await signal({ op: "get_offer", room });
    if (typeof offerResult.offer !== "string" || offerResult.offer.length > maximumSdpLength) {
      throw new Error("The signaling offer is missing or invalid.");
    }

    peer = new RTCPeerConnection({ iceServers: [] });
    peer.oniceconnectionstatechange = () => {
      elements["ice-state"].textContent = peer.iceConnectionState;
      if (["connected", "completed"].includes(peer.iceConnectionState)) updateSelectedRoute();
      if (peer.iceConnectionState === "failed") setOverall("ICE failed", "bad");
    };
    peer.onconnectionstatechange = () => {
      if (peer.connectionState === "failed") setOverall("Connection failed", "bad");
    };
    peer.ondatachannel = (event) => attachChannel(event.channel);

    await peer.setRemoteDescription({ type: "offer", sdp: offerResult.offer });
    const answer = await peer.createAnswer();
    await peer.setLocalDescription(answer);
    elements["signaling-state"].textContent = "Gathering complete answer";
    await waitForIceGathering(peer);
    if (!peer.localDescription?.sdp || peer.localDescription.sdp.length > maximumSdpLength) {
      throw new Error("The complete browser answer is missing or too large.");
    }
    await signal({ op: "set_answer", room, answer: peer.localDescription.sdp });
    elements["signaling-state"].textContent = "Answer posted; signaling finished";
    setOverall("Connecting", "pending");
  };

  start().catch((error) => {
    elements["signaling-state"].textContent = error.message;
    setOverall("Failed", "bad");
  });
})();
