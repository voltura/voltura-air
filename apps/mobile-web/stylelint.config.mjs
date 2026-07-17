/** @type {import("stylelint").Config} */
export default {
  extends: ["stylelint-config-standard"],
  ignoreFiles: ["dist/**", "src/styles/generated/**"],
  reportDescriptionlessDisables: true,
  reportDisables: true,
  rules: {
    "color-no-hex": true,
    "declaration-no-important": true,
    "function-disallowed-list": ["/^(?:rgb|rgba|hsl|hsla)$/"],
    "max-nesting-depth": 3,
    // Component styles intentionally group state and responsive variants by
    // ownership. The heuristic treats unrelated selectors as one specificity
    // chain, so enforce bounded selectors instead of reordering the cascade.
    "no-descending-specificity": null,
    "property-no-vendor-prefix": [
      true,
      {
        ignoreProperties: ["-webkit-appearance", "-webkit-user-select"]
      }
    ],
    "selector-id-pattern": "^root$",
    "selector-max-id": 1,
    "selector-max-specificity": "0,5,3",
    "selector-max-universal": 1,
    "selector-not-notation": "complex",
    "selector-pseudo-element-colon-notation": "double"
  }
};
