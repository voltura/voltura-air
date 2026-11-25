export function webRtcSetupAbortError(signal?: AbortSignal): Error {
  const reason: unknown = signal?.reason;
  return reason instanceof Error || reason instanceof DOMException
    ? reason
    : new DOMException("WebRTC setup stopped.", "AbortError");
}

export function runWebRtcSetupStep<T>(signal: AbortSignal, step: () => Promise<T>): Promise<T> {
  if (signal.aborted) {
    return Promise.reject(webRtcSetupAbortError(signal));
  }
  return new Promise((resolve, reject) => {
    const cleanup = () => signal.removeEventListener("abort", onAbort);
    const fail = (error: unknown) => {
      cleanup();
      reject(
        error instanceof Error || error instanceof DOMException
          ? error
          : new Error("WebRTC setup failed.", { cause: error }),
      );
    };
    const onAbort = () => fail(webRtcSetupAbortError(signal));
    signal.addEventListener("abort", onAbort, { once: true });
    try {
      void step().then((result) => {
        cleanup();
        resolve(result);
      }, fail);
    } catch (error) {
      fail(error);
    }
  });
}
