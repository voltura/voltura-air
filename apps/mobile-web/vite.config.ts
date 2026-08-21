import { randomUUID } from "node:crypto";
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { brotliCompressSync, constants, gzipSync } from "node:zlib";
import { defineConfig, type HtmlTagDescriptor, type Plugin } from "vite";
import react from "@vitejs/plugin-react";

const packageJson = JSON.parse(readFileSync(new URL("./package.json", import.meta.url), "utf8")) as { version: string };
const appleStartupDevices = JSON.parse(
  readFileSync(new URL("../../assets/branding/apple-startup-devices.json", import.meta.url), "utf8")
) as AppleStartupDevice[];
const configuredWebBuildId = process.env.VOLTURA_AIR_WEB_BUILD_ID?.trim();
const webBuildId = configuredWebBuildId && configuredWebBuildId.length > 0 ? configuredWebBuildId : randomUUID();
const isHosted = process.env.VOLTURA_AIR_HOSTED === "1";
const hostedChannel = process.env.VOLTURA_AIR_HOSTED_CHANNEL === "development" ? "development" : "stable";
const hostedDirectory = hostedChannel === "development" ? "dev-app" : "app";
const appBase = isHosted ? `/air/${hostedDirectory}/` : "/";
const buildOutputDirectory = fileURLToPath(new URL(
  isHosted ? `../../apps/public-site/${hostedDirectory}` : "./dist",
  import.meta.url
));
const relayService = JSON.parse(
  readFileSync(new URL("../windows-host/relay-service.json", import.meta.url), "utf8").replace(/^\uFEFF/u, "")
) as { serviceId: string; httpsBase: string };

interface AppleStartupDevice {
  name: string;
  width: number;
  height: number;
  dpr: number;
}

ignoreDevSocketResets();

export default defineConfig({
  base: appBase,
  build: {
    chunkSizeWarningLimit: 750,
    outDir: buildOutputDirectory,
    emptyOutDir: true
  },
  define: {
    __APP_VERSION__: JSON.stringify(packageJson.version),
    __WEB_BUILD_ID__: JSON.stringify(webBuildId),
    __RELAY_SERVICE_ID__: JSON.stringify(relayService.serviceId),
    __RELAY_HTTPS_BASE__: JSON.stringify(relayService.httpsBase)
  },
  plugins: [react(), appleStartupImages(appBase), webBuildIdFile(webBuildId, buildOutputDirectory), hostedManifest(appBase, buildOutputDirectory), compressedJavaScriptAssets(buildOutputDirectory)]
});

function appleStartupImages(base: string): Plugin {
  return {
    name: "apple-startup-images",
    transformIndexHtml(): HtmlTagDescriptor[] {
      return appleStartupDevices.flatMap((device) =>
        (["dark", "light"] as const).flatMap((theme) =>
          (["portrait", "landscape"] as const).map((orientation) => ({
            tag: "link",
            injectTo: "head",
            attrs: {
              rel: "apple-touch-startup-image",
              href: `${base}startup-images/${startupFileName(device, theme, orientation)}`,
              media: [
                "screen",
                `(prefers-color-scheme: ${theme})`,
                `(device-width: ${device.width}px)`,
                `(device-height: ${device.height}px)`,
                `(-webkit-device-pixel-ratio: ${device.dpr})`,
                `(orientation: ${orientation})`
              ].join(" and ")
            }
          }))
        )
      );
    }
  };
}

function hostedManifest(base: string, outputDirectory: string): Plugin {
  return {
    name: "hosted-manifest",
    apply: "build",
    closeBundle() {
      if (base === "/") {return;}
      const output = join(outputDirectory, "manifest.webmanifest");
      const manifest = JSON.parse(readFileSync(output, "utf8")) as Record<string, unknown> & { icons?: { src?: string }[] };
      manifest.id = base;
      manifest.start_url = base;
      manifest.scope = base;
      for (const icon of manifest.icons ?? []) {if (icon.src?.startsWith("/")) {icon.src = `${base}${icon.src.slice(1)}`;}}
      writeFileSync(output, `${JSON.stringify(manifest, null, 2)}\n`);
    }
  };
}

function startupFileName(
  device: AppleStartupDevice,
  theme: "dark" | "light",
  orientation: "portrait" | "landscape"
): string {
  return `${device.name}-${device.width}x${device.height}-${device.dpr}x-${theme}-${orientation}.png`;
}

function ignoreDevSocketResets(): void {
  if (process.env.NODE_ENV === "production") {
    return;
  }

  process.on("uncaughtException", (error: unknown) => {
    const socketError = error as { code?: string; syscall?: string; message?: string };
    if (socketError.code === "ECONNRESET" && socketError.syscall === "read") {
      console.warn("Ignored mobile dev-server socket reset.");
      return;
    }

    throw error;
  });
}

function webBuildIdFile(buildId: string, outputDirectory: string): Plugin {
  const writeBuildId = () => {
    mkdirSync(outputDirectory, { recursive: true });
    writeFileSync(join(outputDirectory, "web-build-id.txt"), `${buildId}\n`);
  };

  return {
    name: "web-build-id-file",
    configureServer() {
      writeBuildId();
    },
    closeBundle() {
      writeBuildId();
    }
  };
}

function compressedJavaScriptAssets(outputDirectory: string): Plugin {
  return {
    name: "compressed-javascript-assets",
    apply: "build",
    closeBundle() {
      if (!existsSync(outputDirectory)) {
        return;
      }

      for (const file of findJavaScriptFiles(outputDirectory)) {
        const source = readFileSync(file);
        const brotli = brotliCompressSync(source, {
          params: {
            [constants.BROTLI_PARAM_QUALITY]: 11
          }
        });

        writeFileSync(`${file}.br`, brotli);
        writeFileSync(`${file}.gz`, gzipSync(source));
      }
    }
  };
}

function findJavaScriptFiles(directory: string): string[] {
  const files: string[] = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...findJavaScriptFiles(fullPath));
      continue;
    }

    if (entry.isFile() && fullPath.endsWith(".js")) {
      files.push(fullPath);
    }
  }

  return files;
}
