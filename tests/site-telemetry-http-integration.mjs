import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import net from "node:net";
import path from "node:path";
import process from "node:process";
import { randomUUID } from "node:crypto";
import {
  stopFixtureHolder,
  terminateChild,
  trackChild,
  waitForChildClose,
} from "./site-telemetry-process-lifecycle.mjs";

const root = path.resolve(import.meta.dirname, "..");
const phpArgs = ["-c", path.join(root, ".site-dev", "php.ini")];
const fixturePath = path.join(root, "tests", "site-telemetry-http-fixture.php");
const routerPath = path.join(root, "tests", "site-telemetry-http-router.php");
const siteRoot = path.join(root, "apps", "public-site");
const source = `telemetry-http-${randomUUID()}`;
const installationId = randomUUID();
const batchId = randomUUID();
const configPath = path.join(root, ".site-dev", "config.php");
const testEnvironment = {
  ...process.env,
  VOLTURA_AIR_TELEMETRY_TEST_TABLES: "1",
};
await verifyFixtureCleanupRecovery(testEnvironment);
const fixtureHolder = startFixtureHolder(testEnvironment);
let primaryError = null;

try {
  await fixtureHolder.ready;
  await verifyCleanupWriterSerialization(testEnvironment);

  await withPhpServer(configPath, async (baseUrl) => {
    const health = await request(baseUrl, "/telemetry/v1/health.php");
    await assertResponse(health, 204, "");

    const method = await request(baseUrl, "/telemetry/v1/ingest.php", { method: "GET" });
    await assertResponse(method, 405, '{"schemaVersion":1,"status":"method-not-allowed"}');
    assert.equal(method.headers.get("allow"), "POST");
    assert.equal(method.headers.has("access-control-allow-origin"), false);

    await assertResponse(
      await request(baseUrl, "/telemetry/v1/ingest.php", { method: "POST", body: "{}", headers: { "content-type": "text/plain" } }),
      415,
      '{"schemaVersion":1,"status":"unsupported-media-type"}',
    );
    await assertResponse(
      await request(baseUrl, "/telemetry/v1/ingest.php", { method: "POST", body: "x".repeat(4097), headers: jsonHeaders() }),
      413,
      '{"schemaVersion":1,"status":"body-too-large"}',
    );
    await assertResponse(
      await request(baseUrl, "/telemetry/v1/ingest.php", { method: "POST", body: "{", headers: jsonHeaders() }),
      400,
      '{"schemaVersion":1,"status":"invalid"}',
    );

    const body = JSON.stringify(validBatch(installationId, batchId));
    await assertResponse(
      await request(baseUrl, "/telemetry/v1/ingest.php", { method: "POST", body, headers: jsonHeaders() }),
      202,
      '{"schemaVersion":1,"status":"accepted"}',
    );
    for (let requestIndex = 1; requestIndex < 24; requestIndex += 1) {
      await assertResponse(
        await request(baseUrl, "/telemetry/v1/ingest.php", { method: "POST", body, headers: jsonHeaders() }),
        202,
        '{"schemaVersion":1,"status":"accepted"}',
      );
    }
    const limited = await request(baseUrl, "/telemetry/v1/ingest.php", { method: "POST", body, headers: jsonHeaders() });
    await assertResponse(limited, 429, '{"schemaVersion":1,"status":"rate-limited"}');
    assert.equal(limited.headers.get("retry-after"), "900");
  });

  await withPhpServer(
    configPath,
    async (baseUrl) => {
      await assertResponse(
        await request(baseUrl, "/telemetry/v1/ingest.php", {
          method: "POST",
          body: "{",
          headers: jsonHeaders(),
        }),
        503,
        '{"schemaVersion":1,"status":"unavailable"}',
      );
    },
    "record_invalid_before_commit,record_invalid_rollback",
  );

  await withPhpServer(
    configPath,
    async (baseUrl) => {
      const failureBody = JSON.stringify(validBatch(randomUUID(), randomUUID()));
      await assertResponse(
        await request(baseUrl, "/telemetry/v1/ingest.php", {
          method: "POST",
          body: failureBody,
          headers: jsonHeaders(),
        }),
        503,
        '{"schemaVersion":1,"status":"unavailable"}',
      );
    },
    "after_daily_upsert,ingest_rollback,record_server_failure_before_commit,record_server_failure_rollback",
  );

  await withPhpServer(path.join(root, ".site-dev", "missing-telemetry-config.php"), async (baseUrl) => {
    await assertResponse(
      await request(baseUrl, "/telemetry/v1/ingest.php", { method: "POST", body: "{}", headers: jsonHeaders() }),
      503,
      '{"schemaVersion":1,"status":"unavailable"}',
    );
    await assertResponse(
      await request(baseUrl, "/telemetry/v1/health.php"),
      503,
      '{"schemaVersion":1,"status":"unavailable"}',
    );
  });
  runPhp([fixturePath, "verify", installationId], testEnvironment);
  runPhp([path.join(root, "tests", "site-telemetry-integration.php")], testEnvironment);
  console.log("Telemetry HTTP endpoint integration passed.");
} catch (error) {
  primaryError = error;
} finally {
  let cleanupError = null;
  try {
    await stopFixtureHolder(fixtureHolder, {
      forceCleanup: () => runPhp([fixturePath, "cleanup"], testEnvironment),
    });
  } catch (error) {
    cleanupError = error;
  }
  if (primaryError && cleanupError) {
    throw new AggregateError([primaryError, cleanupError], "Telemetry HTTP validation and test-table cleanup both failed.");
  }
  if (cleanupError) throw cleanupError;
}
if (primaryError) throw primaryError;

