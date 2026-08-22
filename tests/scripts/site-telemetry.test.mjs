import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { readFileSync } from "node:fs";
import test from "node:test";
import { stopFixtureHolder } from "../site-telemetry-process-lifecycle.mjs";

const read = (path) => readFileSync(new URL(`../../${path}`, import.meta.url), "utf8");

test("hung telemetry fixture cleanup is force-stopped and followed by closed-table recovery", async () => {
  class HangingChild extends EventEmitter {
    exitCode = null;
    signals = [];
    stdin = { end() {} };

    kill(signal = "SIGTERM") {
      this.signals.push(signal);
      if (signal === "SIGKILL") {
        queueMicrotask(() => {
          this.exitCode = 137;
          this.emit("close", 137);
        });
      }
      return true;
    }
  }

  const child = new HangingChild();
  let fallbackCleanupRan = false;
  await assert.rejects(
    stopFixtureHolder(
      { child, errors: [], output: ["TELEMETRY_TEST_TABLES_READY\n"] },
      {
        cleanupTimeoutMs: 5,
        terminationTimeoutMs: 5,
        forceCleanup: () => { fallbackCleanupRan = true; },
      },
    ),
    /cleanup did not exit/u,
  );

  assert.deepEqual(child.signals, ["SIGTERM", "SIGKILL"]);
  assert.equal(child.exitCode, 137);
  assert.equal(fallbackCleanupRan, true);
});

test("telemetry schema is additive, bounded, indexed, and isolated from the catalog", () => {
  const schema = read("apps/public-site/telemetry/schema.sql");
  for (const table of [
    "air_telemetry_daily",
    "air_telemetry_batches",
    "air_telemetry_rate_buckets",
    "air_telemetry_ingest_daily",
    "air_telemetry_maintenance",
  ]) {
    assert.match(schema, new RegExp(`CREATE TABLE IF NOT EXISTS ${table}`, "u"));
  }
  assert.match(schema, /installation_hash BINARY\(32\)/u);
  assert.match(schema, /batch_id BINARY\(16\)/u);
  assert.match(schema, /ENGINE=InnoDB/u);
  assert.match(schema, /air_telemetry_daily_version_date_installation/u);
  assert.match(schema, /air_telemetry_batches_received/u);
  assert.match(schema, /air_telemetry_rate_window/u);
  assert.doesNotMatch(schema, /air_screen_/u);
  assert.doesNotMatch(schema, /DROP\s+(?:TABLE|DATABASE)/iu);
});

