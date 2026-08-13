((root, factory) => {
  "use strict";
  const createCaptureGeneration = factory();
  if (typeof module === "object" && module.exports) module.exports = createCaptureGeneration;
  root.createCaptureGeneration = createCaptureGeneration;
})(typeof globalThis === "object" ? globalThis : window, () => {
  "use strict";
  return (isHidden) => {
    let generation = 0;
    return Object.freeze({
      begin: () => ++generation,
      invalidate: () => { ++generation; },
      isCurrent: (value) => value === generation && !isHidden(),
      assertCurrent(value) {
        if (value !== generation || isHidden()) throw new Error("Capture start was cancelled.");
      }
    });
  };
});
