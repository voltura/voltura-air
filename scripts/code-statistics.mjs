import { spawn } from "node:child_process";
import { readFile, readdir, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";

const root = process.cwd();
const arguments_ = process.argv.slice(2);
const reportMode = arguments_.includes("--report") || process.env.npm_config_report === "true";
const quietMode = arguments_.includes("--quiet");
const openReport = reportMode && !arguments_.includes("--no-open");
const supportedArguments = new Set(["--no-open", "--quiet", "--report"]);
const unsupportedArguments = arguments_.filter((argument) => !supportedArguments.has(argument));
const reportPath = path.join(root, "docs", "site", "stats.html");

if (unsupportedArguments.length > 0) {
  throw new Error(`Unsupported option: ${unsupportedArguments.join(", ")}. Use --report [--no-open] [--quiet].`);
}

const reportLines = [];
const excludedDirectories = new Set([
  ".git", ".vs", "artifacts", "bin", "coverage", "dist", "node_modules", "obj", "TestResults"
]);
const reports = [
  {
    title: "Mobile client",
    locations: ["apps/mobile-web"],
    directories: ["apps/mobile-web"],
    extensions: new Set([
      ".css", ".html", ".js", ".json", ".jsx", ".mjs", ".ts", ".tsx", ".webmanifest"
    ])
  },
  {
    title: "Windows host",
    locations: ["apps/windows-host", "apps/cursor-watchdog", "VolturaAir.slnx"],
    directories: ["apps/windows-host", "apps/cursor-watchdog"],
    additionalFiles: ["VolturaAir.slnx"],
    extensions: new Set([
      ".c", ".config", ".cs", ".csproj", ".json", ".props", ".resx", ".slnx", ".targets", ".xaml", ".xml"
    ])
  },
  {
    title: "NSIS installers",
    locations: ["installer"],
    directories: ["installer"],
    extensions: new Set([".nsi"])
  }
];
const assetCategories = [
  { title: "Documents", extensions: [".md"] },
  { title: "Images", extensions: [".png", ".ico", ".svg"] },
  { title: "Cursors", extensions: [".cur"] }
];
const assetExtensions = new Set(assetCategories.flatMap(({ extensions }) => extensions));
const scriptExtensions = [".bat", ".cjs", ".cmd", ".js", ".mjs", ".nsi", ".ps1", ".sh"];
const testReports = [
  {
    title: "Mobile client",
    directories: ["apps/mobile-web"],
    extensions: new Set([".js", ".jsx", ".ts", ".tsx"]),
    filePattern: /\.(?:test|spec)\.(?:js|jsx|ts|tsx)$/i,
    testPattern: /\b(?:it|test)(?:\.(?:each|only|skip|todo))*\s*\(/g
  },
  {
    title: "Windows host",
    directories: ["tests/VolturaAir.Host.Tests"],
    extensions: new Set([".cs"]),
    filePattern: /Tests?\.cs$/i,
    testPattern: /\[(?:Fact|Theory)\]/g
  }
];

function countLines(contents) {
  if (contents.length === 0) {
    return 0;
  }

  const lineFeeds = contents.match(/\n/g)?.length ?? 0;
  return lineFeeds + (contents.endsWith("\n") ? 0 : 1);
}

async function collectFiles(directory, extensions) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const absolutePath = path.join(directory, entry.name);

    if (entry.isDirectory()) {
      if (!excludedDirectories.has(entry.name)) {
        files.push(...await collectFiles(absolutePath, extensions));
      }

      continue;
    }

    if (entry.isFile() && absolutePath !== reportPath && (!extensions || extensions.has(path.extname(entry.name).toLowerCase()))) {
      files.push(absolutePath);
    }
  }

  return files;
}

