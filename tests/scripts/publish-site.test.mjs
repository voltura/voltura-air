import assert from "node:assert/strict";
import test from "node:test";
import {
  clearStoredPassword,
  formatRemoteListing,
  generateStatisticsReport,
  listSite,
  loadPassword,
  publishSite,
  runSitePublication,
  storePassword,
  trimClipboardPassword
} from "../../scripts/publish-site.mjs";

test("site publication refreshes statistics before uploading", async () => {
  const operations = [];
  await runSitePublication({
    generate: () => operations.push("generate"),
    publish: async () => operations.push("publish")
  });
  assert.deepEqual(operations, ["generate", "publish"]);
});

test("statistics generation is quiet and does not open a browser during publication", () => {
  const calls = [];
  generateStatisticsReport({
    npmCliPath: "C:\\npm\\npm-cli.js",
    run: (...args) => {
      calls.push(args);
      return { status: 0 };
    }
  });

  assert.deepEqual(calls[0][1].slice(-6), [
    "run", "code:statistics", "--", "--report", "--no-open", "--quiet"
  ]);
  assert.deepEqual(calls[0][2].stdio, ["ignore", "ignore", "inherit"]);
});

test("removes only the trailing clipboard newline added by PowerShell", () => {
  assert.equal(trimClipboardPassword("long-password\r\n"), "long-password");
  assert.equal(trimClipboardPassword("long-password"), "long-password");
});

test("formats remote files and folders in a stable order", () => {
  assert.deepEqual(
    formatRemoteListing([{ type: "-", name: "index.php" }, { type: "d", name: "assets" }]),
    ["[directory] assets", "[file] index.php"]
  );
});

test("stores the password through Windows protection without exposing it in command arguments", async () => {
  const calls = [];
  const paths = { credentialPath: "C:\\Local\\site-password.dpapi" };

  await storePassword({
    password: "correct horse battery staple",
    paths,
    protect: (command, input) => calls.push({ command, input })
  });

  assert.match(calls[0].command, /ConvertFrom-SecureString/u);
  assert.match(calls[0].input, /correct horse battery staple/u);
  assert.doesNotMatch(calls[0].command, /correct horse battery staple/u);
});

test("loads the protected password without logging it", () => {
  const paths = { credentialPath: "C:\\Local\\site-password.dpapi" };
  const password = loadPassword({
    paths,
    unprotect: (command, input) => {
      assert.match(command, /ConvertTo-SecureString/u);
      assert.equal(input, paths.credentialPath);
      return "secret";
    },
    exists: () => true
  });

  assert.equal(password, "secret");
});

test("explains how to set up a missing stored password", () => {
  assert.throws(
    () => loadPassword({
      paths: { credentialPath: "C:\\Local\\site-password.dpapi" },
      exists: () => false
    }),
    /npm run publish:site:password/u
  );
});

test("clearing a missing stored password succeeds", async () => {
  await clearStoredPassword({
    paths: { credentialPath: "C:\\Local\\site-password.dpapi" },
    remove: async () => {
      const error = new Error("missing");
      error.code = "ENOENT";
      throw error;
    }
  });
});

test("records the first server identity and rejects a changed identity", async () => {
  const writes = [];
  const sftp = {
    async connect(options) {
      assert.equal(options.hostVerifier("first-server"), true);
    },
    async mkdir() {},
    async uploadDir() {},
    async end() {}
  };
  const paths = { hostFingerprintPath: "C:\\Local\\site-host.sha256" };

  await publishSite({
    paths,
    password: "secret",
    createSftp: () => sftp,
    read: async () => {
      const error = new Error("missing");
      error.code = "ENOENT";
      throw error;
    },
    write: async (...args) => writes.push(args),
    makeDirectory: async () => {},
    source: process.cwd(),
    log: () => {}
  });

  assert.deepEqual(writes[0], [paths.hostFingerprintPath, "first-server\n", { encoding: "utf8", flag: "wx" }]);

  let verifier;
  await assert.rejects(
    publishSite({
      paths,
      password: "secret",
      createSftp: () => ({
        async connect(options) {
          verifier = options.hostVerifier;
          if (!verifier("changed-server")) {
            throw new Error("Host key verification failed");
          }
        },
        async end() {}
      }),
      read: async () => "first-server\n",
      source: process.cwd(),
      log: () => {}
    }),
    /Host key verification failed/u
  );
  assert.equal(verifier("first-server"), true);
  assert.equal(verifier("changed-server"), false);
});

test("lists the remote site without creating, uploading, or removing files", async () => {
  const operations = [];
  const output = [];
  const paths = { hostFingerprintPath: "C:\\Local\\site-host.sha256" };

  const entries = await listSite({
    paths,
    password: "secret",
    createSftp: () => ({
      async connect(options) {
        assert.equal(options.hostVerifier("known-server"), true);
        operations.push("connect");
      },
      async list(remotePath) {
        operations.push(`list:${remotePath}`);
        return [{ type: "d", name: "assets" }, { type: "-", name: "index.php" }];
      },
      async mkdir() {
        assert.fail("listing must not create remote folders");
      },
      async uploadDir() {
        assert.fail("listing must not upload files");
      },
      async end() {
        operations.push("end");
      }
    }),
    read: async () => "known-server\n",
    write: async () => assert.fail("listing must not write host state when it already exists"),
    makeDirectory: async () => assert.fail("listing must not create local folders when host state exists"),
    log: (line) => output.push(line)
  });

  assert.equal(entries.length, 2);
  assert.deepEqual(operations, ["connect", "list:air", "end"]);
  assert.deepEqual(output, ["ssh.voltura.se:air", "[directory] assets", "[file] index.php"]);
});
