import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../../${path}`, import.meta.url), "utf8");

test("first-party short redirect validates route and preserves the secret fragment client-side", () => {
  const rewrite = read("apps/public-site/a/.htaccess");
  const endpoint = read("apps/public-site/a/index.php");
  assert.match(rewrite, /\{22\}/u);
  assert.match(rewrite, /Cache-Control" "no-cache/u);
  assert.match(endpoint, /count\(\$_GET\) !== 2/u);
  assert.match(endpoint, /\$location = '\/air\/app\/\?r='/u);
  assert.doesNotMatch(endpoint, /pairToken|token=/u);
  assert.doesNotMatch(endpoint, /Location:.*#/u);
});

test("Secure Direct short redirect selects the hosted Secure Direct bootstrap without exposing the fragment", () => {
  const rewrite = read("apps/public-site/s/.htaccess");
  const endpoint = read("apps/public-site/s/index.php");
  assert.match(rewrite, /Options -Indexes/u);
  assert.match(rewrite, /\{22\}/u);
  assert.match(rewrite, /Cache-Control" "no-cache/u);
  assert.match(endpoint, /count\(\$_GET\) !== 2/u);
  assert.match(endpoint, /\$location = '\/air\/app\/\?m=s&r='/u);
  assert.doesNotMatch(endpoint, /pairToken|token=/u);
  assert.doesNotMatch(endpoint, /Location:.*#/u);
});

test("development short redirect selects isolated hosted assets without exposing the fragment", () => {
  const rewrite = read("apps/public-site/d/.htaccess");
  const endpoint = read("apps/public-site/d/index.php");
  assert.match(rewrite, /Options -Indexes/u);
  assert.match(rewrite, /\{22\}/u);
  assert.match(rewrite, /Cache-Control" "no-cache/u);
  assert.match(endpoint, /count\(\$_GET\) !== 2/u);
  assert.match(endpoint, /\$location = '\/air\/dev-app\/\?m=s&r='/u);
  assert.doesNotMatch(endpoint, /pairToken|token=/u);
  assert.doesNotMatch(endpoint, /Location:.*#/u);
});

test("High relay quality is part of normal packaged builds", () => {
  const project = read("apps/windows-host/VolturaAir.Host.csproj");
  const connectionView = read("apps/windows-host/Features/Connection/ConnectionPageView.xaml");
  assert.doesNotMatch(project, /MaintainerRelayQuality|RejectPublishedMaintainerRelayBuild/u);
  assert.match(connectionView, /High quality · up to 8 Mbps/u);
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
  assert.match(
    composition,
    /coturn:[\s\S]*depends_on:[\s\S]*relay:[\s\S]*condition: service_healthy/u,
  );
  assert.match(environment, /^TURN_PUBLIC_IP=/mu);
  assert.match(edge, /proxy_set_header X-Forwarded-For \$remote_addr;/u);
  assert.doesNotMatch(composition, /"8787:8787"/u);
});
