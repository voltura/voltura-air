"use strict";

const assert = require("node:assert/strict");
const retainSubmittedPeer = require("../submitted-peer.js");

(async () => {
  const connection = { id: "original-peer" };
  const detached = [];
  const sender = { replaceTrack: async (track) => { detached.push(track); } };
  let published = null;

  const active = await retainSubmittedPeer(
    connection,
    sender,
    () => false,
    (retainedConnection, retainedSender) => { published = [retainedConnection, retainedSender]; });

  assert.equal(active, false);
  assert.deepEqual(published, [connection, sender]);
  assert.deepEqual(detached, [null]);
  console.log("submitted-peer-tests-passed");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
