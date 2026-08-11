import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../../${path}`, import.meta.url), "utf8");

test("first-party short redirect validates route and preserves the secret fragment client-side", () => {
  const rewrite = read("docs/site/a/.htaccess");
  const endpoint = read("docs/site/a/index.php");
  assert.match(rewrite, /\{22\}/u);
  assert.match(rewrite, /Cache-Control" "no-cache/u);
  assert.match(endpoint, /count\(\$_GET\) !== 2/u);
  assert.match(endpoint, /\$location = '\/air\/app\/\?r='/u);
  assert.doesNotMatch(endpoint, /pairToken|token=/u);
  assert.doesNotMatch(endpoint, /Location:.*#/u);
});

test("Secure Direct short redirect selects the hosted Secure Direct bootstrap without exposing the fragment", () => {
  const rewrite = read("docs/site/s/.htaccess");
  const endpoint = read("docs/site/s/index.php");
  assert.match(rewrite, /Options -Indexes/u);
  assert.match(rewrite, /\{22\}/u);
  assert.match(rewrite, /Cache-Control" "no-cache/u);
  assert.match(endpoint, /count\(\$_GET\) !== 2/u);
  assert.match(endpoint, /\$location = '\/air\/app\/\?m=s&r='/u);
  assert.doesNotMatch(endpoint, /pairToken|token=/u);
  assert.doesNotMatch(endpoint, /Location:.*#/u);
});

test("site publication uploads the short redirect at the domain root", () => {
  const publication = read("scripts/publish-site.mjs");
  assert.match(publication, /uploadDir\(shortLinkSource, "a"\)/u);
  assert.match(read("package.json"), /"publish:site"[^\n]+site:hosted:build/u);
});

test("relay setup stores restricted Worker secrets and never requests a global key", () => {
  const setup = read("scripts/relay-setup.ps1");
  for (const secret of ["TURN_KEY_ID", "TURN_API_TOKEN", "CLOUDFLARE_ACCOUNT_ID", "CLOUDFLARE_ANALYTICS_TOKEN"]) {
    assert.match(setup, new RegExp(`Set-WranglerSecret '${secret}'`, "u"));
  }
  assert.doesNotMatch(setup, /global api key/iu);
  assert.ok(setup.indexOf("Invoke-Wrangler @('deploy')") < setup.indexOf("Set-WranglerSecret 'CLOUDFLARE_ACCOUNT_ID'"));
  assert.match(setup, /UTF8Encoding\]::new\(\$false\)/u);
  assert.match(setup, /Nothing was uploaded to voltura\.se and no Windows release was created\./u);
});

test("maintainer quality cannot enter a packaged build", () => {
  const project = read("apps/windows-host/VolturaAir.Host.csproj");
  assert.match(project, /RejectPublishedMaintainerRelayBuild/u);
  assert.match(project, /BeforeTargets="Publish"/u);
  assert.match(project, /Maintainer relay quality is local-only and cannot be published/u);
});

test("standalone composition builds from the workspace and exposes only bounded public ports", () => {
  const composition = read("services/relay/self-host/compose.yml");
  const environment = read("services/relay/self-host/.env.example");
  const edge = read("services/relay/self-host/nginx.conf.template");
  assert.match(composition, /context: \.\.\/\.\.\/\.\./u);
  assert.match(composition, /dockerfile: services\/relay\/Dockerfile/u);
  assert.match(composition, /"443:443\/tcp"/u);
  assert.match(composition, /"443:443\/udp"/u);
  assert.match(composition, /"49160-49200:49160-49200\/udp"/u);
  assert.match(composition, /--external-ip=\$\{TURN_PUBLIC_IP:/u);
  assert.match(composition, /TURN_PUBLIC_IP: "\$\{TURN_PUBLIC_IP:/u);
  assert.match(composition, /coturn:[\s\S]*depends_on:[\s\S]*relay:[\s\S]*condition: service_healthy/u);
  assert.match(environment, /^TURN_PUBLIC_IP=/mu);
  assert.match(edge, /proxy_set_header X-Forwarded-For \$remote_addr;/u);
  assert.doesNotMatch(composition, /"8787:8787"/u);
});
