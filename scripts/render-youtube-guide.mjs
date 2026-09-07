import { mkdir, readFile, writeFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const branding = path.join(root, "assets", "branding");
const output = path.join(branding, "youtube");
const guide = process.argv[2] ?? "getting-started";
if (process.argv.length > 3 || !["getting-started", "cloud-relay"].includes(guide)) {
  throw new Error("Usage: node scripts/render-youtube-guide.mjs [getting-started|cloud-relay]");
}
const narrationDirectory = guide === "getting-started" ? "narration" : `${guide}-narration`;
const work = path.join(
  root,
  "artifacts",
  guide === "getting-started" ? "youtube-guide" : `youtube-${guide}`,
);
const ffmpeg = process.env.FFMPEG_PATH || "ffmpeg";
const shots = JSON.parse(await readFile(path.join(output, `${guide}.json`), "utf8"));
const series = guide === "getting-started" ? "GETTING STARTED" : "CLOUD RELAY";
const width = 1920;
const height = 1080;

function xml(text) {
  return text.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}

function graphic(content) {
  return Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">
    <style>text { font-family: 'Segoe UI', Arial, sans-serif; fill: #f7f2e9; }</style>
    ${content}</svg>`);
}

function run(args) {
  const result = spawnSync(ffmpeg, args, {
    encoding: "utf8",
    windowsHide: true,
    maxBuffer: 8 * 1024 * 1024,
  });
  if (result.error || result.status !== 0) {
    throw new Error(result.error?.message || result.stderr || "FFmpeg failed.");
  }
  return result;
}

function duration(file) {
  const result = spawnSync(ffmpeg, ["-hide_banner", "-i", file], {
    encoding: "utf8",
    windowsHide: true,
  });
  if (result.error) throw result.error;
  const match = result.stderr.match(/Duration: (\d+):(\d+):([\d.]+)/u);
  if (!match) throw new Error(`Cannot read audio duration: ${file}`);
  return Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3]);
}

function seconds(stamp) {
  const [h, m, s] = stamp.replace(",", ".").split(":").map(Number);
  return h * 3600 + m * 60 + s;
}

function timestamp(value) {
  const ms = Math.round(value * 1000);
  return `${String(Math.floor(ms / 3600000)).padStart(2, "0")}:${String(Math.floor(ms / 60000) % 60).padStart(2, "0")}:${String(Math.floor(ms / 1000) % 60).padStart(2, "0")},${String(ms % 1000).padStart(3, "0")}`;
}

async function renderFrame(shot, index, file) {
  const phone = shot.screen?.includes("iphone");
  const layers = [
    {
      input: graphic(`
    <rect width="920" height="1080" fill="#061015" opacity=".32"/>
    <text x="171" y="119" font-size="39" font-weight="650">Voltura Air</text>
    <text x="108" y="243" font-size="24" letter-spacing="4" fill="#66e5cf">${series} · ${String(index + 1).padStart(2, "0")}</text>
    ${shot.title.map((line, n) => `<text x="103" y="${370 + n * 101}" font-size="82" font-weight="750">${xml(line)}</text>`).join("")}
    ${shot.lines.map((line, n) => `<rect x="108" y="${573 + n * 78}" width="6" height="30" rx="3" fill="#66e5cf"/><text x="132" y="${599 + n * 78}" font-size="30">${xml(line)}</text>`).join("")}
    <text x="108" y="995" font-size="22" fill="#bad0d3">${xml(shot.note)}</text>
    <text x="1640" y="995" font-size="22">voltura.se/air</text>
    <rect x="108" y="1030" width="1704" height="4" fill="#1b3a41"/>
    <rect x="108" y="1030" width="${(1704 * (index + 1)) / shots.length}" height="4" fill="#66e5cf"/>
  `),
      left: 0,
      top: 0,
    },
  ];
  layers.push({
    input: await sharp(path.join(branding, "voltura-air-master.png"))
      .resize(64, 64)
      .png()
      .toBuffer(),
    left: 100,
    top: 72,
  });
  if (shot.screen || shot.imagePath) {
    let source = sharp(
      shot.imagePath
        ? path.join(root, shot.imagePath)
        : path.join(root, "apps", "public-site", "assets", shot.screen),
    );
    if (shot.crop) source = source.extract(shot.crop);
    const screen = await source
      .resize(phone ? 357 : 950, phone ? 775 : 650, { fit: "inside" })
      .png()
      .toBuffer();
    const size = await sharp(screen).metadata();
    layers.push({
      input: screen,
      left: Math.round(1370 - size.width / 2),
      top: Math.round(540 - size.height / 2),
    });
  } else {
    layers.push({
      input: graphic(`
      <rect x="1040" y="270" width="690" height="488" rx="30" fill="#10272d" stroke="#3d6870" stroke-width="2"/>
      <text x="1102" y="369" font-size="25" letter-spacing="3" fill="#66e5cf">OFFICIAL DOWNLOAD</text>
      <text x="1102" y="450" font-size="60" font-weight="700">voltura.se/air</text>
      <text x="1102" y="547" font-size="38" font-weight="650">Full Windows installer</text>
      <text x="1102" y="605" font-size="27">Required .NET runtimes included</text>
      <text x="1102" y="694" font-size="26">Free · Open source · Windows 11 x64</text>
    `),
      left: 0,
      top: 0,
    });
  }
  await sharp(path.join(branding, "youtube-background.png"))
    .resize(width, height)
    .composite(layers)
    .removeAlpha()
    .png()
    .toFile(file);
}

await mkdir(work, { recursive: true });
let offset = 0;
const captions = [];
const chapters = [];
const clips = [];
for (const [index, shot] of shots.entries()) {
  const number = String(index + 1).padStart(2, "0");
  const audio = path.join(output, narrationDirectory, `${number}.mp3`);
  const subtitles = await readFile(path.join(output, narrationDirectory, `${number}.srt`), "utf8");
  const length = Math.ceil((duration(audio) + 0.7) * 30) / 30;
  const frame = path.join(work, `${number}.png`);
  const clip = path.join(work, `${number}.mp4`);
  await renderFrame(shot, index, frame);
  run([
    "-hide_banner",
    "-loglevel",
    "error",
    "-y",
    "-loop",
    "1",
    "-framerate",
    "30",
    "-i",
    frame,
    "-i",
    audio,
    "-t",
    String(length),
    "-vf",
    `fade=t=in:st=0:d=0.22,fade=t=out:st=${length - 0.22}:d=0.22`,
    "-af",
    "apad",
    "-c:v",
    "libx264",
    "-preset",
    "fast",
    "-crf",
    "19",
    "-pix_fmt",
    "yuv420p",
    "-c:a",
    "aac",
    "-b:a",
    "192k",
    "-ar",
    "48000",
    "-movflags",
    "+faststart",
    clip,
  ]);
  for (const block of subtitles.trim().split(/\r?\n\r?\n/u)) {
    const lines = block.split(/\r?\n/u);
    const times = lines[1]?.split(" --> ");
    if (times?.length !== 2) throw new Error(`Invalid narration captions in segment ${number}`);
    const text = lines
      .slice(2)
      .join(" ")
      .replaceAll("voltura dot S E slash air", "voltura.se/air")
      .replaceAll("Windows eleven", "Windows 11")
      .replaceAll("dot net", ".NET")
      .replaceAll("sixty-four-bit", "64-bit");
    captions.push({ start: seconds(times[0]) + offset, end: seconds(times[1]) + offset, text });
  }
  chapters.push(
    `${Math.floor(offset / 60)}:${String(Math.floor(offset % 60)).padStart(2, "0")} ${shot.title.join(" ")}`,
  );
  offset += length;
  clips.push(`file '${clip.replaceAll("\\", "/").replaceAll("'", "'\\''")}'`);
  console.log(`Rendered ${number}/${shots.length}: ${length.toFixed(1)}s`);
}
const concat = path.join(work, "clips.txt");
await writeFile(concat, clips.join("\n"));
const video = path.join(output, `voltura-air-${guide}.mp4`);
run([
  "-hide_banner",
  "-loglevel",
  "error",
  "-y",
  "-f",
  "concat",
  "-safe",
  "0",
  "-i",
  concat,
  "-c",
  "copy",
  "-movflags",
  "+faststart",
  video,
]);
run(["-hide_banner", "-loglevel", "error", "-i", video, "-f", "null", "-"]);
const srt = captions.map((cue, index) => {
  const end = Math.min(cue.end, captions[index + 1]?.start ?? offset);
  const words = cue.text.split(" ");
  const lines = [""];
  for (const word of words) {
    if (lines.at(-1).length + word.length > 46) lines.push("");
    lines[lines.length - 1] += `${lines.at(-1) ? " " : ""}${word}`;
  }
  return `${index + 1}\n${timestamp(cue.start)} --> ${timestamp(end)}\n${lines.join("\n")}\n`;
});
await writeFile(path.join(output, `voltura-air-${guide}.srt`), srt.join("\n"));
await writeFile(path.join(work, "chapters.txt"), chapters.join("\n") + "\n");
console.log(`Rendered and fully decoded ${offset.toFixed(1)}s: ${video}`);
