# Relay deployment

Cloud Relay is optional. Direct LAN remains the default. Deployment never
publishes the website or creates a Windows release unless those separate
commands are run explicitly.

## Cloudflare production setup

You need a Cloudflare account, a `workers.dev` account name, and a payment
method for Realtime TURN. Do not use or paste a Global API Key. Cloudflare's
TURN dashboard shows the included monthly transfer and any overage price before
activation; billing notifications warn but do not stop charges. Voltura Air
adds its own 750 GB warning/Data Saver threshold and 850 GB credential cutoff.
Those values have one production authority in `services/relay/wrangler.jsonc`;
the relay includes them with its usage snapshot, and the Windows host does not
duplicate them in registry settings or application code.

1. Create a free [Cloudflare account](https://dash.cloudflare.com/sign-up).
2. In **Workers & Pages**, choose the free `workers.dev` account name. No domain
   transfer or DNS change is required.
3. Open **Realtime → TURN Server**, add payment details, activate Realtime, and
   create one key named `Voltura Air production`.
4. Save its key ID and credential-generation token in a password manager.
5. In **Manage account → Account API tokens**, create a separate token with
   only **Account Analytics: Read**. Save it in the password manager.
6. In **Billing → Notifications**, create a small usage/billing notification.
   This is only a warning; it is not a spending limit.
7. From a PowerShell terminal in the repository, run:

   ```powershell
   npm run relay:setup
   ```

   The command opens Cloudflare login, asks for the account ID, TURN key ID,
   hidden restricted tokens, and `workers.dev` name. It validates and deploys
   the Worker/Durable Object, checks `/v1/health`, then writes the verified
   public address to `apps/windows-host/relay-service.json`. Secrets are stored
   as Cloudflare Worker secrets and are not written to the repository.
8. Recheck source with `npm run relay:check` and the deployed endpoint with
   `npm run relay:health`. View current-month TURN transfer with
   `npm run relay:usage`; its token prompt is hidden.
9. Outside a stable release, manually run `npm run publish:site` to upload the
   hosted PWA and first-party `/a/<route>` redirect. `npm run release:full`
   deploys and verifies the official relay before publishing the site and
   Windows release; `release:draft` leaves production relay infrastructure
   unchanged.

Cloudflare TURN credential generation and renewal follow the
[official credential API](https://developers.cloudflare.com/realtime/turn/generate-credentials/).
Usage is read from the
[official TURN GraphQL analytics fields](https://developers.cloudflare.com/realtime/turn/analytics/).

Command pairing and control use outbound HTTPS/WebSocket only. Relay screen
viewing on Windows also leaves through TCP 443: a bounded host-owned loopback
bridge carries the existing libjuice TURN client's messages to Cloudflare's
`turns` endpoint over certificate-validated TLS. No external UDP is required on
the PC side. Verify the complete path from a network that blocks outbound UDP;
an ordinary unrestricted-network test does not prove the TCP-only fallback.

## Advanced WSL self-hosting

This path is for a future custom provider. It uses the same route, host
authentication, pairing, encryption, WebSocket, and TURN response contracts as
Cloudflare. The supplied composition contains the standalone relay, coturn,
TLS/SNI edge, DuckDNS updater, health check, and bounded UDP allocation range.

### 1. Confirm inbound hosting is possible

In the router, note the WAN IPv4 address. On the Windows PC, open
`https://icanhazip.com` and compare it. Stop if the addresses differ or the
router shows an address in `100.64.0.0/10`; that normally indicates CGNAT and
ordinary port forwarding will not work. A publicly routed IPv6 setup is an
alternative only when the router and ISP both support inbound IPv6 firewall
rules.

### 2. Install and prepare WSL

Run PowerShell as Administrator:

```powershell
wsl --install -d Ubuntu
```

Restart Windows when asked. In Ubuntu, create `/etc/wsl.conf`:

```ini
[boot]
systemd=true
```

In Windows, create `%USERPROFILE%\.wslconfig`:

```ini
[wsl2]
networkingMode=mirrored
```

Run `wsl --shutdown`, reopen Ubuntu, then install Docker Engine and the Docker
Compose plugin from Docker's Ubuntu instructions. Confirm with
`docker compose version`.

### 3. Create names, address reservation, and certificates

Create separate free DuckDNS names, one for Relay and one for TURN. In the
router, reserve the Windows PC's current LAN address so it does not change.

Obtain one publicly trusted certificate containing both names. Store the full
chain and private key under `/etc/voltura-air/certs`, readable only by root.
Use an ACME client with DuckDNS DNS validation and install a renewal hook that
runs `docker compose restart edge coturn`. DNS validation is required because
the composition intentionally does not expose TCP port 80.

### 4. Configure and start

From Ubuntu:

```bash
cd /mnt/c/Users/<windows-name>/source/repos/voltura-air/services/relay/self-host
cp .env.example .env
chmod 600 .env
nano .env
docker compose config
docker compose up -d --build
docker compose ps
```

Set both hostnames, `TURN_PUBLIC_IP` to the router's WAN IPv4 address, the
DuckDNS token, the certificate paths, and a new random TURN shared secret of at
least 32 characters. Startup rejects private, reserved, or malformed TURN
addresses. If the WAN address changes, update it and restart `relay` and
`coturn`. Never reuse a Cloudflare or website password.

### 5. Forward only the required ports

Forward these router ports to the reserved Windows PC address:

- TCP 443 → TCP 443 for HTTPS/WebSocket and TURN TLS selected by hostname.
- UDP 443 → UDP 443 for TURN over UDP.
- UDP 49160–49200 → the same UDP range for bounded TURN allocations.

Create matching Windows Defender Firewall and Hyper-V rules only for those
ports. Do not expose the relay's internal port 8787, coturn's internal TLS port
5349, WSL management, SSH, or the Voltura Air LAN listener.

### 6. Start automatically and maintain

Create a Windows Task Scheduler task triggered **At log on** that runs:

```text
wsl.exe -d Ubuntu --cd /mnt/c/Users/<windows-name>/source/repos/voltura-air/services/relay/self-host docker compose up -d
```

Enable **Run whether user is logged on or not** and **Restart the task if it
fails**. Keep Ubuntu packages, Docker images, and the repository updated; run
`docker compose pull`, `docker compose up -d --build`, and verify certificate
renewal regularly.

### 7. Connect Voltura Air

In Windows **Connection**, select **Cloud relay through Voltura**, expand
**Custom relay endpoint**, enter `https://<relay-name>`, then save and restart.
The custom QR carries the validated endpoint to the hosted PWA. Test pairing,
reconnect, commands, and Screen over phone cellular data, then check:

```bash
curl https://<relay-name>/v1/health
docker compose logs --tail=100 relay edge coturn
```

The health response must report protocol `1` and status `ok`. Logs must never
contain pairing tokens, proofs, commands, text, coordinates, SDP, or screen
content.