function runPhp(args, environment = process.env) {
  const result = spawnSync("php", [...phpArgs, ...args], {
    cwd: root,
    encoding: "utf8",
    env: environment,
    timeout: 15000,
    killSignal: "SIGKILL",
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error((result.stderr || result.stdout || `PHP exited with ${result.status}`).trim());
  }
  return result.stdout;
}

async function verifyFixtureCleanupRecovery(environment) {
  const holder = startFixtureHolder({
    ...environment,
    VOLTURA_AIR_TELEMETRY_TEST_HANG_ON_STOP: "1",
  });
  let primaryError = null;
  let stopAttempted = false;
  try {
    await holder.ready;
    stopAttempted = true;
    await assert.rejects(
      stopFixtureHolder(holder, {
        cleanupTimeoutMs: 100,
        terminationTimeoutMs: 100,
        forceCleanup: () => runPhp([fixturePath, "cleanup"], environment),
      }),
      /cleanup did not exit/u,
    );
  } catch (error) {
    primaryError = error;
  }

  let cleanupError = null;
  if (!stopAttempted) {
    try {
      await stopFixtureHolder(holder, {
        cleanupTimeoutMs: 100,
        terminationTimeoutMs: 100,
        forceCleanup: () => runPhp([fixturePath, "cleanup"], environment),
      });
    } catch (error) {
      cleanupError = error;
    }
  }
  if (primaryError && cleanupError) {
    throw new AggregateError(
      [primaryError, cleanupError],
      "Telemetry fixture recovery proof and its cleanup both failed.",
    );
  }
  if (cleanupError) throw cleanupError;
  if (primaryError) throw primaryError;
}

async function verifyCleanupWriterSerialization(environment) {
  const cleanup = startFixtureProcess([fixturePath, "cleanup-race"], {
    ...environment,
    VOLTURA_AIR_TELEMETRY_PAUSE: "admin_cleanup_after_scope_check",
  });
  let writer = null;
  let primaryError = null;
  try {
    await waitForProcessOutput(
      cleanup,
      "TELEMETRY_TEST_PAUSED:admin_cleanup_after_scope_check",
      5000,
    );
    const writerInstallationId = randomUUID();
    writer = startFixtureProcess(
      [fixturePath, "ingest", writerInstallationId, randomUUID()],
      environment,
    );
    await new Promise((resolve) => setTimeout(resolve, 200));
    assert.equal(writer.state.closed, false, "Telemetry writer bypassed the administrator cleanup lock.");

    cleanup.child.stdin.end("continue\n");
    await requireProcessSuccess(cleanup, "TELEMETRY_CLEANUP_RACE_DONE");
    await requireProcessSuccess(writer, "TELEMETRY_WRITER_ACCEPTED");
    runPhp([fixturePath, "verify", writerInstallationId], environment);
  } catch (error) {
    primaryError = error;
  }

  const cleanupErrors = [];
  for (const ownedProcess of [cleanup, writer].filter(Boolean)) {
    if (!ownedProcess.state.closed) {
      try {
        await terminateChild(ownedProcess.child, { label: "Telemetry cleanup-race process" });
      } catch (error) {
        cleanupErrors.push(error);
      }
    }
  }
  if (primaryError && cleanupErrors.length > 0) {
    throw new AggregateError(
      [primaryError, ...cleanupErrors],
      "Telemetry cleanup serialization proof and process cleanup both failed.",
    );
  }
  if (cleanupErrors.length > 0) {
    throw new AggregateError(cleanupErrors, "Telemetry cleanup serialization process cleanup failed.");
  }
  if (primaryError) throw primaryError;
}

function startFixtureProcess(args, environment) {
  const child = spawn("php", [...phpArgs, ...args], {
    cwd: root,
    encoding: "utf8",
    env: environment,
    stdio: ["pipe", "pipe", "pipe"],
    windowsHide: true,
  });
  const state = trackChild(child);
  const output = [];
  const errors = [];
  child.stdout.on("data", (chunk) => output.push(String(chunk)));
  child.stderr.on("data", (chunk) => errors.push(String(chunk)));
  child.on("error", (error) => errors.push(String(error)));
  return { child, state, output, errors };
}

async function waitForProcessOutput(ownedProcess, marker, timeoutMs) {
  if (ownedProcess.output.join("").includes(marker)) return;
  await new Promise((resolve, reject) => {
    const onData = () => {
      if (ownedProcess.output.join("").includes(marker)) finish(resolve);
    };
    const onClose = (code) => finish(
      reject,
      new Error(`Telemetry fixture exited with ${code} before '${marker}'. ${ownedProcess.errors.join("").slice(-2000)}`),
    );
    const timer = setTimeout(
      () => finish(reject, new Error(`Telemetry fixture did not reach '${marker}'.`)),
      timeoutMs,
    );
    const finish = (complete, value) => {
      clearTimeout(timer);
      ownedProcess.child.stdout.off("data", onData);
      ownedProcess.child.off("close", onClose);
      complete(value);
    };
    ownedProcess.child.stdout.on("data", onData);
    ownedProcess.child.once("close", onClose);
  });
}

async function requireProcessSuccess(ownedProcess, marker) {
  const exitCode = await waitForChildClose(ownedProcess.child, 5000, "Telemetry fixture process");
  const output = ownedProcess.output.join("");
  if (exitCode !== 0 || !output.includes(marker)) {
    throw new Error(
      `Telemetry fixture process failed with ${exitCode}. ` +
      `${(ownedProcess.errors.join("") || output).slice(-2000)}`,
    );
  }
}

function startFixtureHolder(environment) {
  const child = spawn("php", [...phpArgs, fixturePath, "hold"], {
    cwd: root,
    encoding: "utf8",
    env: environment,
    stdio: ["pipe", "pipe", "pipe"],
    windowsHide: true,
  });
  trackChild(child);
  const output = [];
  const errors = [];
  let settled = false;
  const ready = new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        reject(new Error(`Telemetry test-table setup timed out. ${errors.join("").slice(-2000)}`));
      }
    }, 15000);
    child.stdout.on("data", (chunk) => {
      output.push(String(chunk));
      if (!settled && output.join("").includes("TELEMETRY_TEST_TABLES_READY")) {
        settled = true;
        clearTimeout(timer);
        resolve();
      }
    });
    child.stderr.on("data", (chunk) => errors.push(String(chunk)));
    child.once("exit", (code) => {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        reject(new Error(`Telemetry test-table holder exited with ${code}. ${(errors.join("") || output.join("")).slice(-2000)}`));
      }
    });
    child.once("error", (error) => {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        reject(error);
      }
    });
  });
  return { child, errors, output, ready };
}

