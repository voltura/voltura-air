import { randomUUID } from "node:crypto";
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { brotliCompressSync, constants, gzipSync } from "node:zlib";
import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";

const packageJson = JSON.parse(readFileSync(new URL("./package.json", import.meta.url), "utf8")) as { version: string };
const webBuildId = process.env.VOLTURA_AIR_WEB_BUILD_ID?.trim() || randomUUID();

ignoreDevSocketResets();

export default defineConfig({
  define: {
    __APP_VERSION__: JSON.stringify(packageJson.version),
    __WEB_BUILD_ID__: JSON.stringify(webBuildId)
  },
  plugins: [react(), webBuildIdFile(webBuildId), compressedJavaScriptAssets()]
});

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

function webBuildIdFile(buildId: string): Plugin {
  const writeBuildId = () => {
    const distDir = fileURLToPath(new URL("./dist", import.meta.url));
    mkdirSync(distDir, { recursive: true });
    writeFileSync(join(distDir, "web-build-id.txt"), `${buildId}\n`);
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

function compressedJavaScriptAssets(): Plugin {
  return {
    name: "compressed-javascript-assets",
    apply: "build",
    closeBundle() {
      const distDir = fileURLToPath(new URL("./dist", import.meta.url));
      if (!existsSync(distDir)) {
        return;
      }

      for (const file of findJavaScriptFiles(distDir)) {
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
