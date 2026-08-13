"use strict";

const assert = require("node:assert/strict");
const createCaptureGeneration = require("../capture-generation.js");

let hidden = false;
const generation = createCaptureGeneration(() => hidden);
const pendingStart = generation.begin();

hidden = true;
generation.invalidate();
assert.equal(generation.isCurrent(pendingStart), false);
assert.throws(() => generation.assertCurrent(pendingStart), /cancelled/);

hidden = false;
const replacementStart = generation.begin();
assert.equal(generation.isCurrent(replacementStart), true);
generation.assertCurrent(replacementStart);

console.log("capture-generation-tests-passed");