async function createSourceReport({ title, locations, directories, additionalFiles = [], extensions }) {
  const files = (await Promise.all(directories.map((directory) => collectFiles(path.join(root, directory), extensions)))).flat();
  files.push(...additionalFiles
    .map((file) => path.join(root, file))
    .filter((file) => extensions.has(path.extname(file).toLowerCase())));
  const byExtension = new Map();

  for (const file of files) {
    const extension = path.extname(file).toLowerCase();
    const lines = countLines(await readFile(file, "utf8"));
    const statistics = byExtension.get(extension) ?? { files: 0, lines: 0 };

    statistics.files += 1;
    statistics.lines += lines;
    byExtension.set(extension, statistics);
  }

  const totals = [...byExtension.values()].reduce(
    (result, statistics) => ({
      files: result.files + statistics.files,
      lines: result.lines + statistics.lines
    }),
    { files: 0, lines: 0 }
  );

  return { title, locations, totals, byExtension, files };
}

async function createAssetsReport() {
  const files = await collectFiles(root, assetExtensions);
  const counts = new Map();

  for (const file of files) {
    const extension = path.extname(file).toLowerCase();
    counts.set(extension, (counts.get(extension) ?? 0) + 1);
  }

  return { total: files.length, counts };
}

async function createTestsReport() {
  return Promise.all(testReports.map(async ({ title, directories, extensions, filePattern, testPattern }) => {
    const files = (await Promise.all(directories.map((directory) => collectFiles(path.join(root, directory), extensions))))
      .flat()
      .filter((file) => filePattern.test(path.basename(file)));
    const cases = (await Promise.all(files.map(async (file) =>
      [...(await readFile(file, "utf8")).matchAll(testPattern)].length)))
      .reduce((total, count) => total + count, 0);

    return { title, files: files.length, cases };
  }));
}

function createScriptsReport(repositoryFiles) {
  const counts = new Map(scriptExtensions.map((extension) => [extension, 0]));

  for (const file of repositoryFiles) {
    const extension = path.extname(file).toLowerCase();
    if (counts.has(extension)) {
      counts.set(extension, counts.get(extension) + 1);
    }
  }

  const total = [...counts.values()].reduce((sum, count) => sum + count, 0);
  return { total, counts };
}

async function createNpmCommandsReport(repositoryFiles) {
  const packageFiles = repositoryFiles.filter((file) => path.basename(file) === "package.json");
  const packages = await Promise.all(packageFiles.map(async (file) => {
    const { scripts = {} } = JSON.parse(await readFile(file, "utf8"));
    return { file, commandCount: Object.keys(scripts).length };
  }));

  return {
    total: packages.reduce((sum, { commandCount }) => sum + commandCount, 0),
    packages: packages.sort((left, right) => relativePath(left.file).localeCompare(relativePath(right.file)))
  };
}

async function createFileDetailsReport(repositoryFiles) {
  const files = await Promise.all(repositoryFiles.map(async (file) => {
    const metadata = await stat(file);
    return { file, modified: metadata.mtime, size: metadata.size };
  }));
  const byModifiedDate = [...files].sort((left, right) => left.modified - right.modified);

  return {
    oldest: byModifiedDate[0],
    newest: byModifiedDate.at(-1),
    largest: [...files].sort((left, right) => right.size - left.size).slice(0, 10)
  };
}

async function createCodeFileDetailsReport(codeReports) {
  const files = await Promise.all(codeReports.flatMap(({ files: reportFiles }) => reportFiles).map(async (file) => {
    const metadata = await stat(file);
    return { file, size: metadata.size };
  }));

  return files.sort((left, right) => right.size - left.size).slice(0, 10);
}

function createGrandTotal(reports_) {
  return reports_.reduce(
    (result, { totals }) => ({
      files: result.files + totals.files,
      lines: result.lines + totals.lines
    }),
    { files: 0, lines: 0 }
  );
}

function formatNumber(value) {
  return new Intl.NumberFormat("en-US").format(value);
}

function formatSize(bytes) {
  return `${(bytes / 1024).toFixed(1)} KB`;
}

function formatDate(date) {
  return date.toISOString().slice(0, 10);
}

function relativePath(file) {
  return path.relative(root, file).split(path.sep).join("/");
}

function writeLine(line = "") {
  reportLines.push(line);
  if (!reportMode) {
    console.log(line);
  }
}