test("public ingest has a closed schema, fixed HMAC domains, and no browser telemetry surface", () => {
  const library = read("apps/public-site/telemetry/v1/lib.php");
  const ingest = read("apps/public-site/telemetry/v1/ingest.php");
  const health = read("apps/public-site/telemetry/v1/health.php");
  const mobileFiles = [
    "apps/mobile-web/src/App.tsx",
    "apps/mobile-web/src/foundation/protocol/messages.ts",
    "apps/mobile-web/src/foundation/connection/useConnectionSender.ts",
  ].map(read).join("\n");

  assert.match(library, /AIR_TELEMETRY_MAX_BODY_BYTES = 4096/u);
  assert.match(library, /air_telemetry_exact_keys/u);
  assert.match(library, /telemetry-install-v1:/u);
  assert.match(library, /telemetry-install-rate-v1:/u);
  assert.match(library, /telemetry-source-rate-v1:/u);
  assert.match(library, /function air_telemetry_database_clock/u);
  assert.match(library, /function air_telemetry_best_effort_rollback/u);
  assert.match(library, /record_invalid_rollback/u);
  assert.match(library, /ingest_rollback/u);
  assert.match(library, /record_server_failure_rollback/u);
  assert.match(ingest, /Failure accounting cannot replace the endpoint's fixed 503 response/u);
  assert.doesNotMatch(library, /gmdate\(/u);
  assert.match(library, /PDO::ATTR_EMULATE_PREPARES => false/u);
  assert.match(ingest, /REQUEST_METHOD.*POST/su);
  assert.match(ingest, /application\\\/json/u);
  assert.match(library, /air_telemetry_error_response\(413\)/u);
  assert.match(health, /air_telemetry_secret\(\)/u);
  assert.doesNotMatch(ingest + library, /Access-Control-Allow-Origin/iu);
  assert.doesNotMatch(mobileFiles, /telemetry\/v1|installationId|UsageTelemetry/iu);
});

test("telemetry retention and cleanup use only fixed telemetry tables and bounded chunks", () => {
  const publicLibrary = read("apps/public-site/telemetry/v1/lib.php");
  const adminLibrary = read("apps/public-site/telemetry/admin.php");
  assert.match(publicLibrary, /AIR_TELEMETRY_CLEANUP_LIMIT = 500/u);
  assert.match(publicLibrary, /received_at < UTC_TIMESTAMP\(6\) - INTERVAL 1 DAY/u);
  assert.match(publicLibrary, /activity_date < UTC_DATE\(\) - INTERVAL 180 DAY/u);
  assert.match(publicLibrary, /function air_telemetry_lock_data_writes/u);
  assert.match(publicLibrary, /air_telemetry_table\('maintenance'\)[\s\S]+FOR UPDATE/u);
  assert.match(adminLibrary, /AIR_TELEMETRY_ADMIN_DELETE_LIMIT = 1000/u);
  assert.match(adminLibrary, /air_telemetry_lock_data_writes\(\$database\)/u);
  assert.doesNotMatch(adminLibrary, /SELECT COUNT\(\*\)[^;]+FOR UPDATE/su);
  assert.match(adminLibrary, /match \(\$request\['action'\]\)/u);
  assert.match(adminLibrary, /\['retention', 'before', 'all'\]/u);
  assert.match(adminLibrary, /admin_cleanup_after_delete/u);
  assert.match(adminLibrary, /admin_cleanup_before_commit/u);
  assert.match(adminLibrary, /admin_cleanup_commit/u);
  assert.match(adminLibrary, /admin_cleanup_rollback/u);
  assert.match(adminLibrary, /AirTelemetryCleanupOutcomeUnknown/u);
  assert.doesNotMatch(adminLibrary, /air_screen_(?:users|packages|ratings|reports)/u);
  assert.doesNotMatch(adminLibrary, /DROP\s+(?:TABLE|DATABASE)|TRUNCATE/iu);
});

test("usage statistics dashboard reuses administrator and CSRF ownership without exposing identifiers", () => {
  const page = read("apps/public-site/screens/telemetry.php");
  const admin = read("apps/public-site/telemetry/admin.php");
  const navigation = read("apps/public-site/screens/lib.php");
  assert.match(page, /air_screen_require_admin\(\)/u);
  assert.match(page, /air_screen_require_csrf\(\)/u);
  assert.match(page, /Active installations/u);
  assert.match(page, /Versions in use/u);
  assert.match(page, /<details class="telemetry-maintenance">/u);
  assert.match(page, /<summary>Data cleanup<\/summary>/u);
  assert.match(page, /id="telemetry-cleanup"/u);
  assert.equal((page.match(/action="#telemetry-cleanup"/gu) ?? []).length, 2);
  assert.doesNotMatch(page, /Telemetry requests|Delivery health|Approximate size|Custom dates|Host version.*select/u);
  assert.match(page, /class="telemetry-cleanup-confirm"/u);
  assert.match(page, /AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION/u);
  assert.match(page, /cleanup_token/u);
  assert.match(admin, /air_telemetry_admin_consume_cleanup_authorization/u);
  assert.match(page, /air_telemetry_admin_database_state\(\)/u);
  assert.match(page, /if \(\$database instanceof PDO\)[\s\S]*telemetry_cleanup_html/u);
  assert.match(page, /cleanup is unavailable until the database connection is healthy/u);
  assert.doesNotMatch(page, /cleanup_expected_/u);
  assert.match(admin, /AIR_TELEMETRY_ADMIN_VERSION_LIMIT = 20/u);
  assert.match(admin, /'host_version' => 'Other'/u);
  assert.match(navigation, /href="telemetry\.php"[^>]*>Usage statistics<\/a>/u);
  assert.doesNotMatch(page, /installation_hash|installationId|batch_id|UUID/iu);
});

test("local telemetry integration is mandatory, isolated, and applies schema independently", () => {
  const packageJson = JSON.parse(read("package.json"));
  const initializer = read("scripts/site-dev-init.ps1");
  const integration = read("tests/site-telemetry-integration.php");
  const httpIntegration = read("tests/site-telemetry-http-integration.mjs");
  const httpFixture = read("tests/site-telemetry-http-fixture.php");
  assert.match(packageJson.scripts["test:site-telemetry-integration"], /site-telemetry-http-integration\.mjs/u);
  assert.match(packageJson.scripts["test:site-telemetry-integration"], /VOLTURA_AIR_CATALOG_SECRET/u);
  assert.match(initializer, /apps\\public-site\\telemetry\\schema\.sql/u);
  assert.match(initializer, /additive development telemetry schema/u);
  assert.match(integration, /catalogCounts/u);
  assert.match(integration, /admin_cleanup_after_delete/u);
  assert.match(integration, /air_telemetry_table\('daily'\)/u);
  assert.match(integration, /randomTelemetryUuid/u);
  assert.match(integration, /admin_cleanup_commit/u);
  assert.match(integration, /admin_cleanup_rollback/u);
  assert.match(httpIntegration, /site-telemetry-integration\.php/u);
  assert.match(httpIntegration, /VOLTURA_AIR_TELEMETRY_TEST_TABLES/u);
  assert.match(httpFixture, /air_telemetry_test_daily/u);
  assert.match(httpFixture, /GET_LOCK/u);
  assert.match(httpFixture, /Refusing to manage telemetry test tables outside explicit site-dev test mode/u);
  assert.doesNotMatch(httpFixture, /snapshot|restoreHealth/u);
  assert.doesNotMatch(httpFixture, /CREATE DATABASE|DROP DATABASE/iu);
  assert.match(httpIntegration, /record_invalid_before_commit,record_invalid_rollback/u);
  assert.match(httpIntegration, /record_server_failure_before_commit,record_server_failure_rollback/u);
  for (const status of [202, 400, 405, 413, 415, 429, 503]) {
    assert.match(httpIntegration, new RegExp(`\\b${status}\\b`, "u"));
  }
  assert.doesNotMatch(integration, /TRUNCATE|DROP\s+(?:TABLE|DATABASE)/iu);
  assert.doesNotMatch(
    integration,
    /(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+air_telemetry_(?:daily|batches|rate_buckets|ingest_daily|maintenance)/iu,
  );
});
