import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export const commandDescriptions = {
  "actions:restore": "Install the checked-in GitHub Actions workflow files.",
  "ai:init": "Install the newest ChatGPT/Codex package if needed, then configure the daily task and desktop shortcut.",
  "ai:schedule:create": "Create or refresh the hidden daily ChatGPT/Codex update task; accepts --time HH:mm:ss.",
  "ai:schedule:remove": "Remove every ChatGPT/Codex updater scheduled task created by this repository.",
  "ai:shortcut:create": "Create or refresh the desktop shortcut for a visible ChatGPT/Codex update check.",
  "ai:shortcut:remove": "Remove the ChatGPT/Codex updater desktop shortcut created by this repository.",
  "ai:update": "Check the official ChatGPT/Codex package version and silently install it when newer.",
  "branch:sync": "Synchronize the current branch with its configured upstream.",
  build: "Run the full cross-runtime production build gate for broad/shared or release work.",
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
  "dev:quick": "Rebuild current sources quickly and start the host with normal production settings for human device validation.",
  "dev:source": "Create a clean source archive for development handoff.",
  "dev:ui": "Open an isolated Chrome device-mode session against the real pairing flow.",
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
  "publish:site": "Build hosted/preview clients, refresh statistics, then publish the public site and short relay redirect.",
  "publish:site:prepared": "Publish the already prepared public-site snapshot without regenerating tracked files.",
  "publish:site:list": "List the public-site deployment configuration.",
  "publish:site:password": "Store the public-site deployment password securely for this user.",
  "publish:site:password:clear": "Remove the stored public-site deployment password.",
  "site:dev:init": "Install/check local PHP and MariaDB, then initialize the development catalog database.",
  "site:dev:admin": "Promote an existing local catalog account to administrator.",
  "site:dev": "Run the PHP public site locally against the development catalog database.",
  "site:hosted:build": "Build the separately scoped hosted Relay PWA under docs/site/app.",
  "site:preview:build": "Build the catalog preview from the real mobile custom-screen renderer.",
  "third-party:check": "Verify shipped dependency versions, native provenance, and generated browser notices.",
  "third-party:generate": "Regenerate the mobile PWA's complete third-party license notice from installed production packages.",
  release: "Prepare a versioned release and update its authoritative version values.",
  "release:bump": "Advance version values only through the project's one-digit patch and minor sequence.",
  "release:draft": "Build, test, package, push, deploy the site, and create an audited GitHub draft; accepts an optional version.",
  "release:full": "Run the complete stable release: build, test, package, push, deploy and verify the relay, deploy the site, and publish GitHub Latest; accepts an optional version.",
  "release:sync-release-notes": "Synchronize a published GitHub release's marked editorial notes into the matching local section.",
  "relay:check": "Build and test the portable relay core and adapters without deploying.",
  "relay:deploy": "Deploy the configured Cloudflare Worker and Durable Object.",
  "relay:health": "Verify the configured production relay health endpoint with bounded HTTPS validation.",
  "relay:setup": "Interactively configure restricted Cloudflare secrets, deploy, verify, and save the relay address.",
  "relay:usage": "Read current-month Cloudflare TURN transfer using a hidden restricted-token prompt.",
  "screenshots:site": "Capture screenshots for the public documentation site.",
  "screens:check": "Validate generated official screens with the current host reader and responsive portrait/landscape rendering.",
  "screens:generate": "Generate the 14 official custom-screen packages and catalog metadata.",
  "screens:layout-check": "Render all 14 official custom screens at phone portrait and landscape sizes and reject overflow.",
  "screens:official": "Generate the official custom-screen packages and deterministic import bundle.",
  "screen-view:layout-check": "Render the direct Screen View control in a real browser and verify its video hit target.",
  "size:check": "Fail if strong source-size warnings lack current review rationales.",
  "size:report": "Report source-file size and ownership signals.",
  test: "Run the full repository test gate for release or repository-wide shared-contract work.",
  "test:host": "Run the Windows host test suite.",
  "test:scripts": "Run tests for repository automation scripts.",
  "test:site-import-integration": "Exercise official-screen import success, rollback boundaries, and stable updates against isolated local MariaDB.",
  "test:ui": "Run the isolated browser device-mode smoke test through the real pairing flow.",
  "test:web": "Run the mobile web unit suite and real-browser Screen View layout check.",
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