function printSourceReport({ title, locations, totals, byExtension }) {
  writeLine(`${title} (${locations.join(", ")})`);
  writeLine(`  Total: ${formatNumber(totals.files)} files, ${formatNumber(totals.lines)} lines`);

  for (const [extension, statistics] of [...byExtension.entries()].sort(([left], [right]) => left.localeCompare(right))) {
    writeLine(`  ${extension.padEnd(12)} ${formatNumber(statistics.files).padStart(4)} files  ${formatNumber(statistics.lines).padStart(8)} lines`);
  }
}

function printAssetsReport({ total, counts }) {
  writeLine("Assets (repository)");
  writeLine(`  Total: ${formatNumber(total)} files`);
  for (const category of assetCategories) {
    writeLine(`  ${category.title}`);
    for (const extension of category.extensions) {
      writeLine(`    ${extension.padEnd(10)} ${formatNumber(counts.get(extension) ?? 0)} files`);
    }
  }
}

function printTestsReport(tests) {
  writeLine("Tests (declared cases; parameterized cases are counted once)");
  for (const { title, files, cases } of tests) {
    writeLine(`  ${title.padEnd(15)} ${formatNumber(files)} files  ${formatNumber(cases)} cases`);
  }
}

function printScriptsReport({ total, counts }) {
  writeLine("Scripts (repository)");
  writeLine(`  Total: ${formatNumber(total)} files`);
  for (const extension of scriptExtensions) {
    const count = counts.get(extension);
    if (count > 0) {
      writeLine(`  ${extension.padEnd(10)} ${formatNumber(count)} files`);
    }
  }
}

function printNpmCommandsReport({ total, packages }) {
  writeLine("NPM commands");
  writeLine(`  Total: ${formatNumber(total)} commands across ${formatNumber(packages.length)} package files`);
  for (const { file, commandCount } of packages) {
    writeLine(`  ${relativePath(file).padEnd(30)} ${formatNumber(commandCount)} commands`);
  }
}

function printFileDetailsReport({ oldest, newest, largest }) {
  writeLine("File dates (last modified)");
  writeLine(`  Oldest: ${formatDate(oldest.modified)}  ${relativePath(oldest.file)}`);
  writeLine(`  Newest: ${formatDate(newest.modified)}  ${relativePath(newest.file)}`);
  writeLine("Top 10 largest maintained files");
  for (const { file, size } of largest) {
    writeLine(`  ${formatSize(size).padStart(10)}  ${relativePath(file)}`);
  }
}

function printLargestCodeFiles(largestCodeFiles) {
  writeLine("Top 10 largest source code files");
  for (const { file, size } of largestCodeFiles) {
    writeLine(`  ${formatSize(size).padStart(10)}  ${relativePath(file)}`);
  }
}

