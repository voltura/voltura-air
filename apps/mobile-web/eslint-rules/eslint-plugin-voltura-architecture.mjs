import { architectureRule } from "./architecture.mjs";

export default {
  meta: {
    name: "eslint-plugin-voltura-architecture",
    version: "1.0.0",
  },
  rules: {
    "dependency-direction": architectureRule,
  },
};
