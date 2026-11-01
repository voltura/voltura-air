import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

test(
  "native webcam reader reuses bounded buffers and preserves frame ownership",
  {
    skip: process.platform !== "win32",
    timeout: 60_000,
  },
  () => {
    const root = fileURLToPath(new URL("../../", import.meta.url));
    const outputRoot = path.join(root, "artifacts", "test", "phone-webcam-frame-buffer");
    mkdirSync(outputRoot, { recursive: true });
    const output = mkdtempSync(path.join(outputRoot, "run-"));
    try {
      const vswhere = path.join(
        process.env["ProgramFiles(x86)"],
        "Microsoft Visual Studio",
        "Installer",
        "vswhere.exe",
      );
      const visualStudio = execFileSync(
        vswhere,
        [
          "-latest",
          "-products",
          "*",
          "-requires",
          "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
          "-property",
          "installationPath",
        ],
        { encoding: "utf8", windowsHide: true },
      ).trim();
      assert.ok(visualStudio, "The supported MSVC toolchain is required.");
      const nativeRoot = path.join(
        root,
        "apps",
        "windows-host",
        "Features",
        "PhoneWebcam",
        "Native",
        "VirtualCameraMediaSource",
      );
      for (const name of ["SimpleFrameGenerator.cpp", "SimpleFrameGenerator.h"]) {
        copyFileSync(path.join(nativeRoot, name), path.join(output, name));
      }
      copyFileSync(
        path.join(root, "tests", "native", "phone-webcam-frame-buffer.cpp"),
        path.join(output, "frame-buffer.cpp"),
      );
      copyFileSync(
        path.join(root, "tests", "native", "phone-webcam-frame-buffer-pch.h"),
        path.join(output, "pch.h"),
      );
      const build = path.join(output, "build.cmd");
      writeFileSync(
        build,
        [
          `@call "${path.join(visualStudio, "Common7", "Tools", "VsDevCmd.bat")}" -arch=x64 -host_arch=x64 >nul`,
          "@if errorlevel 1 exit /b 1",
          "@cl /nologo /std:c++20 /EHsc /O2 /W4 /WX /Fe:frame-buffer.exe /Fo:frame-buffer.obj frame-buffer.cpp /link mfplat.lib mfuuid.lib advapi32.lib ole32.lib",
          "@exit /b %errorlevel%",
          "",
        ].join("\r\n"),
      );
      execFileSync(process.env.ComSpec ?? "cmd.exe", ["/d", "/c", build], {
        cwd: output,
        stdio: "pipe",
        windowsHide: true,
        timeout: 30_000,
      });
      const result = execFileSync(path.join(output, "frame-buffer.exe"), [], {
        encoding: "utf8",
        windowsHide: true,
        timeout: 20_000,
      });
      assert.match(result, /100 frames: two reusable native buffers/u);
      process.stdout.write(result);
    } finally {
      assert.equal(path.dirname(output), outputRoot);
      assert.ok(path.basename(output).startsWith("run-"));
      rmSync(output, { recursive: true, force: true });
    }
  },
);
