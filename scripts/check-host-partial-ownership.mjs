import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";

const partialTypePattern = /^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|readonly|ref|unsafe|new)\s+)*partial\s+(?:class|struct|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)/gmu;
const namespacePattern = /^\s*namespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*[;{]/mu;
const excludedDirectories = new Set(["bin", "obj"]);

export async function findMultiFilePartialTypes(hostRoot) {
  const declarations = new Map();
  for (const filePath of await collectCSharpFiles(hostRoot)) {
    const source = await readFile(filePath, "utf8");
    const namespace = source.match(namespacePattern)?.[1] ?? "<global>";
    for (const match of source.matchAll(partialTypePattern)) {
      const qualifiedName = `${namespace}.${match[1]}`;
      const files = declarations.get(qualifiedName) ?? new Set();
      files.add(filePath);
      declarations.set(qualifiedName, files);
    }
  }

  return [...declarations]
    .filter(([, files]) => files.size > 1)
    .map(([type, files]) => ({ type, files: [...files].sort() }))
    .sort((left, right) => left.type.localeCompare(right.type));
}

async function collectCSharpFiles(directory) {
  const files = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.isDirectory() && excludedDirectories.has(entry.name)) {
      continue;
    }

    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectCSharpFiles(absolutePath));
    } else if (entry.isFile() && entry.name.endsWith(".cs") && !isGeneratedSource(entry.name)) {
      files.push(absolutePath);
    }
  }

  return files;
}

function isGeneratedSource(fileName) {
  return fileName.endsWith(".g.cs") || fileName.includes(".generated.");
}

async function main() {
  const repositoryRoot = process.cwd();
  const hostRoot = path.join(repositoryRoot, "apps", "windows-host");
  const findings = await findMultiFilePartialTypes(hostRoot);
  if (findings.length === 0) {
    console.log("Host partial types are confined to one maintained source file each.");
    return;
  }

  console.error("Host types split across maintained partial files:");
  for (const finding of findings) {
    console.error(`- ${finding.type}`);
    for (const file of finding.files) {
      console.error(`  - ${path.relative(repositoryRoot, file).split(path.sep).join("/")}`);
    }
  }

  console.error("Use a named owner for each responsibility; reserve partial for framework or generated code in one maintained file.");
  process.exitCode = 1;
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  await main();
}
