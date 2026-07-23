import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export const commandDescriptions = {
  "actions:restore": "Restore the archived GitHub Actions workflow files for deliberate future re-enabling.",
  "ai:init": "Install the newest ChatGPT/Codex package if needed, then configure the daily task and desktop shortcut.",
  "ai:schedule:create": "Create or refresh the hidden daily ChatGPT/Codex update task; accepts --time HH:mm:ss.",
  "ai:schedule:remove": "Remove every ChatGPT/Codex updater scheduled task created by this repository.",
  "ai:shortcut:create": "Create or refresh the desktop shortcut for a visible ChatGPT/Codex update check.",
  "ai:shortcut:remove": "Remove the ChatGPT/Codex updater desktop shortcut created by this repository.",
  "ai:update": "Check the official ChatGPT/Codex package version and silently install it when newer.",
  "branch:sync": "Synchronize the current branch with its configured upstream.",
  build: "Validate UI tokens, then build the mobile app and Windows host.",
  "branding:generate": "Generate application icons, NSIS installer artwork, and public-site screenshots.",
  "cache:purge": "Clear stale Windows icon cache entries and restart Explorer.",
  "clean:git": "Compact the local Git object database and prune unreachable objects.",
  "clean:temp": "Remove ignored build and cache files while preserving local editor settings.",
  "clean:temp:preview": "Show which ignored build and cache files clean:temp would remove.",
  "code:statistics": "Print source statistics; append -- --report to refresh docs/site/stats.html and open it.",
  "deps:update": "Update dependencies within their declared version ranges.",
  "dev": "Start the normal checked development loop for the host and mobile client.",
  "dev:bare-source": "Create a source archive without repository metadata or development files.",
  "dev:host": "Start only the Windows host development server.",
  "dev:quick": "Start the host quickly while rebuilding the host-served client without validation or watchdog compilation.",
  "dev:source": "Create a clean source archive for development handoff.",
  "dev:ui": "Open an interactive Chrome device-mode session against the real pairing flow.",
  "dev:web": "Start only the mobile web development server.",
  "docs:check": "Verify the documentation catalog and internal document links.",
  help: "List every root npm command with its purpose and implementation.",
  "host:ownership:check": "Check that Windows host partial classes retain clear ownership boundaries.",
  "icons:generate": "Generate application icons from the authoritative branding artwork.",
  lint: "Lint the mobile web application.",
  "maintenance:full": "Run icon-cache cleanup, temporary-file cleanup, Git maintenance, and dependency updates.",
  "package:source": "Create a clean source-code ZIP archive.",
  "package:source:bare": "Create a minimal source-code ZIP archive.",
  "package:win": "Build the full Windows installer package.",
  "package:win:small": "Build only the framework-dependent Windows installer package.",
  "package:win:test": "Build an uncompressed Windows installer for testing.",
  "publish:site": "Refresh the code statistics page, then publish the public documentation site.",
  "publish:site:list": "List the public-site deployment configuration.",
  "publish:site:password": "Store the public-site deployment password securely for this user.",
  "publish:site:password:clear": "Remove the stored public-site deployment password.",
  release: "Prepare a versioned release and update its authoritative version values.",
  "release:bump": "Advance version values only through the project's one-digit patch and minor sequence.",
  "release:draft": "Build, test, package, push, deploy the site, and create an audited GitHub draft; accepts an optional version.",
  "release:full": "Run the complete stable release: build, test, package, push, deploy the site, and publish GitHub Latest; accepts an optional version.",
  "release:sync-release-notes": "Synchronize a published GitHub release's marked editorial notes into the matching local section.",
  "screenshots:site": "Capture screenshots for the public documentation site.",
  "size:check": "Fail if strong source-size warnings lack current review rationales.",
  "size:report": "Report source-file size and ownership signals.",
  test: "Run documentation, UI, source-size, host-ownership, web, script, and host tests.",
  "test:host": "Run the Windows host test suite.",
  "test:scripts": "Run tests for repository automation scripts.",
  "test:ui": "Run the browser device-mode smoke test through the real pairing flow.",
  "test:web": "Run the mobile web test suite.",
  "ui:tokens:check": "Verify generated UI tokens are current.",
  "ui:tokens:generate": "Regenerate UI tokens from their source definitions."
};

export function findUndocumentedCommands(scripts) {
  return Object.keys(scripts).filter((name) => !(name in commandDescriptions));
}

export function findStaleDescriptions(scripts) {
  return Object.keys(commandDescriptions).filter((name) => !(name in scripts));
}

export function formatCommandHelp(scripts, filterText = "", { useColor = false } = {}) {
  const paint = (code, text) => useColor ? `\u001b[${code}m${text}\u001b[0m` : text;
  const normalizedFilter = filterText.toLocaleLowerCase();
  const commands = Object.keys(scripts)
    .filter((name) => name.toLocaleLowerCase().includes(normalizedFilter))
    .sort();
  const widestName = Math.max(...commands.map((name) => name.length));
  const heading = filterText
    ? `Voltura Air npm commands matching "${filterText}"`
    : "Voltura Air npm commands";

  if (commands.length === 0) {
    return [heading, "", "No npm commands matched the filter.", "", "Run a command with: npm run <name>"].join("\n");
  }

  return [
    paint("1;36", heading),
    "",
    ...commands.flatMap((name) => [
      paint("1;33", `npm run ${name}`),
      `  ${paint("36", "Purpose:")} ${commandDescriptions[name]}`,
      `  ${paint("2", "Runs:")}    ${scripts[name]}`,
      ""
    ]),
    paint("2", "Filter this list with: npm run help -- <name-fragment>")
  ].join("\n");
}

async function main() {
  const packageJsonPath = path.join(repositoryRoot, "package.json");
  const packageJson = JSON.parse(await readFile(packageJsonPath, "utf8"));
  const undocumented = findUndocumentedCommands(packageJson.scripts);
  const stale = findStaleDescriptions(packageJson.scripts);

  if (undocumented.length > 0 || stale.length > 0) {
    const issues = [
      undocumented.length > 0 && `Missing descriptions: ${undocumented.join(", ")}`,
      stale.length > 0 && `Descriptions for missing scripts: ${stale.join(", ")}`
    ].filter(Boolean);
    throw new Error(`Command help is out of date. ${issues.join(". ")}`);
  }

  console.log(formatCommandHelp(
    packageJson.scripts,
    process.argv.slice(2).join(" "),
    { useColor: Boolean(process.stdout.isTTY && !process.env.NO_COLOR) }
  ));
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  await main();
}