function escapeHtml(value) {
  return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

function renderSourceReport({ title, locations, totals, byExtension }) {
  const rows = [...byExtension.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([extension, statistics]) => `<tr><td><code>${escapeHtml(extension)}</code></td><td>${formatNumber(statistics.files)}</td><td>${formatNumber(statistics.lines)}</td></tr>`)
    .join("");

  return `<section class="source-section">
  <div class="section-heading">
    <div><h2>${escapeHtml(title)}</h2><p>${escapeHtml(locations.join(" · "))}</p></div>
    <dl><div><dt>Files</dt><dd>${formatNumber(totals.files)}</dd></div><div><dt>Lines</dt><dd>${formatNumber(totals.lines)}</dd></div></dl>
  </div>
  <table><thead><tr><th>Type</th><th>Files</th><th>Lines</th></tr></thead><tbody>${rows}</tbody></table>
</section>`;
}

function formatPercentage(value, total) {
  return `${Math.round(value / total * 100)}%`;
}

function createDonut(segments, total, unit) {
  const colors = ["var(--chart-1)", "var(--chart-2)", "var(--chart-3)"];
  let position = 0;
  const gradient = segments.map(({ value }, index) => {
    const end = position + value / total * 100;
    const value_ = `${colors[index]} ${position.toFixed(2)}% ${end.toFixed(2)}%`;
    position = end;
    return value_;
  }).join(", ");
  const legend = segments.map(({ title, value }, index) => `<li><span class="legend-swatch legend-${index + 1}"></span><span>${escapeHtml(title)}</span><strong>${formatPercentage(value, total)}</strong></li>`).join("");

  return `<div class="donut-layout">
  <div class="donut" style="--chart: conic-gradient(${gradient})"><div><strong>${formatNumber(total)}</strong><span>${escapeHtml(unit)}</span></div></div>
  <ul class="legend">${legend}</ul>
</div>`;
}

function renderVisualOverview(codeReports, assets) {
  const codeSegments = codeReports.map(({ title, totals }) => ({ title, value: totals.lines }));
  const assetSegments = assetCategories.map(({ title, extensions }) => ({
    title,
    value: extensions.reduce((total, extension) => total + (assets.counts.get(extension) ?? 0), 0)
  }));
  const largestSource = Math.max(...codeSegments.map(({ value }) => value));
  const bars = codeSegments.map(({ title, value }) => `<li>
  <div><span>${escapeHtml(title)}</span><strong>${formatNumber(value)} lines</strong></div>
  <span class="bar-track"><span class="bar-fill" style="--value: ${(value / largestSource * 100).toFixed(2)}%"></span></span>
</li>`).join("");
  const primaryCode = [...codeSegments].sort((left, right) => right.value - left.value)[0];
  const primaryAssets = [...assetSegments].sort((left, right) => right.value - left.value)[0];

  return `<section class="visual-grid" aria-label="Repository composition">
  <div class="visual-panel"><h2>Source lines by area</h2>${createDonut(codeSegments, codeSegments.reduce((total, { value }) => total + value, 0), "lines")}</div>
  <div class="visual-panel"><h2>Assets by group</h2>${createDonut(assetSegments, assetSegments.reduce((total, { value }) => total + value, 0), "files")}</div>
  <div class="visual-panel source-bars"><h2>Source line comparison</h2><ul>${bars}</ul></div>
  <p class="insight">${escapeHtml(primaryCode.title)} contains ${formatPercentage(primaryCode.value, codeSegments.reduce((total, { value }) => total + value, 0))} of the maintained source lines. ${escapeHtml(primaryAssets.title)} make up ${formatPercentage(primaryAssets.value, assetSegments.reduce((total, { value }) => total + value, 0))} of counted assets.</p>
</section>`;
}

function createHtmlReport({ codeReports, grandTotal, assets, tests, scripts, npmCommands, fileDetails, largestCodeFiles }) {
  const totalTestCases = tests.reduce((total, { cases }) => total + cases, 0);
  const scriptRows = scriptExtensions
    .filter((extension) => scripts.counts.get(extension) > 0)
    .map((extension) => `<tr><td><code>${escapeHtml(extension)}</code></td><td>${formatNumber(scripts.counts.get(extension))}</td></tr>`)
    .join("");
  const assetRows = assetCategories.flatMap(({ title, extensions }) => extensions.map((extension) => `<tr><td>${escapeHtml(title)}</td><td><code>${escapeHtml(extension)}</code></td><td>${formatNumber(assets.counts.get(extension) ?? 0)}</td></tr>`)).join("");
  const testRows = tests.map(({ title, files, cases }) => `<tr><td>${escapeHtml(title)}</td><td>${formatNumber(files)}</td><td>${formatNumber(cases)}</td></tr>`).join("");
  const npmRows = npmCommands.packages.map(({ file, commandCount }) => `<tr><td><code>${escapeHtml(relativePath(file))}</code></td><td>${formatNumber(commandCount)}</td></tr>`).join("");
  const largestSize = fileDetails.largest[0]?.size ?? 1;
  const largestRows = fileDetails.largest.map(({ file, size }) => `<li><div><code>${escapeHtml(relativePath(file))}</code><strong>${formatSize(size)}</strong></div><span style="--size: ${Math.max(2, size / largestSize * 100).toFixed(2)}%"></span></li>`).join("");
  const largestCodeSize = largestCodeFiles[0]?.size ?? 1;
  const largestCodeRows = largestCodeFiles.map(({ file, size }) => `<li><div><code>${escapeHtml(relativePath(file))}</code><strong>${formatSize(size)}</strong></div><span style="--size: ${Math.max(2, size / largestCodeSize * 100).toFixed(2)}%"></span></li>`).join("");
  const generatedAt = fileDetails.newest.modified.toLocaleString("en-US", { dateStyle: "medium", timeStyle: "short" });

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Voltura Air code statistics</title>
  <style>
    :root { color-scheme: dark; --page: #10151d; --surface: #18212d; --surface-alt: #202c3a; --text: #f3f6fa; --muted: #9eadbd; --line: #334256; --accent: #65d7ba; --accent-soft: #243e42; --chart-1: #65d7ba; --chart-2: #84b4f7; --chart-3: #e8b96c; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
    * { box-sizing: border-box; }
    body { margin: 0; background: radial-gradient(circle at top right, #1e3a47 0, transparent 32rem), var(--page); color: var(--text); }
    main { width: min(1100px, calc(100% - 32px)); margin: 0 auto; padding: 56px 0 72px; }
    header { display: flex; align-items: end; justify-content: space-between; gap: 24px; margin-bottom: 28px; }
    .back { display: inline-block; margin-bottom: 12px; color: var(--accent); font-weight: 700; text-decoration: none; }
    .back:hover { text-decoration: underline; }
    h1, h2, p { margin: 0; }
    h1 { font-size: clamp(2rem, 5vw, 3.6rem); letter-spacing: -0.055em; }
    h2 { font-size: 1.25rem; letter-spacing: -0.02em; }
    p, dt, .meta { color: var(--muted); }
    header p { margin-top: 10px; }
    .meta { text-align: right; font-size: 0.9rem; }
    .summary { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 42px; }
    .metric { min-height: 132px; padding: 20px; border: 1px solid var(--line); border-radius: 16px; background: linear-gradient(145deg, var(--surface), var(--surface-alt)); }
    .metric dt { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.08em; }
    .metric dd { margin: 14px 0 0; font-size: clamp(1.7rem, 4vw, 2.5rem); font-weight: 700; letter-spacing: -0.04em; }
    .metric dd span { display: block; margin-top: 4px; color: var(--muted); font-size: 0.82rem; font-weight: 400; letter-spacing: 0; }
    .visual-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 28px; margin-bottom: 42px; }
    .visual-panel { min-width: 0; }
    .visual-panel h2 { margin-bottom: 16px; }
    .donut-layout { display: flex; align-items: center; gap: 24px; }
    .donut { display: grid; flex: 0 0 148px; width: 148px; aspect-ratio: 1; place-items: center; border-radius: 50%; background: var(--chart); }
    .donut > div { display: grid; width: 98px; aspect-ratio: 1; place-items: center; align-content: center; border-radius: 50%; background: var(--page); text-align: center; }
    .donut strong { font-size: 1.35rem; letter-spacing: -0.04em; }
    .donut span { color: var(--muted); font-size: 0.75rem; }
    .legend, .source-bars ul { display: grid; flex: 1; gap: 8px; padding: 0; margin: 0; list-style: none; }
    .legend li { display: grid; grid-template-columns: 10px 1fr auto; align-items: center; gap: 8px; color: var(--muted); font-size: 0.88rem; }
    .legend strong { color: var(--text); font-variant-numeric: tabular-nums; }
    .legend-swatch { width: 10px; height: 10px; border-radius: 50%; background: var(--chart-1); }
    .legend-2 { background: var(--chart-2); }
    .legend-3 { background: var(--chart-3); }
    .source-bars { grid-column: span 2; }
    .source-bars li { display: grid; gap: 7px; }
    .source-bars li > div { display: flex; justify-content: space-between; gap: 16px; font-size: 0.9rem; }
    .source-bars strong { font-variant-numeric: tabular-nums; }
    .bar-track { height: 9px; overflow: hidden; border-radius: 999px; background: var(--surface-alt); }
    .bar-fill { display: block; width: var(--value); height: 100%; border-radius: inherit; background: linear-gradient(90deg, var(--chart-1), var(--chart-2)); }
    .insight { grid-column: span 2; padding-top: 4px; color: var(--muted); }
    .source-section, .details-grid, .largest { margin-top: 30px; }
    .section-heading { display: flex; align-items: end; justify-content: space-between; gap: 20px; margin-bottom: 14px; }
    .section-heading p { margin-top: 6px; font-size: 0.9rem; }
    .section-heading dl { display: flex; gap: 20px; margin: 0; }
    .section-heading dt { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.08em; }
    .section-heading dd { margin: 4px 0 0; font-weight: 700; text-align: right; }
    table { width: 100%; border-collapse: collapse; background: color-mix(in srgb, var(--surface) 88%, transparent); }
    th, td { padding: 11px 14px; border-bottom: 1px solid var(--line); text-align: right; font-variant-numeric: tabular-nums; }
    th:first-child, td:first-child { text-align: left; }
    th { color: var(--muted); font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.08em; }
    tbody tr:last-child td { border-bottom: 0; }
    code { color: #b9e8dd; font-family: "Cascadia Code", Consolas, monospace; font-size: 0.9em; overflow-wrap: anywhere; }
    .details-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 28px; }
    .details-grid h2, .largest h2 { margin-bottom: 14px; }
    .date-list { display: grid; gap: 10px; }
    .date-list div { display: flex; justify-content: space-between; gap: 16px; padding-bottom: 10px; border-bottom: 1px solid var(--line); }
    .date-list strong { font-size: 0.9rem; }
    .largest ol { display: grid; gap: 11px; padding: 0; margin: 0; list-style: none; }
    .largest li > div { display: flex; justify-content: space-between; gap: 20px; align-items: baseline; margin-bottom: 6px; }
    .largest strong { white-space: nowrap; font-variant-numeric: tabular-nums; }
    .largest li > span { display: block; height: 8px; width: var(--size); border-radius: 999px; background: linear-gradient(90deg, var(--accent), #84b4f7); }
    footer { margin-top: 42px; color: var(--muted); font-size: 0.85rem; }
    @media (max-width: 700px) { main { width: min(100% - 24px, 1100px); padding-top: 32px; } header, .section-heading { display: block; } .meta { margin-top: 12px; text-align: left; } .summary, .details-grid { grid-template-columns: 1fr 1fr; } .section-heading dl { margin-top: 16px; } th, td { padding: 10px; } .visual-grid { grid-template-columns: 1fr; } .source-bars, .insight { grid-column: auto; } }
    @media (max-width: 430px) { .summary, .details-grid { grid-template-columns: 1fr; } .section-heading dl { justify-content: space-between; } .largest li > div { gap: 10px; } .donut-layout { gap: 14px; } .donut { flex-basis: 128px; width: 128px; } .donut > div { width: 84px; } }
  </style>
</head>
<body>
  <main>
    <header><div><a class="back" href="./">← Voltura Air home</a><h1>Code statistics</h1><p>Voltura Air repository overview</p></div><div class="meta">Source snapshot ${escapeHtml(generatedAt)}<br>Physical lines include blank lines</div></header>
    <section class="summary" aria-label="Repository summary">
      <dl class="metric"><dt>Source files</dt><dd>${formatNumber(grandTotal.files)}<span>${formatNumber(grandTotal.lines)} total lines</span></dd></dl>
      <dl class="metric"><dt>Test cases</dt><dd>${formatNumber(totalTestCases)}<span>${formatNumber(tests.reduce((total, { files }) => total + files, 0))} test files</span></dd></dl>
      <dl class="metric"><dt>Assets</dt><dd>${formatNumber(assets.total)}<span>Documents, images, cursors</span></dd></dl>
      <dl class="metric"><dt>NPM commands</dt><dd>${formatNumber(npmCommands.total)}<span>${formatNumber(scripts.total)} script files</span></dd></dl>
    </section>
    ${renderVisualOverview(codeReports, assets)}
    ${codeReports.map(renderSourceReport).join("\n")}
    <section class="details-grid" aria-label="Repository inventory">
      <div><h2>Assets</h2><table><thead><tr><th>Group</th><th>Type</th><th>Files</th></tr></thead><tbody>${assetRows}</tbody></table></div>
      <div><h2>Scripts</h2><table><thead><tr><th>Type</th><th>Files</th></tr></thead><tbody>${scriptRows}</tbody></table></div>
      <div><h2>Tests</h2><table><thead><tr><th>Area</th><th>Files</th><th>Cases</th></tr></thead><tbody>${testRows}</tbody></table></div>
      <div><h2>NPM commands</h2><table><thead><tr><th>Package</th><th>Commands</th></tr></thead><tbody>${npmRows}</tbody></table></div>
    </section>
    <section class="details-grid" aria-label="File dates and largest files">
      <div><h2>File dates</h2><div class="date-list"><div><span>Oldest modified</span><strong>${formatDate(fileDetails.oldest.modified)}</strong></div><code>${escapeHtml(relativePath(fileDetails.oldest.file))}</code><div><span>Newest modified</span><strong>${formatDate(fileDetails.newest.modified)}</strong></div><code>${escapeHtml(relativePath(fileDetails.newest.file))}</code></div></div>
      <div><h2>Largest file</h2><div class="date-list"><div><span>Size</span><strong>${formatSize(fileDetails.largest[0].size)}</strong></div><code>${escapeHtml(relativePath(fileDetails.largest[0].file))}</code></div></div>
    </section>
    <section class="largest"><h2>Top 10 largest source code files</h2><ol>${largestCodeRows}</ol></section>
    <section class="largest"><h2>Top 10 largest maintained files</h2><ol>${largestRows}</ol></section>
    <footer>Build output, dependencies, and other generated directories are excluded.</footer>
  </main>
</body>
</html>`;
}

function openInDefaultBrowser(reportPath) {
  const url = pathToFileURL(reportPath).href;
  const command = process.platform === "win32" ? "cmd.exe" : process.platform === "darwin" ? "open" : "xdg-open";
  const argumentsForCommand = process.platform === "win32" ? ["/c", "start", "", url] : [url];
  const process_ = spawn(command, argumentsForCommand, { detached: true, stdio: "ignore", windowsHide: true });
  process_.unref();
}

const codeReports = await Promise.all(reports.map(createSourceReport));
const grandTotal = createGrandTotal(codeReports);
const largestCodeFiles = await createCodeFileDetailsReport(codeReports);
const assets = await createAssetsReport();
const tests = await createTestsReport();
const repositoryFiles = await collectFiles(root);
const scripts = createScriptsReport(repositoryFiles);
const npmCommands = await createNpmCommandsReport(repositoryFiles);
const fileDetails = await createFileDetailsReport(repositoryFiles);

if (!quietMode) {
  writeLine("Voltura Air code statistics (physical lines, including blank lines)");
  for (const report of codeReports) {
    printSourceReport(report);
    writeLine();
  }
  writeLine("Grand total source code");
  writeLine(`  Total: ${formatNumber(grandTotal.files)} files, ${formatNumber(grandTotal.lines)} lines`);
  writeLine();
  printAssetsReport(assets);
  writeLine();
  printTestsReport(tests);
  writeLine();
  printScriptsReport(scripts);
  writeLine();
  printNpmCommandsReport(npmCommands);
  writeLine();
  printLargestCodeFiles(largestCodeFiles);
  writeLine();
  printFileDetailsReport(fileDetails);
}

if (reportMode) {
  await writeFile(reportPath, createHtmlReport({ codeReports, grandTotal, assets, tests, scripts, npmCommands, fileDetails, largestCodeFiles }), "utf8");
  if (openReport) {
    openInDefaultBrowser(reportPath);
  }
  if (!quietMode) {
    console.log(`${openReport ? "Opened" : "Generated"} HTML report: ${reportPath}`);
  }
}
