import path from "node:path";

const sourceRootSegment = `${path.sep}src${path.sep}`;

function normalizePath(filePath) {
  return path.normalize(filePath).toLowerCase();
}

function classify(filePath) {
  const normalized = normalizePath(filePath);
  const sourceRootIndex = normalized.lastIndexOf(sourceRootSegment);
  if (sourceRootIndex < 0) {
    return null;
  }

  const relative = normalized.slice(sourceRootIndex + sourceRootSegment.length).split(path.sep);
  const first = relative[0];
  if (first === "app" || first.startsWith("app.") || first === "main.tsx") {
    return { layer: "app" };
  }

  if (first === "vite-env.d.ts") {
    return { layer: "config" };
  }

  if (first === "features" && relative[1]) {
    return { layer: "feature", slice: relative[1] };
  }

  if (first === "ui") {
    return { layer: "ui" };
  }

  if (first === "foundation" && relative[1] && relative[2]) {
    return { layer: "foundation" };
  }

  return { layer: "invalid" };
}

function isPublicFeatureImport(resolvedPath, target) {
  const normalized = normalizePath(resolvedPath);
  return normalized.endsWith(`${path.sep}features${path.sep}${target.slice}`);
}

function dependencyError(source, target, resolvedPath) {
  if (source.layer === "invalid" || target.layer === "invalid" || target.layer === "config") {
    return null;
  }

  if (source.layer === "app") {
    if (target.layer === "feature" && !isPublicFeatureImport(resolvedPath, target)) {
      return `Import feature '${target.slice}' through its public index.`;
    }

    return null;
  }

  if (source.layer === "ui") {
    return target.layer === "ui" ? null : "Shared UI may depend only on shared UI and external packages.";
  }

  if (source.layer === "foundation") {
    return target.layer === "foundation" ? null : "Foundation code may depend only on foundation code and external packages.";
  }

  if (target.layer === "app") {
    return "Feature code must not depend on the app composition root.";
  }

  if (target.layer === "feature" && target.slice !== source.slice) {
    return `Feature '${source.slice}' must not import private code from feature '${target.slice}'. Promote a shared contract or use the feature's public API from the app root.`;
  }

  return null;
}

function checkDependency(context, node, specifier) {
  if (typeof specifier !== "string" || !specifier.startsWith(".")) {
    return;
  }

  const source = classify(context.filename);
  const resolvedPath = path.resolve(path.dirname(context.filename), specifier);
  const target = classify(resolvedPath);
  if (!source || !target) {
    return;
  }

  const message = dependencyError(source, target, resolvedPath);
  if (message) {
    context.report({ node, message });
  }
}

export const architectureRule = {
  meta: {
    type: "problem",
    docs: {
      description: "Enforce Voltura Air's app, feature, UI, and domain-owned foundation dependency direction."
    },
    schema: []
  },
  create(context) {
    return {
      Program(node) {
        if (classify(context.filename)?.layer === "invalid") {
          context.report({
            node,
            message: "Mobile source must be owned by app, features/<capability>, ui, or foundation/<domain>."
          });
        }
      },
      ImportDeclaration(node) {
        checkDependency(context, node.source, node.source.value);
      },
      ExportAllDeclaration(node) {
        checkDependency(context, node.source, node.source.value);
      },
      ExportNamedDeclaration(node) {
        if (node.source) {
          checkDependency(context, node.source, node.source.value);
        }
      },
      ImportExpression(node) {
        if (node.source.type === "Literal") {
          checkDependency(context, node.source, node.source.value);
        }
      }
    };
  }
};