async function withPhpServer(configPath, action, telemetryFailures = "") {
  const port = await reservePort();
  const errors = [];
  const child = spawn("php", [...phpArgs, "-S", `127.0.0.1:${port}`, "-t", siteRoot, routerPath], {
    cwd: root,
    env: {
      ...process.env,
      VOLTURA_AIR_TELEMETRY_TEST_TABLES: "1",
      VOLTURA_AIR_SCREENS_CONFIG: configPath,
      VOLTURA_AIR_TELEMETRY_FAIL: telemetryFailures,
      VOLTURA_AIR_TELEMETRY_TEST_SOURCE: source,
    },
    stdio: ["ignore", "ignore", "pipe"],
    windowsHide: true,
  });
  trackChild(child);
  child.stderr.on("data", (chunk) => errors.push(String(chunk)));
  let primaryError = null;
  try {
    await waitForServer(port, child, errors);
    await action(`http://127.0.0.1:${port}`);
  } catch (error) {
    primaryError = error;
  }
  let cleanupError = null;
  try {
    await terminateChild(child, { label: "Telemetry PHP test server" });
  } catch (error) {
    cleanupError = error;
  }
  if (primaryError && cleanupError) {
    throw new AggregateError([primaryError, cleanupError], "Telemetry HTTP request and PHP server cleanup both failed.");
  }
  if (cleanupError) throw cleanupError;
  if (primaryError) throw primaryError;
}

