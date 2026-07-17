import { access, readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const catalogRelativePath = "docs/README.md";
const ignoredDirectories = new Set([
  ".git",
  ".vs",
  "artifacts",
  "bin",
  "coverage",
  "dist",
  "node_modules",
  "obj"
]);

export async function checkDocumentationMap({
  root = process.cwd(),
  publicSurfaces
} = {}) {
  const markdownFiles = await collectMarkdownFiles(root, root);
  const resolvedPublicSurfaces = publicSurfaces ?? await collectPublicDocumentationSurfaces(root);
  const publicIntakeSurfaces = await collectPublicIntakeSurfaces(root);
  const requiredFiles = [...new Set(
    markdownFiles
      .filter((file) => file !== catalogRelativePath)
      .concat(resolvedPublicSurfaces, publicIntakeSurfaces)
  )].sort();
  const errors = [];

  if (!markdownFiles.includes(catalogRelativePath)) {
    return {
      checkedLinks: 0,
      errors: [`Missing canonical documentation map: ${catalogRelativePath}`],
      requiredFiles
    };
  }

  for (const publicSurface of resolvedPublicSurfaces) {
    if (!await exists(path.join(root, publicSurface))) {
      errors.push(`Missing required public documentation surface: ${publicSurface}`);
    }
  }

  const catalogContents = await readFile(path.join(root, catalogRelativePath), "utf8");
  const catalogTargets = new Map();
  for (const target of extractLocalLinkTargets(catalogContents)) {
    const resolved = resolveTarget(root, catalogRelativePath, target);
    if (!resolved) {
      continue;
    }

    catalogTargets.set(resolved, (catalogTargets.get(resolved) ?? 0) + 1);
  }

  for (const requiredFile of requiredFiles) {
    const count = catalogTargets.get(requiredFile) ?? 0;
    if (count === 0) {
      errors.push(`Documentation map does not catalog: ${requiredFile}`);
    } else if (count > 1) {
      errors.push(`Documentation map catalogs '${requiredFile}' ${count} times; keep one authoritative row.`);
    }
  }

  let checkedLinks = 0;
  for (const sourceFile of markdownFiles) {
    const contents = await readFile(path.join(root, sourceFile), "utf8");
    for (const target of extractLocalLinkTargets(contents)) {
      const resolved = resolveTarget(root, sourceFile, target);
      if (!resolved) {
        continue;
      }

      checkedLinks += 1;
      if (!await exists(path.join(root, resolved))) {
        errors.push(`${sourceFile} links to missing local target: ${target}`);
      }
    }
  }

  return { checkedLinks, errors, requiredFiles };
}

async function collectPublicDocumentationSurfaces(root) {
  const siteRoot = path.join(root, "docs", "site");
  if (!await exists(siteRoot)) {
    return [];
  }

  return (await collectFiles(siteRoot, root))
    .filter((file) => {
      const extension = path.extname(file).toLowerCase();
      return extension === ".html" || extension === ".php" || path.basename(file).toLowerCase() === "llms.txt";
    })
    .sort();
}

async function collectPublicIntakeSurfaces(root) {
  const issueTemplateRoot = path.join(root, ".github", "ISSUE_TEMPLATE");
  if (!await exists(issueTemplateRoot)) {
    return [];
  }

  return (await collectFiles(issueTemplateRoot, root))
    .filter((file) => {
      const extension = path.extname(file).toLowerCase();
      return extension === ".yml" || extension === ".yaml";
    })
    .sort();
}

async function collectFiles(directory, root) {
  const files = [];
  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectFiles(absolutePath, root));
    } else if (entry.isFile()) {
      files.push(toRepoPath(path.relative(root, absolutePath)));
    }
  }

  return files;
}

async function collectMarkdownFiles(directory, root) {
  const files = [];
  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) {
      continue;
    }

    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectMarkdownFiles(absolutePath, root));
    } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".md")) {
      files.push(toRepoPath(path.relative(root, absolutePath)));
    }
  }

  return files.sort();
}

function extractLocalLinkTargets(contents) {
  const targets = [];
  const inlineLinkPattern = /(?<!!)\[[^\]\r\n]*\]\((?<target><[^>\r\n]+>|[^)\r\n]+)\)/gu;
  const referenceLinkPattern = /^\s*\[[^\]\r\n]+\]:\s*(?<target><[^>\r\n]+>|\S+)/gmu;

  for (const pattern of [inlineLinkPattern, referenceLinkPattern]) {
    for (const match of contents.matchAll(pattern)) {
      const target = normalizeRawTarget(match.groups?.target ?? "");
      if (target && !isExternalOrAnchor(target)) {
        targets.push(target);
      }
    }
  }

  return targets;
}

function normalizeRawTarget(rawTarget) {
  const trimmed = rawTarget.trim();
  if (trimmed.startsWith("<") && trimmed.endsWith(">")) {
    return trimmed.slice(1, -1);
  }

  return trimmed.split(/\s+(?=["'])/u, 1)[0] ?? "";
}

function isExternalOrAnchor(target) {
  return target.startsWith("#") || /^[a-z][a-z0-9+.-]*:/iu.test(target) || target.startsWith("//");
}

function resolveTarget(root, sourceFile, target) {
  const pathOnly = target.split(/[?#]/u, 1)[0];
  if (!pathOnly) {
    return null;
  }

  let decoded;
  try {
    decoded = decodeURIComponent(pathOnly);
  } catch {
    return null;
  }

  const absoluteTarget = decoded.startsWith("/")
    ? path.resolve(root, decoded.slice(1))
    : path.resolve(root, path.dirname(sourceFile), decoded);
  const relativeTarget = path.relative(root, absoluteTarget);
  if (relativeTarget.startsWith("..") || path.isAbsolute(relativeTarget)) {
    return null;
  }

  return toRepoPath(relativeTarget);
}

async function exists(filePath) {
  try {
    await access(filePath);
    return true;
  } catch {
    return false;
  }
}

function toRepoPath(filePath) {
  return filePath.split(path.sep).join("/");
}

const currentFile = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === currentFile) {
  const result = await checkDocumentationMap();
  if (result.errors.length > 0) {
    for (const error of result.errors) {
      console.error(`docs:check: ${error}`);
    }
    process.exitCode = 1;
  } else {
    console.log(
      `Documentation map covers ${result.requiredFiles.length} maintained files and ${result.checkedLinks} local links.`
    );
  }
}
