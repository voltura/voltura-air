((root, factory) => {
  "use strict";
  const retainSubmittedPeer = factory();
  if (typeof module === "object" && module.exports) module.exports = retainSubmittedPeer;
  root.retainSubmittedPeer = retainSubmittedPeer;
})(typeof globalThis === "object" ? globalThis : window, () => {
  "use strict";
  return async (connection, sender, isCurrent, publish) => {
    publish(connection, sender);
    if (isCurrent()) return true;
    await sender.replaceTrack(null).catch(() => {});
    return false;
  };
});