async function reservePort() {
  const server = net.createServer();
  await new Promise((resolve, reject) => server.listen(0, "127.0.0.1", resolve).once("error", reject));
  const address = server.address();
  const port = typeof address === "object" && address ? address.port : 0;
  await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  return port;
}

async function waitForServer(port, child, errors) {
  for (let attempt = 0; attempt < 50; attempt += 1) {
    if (child.exitCode !== null) throw new Error(`PHP server exited early. ${errors.join("").slice(-2000)}`);
    try {
      await fetch(`http://127.0.0.1:${port}/telemetry/v1/health.php`);
      return;
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
  }
  throw new Error(`PHP server did not start. ${errors.join("").slice(-2000)}`);
}

async function request(baseUrl, pathname, options = {}) {
  return fetch(`${baseUrl}${pathname}`, { redirect: "manual", ...options });
}

async function assertResponse(response, status, expectedBody) {
  assert.equal(response.status, status);
  const body = await response.text();
  assert.equal(body, expectedBody);
  assert.ok(body.length <= 1024);
}

function jsonHeaders() {
  return { "content-type": "application/json; charset=utf-8" };
}

function validBatch(id, idempotencyId) {
  return {
    schemaVersion: 1,
    installationId: id,
    batchId: idempotencyId,
    hostVersion: "1.0.5",
    hostStarts: 1,
    connections: { standardLocal: 0, enhancedDirect: 0, relay: 0 },
    features: {
      trackpad: 1,
      keyboard: 0,
      dictation: 0,
      mediaControls: 0,
      presentation: 0,
      customScreens: 0,
      files: 0,
      screenViewing: 0,
      phoneWebcam: 0,
      gyroMouse: 0,
    },
  };
}
